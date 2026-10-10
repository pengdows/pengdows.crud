using CrudBenchmarks;
using pengdows.crud.enums;

namespace CrudBenchmarks.Tests;

// The multi-slot synchronous storm sends more threads than the pool has slots through the library's blocking
// BeginTransaction, which is the path the gate's blocking waiter serves. It runs against a real server in the
// benchmark; these tests run the same workload against fakeDb to hold it to account without Docker.
public class SyncPoolStormShapeTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void TheStorm_HonoursTheConfiguredSlotCount_AndCommitsEveryTransaction(int slots)
    {
        using var storm = SyncPoolStorm.OnFakeDb(slots);

        var committed = storm.Run(threads: 12, transactionsPerThread: 30, holdMicroseconds: 200, thinkMicroseconds: 0);

        Assert.Equal(12 * 30, committed);
        var stats = storm.Context.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        Assert.Equal(slots, stats.MaxSlots);
        // Measured inside the transactions: the governor's own PeakInUse can transiently exceed the slot count,
        // because a release frees its permit before it decrements the in-use counter.
        Assert.InRange(storm.PeakConcurrentTransactions, 1, slots);
        Assert.Equal(12 * 30, stats.TotalAcquired);
    }

    [Fact]
    public void MoreThreadsThanSlots_MakesSynchronousCallersActuallyWait()
    {
        using var storm = SyncPoolStorm.OnFakeDb(slots: 2);

        storm.Run(threads: 12, transactionsPerThread: 30, holdMicroseconds: 200, thinkMicroseconds: 0);

        // A storm that never queued would measure the fast path and call it contention.
        Assert.True(storm.Context.GetPoolStatisticsSnapshot(PoolLabel.Reader).TotalWaits > 0,
            "No synchronous caller ever had to wait for a slot.");
    }

    [Fact]
    public void TheStorm_LeavesNothingInUseAndNothingQueued()
    {
        using var storm = SyncPoolStorm.OnFakeDb(slots: 2);

        storm.Run(threads: 8, transactionsPerThread: 20, holdMicroseconds: 100, thinkMicroseconds: 20);

        var stats = storm.Context.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        Assert.Equal(0, stats.InUse);
        Assert.Equal(0, stats.Queued);
    }
}
