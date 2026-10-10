using System;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PERF-024: every uncontended acquire made a new drain signal (a TaskCompletionSource and its
/// Task, about 96 B) and took a lock on acquire and on release, for WaitForDrainAsync, which runs
/// at shutdown. The signal is made by a drain waiter, so an acquire and release allocate only the
/// slot's token.
/// </summary>
[Collection("AllocationSerial")]
public class PoolGovernorAllocationTests
{
    [Fact]
    public async Task UncontendedAcquireAndRelease_AllocatesOnlyTheSlotToken()
    {
        using var governor = new PoolGovernor(PoolLabel.Writer, "alloc", 1, TimeSpan.FromSeconds(5));
        for (var i = 0; i < 100; i++)
        {
            var warm = await governor.AcquireAsync();
            warm.Dispose();
        }

        // The least of three passes: a one-off allocation on this thread while the full suite runs
        // inflates one pass, not three.
        var least = long.MaxValue;
        for (var pass = 0; pass < 3; pass++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                var slot = await governor.AcquireAsync();
                slot.Dispose();
            }

            least = Math.Min(least, (GC.GetAllocatedBytesForCurrentThread() - before) / 1000);
        }

        var budget = OptimizedBuild.Budget(releaseBytes: 48, debugExtraBytes: 256);
        Assert.True(least <= budget, $"{least} B per acquire and release; the budget is {budget} B (the slot token is 48 B)");
    }

    // The lock-free handshake: a release racing a waiter that is publishing its signal must still
    // wake it (a missed wake-up shows as a drain timeout).
    [Fact]
    public async Task ReleaseRacingADrainWaiter_AlwaysWakesIt()
    {
        using var governor = new PoolGovernor(PoolLabel.Writer, "drain-race", 1, TimeSpan.FromSeconds(5));
        for (var i = 0; i < 5_000; i++)
        {
            var slot = await governor.AcquireAsync();
            var waiter = Task.Run(() => governor.WaitForDrainAsync(TimeSpan.FromSeconds(5)));
            var release = Task.Run(() => slot.Dispose());
            await Task.WhenAll(waiter, release);
        }

        Assert.Equal(0, governor.GetSnapshot().InUse);
    }
}
