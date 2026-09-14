using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The synchronous <see cref="PoolGovernor.Acquire"/> entry point duplicates
/// <see cref="PoolGovernor.AcquireAsync"/>'s admission-control logic (fast path, turnstile
/// gating, queue-depth caps, slow-path timeout) as its own separate method body — not a thin
/// wrapper over the async version. The existing test suite's queue-depth/turnstile-fairness
/// coverage exercises almost all of it exclusively through <c>AcquireAsync</c> (or, for the
/// turnstile-side queue cap specifically, via <c>Acquire</c> on a background thread only for the
/// "already saturated" case) — leaving the sync method's own successful-after-contention paths
/// unverified independently of its async twin. These tests close that gap directly.
/// </summary>
public sealed class PoolGovernorSyncAcquireTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Directly bumps the shared per-turnstile WritersActiveOrWaiting counter (see
    /// PoolGovernor.TurnstileState, reached via the private static SharedTurnstileStates table) so
    /// a reader governor sharing this turnstile sees ShouldUseTurnstileGate return true, without
    /// needing a real writer thread that would otherwise also have to be holding/contending for
    /// the turnstile itself at that exact instant — see the remarks on
    /// Acquire_ReaderFastPath_WithWriterInterestAlreadyRegistered_ReleasesTurnstileImmediately for
    /// why a real writer can't reliably produce this precondition. Returns an action that restores
    /// the counter to zero.
    /// </summary>
    private static Action SimulateWriterInterestRegistered(SemaphoreSlim turnstile)
    {
        var sharedStatesField = typeof(PoolGovernor).GetField("SharedTurnstileStates", BindingFlags.NonPublic | BindingFlags.Static)!;
        var sharedStates = sharedStatesField.GetValue(null)!;
        var tryGetValue = sharedStates.GetType().GetMethod("TryGetValue")!;
        var args = new object?[] { turnstile, null };
        var found = (bool)tryGetValue.Invoke(sharedStates, args)!;
        if (!found)
        {
            throw new InvalidOperationException(
                "No TurnstileState registered for this turnstile yet — construct at least one " +
                "PoolGovernor against it first (its constructor materializes the shared entry).");
        }

        var turnstileState = args[1]!;
        var field = turnstileState.GetType().GetField("WritersActiveOrWaiting", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(turnstileState, 1L);
        return () => field.SetValue(turnstileState, 0L);
    }

    [Fact]
    public void Constructor_NegativeMaxQueueDepth_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PoolGovernor(PoolLabel.Reader, "neg-qd", 1, TimeSpan.FromMilliseconds(10), maxQueueDepth: -1));
    }

    [Fact]
    public void Forbidden_Property_ReturnsTrueForForbiddenGovernor_AndFalseOtherwise()
    {
        var forbidden = new PoolGovernor(PoolLabel.Writer, "forbidden-key", 1,
            TimeSpan.FromMilliseconds(10), forbidden: true);
        var normal = new PoolGovernor(PoolLabel.Writer, "normal-key", 1, TimeSpan.FromMilliseconds(10));

        Assert.True(forbidden.Forbidden);
        Assert.False(normal.Forbidden);
    }

    // Mirrors PoolGovernorTests.AcquireAsync_WhenQueueDepthExceeded_ThrowsImmediately_
    // NotAfterFullTimeout, but drives the SYNCHRONOUS Acquire() method instead — that test only
    // ever proved the async path fast-fails on a saturated queue; Acquire()'s own independent
    // queue-depth check (a separate code path, not a wrapper over AcquireAsync) had no direct
    // coverage of its own.
    [Fact]
    public async Task Acquire_WhenSemaphoreQueueDepthExceeded_ThrowsImmediately_NotAfterFullTimeout()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "sync-queue-cap-key", 1,
            TimeSpan.FromSeconds(30), trackMetrics: true);

        using var held = governor.Acquire(); // occupies the only slot indefinitely
        using var cts = new CancellationTokenSource();

        var cap = governor.MaxQueueDepth;
        var blockedThreads = new Thread[cap];
        for (var i = 0; i < cap; i++)
        {
            var thread = new Thread(() =>
            {
                try { governor.Acquire(cts.Token); }
                catch (OperationCanceledException) { /* expected once we cancel during cleanup */ }
            })
            { IsBackground = true };
            thread.Start();
            blockedThreads[i] = thread;
        }

        try
        {
            await WaitUntilAsync(() => governor.GetSnapshot().Queued >= cap);

            var sw = Stopwatch.StartNew();
            Assert.Throws<PoolSaturatedException>(() => governor.Acquire());
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 1000,
                $"Expected an immediate PoolSaturatedException from Acquire()'s own queue-depth " +
                $"admission control, but it took {sw.ElapsedMilliseconds}ms.");
        }
        finally
        {
            cts.Cancel();
            foreach (var thread in blockedThreads)
            {
                thread.Join(TimeSpan.FromSeconds(5));
            }
        }
    }

    // Exercises Acquire()'s slow path end-to-end on a genuinely contended (but not saturated)
    // turnstile: the reader's very first, non-blocking turnstile probe fails (writer holds it),
    // forcing entry into the timed slow-path wait; once the writer releases, the reader (a) wins
    // the turnstile, (b) immediately releases it again since readers don't hold turnstiles, and
    // (c) finds its own semaphore free the whole time, succeeding on that immediate re-check
    // rather than needing a timed semaphore wait too.
    [Fact]
    public async Task Acquire_ReaderWaitsOutBusyTurnstileThenSucceeds_ReleasesTurnstileAndAcquiresFreeSlotImmediately()
    {
        using var turnstile = new SemaphoreSlim(1, 1);
        using var writer = new PoolGovernor(PoolLabel.Writer, "slow-path-w", 1,
            TimeSpan.FromSeconds(30), turnstile: turnstile, holdTurnstile: true);
        using var reader = new PoolGovernor(PoolLabel.Reader, "slow-path-r", 5,
            TimeSpan.FromSeconds(10), trackMetrics: true, turnstile: turnstile, holdTurnstile: false);

        // Writer takes the turnstile and holds it (its slot), forcing the reader's own initial
        // non-blocking turnstile probe to fail and fall into the timed slow-path wait.
        var writerSlot = writer.Acquire();

        var readerResult = default(PoolSlot);
        var readerThread = new Thread(() => { readerResult = reader.Acquire(); }) { IsBackground = true };
        readerThread.Start();

        // Deterministically confirm the reader actually reached the turnstile's slow-path wait
        // (registered in the turnstile queue) before releasing the writer — otherwise this could
        // pass trivially via the reader's own fast path instead of the slow path this test targets.
        await WaitUntilAsync(() => reader.GetSnapshot().TurnstileQueued >= 1);
        Assert.Equal(1, reader.GetSnapshot().TurnstileQueued);

        writerSlot.Dispose();

        Assert.True(readerThread.Join(TimeSpan.FromSeconds(10)),
            "Reader never completed after the writer released the turnstile.");

        try
        {
            Assert.Equal(1, reader.GetSnapshot().InUse);
            // The turnstile itself must be free again — the reader touch-and-released it rather
            // than holding it (that's the writer-only behavior).
            Assert.True(turnstile.Wait(0), "Turnstile should be free after the reader's touch-and-release.");
            turnstile.Release();
        }
        finally
        {
            readerResult.Dispose();
        }
    }

    // The reader-side fast path (an immediate, non-blocking turnstile grab-then-release before an
    // equally immediate free-slot grab) requires the turnstile to be free at the exact instant a
    // writer sharing the same turnstile has *already* registered interest but has not yet (or no
    // longer) actually holds the turnstile permit itself. A real writer's own Acquire() call
    // registers interest and attempts the turnstile essentially back-to-back with no yield point
    // in between, so this precondition cannot be constructed reliably from two real, independently
    // scheduled threads without flaking. The shared per-turnstile interest counter is private
    // implementation state (PoolGovernor.TurnstileState, reached via the private static
    // SharedTurnstileStates table) specifically so it can be manipulated directly here to
    // deterministically simulate that window, rather than leaving this branch's behavior
    // completely unverified.
    [Fact]
    public void Acquire_ReaderFastPath_WithWriterInterestAlreadyRegistered_ReleasesTurnstileImmediately()
    {
        using var turnstile = new SemaphoreSlim(1, 1);
        using var writer = new PoolGovernor(PoolLabel.Writer, "fastpath-w", 1,
            TimeSpan.FromSeconds(10), turnstile: turnstile, holdTurnstile: true);
        using var reader = new PoolGovernor(PoolLabel.Reader, "fastpath-r", 1,
            TimeSpan.FromSeconds(10), turnstile: turnstile, holdTurnstile: false);

        var restore = SimulateWriterInterestRegistered(turnstile);
        try
        {
            // Turnstile is genuinely free (writer never actually acquired it) — the reader's fast
            // path should win the immediate Wait(0), then release again immediately since readers
            // never hold turnstiles, then win its own free semaphore slot immediately too.
            Assert.Equal(1, turnstile.CurrentCount);

            using var readerSlot = reader.Acquire();

            Assert.Equal(1, reader.GetSnapshot().InUse);
            Assert.Equal(1, turnstile.CurrentCount); // still free — reader touched and released it
        }
        finally
        {
            restore();
        }
    }

    [Fact]
    public void TryAcquire_ReaderFastPath_WithWriterInterestAlreadyRegistered_ReleasesTurnstileImmediately()
    {
        using var turnstile = new SemaphoreSlim(1, 1);
        using var writer = new PoolGovernor(PoolLabel.Writer, "tryfastpath-w", 1,
            TimeSpan.FromSeconds(10), turnstile: turnstile, holdTurnstile: true);
        using var reader = new PoolGovernor(PoolLabel.Reader, "tryfastpath-r", 1,
            TimeSpan.FromSeconds(10), turnstile: turnstile, holdTurnstile: false);

        var restore = SimulateWriterInterestRegistered(turnstile);
        try
        {
            var acquired = reader.TryAcquire(out var readerSlot);

            Assert.True(acquired);
            Assert.Equal(1, reader.GetSnapshot().InUse);
            Assert.Equal(1, turnstile.CurrentCount); // still free — reader touched and released it

            readerSlot.Dispose();
        }
        finally
        {
            restore();
        }
    }

    // An acquire timeout of essentially zero means _acquireTimeoutStopwatchTicks is clamped to the
    // minimum of 1 stopwatch tick (ConvertTimeoutToStopwatchTicks's own Math.Max(1L, ...) floor) —
    // by the time the slow path's own bookkeeping (an Interlocked increment, a queue-depth check,
    // a few branches) completes, real elapsed time has already exceeded that single tick on any
    // real system, so GetRemainingTimeout reliably reports TimeSpan.Zero on the very first check
    // rather than needing a real, timing-sensitive race against another thread to arrange it.
    [Fact]
    public void Acquire_ZeroAcquireTimeoutWithBusySlot_ThrowsPoolSaturatedWithoutRealTimedWait()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "zero-timeout-key", 1, TimeSpan.Zero);
        using var held = governor.Acquire();

        var sw = Stopwatch.StartNew();
        Assert.Throws<PoolSaturatedException>(() => governor.Acquire());
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Expected an essentially-immediate PoolSaturatedException, took {sw.ElapsedMilliseconds}ms.");
    }

    // TryAcquire's bare `catch` cleanup block (release the turnstile if this call is the one
    // holding it) has to fire for ANY exception escaping the semaphore wait, not just
    // OperationCanceledException specifically — disposing the (shared, externally-owned) semaphore
    // out from under the call after the turnstile has already been won is a fully deterministic
    // way to force that, rather than racing a real cancellation against the exact instant the
    // turnstile wait resolves.
    [Fact]
    public void TryAcquire_ExceptionAfterWinningTurnstile_ReleasesTurnstileBeforeRethrowing()
    {
        using var turnstile = new SemaphoreSlim(1, 1);
        var sharedSemaphore = new SemaphoreSlim(1, 1);
        using var writer = new PoolGovernor(PoolLabel.Writer, "tryacquire-cleanup-key", 1,
            TimeSpan.FromSeconds(10), sharedSemaphore: sharedSemaphore, turnstile: turnstile, holdTurnstile: true);

        sharedSemaphore.Dispose();

        Assert.Throws<ObjectDisposedException>(() => writer.TryAcquire(out _));

        // The turnstile permit must have been released by the cleanup block despite the semaphore
        // failure, rather than left stranded.
        Assert.True(turnstile.Wait(0), "Turnstile should have been released by TryAcquire's cleanup catch block.");
        turnstile.Release();
    }

    // OnAcquired's own `if (IsClosed)` re-check exists specifically for the TOCTOU window between
    // Acquire/AcquireAsync/TryAcquire/TryAcquireAsync's single top-of-method ThrowIfClosed() call
    // and a slow-path Wait() actually completing — a concurrent Close() landing in between lets a
    // straggler reach OnAcquired with a real permit already in hand despite the governor now being
    // closed (see OnAcquired's own doc comment). Reproducing that from two real, independently
    // scheduled threads would mean winning a race against the exact instant a blocking Wait()
    // resolves — reflection instead constructs the exact precondition directly: acquire the
    // semaphore permit exactly as the slow path would, close the governor, then invoke the
    // private OnAcquired the same way Acquire's slow path does immediately afterward.
    [Fact]
    public void OnAcquired_GovernorClosedBetweenTopCheckAndSlotAcquisition_ReleasesPermitAndThrows()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "toctou-key", 1, TimeSpan.FromSeconds(10));

        var semaphoreField = typeof(PoolGovernor).GetField("_semaphore", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var semaphore = (SemaphoreSlim)semaphoreField.GetValue(governor)!;

        // Simulates the slow path's own semaphore.Wait(...) having just succeeded.
        Assert.True(semaphore.Wait(0));

        governor.Close();

        var onAcquired = typeof(PoolGovernor).GetMethod("OnAcquired", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var ex = Assert.Throws<TargetInvocationException>(() =>
            onAcquired.Invoke(governor, new object[] { Stopwatch.GetTimestamp(), false }));

        Assert.IsType<ObjectDisposedException>(ex.InnerException);

        // The permit OnAcquired grabbed on our behalf must have been handed back, not stranded,
        // despite the call ultimately failing.
        Assert.Equal(1, semaphore.CurrentCount);
        Assert.Equal(0, governor.GetSnapshot().InUse);
    }

    // Same TOCTOU precondition as above, but for a WRITER whose registered turnstile interest
    // (WritersActiveOrWaiting) must also be unwound on this failure path — a second, independent
    // cleanup branch inside OnAcquired's closed-check gated on releaseWriterTurnstileInterestOnRelease,
    // not exercised by the reader case above (which always passes false for that argument).
    [Fact]
    public void OnAcquired_GovernorClosedForWriterReleasingTurnstileInterest_DecrementsInterestAndThrows()
    {
        using var turnstile = new SemaphoreSlim(1, 1);
        using var governor = new PoolGovernor(PoolLabel.Writer, "toctou-writer-key", 1,
            TimeSpan.FromSeconds(10), turnstile: turnstile, holdTurnstile: true);

        var semaphoreField = typeof(PoolGovernor).GetField("_semaphore", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var semaphore = (SemaphoreSlim)semaphoreField.GetValue(governor)!;
        Assert.True(semaphore.Wait(0));

        // Simulates RegisterWriterTurnstileInterest already having run for this call.
        var restore = SimulateWriterInterestRegistered(turnstile);
        try
        {
            governor.Close();

            var onAcquired = typeof(PoolGovernor).GetMethod("OnAcquired", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var ex = Assert.Throws<TargetInvocationException>(() =>
                onAcquired.Invoke(governor, new object[] { Stopwatch.GetTimestamp(), true }));

            Assert.IsType<ObjectDisposedException>(ex.InnerException);
            Assert.Equal(1, semaphore.CurrentCount);
        }
        finally
        {
            restore();
        }
    }

    [Fact]
    public void Label_And_PoolKeyHash_ReturnConstructorValues()
    {
        using var governor = new PoolGovernor(PoolLabel.Writer, "some-pool-key", 1, TimeSpan.FromSeconds(1));

        Assert.Equal(PoolLabel.Writer, governor.Label);
        Assert.Equal("some-pool-key", governor.PoolKeyHash);
    }

    // No turnstile at all: the fast path's two back-to-back immediate (zero-timeout) semaphore
    // probes (one before the slow path starts, one immediately after turnstile handling — which
    // is skipped entirely here) both fail while the slot is held elsewhere, forcing Acquire() into
    // its own genuinely-timed Wait(semRemaining, ...) call — which then succeeds once the holder
    // releases, rather than either failing outright or succeeding on one of the immediate checks.
    [Fact]
    public async Task Acquire_SlotBusyThenReleasedWithinTimeout_SucceedsViaTimedSemaphoreWait()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "timed-wait-key", 1,
            TimeSpan.FromSeconds(10));

        var holderSlot = governor.Acquire();

        var releaseAfter = Task.Run(async () =>
        {
            await Task.Delay(100);
            holderSlot.Dispose();
        });

        var sw = Stopwatch.StartNew();
        using var acquiredSlot = governor.Acquire();
        sw.Stop();

        await releaseAfter;

        Assert.True(sw.ElapsedMilliseconds >= 80,
            $"Expected Acquire() to have genuinely waited for the release (~100ms), but it returned after {sw.ElapsedMilliseconds}ms.");
        Assert.Equal(1, governor.GetSnapshot().InUse);
    }
}
