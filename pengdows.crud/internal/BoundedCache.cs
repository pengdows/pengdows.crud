// =============================================================================
// FILE: BoundedCache.cs
// PURPOSE: Thread-safe bounded LRU-style cache with automatic eviction.
//
// AI SUMMARY:
// - Generic bounded cache with configurable max size.
// - Thread-safe: uses ConcurrentDictionary + ConcurrentQueue.
// - FIFO eviction: oldest entries removed when capacity exceeded.
// - Key methods:
//   * GetOrAdd(key, factory) - retrieves or creates entry
//   * TryGet(key, out value) - retrieves without creating
//   * Clear() - removes all entries
// - Uses Interlocked for thread-safe count tracking.
// - Used internally for caching compiled accessors, type info, etc.
// - Eviction happens inline during GetOrAdd, not in background.
// =============================================================================

using System.Collections.Concurrent;

namespace pengdows.crud.@internal;

internal sealed class BoundedCache<TKey, TValue> where TKey : notnull
{
    private readonly int _max;
    private readonly ConcurrentDictionary<TKey, TValue> _map = new();
    private readonly ConcurrentQueue<TKey> _order = new();
    private int _count;

    public BoundedCache(int max)
    {
        _max = Math.Max(1, max);
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
    {
        if (_map.TryGetValue(key, out var v))
        {
            return v;
        }

        var created = factory(key);
        if (_map.TryAdd(key, created))
        {
            _order.Enqueue(key);
            Interlocked.Increment(ref _count);

            // Evict entries if we're over the limit
            while (Volatile.Read(ref _count) > _max && _order.TryDequeue(out var old))
            {
                if (_map.TryRemove(old, out _))
                {
                    Interlocked.Decrement(ref _count);
                }
            }
        }

        return created;
    }

    public bool TryGet(TKey key, out TValue v)
    {
        return _map.TryGetValue(key, out v!);
    }

    public void Clear()
    {
        while (_order.TryDequeue(out _))
        {
        }

        _map.Clear();
        Interlocked.Exchange(ref _count, 0);
    }
}