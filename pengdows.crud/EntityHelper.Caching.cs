using System.Collections.Concurrent;
using System.Threading;

namespace pengdows.crud;

public partial class EntityHelper<TEntity, TRowID>
{
    private const int MaxCacheSize = 100;

    private static void TryAddWithLimit<TKey, TValue>(ConcurrentDictionary<TKey, TValue> cache, TKey key,
        TValue value, ConcurrentQueue<TKey> order) where TKey : notnull
    {
        // Use GetOrAdd to ensure we don't lose the value if already present
        if (cache.TryAdd(key, value))
        {
            // Only track order for new entries
            order.Enqueue(key);

            // Use queue-based FIFO eviction when limit is exceeded
            while (cache.Count > MaxCacheSize && order.TryDequeue(out var oldKey))
            {
                cache.TryRemove(oldKey, out _);
            }
        }
    }

    /// <summary>
    /// Thread-safe method to add reader plans with bounded cache size using atomic dictionary swap
    /// </summary>
    private void TryAddReaderPlanWithLimit(int hash, ColumnPlan[] plan)
    {
        // Use GetOrAdd to ensure we don't lose the value if already present
        _readerPlans.GetOrAdd(hash, plan);

        // If cache size exceeds limit, atomically swap to a new dictionary
        if (_readerPlans.Count > MaxReaderPlans)
        {
            var newCache = new ConcurrentDictionary<int, ColumnPlan[]>();
            newCache.TryAdd(hash, plan);
            // Atomic swap - other threads will start using the new cache
            Interlocked.Exchange(ref _readerPlans, newCache);
        }
    }

    public void ClearCaches()
    {
        _readerConverters.Clear();
        _readerConvertersOrder.Clear();
        _readerPlans.Clear();
        _columnListCache.Clear();
        _columnListCacheOrder.Clear();
        _queryCache.Clear();
        _queryCacheOrder.Clear();
        _whereParameterNames.Clear();
        _whereParameterNamesOrder.Clear();
    }
}
