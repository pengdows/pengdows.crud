using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.exceptions;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PoolGovernorTests
{
    [Fact]
    public async Task AcquireAsync_WhenCapacityAvailable_TracksStats()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 2, TimeSpan.FromMilliseconds(100), trackMetrics: true);

        await using var slot = await governor.AcquireAsync();

        var snapshot = governor.GetSnapshot();
        Assert.Equal(2, snapshot.MaxSlots);
        Assert.Equal(1, snapshot.InUse);
        Assert.Equal(1, snapshot.PeakInUse);
        Assert.Equal(0, snapshot.Queued);
        Assert.Equal(1, snapshot.TotalAcquired);
    }

    [Fact]
    public async Task AcquireAsync_WhenContended_QueuesAndCompletes()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 1, TimeSpan.FromSeconds(5), trackMetrics: true);
        await using var first = await governor.AcquireAsync();

        var waiter = governor.AcquireAsync();
        await Task.Delay(50);

        var queuedSnapshot = governor.GetSnapshot();
        Assert.True(queuedSnapshot.Queued >= 1);

        await first.DisposeAsync();
        await using var second = await waiter;

        var finalSnapshot = governor.GetSnapshot();
        Assert.Equal(0, finalSnapshot.Queued);
        Assert.Equal(2, finalSnapshot.TotalAcquired);
    }

    [Fact]
    public async Task AcquireAsync_WhenTimeout_ThrowsPoolSaturatedException()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 1, TimeSpan.FromMilliseconds(25), trackMetrics: true);
        await using var slot = await governor.AcquireAsync();

        var ex = await Assert.ThrowsAsync<PoolSaturatedException>(async () => await governor.AcquireAsync());
        Assert.Equal(PoolLabel.Reader, ex.PoolLabel);
        Assert.Equal("reader-key", ex.PoolKeyHash);
        Assert.True(ex.Snapshot.TotalSlotTimeouts >= 1);
    }

    [Fact]
    public async Task SlotDispose_ReleasesCapacity()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 1, TimeSpan.FromMilliseconds(100), trackMetrics: true);
        await using (await governor.AcquireAsync())
        {
        }

        var snapshot = governor.GetSnapshot();
        Assert.Equal(0, snapshot.InUse);

        await using var second = await governor.AcquireAsync();
        var after = governor.GetSnapshot();
        Assert.Equal(1, after.InUse);
    }

    [Fact]
    public async Task AcquireAsync_WhenTimeout_DoesNotPolluteTotalWaitAndHoldTicks()
    {
        // Slot saturation: fill pool, then let a second acquire time out.
        // Hold/wait ticks must only reflect successful slot acquisitions, not failed ones.
        var governor = new PoolGovernor(PoolLabel.Writer, "writer-key", 1,
            TimeSpan.FromMilliseconds(25), trackMetrics: true);

        // First acquire succeeds immediately (no contention yet)
        await using var first = await governor.AcquireAsync();

        // Second acquire times out — should NOT update wait/hold ticks
        await Assert.ThrowsAsync<PoolSaturatedException>(async () => await governor.AcquireAsync());

        // Check metrics BEFORE releasing first slot (ReleaseToken records hold on dispose)
        // If the timed-out attempt had called RecordWaitAndHold, these would be non-zero
        var snapshot = governor.GetSnapshot();
        Assert.Equal(1, snapshot.TotalSlotTimeouts);  // timeout was counted
        Assert.Equal(0, snapshot.TotalHoldTicks);     // no hold recorded yet (first still held)
        Assert.Equal(0, snapshot.TotalWaitTicks);     // first was acquired with no wait
        Assert.Equal(1, snapshot.TotalAcquired);      // only one successful acquisition
    }

    [Fact]
    public void Acquire_WhenTimeout_DoesNotPolluteTotalWaitAndHoldTicks()
    {
        // Same as async variant but exercises the synchronous Acquire() code path
        var governor = new PoolGovernor(PoolLabel.Writer, "writer-key", 1,
            TimeSpan.FromMilliseconds(25), trackMetrics: true);

        using var first = governor.Acquire();

        Assert.Throws<PoolSaturatedException>(() => governor.Acquire());

        var snapshot = governor.GetSnapshot();
        Assert.Equal(1, snapshot.TotalSlotTimeouts);
        Assert.Equal(0, snapshot.TotalHoldTicks);
        Assert.Equal(0, snapshot.TotalWaitTicks);
        Assert.Equal(1, snapshot.TotalAcquired);
    }

    [Fact]
    public async Task AcquireAsync_SaturationThenRecovery_InUseReturnsToZero()
    {
        // Fill pool, exhaust it with a timeout, release all slots, prove recovery.
        // This is the "no death spiral" proof: the governor must be fully re-entrant
        // after saturating, with InUse and Queued both returning to 0.
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 2,
            TimeSpan.FromMilliseconds(25), trackMetrics: true);

        await using var first = await governor.AcquireAsync();
        await using var second = await governor.AcquireAsync();

        // Pool is full — a third acquisition must timeout
        var ex = await Assert.ThrowsAsync<PoolSaturatedException>(() => governor.AcquireAsync().AsTask());
        Assert.Equal(2, ex.Snapshot.InUse);
        Assert.Equal(1, governor.GetSnapshot().TotalSlotTimeouts);

        // Release both slots
        await first.DisposeAsync();
        await second.DisposeAsync();

        var afterRelease = governor.GetSnapshot();
        Assert.Equal(0, afterRelease.InUse);
        Assert.Equal(0, afterRelease.Queued);

        // Governor must accept new acquisitions immediately — no residual saturation state
        await using var recovered = await governor.AcquireAsync();
        Assert.Equal(1, governor.GetSnapshot().InUse);
        Assert.Equal(3, governor.GetSnapshot().TotalAcquired); // first + second + recovered
    }

    [Fact]
    public async Task AcquireAsync_WhenCanceled_IncrementsCanceledWaitsAndLeavesNoLeak()
    {
        // Cancellation must increment TotalCanceledWaits and must NOT leak the slot.
        // After cancel: InUse stays at 1 (the held slot), Queued returns to 0.
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 1,
            TimeSpan.FromSeconds(5), trackMetrics: true);

        // Hold the only slot
        await using var first = await governor.AcquireAsync();

        // Queue a second acquire — will block because pool is full
        using var cts = new CancellationTokenSource();
        var waiter = governor.AcquireAsync(cts.Token);

        // Give the waiter time to enter the semaphore queue
        await Task.Delay(50);
        Assert.True(governor.GetSnapshot().Queued >= 1);

        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await waiter);

        var snapshot = governor.GetSnapshot();
        Assert.Equal(1, snapshot.TotalCanceledWaits);    // cancellation, not timeout
        Assert.Equal(0, snapshot.TotalSlotTimeouts);      // no timeout fired
        Assert.Equal(0, snapshot.Queued);                 // waiter dequeued on cancel
        Assert.Equal(1, snapshot.InUse);                  // first still held
        Assert.Equal(0, snapshot.TotalHoldTicks);         // hold ticks: first still active, none released yet

        // Release the held slot — InUse must drop to 0
        await first.DisposeAsync();
        Assert.Equal(0, governor.GetSnapshot().InUse);
    }
}
