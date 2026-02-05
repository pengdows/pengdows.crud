// =============================================================================
// FILE: BoundedCache.cs
// PURPOSE: Thread-safe bounded LRU cache with automatic eviction.
//
// AI SUMMARY:
// - Generic bounded cache with configurable max size.
// - Thread-safe: uses ConcurrentDictionary with per-entry Lazy and LRU timestamps.
// - LRU eviction: least-recently-accessed entries removed when capacity exceeded.
// - Key methods:
//   * GetOrAdd(key, factory) - retrieves or creates entry; factory runs at most once per key
//   * TryGet(key, out value) - retrieves and touches entry for LRU ordering
//   * Clear() - removes all entries
// - Lazy<TValue> with ExecutionAndPublication ensures the value factory executes
//   exactly once per key, even when multiple threads race on the same missing key.
// - Eviction is a linear scan for the entry with the lowest access timestamp;
//   cache sizes are 32-512, so this is sub-microsecond.
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
            Volatile.Write(ref existing.LastAccess, Interlocked.Increment(ref _clock));
            return existing.Value;
        }

        // Miss: create entry.  ConcurrentDictionary may discard our CacheEntry if
        // another thread wins the race; the Lazy inside the *kept* entry guarantees
        // the value factory runs exactly once regardless.
        var tick = Interlocked.Increment(ref _clock);
        var entry = _map.GetOrAdd(key, k => new CacheEntry(() => factory(k), tick));

        // Touch the returned entry (might be one another thread inserted)
        Volatile.Write(ref entry.LastAccess, Interlocked.Increment(ref _clock));

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
            Volatile.Write(ref entry.LastAccess, Interlocked.Increment(ref _clock));
            v = entry.Value;
            return true;
        }

        v = default!;
        return false;
    }

    public void Clear()
    {
        _map.Clear();
        Interlocked.Exchange(ref _clock, 0);
    }

    /// <summary>
    /// Scans all entries for the one with the lowest LastAccess timestamp and removes it.
    /// Cache sizes are small (32-512), so a linear scan is faster than maintaining a
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
