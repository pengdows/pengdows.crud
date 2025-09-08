using System.Collections.Concurrent;
using System.Threading;

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
            var count = Interlocked.Increment(ref _count);
            while (count > _max && _order.TryDequeue(out var old))
            {
                if (_map.TryRemove(old, out _))
                {
                    count = Interlocked.Decrement(ref _count);
                }
                else
                {
                    count = Volatile.Read(ref _count);
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

