using CrudBenchmarks;
using pengdows.crud.enums;

namespace CrudBenchmarks.Tests;

// The synchronous write storm exists to exercise the gate's blocking-waiter path through the real library:
// many threads, one writer slot, short transactions, the synchronous API. A storm that never queued, lost a
// write, or went through the async path would measure something else, so the harness is held to account.
public class SQLiteSyncWriteStormBenchmarkShapeTests
{
    [Fact]
    public void TheStorm_CommitsEveryTransaction_AndEveryWriteLandsOnce()
    {
        using var storm = SQLiteSyncWriteStormBenchmarks.CreateStorm();

        var committed = storm.Run(threads: 8, transactionsPerThread: 50, thinkMicroseconds: 0);

        Assert.Equal(8 * 50, committed);
        Assert.Equal(8 * 50, storm.WritesRecorded());
    }

    [Fact]
    public void TheStorm_WithOneWriterSlot_MakesSynchronousCallersActuallyWait()
    {
        using var storm = SQLiteSyncWriteStormBenchmarks.CreateStorm();

        storm.Run(threads: 8, transactionsPerThread: 100, thinkMicroseconds: 0);

        // Proves the blocking path ran: a storm that never queued would measure the fast path.
        Assert.True(storm.Context.GetPoolStatisticsSnapshot(PoolLabel.Writer).TotalWaits > 0,
            "No synchronous writer ever had to wait for the single writer slot.");
    }

    [Fact]
    public void TheStorm_LeavesTheWriterPoolEmptyAndNothingQueued()
    {
        using var storm = SQLiteSyncWriteStormBenchmarks.CreateStorm();

        storm.Run(threads: 8, transactionsPerThread: 50, thinkMicroseconds: 20);

        var stats = storm.Context.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.Equal(0, stats.InUse);
        Assert.Equal(0, stats.Queued);
    }
}
