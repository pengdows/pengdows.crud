using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public class BoundedCacheLruTests
{
    [Fact]
    public void EvictsLeastRecentlyUsed_NotOldestInserted()
    {
        // cap=2: add 1, add 2, touch 1 via TryGet, add 3.
        // LRU evicts 2 (least recently used).  FIFO would evict 1 (oldest inserted).
        var cache = new BoundedCache<int, string>(2);
        cache.GetOrAdd(1, _ => "a");
        cache.GetOrAdd(2, _ => "b");

        // Touch entry 1 — it becomes most-recently-used
        Assert.True(cache.TryGet(1, out _));

        // Adding 3 pushes count to 3; eviction should drop entry 2
        cache.GetOrAdd(3, _ => "c");

        Assert.True(cache.TryGet(1, out _), "Entry 1 was touched and should survive eviction");
        Assert.False(cache.TryGet(2, out _), "Entry 2 is LRU and should be evicted");
        Assert.True(cache.TryGet(3, out _), "Entry 3 was just added");
    }

    [Fact]
    public void TryGet_TouchesEntryForLruOrdering()
    {
        // cap=3: add 1,2,3, touch 1, add 4.
        // LRU evicts 2 (oldest access).  FIFO would evict 1 (oldest insert).
        var cache = new BoundedCache<int, string>(3);
        cache.GetOrAdd(1, _ => "a");
        cache.GetOrAdd(2, _ => "b");
        cache.GetOrAdd(3, _ => "c");

        // Touch entry 1
        Assert.True(cache.TryGet(1, out _));

        // Adding 4 overflows; LRU is entry 2
        cache.GetOrAdd(4, _ => "d");

        Assert.True(cache.TryGet(1, out _), "Entry 1 was touched and should survive eviction");
        Assert.False(cache.TryGet(2, out _), "Entry 2 is LRU and should be evicted");
        Assert.True(cache.TryGet(3, out _), "Entry 3 should still be present");
        Assert.True(cache.TryGet(4, out _), "Entry 4 was just added");
    }

    [Fact]
    public async Task GetOrAdd_ConcurrentRace_FactoryCalledOnce()
    {
        // 16 threads race GetOrAdd on the same key.  With Lazy<T> (ExecutionAndPublication)
        // the value factory runs exactly once.  Without it (current code calls factory
        // before TryAdd) every racing thread invokes the factory.
        var cache = new BoundedCache<int, string>(32);
        var counter = 0;
        var barrier = new Barrier(16);

        var tasks = new Task[16];
        for (var i = 0; i < 16; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                barrier.SignalAndWait();
                cache.GetOrAdd(42, _ =>
                {
                    Interlocked.Increment(ref counter);
                    return "value";
                });
            });
        }

        await Task.WhenAll(tasks);

        Assert.Equal(1, counter);
    }
}
