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

    // REV-057: every hit used to Interlocked.Increment the shared clock and write the entry's
    // timestamp, so threads hitting the cache serialized on one cache line (WrapObjectName went
    // 15 -> 286 ns at 8 threads). Only a miss advances the clock now; a hit stamps its entry at
    // most once per miss, so repeated hits write nothing.
    [Fact]
    public void RepeatedHits_DoNotAdvanceTheSharedClock()
    {
        var cache = new BoundedCache<int, string>(4);
        cache.GetOrAdd(1, _ => "a");
        cache.GetOrAdd(2, _ => "b");
        cache.TryGet(1, out _);
        var clock = cache.Clock;

        for (var i = 0; i < 1000; i++)
        {
            cache.GetOrAdd(1, _ => "x");
            cache.TryGet(2, out _);
        }

        Assert.Equal(clock, cache.Clock);
    }

    [Fact]
    public void HitAfterAMiss_StillOutranksEntriesNotTouchedSince()
    {
        // cap=3: 1,2 then hits on both, then 3 (a miss), then a hit on 1 only; adding 4 must
        // evict 2 — touched before the last miss — not 1.
        var cache = new BoundedCache<int, string>(3);
        cache.GetOrAdd(1, _ => "a");
        cache.GetOrAdd(2, _ => "b");
        cache.TryGet(1, out _);
        cache.TryGet(2, out _);
        cache.GetOrAdd(3, _ => "c");
        cache.TryGet(1, out _);
        cache.TryGet(3, out _);

        cache.GetOrAdd(4, _ => "d");

        Assert.True(cache.TryGet(1, out _));
        Assert.False(cache.TryGet(2, out _));
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