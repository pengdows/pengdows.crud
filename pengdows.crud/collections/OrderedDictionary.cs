using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace pengdows.crud.collections;

/// <summary>
/// Highly optimized ordered dictionary for database parameters.
/// Optimized for .NET 8. No artificial capacity ceiling beyond array limits.
/// Guarantees insertion order during enumeration.
/// This class is NOT thread-safe.
/// </summary>
/// <typeparam name="TKey">The type of keys (typically string for DB parameters)</typeparam>
/// <typeparam name="TValue">The type of values (typically DbParameter for DB parameter values)</typeparam>
[DebuggerDisplay("Count = {Count}")]
public sealed class OrderedDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    // No artificial MaxCapacity/MaxBucketSize
    private const int DefaultCapacity = 16;
    private const int SmallCapacity = 8;

    // Use struct for better cache locality and reduced allocations
    private struct Entry
    {
        public uint HashCode;
        public int Next;     // 0 = end of chain, else index+1
        public TKey Key;
        public TValue Value;
    }

    private Entry[] _entries = Array.Empty<Entry>();
    private int[] _buckets = Array.Empty<int>();        // stores index+1, 0 = empty
    private int[] _insertionOrder = Array.Empty<int>(); // stores index+1
    private int _count;
    private int _freeList;         // 0 = none, else index+1
    private int _freeCount;
    private int _version;
    private IEqualityComparer<TKey> _comparer = EqualityComparer<TKey>.Default;

    public OrderedDictionary() : this(DefaultCapacity, null) { }

    public OrderedDictionary(int capacity) : this(capacity, null) { }

    public OrderedDictionary(IEqualityComparer<TKey>? comparer) : this(DefaultCapacity, comparer) { }

    public OrderedDictionary(int capacity, IEqualityComparer<TKey>? comparer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);

        if (capacity > 0)
        {
            Initialize(capacity);
        }

        _comparer = comparer ?? EqualityComparer<TKey>.Default;
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
            ref var value = ref FindValue(key);
            if (!Unsafe.IsNullRef(ref value))
            {
                return value;
            }
            throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");
        }
        set => TryInsert(key, value, InsertionBehavior.OverwriteExisting);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Initialize(int capacity)
    {
        var size = GetPrime(Math.Max(capacity, 1));
        _buckets = new int[size];              // zero-initialized (0 = empty)
        var entryCapacity = Math.Max(capacity, 1); // Ensure at least 1
        _entries = new Entry[entryCapacity];
        _insertionOrder = new int[entryCapacity];
        _freeList = 0;                            // 0 = none
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetEntryIndexAt(int orderIndex)
    {
        return _buckets.Length == 0 ? orderIndex : _insertionOrder[orderIndex] - 1;
    }

    public void Add(TKey key, TValue value)
    {
        var modified = TryInsert(key, value, InsertionBehavior.ThrowOnExisting);
        Debug.Assert(modified);
    }

    public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

    public void Clear()
    {
        var count = _count;
        if (count > 0)
        {
            if (_buckets.Length == 0)
            {
                Array.Clear(_entries, 0, count);
            }
            else
            {
                Array.Clear(_buckets, 0, _buckets.Length);    // 0 = empty
                Array.Clear(_entries, 0, count);
                Array.Clear(_insertionOrder, 0, _insertionOrder.Length);
            }

            _count = 0;
            _freeList = 0;      // 0 = none
            _freeCount = 0;
            _version++;
        }
    }

    public bool ContainsKey(TKey key) => !Unsafe.IsNullRef(ref FindValue(key));

    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        ref var value = ref FindValue(item.Key);
        return !Unsafe.IsNullRef(ref value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);
    }

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(arrayIndex, array.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Count, array.Length - arrayIndex);

        // Use insertion order for consistent ordering
        for (int i = 0; i < Count; i++)
        {
            int entryIndex = GetEntryIndexAt(i);
            ref var entry = ref _entries[entryIndex];
            array[arrayIndex++] = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
        }
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref TValue FindValue(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_buckets.Length != 0)
        {
            Debug.Assert(_entries != null);

            var comparer = _comparer;
            var hashCode = (uint)comparer.GetHashCode(key);
            var bucket = hashCode % (uint)_buckets.Length;
            int iPlus1 = _buckets[bucket];

            while (iPlus1 != 0)
            {
                int i = iPlus1 - 1;
                ref var entry = ref _entries[i];
                if (entry.HashCode == hashCode && comparer.Equals(entry.Key, key))
                {
                    return ref entry.Value;
                }
                iPlus1 = entry.Next;
            }
        }

        var smallComparer = _comparer;
        for (int i = 0; i < _count; i++)
        {
            ref var entry = ref _entries[i];
            if (smallComparer.Equals(entry.Key, key))
            {
                return ref entry.Value;
            }
        }

        return ref Unsafe.NullRef<TValue>();
    }

    private bool TryInsert(TKey key, TValue value, InsertionBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_entries.Length == 0)
        {
            _entries = new Entry[SmallCapacity];
        }

        if (_buckets.Length == 0)
        {
            var smallComparer = _comparer;
            for (int i = 0; i < _count; i++)
            {
                if (smallComparer.Equals(_entries[i].Key, key))
                {
                    if (behavior == InsertionBehavior.ThrowOnExisting)
                    {
                        ThrowAddingDuplicateWithKeyArgumentException(key);
                    }
                    if (behavior == InsertionBehavior.NoneIfExists)
                    {
                        return false;
                    }

                    _entries[i].Value = value;
                    _version++;
                    return true;
                }
            }

            if (_count < _entries.Length)
            {
                var smallHash = (uint)smallComparer.GetHashCode(key);
                _entries[_count].HashCode = smallHash;
                _entries[_count].Key = key;
                _entries[_count].Value = value;
                _count++;
                _version++;
                return true;
            }

            Resize(DefaultCapacity);
        }

        var comparer = _comparer;
        var hashCode = (uint)comparer.GetHashCode(key);
        var bucket = hashCode % (uint)_buckets.Length;
        int iPlus1 = _buckets[bucket];

        // Check if key already exists
        while (iPlus1 != 0)
        {
            int i = iPlus1 - 1;
            ref var entry = ref _entries[i];
            if (entry.HashCode == hashCode && comparer.Equals(entry.Key, key))
            {
                if (behavior == InsertionBehavior.ThrowOnExisting)
                {
                    ThrowAddingDuplicateWithKeyArgumentException(key);
                }
                if (behavior == InsertionBehavior.NoneIfExists)
                {
                    return false;
                }

                entry.Value = value;
                _version++;
                return true;
            }
            iPlus1 = entry.Next;
        }

        // Add new entry
        int index;
        var logicalCount = Count; // Current logical count for insertion order

        if (_freeCount != 0)
        {
            // Reuse from free list
            int indexPlus1 = _freeList;
            int i = indexPlus1 - 1;
            _freeList = _entries[i].Next;        // might be 0
            _freeCount--;
            index = i;
        }
        else
        {
            // Need a new slot
            if (_count == _entries.Length)
            {
                Resize();
                bucket = hashCode % (uint)_buckets.Length;
            }
            index = _count++;
        }

        ref var newEntry = ref _entries[index];
        newEntry.HashCode = hashCode;
        newEntry.Key = key;
        newEntry.Value = value;

        // Link into bucket chain
        var head = _buckets[bucket];
        newEntry.Next = head;                    // 0 if none
        _buckets[bucket] = (index + 1);

        // Record insertion order
        Debug.Assert(_insertionOrder[logicalCount] == 0, "Insertion order slot should be empty");
        _insertionOrder[logicalCount] = (index + 1);

        _version++;
        return true;
    }

    public bool Remove(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_buckets.Length != 0)
        {
            Debug.Assert(_entries != null);

            var comparer = _comparer;
            var hashCode = (uint)comparer.GetHashCode(key);
            var bucket = hashCode % (uint)_buckets.Length;
            int lastPlus1 = 0;
            int iPlus1 = _buckets[bucket];

            while (iPlus1 != 0)
            {
                int i = iPlus1 - 1;
                ref var entry = ref _entries[i];

                if (entry.HashCode == hashCode && comparer.Equals(entry.Key, key))
                {
                    // Remove from hash chain
                    if (lastPlus1 == 0)
                    {
                        _buckets[bucket] = entry.Next;
                    }
                    else
                    {
                        _entries[lastPlus1 - 1].Next = entry.Next;
                    }

                    // Remove from insertion order and compact
                    RemoveFromInsertionOrder((i + 1));

                    // Clear entry and add to free list
                    entry.HashCode = 0;
                    entry.Key = default!;
                    entry.Value = default!;
                    entry.Next = _freeList;            // 0 if none
                    _freeList = (i + 1);
                    _freeCount++;
                    _version++;
                    return true;
                }

                lastPlus1 = iPlus1;
                iPlus1 = entry.Next;
            }

            return false;
        }

        var smallComparer = _comparer;
        for (int i = 0; i < _count; i++)
        {
            if (smallComparer.Equals(_entries[i].Key, key))
            {
                for (int j = i; j < _count - 1; j++)
                {
                    _entries[j] = _entries[j + 1];
                }
                _entries[_count - 1] = default!;
                _count--;
                _version++;
                return true;
            }
        }

        return false;
    }

    private void RemoveFromInsertionOrder(int indexPlus1)
    {
        var currentCount = Count; // logical count BEFORE incrementing _freeCount

        // Find the position of this entry in insertion order
        for (int i = 0; i < currentCount; i++)
        {
            if (_insertionOrder[i] == indexPlus1)
            {
                // Shift all subsequent entries left
                for (int j = i; j < currentCount - 1; j++)
                {
                    _insertionOrder[j] = _insertionOrder[j + 1];
                }
                _insertionOrder[currentCount - 1] = 0; // Clear tail slot
                break;
            }
        }
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

    public bool TryAdd(TKey key, TValue value) => TryInsert(key, value, InsertionBehavior.NoneIfExists);

    public bool Remove(TKey key, out TValue value)
    {
        if (TryGetValue(key, out value))
        {
            return Remove(key);
        }
        return false;
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        ref var valRef = ref FindValue(key);
        if (!Unsafe.IsNullRef(ref valRef))
        {
            value = valRef;
            return true;
        }

        value = default!;
        return false;
    }

    public void EnsureCapacity(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        if (capacity > (_entries?.Length ?? 0))
        {
            Resize(capacity); // Let Resize pick bucket prime
        }
    }

    public void TrimExcess()
    {
        var currentLength = _entries?.Length ?? 0;
        if (currentLength == 0)
        {
            return;
        }

        if (Count == 0)
        {
            _entries = Array.Empty<Entry>();
            _buckets = Array.Empty<int>();
            _insertionOrder = Array.Empty<int>();
            _count = 0;
            _freeList = 0;
            _freeCount = 0;
            _version++;
            return;
        }

        int threshold = (int)(currentLength * 0.9);
        if (Count < threshold)
        {
            Resize(Count); // Compact to logical count; Resize will choose bucket prime
        }
    }

    private void Resize()
    {
        var newSize = GetPrime(Math.Max(_count * 2, DefaultCapacity));
        Resize(newSize);
    }

    private void Resize(int targetSize)
    {
        var newSize = Math.Max(targetSize, 1);
        var newBucketSize = GetPrime(newSize);

        var newBuckets = new int[newBucketSize];    // zero-initialized (0 = empty)
        var newEntries = new Entry[newSize];
        var newInsertionOrder = new int[newSize];

        // Rebuild from insertion order (or small array) - only copy active entries
        int activeCount = Count;
        for (int k = 0; k < activeCount; k++)
        {
            int oldIndex = _buckets.Length == 0 ? k : _insertionOrder[k] - 1;
            ref readonly var oldEntry = ref _entries[oldIndex];

            // Place entries sequentially for better cache locality
            int newIndex = k;
            ref var newEntry = ref newEntries[newIndex];
            newEntry.HashCode = oldEntry.HashCode;
            newEntry.Key = oldEntry.Key;
            newEntry.Value = oldEntry.Value;

            // Rebuild hash chain
            var bucket = newEntry.HashCode % (uint)newBucketSize;
            var head = newBuckets[bucket];
            newEntry.Next = head;                      // 0 if none
            newBuckets[bucket] = (newIndex + 1);

            newInsertionOrder[k] = (newIndex + 1);
        }

        // Update arrays and reset free list
        _buckets = newBuckets;
        _entries = newEntries;
        _insertionOrder = newInsertionOrder;
        _freeList = 0;                                 // 0 = none
        _freeCount = 0;
        _count = activeCount;
    }

    // Optimized prime calculation up to 65521
    private static int GetPrime(int min)
    {
        ReadOnlySpan<int> primes = stackalloc int[]
        {
            17, 37, 79, 163, 331, 673, 1361, 2729, 5471, 10949, 21911, 43853,
            87313, 139901, 223729, 357913, 573343, 917513, 1468007, 2359297
        };

        foreach (var prime in primes)
        {
            if (prime >= min)
            {
                return prime;
            }
        }

        // Fallback calculation: find next odd prime without upper clamp
        if ((min & 1) == 0)
        {
            min++;
        }
        for (int candidate = min; candidate < int.MaxValue - 2; candidate += 2)
        {
            if (IsPrime(candidate))
            {
                return candidate;
            }
        }

        return min;
    }

    private static bool IsPrime(int candidate)
    {
        if (candidate <= 1)
        {
            return false;
        }

        if (candidate <= 3)
        {
            return true;
        }

        if ((candidate & 1) == 0)
        {
            return false;
        }

        int limit = (int)Math.Sqrt(candidate);
        for (int divisor = 3; divisor <= limit; divisor += 2)
        {
            if (candidate % divisor == 0)
            {
                return false;
            }
        }
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

    // Custom enumerator that respects insertion order
    public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
    {
        private readonly OrderedDictionary<TKey, TValue> _dictionary;
        private readonly int _version;
        private int _index;
        private KeyValuePair<TKey, TValue> _current;

        internal Enumerator(OrderedDictionary<TKey, TValue> dictionary)
        {
            _dictionary = dictionary;
            _version = dictionary._version;
            _index = 0;
            _current = default;
        }

        public bool MoveNext()
        {
            if (_version != _dictionary._version)
            {
                ThrowInvalidOperationException();
            }

            if (_index < _dictionary.Count)
            {
                int entryIndex = _dictionary.GetEntryIndexAt(_index);
                ref var entry = ref _dictionary._entries[entryIndex];
                _current = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                _index++;
                return true;
            }

            _index = _dictionary.Count + 1;
            _current = default;
            return false;
        }

        public readonly KeyValuePair<TKey, TValue> Current => _current;

        readonly object IEnumerator.Current => Current;

        void IEnumerator.Reset()
        {
            if (_version != _dictionary._version)
            {
                ThrowInvalidOperationException();
            }
            _index = 0;
            _current = default;
        }

        public readonly void Dispose() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidOperationException() =>
            throw new InvalidOperationException("Collection was modified during enumeration.");
    }

    // Optimized key collection that maintains order
    private sealed class KeyCollection : ICollection<TKey>, IReadOnlyCollection<TKey>
    {
        private readonly OrderedDictionary<TKey, TValue> _dict;

        public KeyCollection(OrderedDictionary<TKey, TValue> dict) => _dict = dict;

        public int Count => _dict.Count;

        public bool IsReadOnly => true;

        public void Add(TKey item) => ThrowNotSupportedException();

        public void Clear() => ThrowNotSupportedException();

        public bool Contains(TKey item) => _dict.ContainsKey(item);

        public void CopyTo(TKey[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(_dict.Count, array.Length - arrayIndex);

            // Use insertion order
            for (int i = 0; i < _dict.Count; i++)
            {
                int entryIndex = _dict.GetEntryIndexAt(i);
                array[arrayIndex++] = _dict._entries[entryIndex].Key;
            }
        }

        public bool Remove(TKey item) => ThrowNotSupportedException<bool>();

        public IEnumerator<TKey> GetEnumerator() => new KeyEnumerator(_dict);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowNotSupportedException() =>
            throw new NotSupportedException("Collection is read-only.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static T ThrowNotSupportedException<T>() =>
            throw new NotSupportedException("Collection is read-only.");
    }

    // Optimized value collection that maintains order
    private sealed class ValueCollection : ICollection<TValue>, IReadOnlyCollection<TValue>
    {
        private readonly OrderedDictionary<TKey, TValue> _dict;

        public ValueCollection(OrderedDictionary<TKey, TValue> dict) => _dict = dict;

        public int Count => _dict.Count;

        public bool IsReadOnly => true;

        public void Add(TValue item) => ThrowNotSupportedException();

        public void Clear() => ThrowNotSupportedException();

        public bool Contains(TValue item)
        {
            var comparer = EqualityComparer<TValue>.Default;
            for (int i = 0; i < _dict.Count; i++)
            {
                int entryIndex = _dict.GetEntryIndexAt(i);
                if (comparer.Equals(_dict._entries[entryIndex].Value, item))
                {
                    return true;
                }
            }
            return false;
        }

        public void CopyTo(TValue[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(_dict.Count, array.Length - arrayIndex);

            // Use insertion order
            for (int i = 0; i < _dict.Count; i++)
            {
                int entryIndex = _dict.GetEntryIndexAt(i);
                array[arrayIndex++] = _dict._entries[entryIndex].Value;
            }
        }

        public bool Remove(TValue item) => ThrowNotSupportedException<bool>();

        public IEnumerator<TValue> GetEnumerator() => new ValueEnumerator(_dict);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowNotSupportedException() =>
            throw new NotSupportedException("Collection is read-only.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static T ThrowNotSupportedException<T>() =>
            throw new NotSupportedException("Collection is read-only.");
    }

    private struct KeyEnumerator : IEnumerator<TKey>
    {
        private readonly OrderedDictionary<TKey, TValue> _dict;
        private readonly int _version;
        private int _index;
        private TKey _current;

        internal KeyEnumerator(OrderedDictionary<TKey, TValue> dict)
        {
            _dict = dict;
            _version = dict._version;
            _index = 0;
            _current = default!;
        }

        public bool MoveNext()
        {
            if (_version != _dict._version)
            {
                ThrowInvalidOperationException();
            }

            if (_index < _dict.Count)
            {
                int entryIndex = _dict.GetEntryIndexAt(_index);
                _current = _dict._entries[entryIndex].Key;
                _index++;
                return true;
            }

            _index = _dict.Count + 1;
            _current = default!;
            return false;
        }

        public readonly TKey Current => _current;

        readonly object IEnumerator.Current => Current!;

        void IEnumerator.Reset()
        {
            if (_version != _dict._version)
            {
                ThrowInvalidOperationException();
            }
            _index = 0;
            _current = default!;
        }

        public readonly void Dispose() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidOperationException() =>
            throw new InvalidOperationException("Collection was modified during enumeration.");
    }

    private struct ValueEnumerator : IEnumerator<TValue>
    {
        private readonly OrderedDictionary<TKey, TValue> _dict;
        private readonly int _version;
        private int _index;
        private TValue _current;

        internal ValueEnumerator(OrderedDictionary<TKey, TValue> dict)
        {
            _dict = dict;
            _version = dict._version;
            _index = 0;
            _current = default!;
        }

        public bool MoveNext()
        {
            if (_version != _dict._version)
            {
                ThrowInvalidOperationException();
            }

            if (_index < _dict.Count)
            {
                int entryIndex = _dict.GetEntryIndexAt(_index);
                _current = _dict._entries[entryIndex].Value;
                _index++;
                return true;
            }

            _index = _dict.Count + 1;
            _current = default!;
            return false;
        }

        public readonly TValue Current => _current;

        readonly object IEnumerator.Current => Current!;

        void IEnumerator.Reset()
        {
            if (_version != _dict._version)
            {
                ThrowInvalidOperationException();
            }
            _index = 0;
            _current = default!;
        }

        public readonly void Dispose() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidOperationException() =>
            throw new InvalidOperationException("Collection was modified during enumeration.");
    }
}
