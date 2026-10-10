using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// What a caller sees when it hands the governor a token that is already cancelled. The asynchronous acquire
/// reports it as a cancelled task (<see cref="TaskCanceledException"/>); the blocking acquire throws a plain
/// <see cref="OperationCanceledException"/>. Callers up the stack pin those exact types, so the two acquires
/// sharing code must not change either, and neither may leave a permit, the turnstile or the writer interest
/// behind.
/// </summary>
public class PoolGovernorCancelledTokenContractTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static TheoryData<bool> WithAndWithoutTurnstile => new() { false, true };

    [Theory]
    [MemberData(nameof(WithAndWithoutTurnstile))]
    public async Task AsyncAcquire_WithACancelledToken_ThrowsTaskCanceledException_AndLeavesNothingBehind(bool withTurnstile)
    {
        using var rig = Rig.Create(withTurnstile);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await rig.Governor.AcquireAsync(cancelled.Token));

        rig.AssertNothingLeftBehind();
    }

    [Theory]
    [MemberData(nameof(WithAndWithoutTurnstile))]
    public void BlockingAcquire_WithACancelledToken_ThrowsOperationCanceledException_AndLeavesNothingBehind(bool withTurnstile)
    {
        using var rig = Rig.Create(withTurnstile);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => rig.Governor.Acquire(cancelled.Token));

        rig.AssertNothingLeftBehind();
    }

    // The slow path reads the clock first thing after the immediate probe has found no slot free, so a clock that
    // cancels the token on its first reading lands the cancellation exactly between the two probes.
    private sealed class CancellingClock : TimeProvider
    {
        private readonly CancellationTokenSource _source;
        private int _fired;

        internal CancellingClock(CancellationTokenSource source) => _source = source;

        public override long GetTimestamp()
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
            {
                _source.Cancel();
            }

            return Stopwatch.GetTimestamp();
        }
    }

    // A writer registers its interest on the shared turnstile while it tries; readers queue behind the turnstile only
    // while that interest is registered. A failed attempt that kept its interest would send every later reader
    // through the turnstile for good, so after each kind of failure a reader must still bypass a held turnstile.
    [Fact]
    public async Task AFailedWriterAttempt_LeavesNoWriterInterestBehind()
    {
        using var turnstile = new SemaphoreSlim(1, 1);
        using var cancellation = new CancellationTokenSource();
        using var writer = new PoolGovernor(PoolLabel.Writer, "interest-writer", 1, Timeout,
            turnstile: turnstile, holdTurnstile: true, timeProvider: new CancellingClock(cancellation));
        using var reader = new PoolGovernor(PoolLabel.Reader, "interest-reader", 1, TimeSpan.FromSeconds(2),
            turnstile: turnstile, holdTurnstile: false);

        using var alreadyCancelled = new CancellationTokenSource();
        alreadyCancelled.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await writer.AcquireAsync(alreadyCancelled.Token));
        Assert.Throws<OperationCanceledException>(() => writer.Acquire(alreadyCancelled.Token));
        AssertTheReaderBypassesAHeldTurnstile(turnstile, reader);

        // Cancelled while the attempt was queued behind a held slot: the clock cancels on its first reading.
        var held = writer.Acquire();
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await writer.AcquireAsync(cancellation.Token));
        held.Dispose();
        AssertTheReaderBypassesAHeldTurnstile(turnstile, reader);
    }

    private static void AssertTheReaderBypassesAHeldTurnstile(SemaphoreSlim turnstile, PoolGovernor reader)
    {
        Assert.True(turnstile.Wait(0), "The turnstile was left held.");
        try
        {
            var watch = Stopwatch.StartNew();
            reader.Acquire().Dispose();
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1),
                "The reader queued behind the turnstile: a failed writer attempt left its interest registered.");
        }
        finally
        {
            turnstile.Release();
        }
    }

    [Fact]
    public async Task AsyncAcquire_CancelledBetweenTheImmediateAndTheQueuedProbe_StillThrowsTaskCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        using var governor = new PoolGovernor(PoolLabel.Reader, "cancel-window", 1, Timeout,
            timeProvider: new CancellingClock(cancellation));
        var held = governor.Acquire();

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await governor.AcquireAsync(cancellation.Token));

        Assert.Equal(0, governor.QueueDepth);
        held.Dispose();
        Assert.Equal(1, governor.AvailablePermits);
    }

    [Fact]
    public void BlockingAcquire_CancelledBetweenTheImmediateAndTheQueuedProbe_ThrowsOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        using var governor = new PoolGovernor(PoolLabel.Reader, "cancel-window-sync", 1, Timeout,
            timeProvider: new CancellingClock(cancellation));
        var held = governor.Acquire();

        Assert.Throws<OperationCanceledException>(() => governor.Acquire(cancellation.Token));

        Assert.Equal(0, governor.QueueDepth);
        held.Dispose();
        Assert.Equal(1, governor.AvailablePermits);
    }

    private sealed class Rig : IDisposable
    {
        private readonly SemaphoreSlim? _turnstile;

        private Rig(PoolGovernor governor, SemaphoreSlim? turnstile)
        {
            Governor = governor;
            _turnstile = turnstile;
        }

        internal PoolGovernor Governor { get; }

        internal static Rig Create(bool withTurnstile)
        {
            if (!withTurnstile)
            {
                return new Rig(new PoolGovernor(PoolLabel.Reader, "cancelled-plain", 1, Timeout), null);
            }

            var turnstile = new SemaphoreSlim(1, 1);
            return new Rig(new PoolGovernor(PoolLabel.Writer, "cancelled-turnstile", 1, Timeout,
                turnstile: turnstile, holdTurnstile: true), turnstile);
        }

        internal void AssertNothingLeftBehind()
        {
            Assert.Equal(1, Governor.AvailablePermits);
            if (_turnstile != null)
            {
                Assert.True(_turnstile.Wait(0), "The turnstile was left held.");
                _turnstile.Release();
            }

            // The governor still works: nothing it registered for the failed attempt is left over.
            using (Governor.Acquire())
            {
                Assert.Equal(0, Governor.AvailablePermits);
            }

            Assert.Equal(1, Governor.AvailablePermits);
        }

        public void Dispose()
        {
            Governor.Dispose();
            _turnstile?.Dispose();
        }
    }
}
