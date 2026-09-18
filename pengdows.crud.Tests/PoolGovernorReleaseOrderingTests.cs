using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Regression tests for three related PoolGovernor findings from the drain/dispose audit:
///
/// 1. ReleaseToken previously decremented _inUse BEFORE releasing the semaphore/turnstile, so a
///    concurrent WaitForDrainAsync (or its InUse==0 fast path) could observe "drained" and let a
///    caller safely Dispose() the governor while the releasing thread was still about to call
///    _semaphore.Release()/_turnstile.Release() on now-disposed objects.
/// 2. WaitForDrainAsync's stale-completed-signal branch looped via a bare `continue` with no
///    cancellation check and no yield — under a pathological interleaving this spins forever
///    ignoring even an already-cancelled token.
/// 3. Acquire/TryAcquire/AcquireAsync/TryAcquireAsync only checked ThrowIfClosed() once, before
///    doing any semaphore/turnstile work — a Close() landing in the narrow window between that
///    check and a slow-path Wait() completing let a straggler acquire succeed after close,
///    breaking WaitForDrainAsync's "once closed, _inUse can only decrease" guarantee.
/// </summary>
public class PoolGovernorReleaseOrderingTests
{
    private static readonly BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void ReleaseToken_ReleasesSemaphore_BeforeDecrementingInUse()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "order-check", 1, TimeSpan.FromSeconds(5));
        var slot = governor.Acquire();
        Assert.Equal(0, governor.AvailablePermits);

        var permitsObservedAtDecrementTime = -1;
        governor.TestOnlyBeforeInUseDecrement = () => permitsObservedAtDecrementTime = governor.AvailablePermits;

        slot.Dispose();

        Assert.Equal(1, permitsObservedAtDecrementTime);
    }

    [Fact]
    public async Task WaitForDrainAsync_StaleCompletedSignalWithInUseStillPositive_RespectsCancellationInsteadOfSpinning()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "stale-signal", 1, TimeSpan.FromSeconds(5));
        var slot = governor.Acquire(); // inUse = 1, held for the whole test

        // Force the drain signal into a completed state without actually draining, simulating the
        // transient staleness window called out in WaitForDrainAsync's own comments — but held
        // open indefinitely (nothing ever resets it) so the no-yield/no-cancellation-check bug in
        // the stale-signal branch becomes an observable hang instead of a microsecond race.
        var drainSignalField = typeof(PoolGovernor).GetField("_drainSignal", NonPublicInstance);
        var drainSignal = (TaskCompletionSource<bool>)drainSignalField!.GetValue(governor)!;
        drainSignal.TrySetResult(true);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var drainTask = governor.WaitForDrainAsync(null, cts.Token);
        var completed = await Task.WhenAny(drainTask, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.True(ReferenceEquals(drainTask, completed),
            "WaitForDrainAsync hung instead of observing the already-cancelled token " +
            "(stale-signal branch spun without checking cancellation).");

        await Assert.ThrowsAsync<OperationCanceledException>(() => drainTask);

        slot.Dispose();
    }

    [Fact]
    public void OnAcquired_WhenGovernorClosedBetweenCheckAndAcquire_GivesBackPermitAndThrows()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "toctou", 1, TimeSpan.FromSeconds(5));

        // Simulate the TOCTOU: a caller already passed ThrowIfClosed() and successfully acquired
        // the semaphore permit (Wait(0,...) returned true), and Close() ran in the narrow window
        // before OnAcquired's own bookkeeping executed.
        var semaphoreField = typeof(PoolGovernor).GetField("_semaphore", NonPublicInstance);
        var semaphore = (SemaphoreSlim)semaphoreField!.GetValue(governor)!;
        semaphore.Wait();

        governor.Close();

        var onAcquiredMethod = typeof(PoolGovernor).GetMethod("OnAcquired", NonPublicInstance);
        var ex = Assert.Throws<TargetInvocationException>(() =>
            onAcquiredMethod!.Invoke(governor, new object[] { 0L, false }));
        Assert.IsType<ObjectDisposedException>(ex.InnerException);

        // The permit must have been given back, not silently consumed by a rejected acquire.
        Assert.Equal(1, governor.AvailablePermits);
        Assert.Equal(0, governor.GetSnapshot().InUse);
    }
}
