// =============================================================================
// FILE: BoundedCache.cs
// PURPOSE: Thread-safe bounded LRU cache with automatic eviction.
//
// AI SUMMARY:
// - Generic bounded cache with configurable max size.
// - Thread-safe: uses ConcurrentDictionary with per-entry Lazy and LRU timestamps.
// - Only a miss advances the clock; a hit writes its entry's stamp at most once per miss and
//   never touches shared state, so concurrent hits don't contend (REV-057).
// - LRU eviction: least-recently-accessed entries removed when capacity exceeded.
// - Key methods:
//   * GetOrAdd(key, factory) - retrieves or creates entry; factory runs at most once per key
//   * TryGet(key, out value) - retrieves and touches entry for LRU ordering
//   * Clear() - removes all entries
// - Lazy<TValue> with ExecutionAndPublication ensures the value factory executes
//   exactly once per key, even when multiple threads race on the same missing key.
// - Eviction is a linear scan for the entry with the lowest access timestamp;
//   default cache sizes are 32-512 (reader-plan size is configurable), so this is cheap.
// - Used internally for caching compiled accessors, type info, reader plans, etc.
// =============================================================================

using System.Collections.Concurrent;

namespace pengdows.crud.@internal;

internal sealed class BoundedCache<TKey, TValue> where TKey : notnull
{
    private readonly int _max;
    private readonly ConcurrentDictionary<TKey, CacheEntry> _map = new();
    private long _clock;

    public BoundedCache(int max)
    {
        _max = Math.Max(1, max);
    }

    public int Capacity => _max;

    public int Count => _map.Count;

    /// <summary>The recency clock; exposed for tests.</summary>
    internal long Clock => Volatile.Read(ref _clock);

    private sealed class CacheEntry
    {
        private readonly Lazy<TValue> _value;
        public long LastAccess;

        public CacheEntry(Func<TValue> factory, long initialAccess)
        {
            _value = new Lazy<TValue>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
            LastAccess = initialAccess;
        }

        public TValue Value => _value.Value;
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
    {
        // Fast path: key already cached — touch and return
        if (_map.TryGetValue(key, out var existing))
        {
            Touch(existing);
            return existing.Value;
        }

        // Miss: create entry.  ConcurrentDictionary may discard our CacheEntry if
        // another thread wins the race; the Lazy inside the *kept* entry guarantees
        // the value factory runs exactly once regardless.
        //
        // Poisoned-Lazy note: if the factory throws, Lazy<T> with ExecutionAndPublication
        // caches the exception and re-throws it on every subsequent access to .Value for
        // that entry. The poisoned entry stays in the map until it is evicted by LRU.
        // Callers should ensure their factory does not throw for transient errors; use a
        // try/catch inside the factory and return a sentinel value if recovery is needed.
        // A miss advances the clock by 2 and stamps the entry with it: newer than any hit since
        // the previous miss (those stamp clock + 1), so insertion still counts as a use.
        var tick = Interlocked.Add(ref _clock, 2);
        var entry = _map.GetOrAdd(key, k => new CacheEntry(() => factory(k), tick));

        // Evict LRU entries until we are within capacity
        while (_map.Count > _max)
        {
            if (!EvictLeastRecentlyUsed())
            {
                break; // safety: nothing left to evict
            }
        }

        return entry.Value;
    }

    public bool TryGet(TKey key, out TValue v)
    {
        if (_map.TryGetValue(key, out var entry))
        {
            Touch(entry);
            v = entry.Value;
            return true;
        }

        v = default!;
        return false;
    }

    /// <summary>
    /// Marks <paramref name="entry"/> used since the last miss. Only a miss advances the clock, so
    /// a hit is two reads and, at most once per miss, one write to the entry — never a write to
    /// shared state. A per-hit <c>Interlocked.Increment</c> made every thread hitting the cache
    /// contend on one cache line (REV-057). Recency is therefore tracked per miss: entries hit
    /// since the last miss tie, and all outrank entries not hit since — eviction only happens on
    /// a miss, so that is the ordering it needs.
    /// </summary>
    private void Touch(CacheEntry entry)
    {
        var stamp = Volatile.Read(ref _clock) + 1;
        if (Volatile.Read(ref entry.LastAccess) < stamp)
        {
            Volatile.Write(ref entry.LastAccess, stamp);
        }
    }

    public void Clear()
    {
        _map.Clear();
        Interlocked.Exchange(ref _clock, 0);
    }

    /// <summary>
    /// Scans all entries for the one with the lowest LastAccess timestamp and removes it.
    /// Default cache sizes are small (32-512), so a linear scan is faster than maintaining a
    /// secondary data structure.
    /// </summary>
    /// <returns>True if an entry was successfully removed.</returns>
    private bool EvictLeastRecentlyUsed()
    {
        var found = false;
        TKey minKey = default!;
        var minAccess = long.MaxValue;

        foreach (var kv in _map)
        {
            var access = Volatile.Read(ref kv.Value.LastAccess);
            if (access < minAccess)
            {
                minAccess = access;
                minKey = kv.Key;
                found = true;
            }
        }

        return found && _map.TryRemove(minKey, out _);
    }
}