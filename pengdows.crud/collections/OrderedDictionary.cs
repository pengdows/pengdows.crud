using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace pengdows.crud.collections;

/// <summary>
/// Highly optimized ordered dictionary for database parameters.
/// Optimized for .NET 8. No artificial capacity ceiling beyond array limits.
/// Guarantees insertion order during enumeration.
/// This class is NOT thread-safe.
///
/// MEMORY BEHAVIOR: Clear() aggressively releases memory (unlike Dictionary{TKey,TValue}).
/// This is intentional for the database parameter use case where dictionaries are short-lived.
/// </summary>
[DebuggerDisplay("Count = {Count}")]
public sealed class OrderedDictionary<TKey, TValue> :
    IDictionary<TKey, TValue>,
    IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private const int DefaultCapacity = 16;
    private const int SmallCapacity = 8;
    private const double TrimThreshold = 0.9;

    private struct Entry
    {
        public uint HashCode;
        public int Next; // 0 = end of chain, else index+1
        public TKey Key;
        public TValue Value;
    }

    private Entry[] _entries = Array.Empty<Entry>();
    private int[] _buckets = Array.Empty<int>();        // hash-mode only: index+1
    private int[] _insertionOrder = Array.Empty<int>(); // hash-mode only: index+1
    private int[] _orderIndex = Array.Empty<int>();     // hash-mode only: entryIndex -> orderSlot+1

    private int _orderTail; // hash-mode only: order array length in use (includes tombstones)
    private ulong _fastModMul; // hash-mode only

    private int _count;      // physical count (includes free slots in hash-mode)
    private int _freeList;   // index+1
    private int _freeCount;
    private int _version;

    private readonly IEqualityComparer<TKey> _comparer;

    public OrderedDictionary() : this(0, null) { }
    public OrderedDictionary(int capacity) : this(capacity, null) { }
    public OrderedDictionary(IEqualityComparer<TKey>? comparer) : this(0, comparer) { }

    public OrderedDictionary(int capacity, IEqualityComparer<TKey>? comparer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _comparer = comparer ?? EqualityComparer<TKey>.Default;

        if (capacity <= 0)
            return; // fully lazy

        if (capacity <= SmallCapacity)
        {
            _entries = new Entry[Math.Max(capacity, SmallCapacity)];
            return;
        }

        InitializeHash(capacity);
    }

    public int Count => _count - _freeCount;
    public bool IsReadOnly => false;

    public ICollection<TKey> Keys => new KeyCollection(this);
    public ICollection<TValue> Values => new ValueCollection(this);

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

    public TValue this[TKey key]
    {
        get
        {
            ref var v = ref FindValue(key);
            if (!Unsafe.IsNullRef(ref v)) return v;
            throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");
        }
        set => TryInsert(key, value, InsertionBehavior.OverwriteExisting);
    }

    private bool IsHashMode => _buckets.Length != 0;

    public void Add(TKey key, TValue value)
    {
        var modified = TryInsert(key, value, InsertionBehavior.ThrowOnExisting);
        Debug.Assert(modified);
    }

    public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

    public void Clear()
    {
        if (_count == 0) return;

        _entries = Array.Empty<Entry>();
        _buckets = Array.Empty<int>();
        _insertionOrder = Array.Empty<int>();
        _orderIndex = Array.Empty<int>();

        _count = 0;
        _freeList = 0;
        _freeCount = 0;
        _orderTail = 0;
        _fastModMul = 0;

        _version = unchecked(_version + 1);
    }

    public bool ContainsKey(TKey key) => !Unsafe.IsNullRef(ref FindValue(key));

    public bool TryGetValue(TKey key, out TValue value)
    {
        ref var v = ref FindValue(key);
        if (!Unsafe.IsNullRef(ref v))
        {
            value = v;
            return true;
        }

        value = default!;
        return false;
    }

    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        ref var v = ref FindValue(item.Key);
        return !Unsafe.IsNullRef(ref v) && EqualityComparer<TValue>.Default.Equals(v, item.Value);
    }

    public bool Remove(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var comparer = _comparer;
        var hashCode = (uint)comparer.GetHashCode(key);

        // -------- hash-mode --------
        if (IsHashMode)
        {
            var bucket = (int)FastMod(hashCode, (uint)_buckets.Length, _fastModMul);

            var lastPlus1 = 0;
            var iPlus1 = _buckets[bucket];

            while (iPlus1 != 0)
            {
                var i = iPlus1 - 1;
                ref var e = ref _entries[i];

                if (e.HashCode == hashCode && comparer.Equals(e.Key, key))
                {
                    // unlink from bucket chain
                    if (lastPlus1 == 0)
                    {
                        _buckets[bucket] = e.Next;
                    }
                    else
                    {
                        _entries[lastPlus1 - 1].Next = e.Next;
                    }

                    var slotPlus1 = _orderIndex[i];
                    if (slotPlus1 != 0)
                    {
                        _insertionOrder[slotPlus1 - 1] = 0;
                        _orderIndex[i] = 0;
                    }
                    else
                    {
                        Debug.Fail("Insertion order index corruption detected.");
                    }

                    // free-list
                    e.HashCode = 0;
                    e.Key = default!;
                    e.Value = default!;
                    e.Next = _freeList;
                    _freeList = i + 1;
                    _freeCount++;

                    _version = unchecked(_version + 1);
                    return true;
                }

                lastPlus1 = iPlus1;
                iPlus1 = e.Next;
            }

            return false;
        }

        // -------- small-mode (packed) --------
        for (var i = 0; i < _count; i++)
        {
            ref var e = ref _entries[i];
            if (e.HashCode == hashCode && comparer.Equals(e.Key, key))
            {
                for (var j = i; j < _count - 1; j++)
                    _entries[j] = _entries[j + 1];

                _entries[_count - 1] = default!;
                _count--;

                _version = unchecked(_version + 1);
                return true;
            }
        }

        return false;
    }

    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        if (TryGetValue(item.Key, out var value) &&
            EqualityComparer<TValue>.Default.Equals(value, item.Value))
        {
            return Remove(item.Key);
        }

        return false;
    }

    public bool TryAdd(TKey key, TValue value)
        => TryInsert(key, value, InsertionBehavior.NoneIfExists);

    public bool Remove(TKey key, out TValue value)
    {
        if (TryGetValue(key, out value))
            return Remove(key);

        return false;
    }

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(arrayIndex, array.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Count, array.Length - arrayIndex);

        var scan = 0;
        for (var i = 0; i < Count; i++)
        {
            if (!TryGetNextIndex(ref scan, out var idx))
            {
                break;
            }

            ref var e = ref _entries[idx];
            array[arrayIndex++] = new KeyValuePair<TKey, TValue>(e.Key, e.Value);
        }
    }

    public void EnsureCapacity(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);

        if (capacity <= _entries.Length) return;

        if (!IsHashMode && capacity <= SmallCapacity)
        {
            var newEntries = new Entry[Math.Max(capacity, SmallCapacity)];
            Array.Copy(_entries, 0, newEntries, 0, _count);
            _entries = newEntries;
            return;
        }

        ResizeHash(Math.Max(capacity, DefaultCapacity));
    }

    public void TrimExcess()
    {
        var currentLen = _entries.Length;
        if (currentLen == 0) return;

        var logical = Count;

        if (logical == 0)
        {
            Clear();
            return;
        }

        var threshold = (int)(currentLen * TrimThreshold);
        if (logical >= threshold) return;

        if (logical <= SmallCapacity)
        {
            // shrink back to small-mode packed
            var newEntries = new Entry[Math.Max(logical, SmallCapacity)];
            var scan = 0;
            for (var i = 0; i < logical; i++)
            {
                if (!TryGetNextIndex(ref scan, out var oldIdx))
                {
                    break;
                }

                var e = _entries[oldIdx];
                e.Next = 0;
                newEntries[i] = e;
            }

            _entries = newEntries;
            _buckets = Array.Empty<int>();
            _insertionOrder = Array.Empty<int>();
            _orderIndex = Array.Empty<int>();

            _count = logical;
            _freeList = 0;
            _freeCount = 0;
            _orderTail = 0;
            _fastModMul = 0;

            _version = unchecked(_version + 1);
            return;
        }

        ResizeHash(logical);
        _version = unchecked(_version + 1);
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // -----------------------------
    // Internals
    // -----------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref TValue FindValue(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var comparer = _comparer;
        var hashCode = (uint)comparer.GetHashCode(key);

        if (IsHashMode)
        {
            var bucket = (int)FastMod(hashCode, (uint)_buckets.Length, _fastModMul);
            var iPlus1 = _buckets[bucket];

            while (iPlus1 != 0)
            {
                var i = iPlus1 - 1;
                ref var e = ref _entries[i];
                if (e.HashCode == hashCode && comparer.Equals(e.Key, key))
                    return ref e.Value;

                iPlus1 = e.Next;
            }

            return ref Unsafe.NullRef<TValue>(); // critical: no fall-through
        }

        // small-mode
        for (var i = 0; i < _count; i++)
        {
            ref var e = ref _entries[i];
            if (e.HashCode == hashCode && comparer.Equals(e.Key, key))
                return ref e.Value;
        }

        return ref Unsafe.NullRef<TValue>();
    }

    private bool TryInsert(TKey key, TValue value, InsertionBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_entries.Length == 0)
            _entries = new Entry[SmallCapacity];

        var comparer = _comparer;
        var hashCode = (uint)comparer.GetHashCode(key);

        // -------- small-mode --------
        if (!IsHashMode)
        {
            for (var i = 0; i < _count; i++)
            {
                ref var e = ref _entries[i];
                if (e.HashCode == hashCode && comparer.Equals(e.Key, key))
                {
                    if (behavior == InsertionBehavior.ThrowOnExisting)
                        ThrowAddingDuplicateWithKeyArgumentException(key);
                    if (behavior == InsertionBehavior.NoneIfExists)
                        return false;

                    e.Value = value;
                    _version = unchecked(_version + 1);
                    return true;
                }
            }

            if (_count < _entries.Length)
            {
                ref var ne = ref _entries[_count++];
                ne.HashCode = hashCode;
                ne.Key = key;
                ne.Value = value;
                ne.Next = 0;

                _version = unchecked(_version + 1);
                return true;
            }

            // overflow -> hash-mode
            ResizeHash(Math.Max(DefaultCapacity, _count * 2));
            // fall through
        }

        // -------- hash-mode --------
        var bucket = (int)FastMod(hashCode, (uint)_buckets.Length, _fastModMul);
        var iPlus1 = _buckets[bucket];

        while (iPlus1 != 0)
        {
            var i = iPlus1 - 1;
            ref var e = ref _entries[i];
            if (e.HashCode == hashCode && comparer.Equals(e.Key, key))
            {
                if (behavior == InsertionBehavior.ThrowOnExisting)
                    ThrowAddingDuplicateWithKeyArgumentException(key);
                if (behavior == InsertionBehavior.NoneIfExists)
                    return false;

                e.Value = value;
                _version = unchecked(_version + 1);
                return true;
            }

            iPlus1 = e.Next;
        }

        int index;
        if (_freeCount != 0)
        {
            var indexPlus1 = _freeList;
            index = indexPlus1 - 1;
            _freeList = _entries[index].Next;
            _freeCount--;
        }
        else
        {
            if (_count == _entries.Length)
            {
                ResizeHash(GetPrime(Math.Max(_count * 2, DefaultCapacity)));
                bucket = (int)FastMod(hashCode, (uint)_buckets.Length, _fastModMul);
            }

            index = _count++;
        }

        ref var ne2 = ref _entries[index];
        ne2.HashCode = hashCode;
        ne2.Key = key;
        ne2.Value = value;

        var head = _buckets[bucket];
        ne2.Next = head;
        _buckets[bucket] = index + 1;

        if (_orderTail == _insertionOrder.Length)
        {
            Array.Resize(ref _insertionOrder, _entries.Length);
            Array.Resize(ref _orderIndex, _entries.Length);
        }

        Debug.Assert(_insertionOrder[_orderTail] == 0);
        _insertionOrder[_orderTail] = index + 1;
        _orderIndex[index] = _orderTail + 1;
        _orderTail++;

        _version = unchecked(_version + 1);
        return true;
    }

    private void InitializeHash(int capacity)
    {
        var bucketSize = GetPrime(Math.Max(capacity, 1));
        _buckets = new int[bucketSize];
        _entries = new Entry[Math.Max(capacity, 1)];
        _insertionOrder = new int[_entries.Length];
        _orderIndex = new int[_entries.Length];

        _count = 0;
        _freeList = 0;
        _freeCount = 0;
        _orderTail = 0;
        _fastModMul = GetFastModMultiplier((uint)bucketSize);
    }

    private void ResizeHash(int targetSize)
    {
        var newSize = Math.Max(targetSize, 1);
        var newBucketSize = GetPrime(newSize);

        var newBuckets = new int[newBucketSize];
        var newEntries = new Entry[newSize];
        var newOrder = new int[newSize];
        var newOrderIndex = new int[newSize];

        var activeCount = Count;
        var newFastMod = GetFastModMultiplier((uint)newBucketSize);

        var k = 0;
        if (IsHashMode)
        {
            for (var oi = 0; oi < _orderTail && k < activeCount; oi++)
            {
                var ip1 = _insertionOrder[oi];
                if (ip1 == 0)
                {
                    continue;
                }

                var oldIndex = ip1 - 1;
                ref readonly var oe = ref _entries[oldIndex];

                ref var ne = ref newEntries[k];
                ne.HashCode = oe.HashCode;
                ne.Key = oe.Key;
                ne.Value = oe.Value;

                var bucket = (int)FastMod(ne.HashCode, (uint)newBucketSize, newFastMod);
                var head = newBuckets[bucket];
                ne.Next = head;
                newBuckets[bucket] = k + 1;

                newOrder[k] = k + 1;
                newOrderIndex[k] = k + 1;
                k++;
            }
        }
        else
        {
            for (; k < activeCount; k++)
            {
                ref readonly var oe = ref _entries[k];

                ref var ne = ref newEntries[k];
                ne.HashCode = oe.HashCode;
                ne.Key = oe.Key;
                ne.Value = oe.Value;

                var bucket = (int)FastMod(ne.HashCode, (uint)newBucketSize, newFastMod);
                var head = newBuckets[bucket];
                ne.Next = head;
                newBuckets[bucket] = k + 1;

                newOrder[k] = k + 1;
                newOrderIndex[k] = k + 1;
            }
        }

        _buckets = newBuckets;
        _entries = newEntries;
        _insertionOrder = newOrder;
        _orderIndex = newOrderIndex;

        _freeList = 0;
        _freeCount = 0;
        _count = activeCount;
        _orderTail = activeCount;
        _fastModMul = newFastMod;
    }

    private bool TryGetNextIndex(ref int orderScan, out int index)
    {
        index = -1;
        if (IsHashMode)
        {
            for (; orderScan < _orderTail; orderScan++)
            {
                var ip1 = _insertionOrder[orderScan];
                if (ip1 == 0)
                {
                    continue;
                }

                index = ip1 - 1;
                orderScan++;
                return true;
            }

            return false;
        }

        if (orderScan < _count)
        {
            index = orderScan;
            orderScan++;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FastMod(uint value, uint divisor, ulong multiplier)
    {
        var quotient = (uint)((multiplier * value) >> 32);
        return value - quotient * divisor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong GetFastModMultiplier(uint divisor)
    {
        return ulong.MaxValue / divisor + 1;
    }

    private static int GetPrime(int min)
    {
        ReadOnlySpan<int> primes = stackalloc int[]
        {
            17, 37, 79, 163, 331, 673, 1361, 2729, 5471, 10949, 21911, 43853,
            87313, 139901, 223729, 357913, 573343, 917513, 1468007, 2359297
        };

        foreach (var p in primes)
            if (p >= min) return p;

        if ((min & 1) == 0) min++;

        for (var c = min; c < int.MaxValue - 2; c += 2)
            if (IsPrime(c)) return c;

        return min;
    }

    private static bool IsPrime(int candidate)
    {
        if (candidate <= 1) return false;
        if (candidate <= 3) return true;
        if ((candidate & 1) == 0) return false;

        var limit = (int)Math.Sqrt(candidate);
        for (var d = 3; d <= limit; d += 2)
            if (candidate % d == 0) return false;

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowAddingDuplicateWithKeyArgumentException(TKey key) =>
        throw new ArgumentException($"An item with the same key has already been added. Key: {key}");

    private enum InsertionBehavior : byte
    {
        OverwriteExisting = 1,
        ThrowOnExisting = 2,
        NoneIfExists = 3
    }

    // -----------------------------
    // Enumerator
    // -----------------------------

    public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
    {
        private readonly OrderedDictionary<TKey, TValue> _d;
        private readonly int _version;
        private int _yielded;
        private int _orderScan;
        private KeyValuePair<TKey, TValue> _current;

        internal Enumerator(OrderedDictionary<TKey, TValue> d)
        {
            _d = d;
            _version = d._version;
            _yielded = 0;
            _orderScan = 0;
            _current = default;
        }

        public bool MoveNext()
        {
            if (_version != _d._version) ThrowInvalidOperationException();

            var needed = _d.Count;
            var limit = _d.IsHashMode ? _d._orderTail : needed;

            for (; _yielded < needed && _orderScan < limit; _orderScan++)
            {
                int idx;
                if (_d.IsHashMode)
                {
                    var ip1 = _d._insertionOrder[_orderScan];
                    if (ip1 == 0)
                    {
                        continue;
                    }

                    idx = ip1 - 1;
                }
                else
                {
                    idx = _orderScan;
                }

                ref var e = ref _d._entries[idx];
                _current = new KeyValuePair<TKey, TValue>(e.Key, e.Value);
                _yielded++;
                _orderScan++;
                return true;
            }

            _current = default;
            _yielded = needed + 1;
            return false;
        }

        public readonly KeyValuePair<TKey, TValue> Current => _current;
        readonly object IEnumerator.Current => Current;

        void IEnumerator.Reset()
        {
            if (_version != _d._version) ThrowInvalidOperationException();
            _yielded = 0;
            _orderScan = 0;
            _current = default;
        }

        public readonly void Dispose() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidOperationException() =>
            throw new InvalidOperationException("Collection was modified during enumeration.");
    }

    // -----------------------------
    // Key / Value collections
    // -----------------------------

    private sealed class KeyCollection : ICollection<TKey>, IReadOnlyCollection<TKey>
    {
        private readonly OrderedDictionary<TKey, TValue> _d;
        public KeyCollection(OrderedDictionary<TKey, TValue> d) => _d = d;

        public int Count => _d.Count;
        public bool IsReadOnly => true;

        public void Add(TKey item) => ThrowRO();
        public void Clear() => ThrowRO();
        public bool Remove(TKey item) => ThrowRO<bool>();

        public bool Contains(TKey item) => _d.ContainsKey(item);

        public void CopyTo(TKey[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(_d.Count, array.Length - arrayIndex);

            var scan = 0;
            for (var i = 0; i < _d.Count; i++)
            {
                if (!_d.TryGetNextIndex(ref scan, out var idx))
                {
                    break;
                }

                array[arrayIndex++] = _d._entries[idx].Key;
            }
        }

        public IEnumerator<TKey> GetEnumerator() => new KeyEnumerator(_d);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowRO() => throw new NotSupportedException("Collection is read-only.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static T ThrowRO<T>() => throw new NotSupportedException("Collection is read-only.");
    }

    private sealed class ValueCollection : ICollection<TValue>, IReadOnlyCollection<TValue>
    {
        private readonly OrderedDictionary<TKey, TValue> _d;
        public ValueCollection(OrderedDictionary<TKey, TValue> d) => _d = d;

        public int Count => _d.Count;
        public bool IsReadOnly => true;

        public void Add(TValue item) => ThrowRO();
        public void Clear() => ThrowRO();
        public bool Remove(TValue item) => ThrowRO<bool>();

        public bool Contains(TValue item)
        {
            var cmp = EqualityComparer<TValue>.Default;
            var scan = 0;
            for (var i = 0; i < _d.Count; i++)
            {
                if (!_d.TryGetNextIndex(ref scan, out var idx))
                {
                    break;
                }

                if (cmp.Equals(_d._entries[idx].Value, item)) return true;
            }
            return false;
        }

        public void CopyTo(TValue[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(_d.Count, array.Length - arrayIndex);

            var scan = 0;
            for (var i = 0; i < _d.Count; i++)
            {
                if (!_d.TryGetNextIndex(ref scan, out var idx))
                {
                    break;
                }

                array[arrayIndex++] = _d._entries[idx].Value;
            }
        }

        public IEnumerator<TValue> GetEnumerator() => new ValueEnumerator(_d);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowRO() => throw new NotSupportedException("Collection is read-only.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static T ThrowRO<T>() => throw new NotSupportedException("Collection is read-only.");
    }

    private struct KeyEnumerator : IEnumerator<TKey>
    {
        private readonly OrderedDictionary<TKey, TValue> _d;
        private readonly int _version;
        private int _yielded;
        private int _orderScan;
        private TKey _current;

        internal KeyEnumerator(OrderedDictionary<TKey, TValue> d)
        {
            _d = d;
            _version = d._version;
            _yielded = 0;
            _orderScan = 0;
            _current = default!;
        }

        public bool MoveNext()
        {
            if (_version != _d._version) ThrowInvalidOperationException();

            var needed = _d.Count;
            var limit = _d.IsHashMode ? _d._orderTail : needed;

            for (; _yielded < needed && _orderScan < limit; _orderScan++)
            {
                int idx;
                if (_d.IsHashMode)
                {
                    var ip1 = _d._insertionOrder[_orderScan];
                    if (ip1 == 0)
                    {
                        continue;
                    }

                    idx = ip1 - 1;
                }
                else
                {
                    idx = _orderScan;
                }

                _current = _d._entries[idx].Key;
                _yielded++;
                _orderScan++;
                return true;
            }

            _current = default!;
            _yielded = needed + 1;
            return false;
        }

        public readonly TKey Current => _current;
        readonly object IEnumerator.Current => Current!;

        void IEnumerator.Reset()
        {
            if (_version != _d._version) ThrowInvalidOperationException();
            _yielded = 0;
            _orderScan = 0;
            _current = default!;
        }

        public readonly void Dispose() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidOperationException() =>
            throw new InvalidOperationException("Collection was modified during enumeration.");
    }

    private struct ValueEnumerator : IEnumerator<TValue>
    {
        private readonly OrderedDictionary<TKey, TValue> _d;
        private readonly int _version;
        private int _yielded;
        private int _orderScan;
        private TValue _current;

        internal ValueEnumerator(OrderedDictionary<TKey, TValue> d)
        {
            _d = d;
            _version = d._version;
            _yielded = 0;
            _orderScan = 0;
            _current = default!;
        }

        public bool MoveNext()
        {
            if (_version != _d._version) ThrowInvalidOperationException();

            var needed = _d.Count;
            var limit = _d.IsHashMode ? _d._orderTail : needed;

            for (; _yielded < needed && _orderScan < limit; _orderScan++)
            {
                int idx;
                if (_d.IsHashMode)
                {
                    var ip1 = _d._insertionOrder[_orderScan];
                    if (ip1 == 0)
                    {
                        continue;
                    }

                    idx = ip1 - 1;
                }
                else
                {
                    idx = _orderScan;
                }

                _current = _d._entries[idx].Value;
                _yielded++;
                _orderScan++;
                return true;
            }

            _current = default!;
            _yielded = needed + 1;
            return false;
        }

        public readonly TValue Current => _current;
        readonly object IEnumerator.Current => Current!;

        void IEnumerator.Reset()
        {
            if (_version != _d._version) ThrowInvalidOperationException();
            _yielded = 0;
            _orderScan = 0;
            _current = default!;
        }

        public readonly void Dispose() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidOperationException() =>
            throw new InvalidOperationException("Collection was modified during enumeration.");
    }
}
