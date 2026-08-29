using System;
using System.Linq;
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

    // CORE-027: PoolGovernor previously had no admission-closed state at all. Dispose()
    // unconditionally tore down the semaphore with no guard against a concurrent Acquire, and
    // WaitForDrainAsync only watched _inUse — nothing stopped new work from being admitted while
    // draining, so a busy pool could never actually finish draining, and Dispose() racing an
    // in-flight Acquire could throw ObjectDisposedException at the acquirer instead of failing
    // predictably. Close() gives the governor an explicit, checked terminal state.
    [Fact]
    public void Close_ThenAcquire_ThrowsObjectDisposedException()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 2, TimeSpan.FromSeconds(5));
        governor.Close();

        Assert.Throws<ObjectDisposedException>(() => governor.Acquire());
    }

    [Fact]
    public void Close_ThenTryAcquire_ThrowsObjectDisposedException()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 2, TimeSpan.FromSeconds(5));
        governor.Close();

        Assert.Throws<ObjectDisposedException>(() => governor.TryAcquire(out _));
    }

    [Fact]
    public async Task Close_ThenAcquireAsync_ThrowsObjectDisposedException()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 2, TimeSpan.FromSeconds(5));
        governor.Close();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await governor.AcquireAsync());
    }

    [Fact]
    public async Task Close_ThenTryAcquireAsync_ThrowsObjectDisposedException()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 2, TimeSpan.FromSeconds(5));
        governor.Close();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await governor.TryAcquireAsync());
    }

    [Fact]
    public async Task Close_PreventsNewAdmission_SoWaitForDrainAsyncActuallyCompletes()
    {
        // Before Close() existed, a steady stream of new acquires could keep _inUse above zero
        // forever, so WaitForDrainAsync had no way to guarantee progress toward completion.
        var governor = new PoolGovernor(PoolLabel.Writer, "writer-key", 1, TimeSpan.FromSeconds(5));
        var slot = await governor.AcquireAsync();

        governor.Close();

        // New admission must be rejected once closed, even though a slot is still held.
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await governor.AcquireAsync());

        var drainTask = governor.WaitForDrainAsync(TimeSpan.FromSeconds(5));
        Assert.False(drainTask.IsCompleted);

        await slot.DisposeAsync();

        await drainTask;
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 1, TimeSpan.FromSeconds(5));

        governor.Dispose();
        var ex = Record.Exception(() => governor.Dispose());

        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_ClosesAdmission()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 1, TimeSpan.FromSeconds(5));

        governor.Dispose();

        Assert.Throws<ObjectDisposedException>(() => governor.Acquire());
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

    // Regression: before the ReleaseToken release-before-drain fix, calling Dispose()
    // concurrently with the last slot release caused ObjectDisposedException because
    // ReleaseToken signaled drain-waiters BEFORE calling _semaphore.Release().
    // WaitForDrainAsync would unblock DisposeAsync, which disposed the semaphore,
    // and then the still-in-flight _semaphore.Release() threw.
    //
    // After the fix: _semaphore.Release() executes BEFORE the drain signal is set,
    // so DisposeAsync never sees the semaphore in a partially-released state.
    [Fact]
    public async Task Dispose_ConcurrentWithLastSlotRelease_DoesNotThrow()
    {
        var governor = new PoolGovernor(PoolLabel.Writer, "writer-key", 1,
            TimeSpan.FromSeconds(5), trackMetrics: true);

        var slot = await governor.AcquireAsync();

        // Race: WaitForDrainAsync blocks until InUse hits 0. Once it does, Dispose()
        // is called. ReleaseToken must have already called _semaphore.Release() by
        // then — otherwise the semaphore is disposed mid-Release().
        var disposeTask = Task.Run(async () =>
        {
            await governor.WaitForDrainAsync(TimeSpan.FromSeconds(5));
            governor.Dispose();
        });

        // Small delay so the drain waiter is parked before we release.
        await Task.Delay(20);

        // Release the slot — triggers ReleaseToken, which must not throw.
        await slot.DisposeAsync();

        // If ObjectDisposedException propagated, this will rethrow it.
        await disposeTask;
    }

    // Regression: today, waiters beyond maxSlots pile up on the semaphore unbounded —
    // the only shedding mechanism is the acquire-timeout deadline. This proves a
    // (cap+1)th waiter is rejected immediately once the queue depth cap is hit,
    // rather than waiting out the (deliberately long) acquire timeout.
    [Fact]
    public async Task AcquireAsync_WhenQueueDepthExceeded_ThrowsImmediately_NotAfterFullTimeout()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "queue-cap-key", 1,
            TimeSpan.FromSeconds(30), trackMetrics: false); // metrics OFF on purpose — must still protect.

        var held = await governor.AcquireAsync(); // occupies the only slot indefinitely
        using var cts = new CancellationTokenSource();

        var cap = governor.MaxQueueDepth;
        var blockedWaiters = new Task[cap];
        for (var i = 0; i < cap; i++)
        {
            blockedWaiters[i] = governor.AcquireAsync(cts.Token).AsTask();
        }

        try
        {
            // Give the queued waiters a moment to actually register as queued before
            // sending the one that should be rejected immediately.
            await Task.Delay(50);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Assert.ThrowsAsync<PoolSaturatedException>(async () => await governor.AcquireAsync());
            sw.Stop();

            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
                $"Expected immediate rejection, took {sw.Elapsed}");
        }
        finally
        {
            // Unblock and clean up the queued waiters so nothing lingers past this test.
            cts.Cancel();
            await held.DisposeAsync();
            await Task.WhenAll(blockedWaiters.Select(async t =>
            {
                try { await t; } catch (OperationCanceledException) { }
            }));
        }
    }
}
