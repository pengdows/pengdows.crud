// =============================================================================
// FILE: PoolGovernorDeadlineTests.cs
// PURPOSE: Prove that Acquire/AcquireAsync honour a single deadline across
//          all internal gates (turnstile + semaphore).
//
// BUG REPRODUCED:
//   PoolGovernor.Acquire/_AcquireAsync_ applies _acquireTimeout independently
//   to the turnstile wait AND the semaphore wait.  When both are contended the
//   total wait can approach 2 × _acquireTimeout, violating the contract that
//   callers set ONE timeout for the whole acquisition.
//
// FIX EXPECTED:
//   Compute a deadline at entry and pass the *remaining* budget to each
//   successive wait, so the sum across all gates ≤ _acquireTimeout.
// =============================================================================

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PoolGovernorDeadlineTests
{
    // ── AcquireAsync deadline ─────────────────────────────────────────────────

    /// <summary>
    /// When the turnstile is held for part of the budget, the semaphore gets
    /// only the *remaining* time — not a fresh full timeout.
    /// Total elapsed must be ≤ _acquireTimeout + scheduling headroom.
    ///
    /// Values chosen so the bug case (≈ holdMs + timeoutMs = 1 600 ms) exceeds
    /// the threshold (1 500 ms), while the fixed case (≈ timeoutMs = 1 000 ms)
    /// stays well below it even under parallel scheduler noise.
    /// </summary>
    [Fact]
    public async Task AcquireAsync_WithPartialTurnstileWait_TotalWaitBoundedByAcquireTimeout()
    {
        const int timeoutMs       = 1000;
        const int turnstileHoldMs = 600;   // 60 % of budget; bug ≈ 1 600 ms > threshold
        const int headroomMs      = 500;   // generous for parallel-scheduler noise

        using var turnstile = new SemaphoreSlim(1, 1);
        using var sem       = new SemaphoreSlim(0, 1); // zero capacity — always fails
        using var gov = new PoolGovernor(
            PoolLabel.Writer, "deadline-async", 1,
            TimeSpan.FromMilliseconds(timeoutMs),
            sharedSemaphore: sem,
            turnstile: turnstile,
            holdTurnstile: true);

        await turnstile.WaitAsync(); // hold it externally
        _ = Task.Delay(turnstileHoldMs).ContinueWith(_ => turnstile.Release());

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<PoolSaturatedException>(() => gov.AcquireAsync());
        sw.Stop();

        // Bug: ≈ 600 + 1000 = 1 600 ms.  Fix: ≤ 1 000 ms + scheduler headroom.
        Assert.True(sw.ElapsedMilliseconds < timeoutMs + headroomMs,
            $"AcquireAsync total wait {sw.ElapsedMilliseconds} ms exceeded " +
            $"timeout {timeoutMs} ms + {headroomMs} ms headroom.  " +
            "Governor is not using a deadline across turnstile + semaphore.");
    }

    /// <summary>
    /// Sync Acquire must also honour the same deadline contract.
    /// </summary>
    [Fact]
    public void Acquire_WithPartialTurnstileWait_TotalWaitBoundedByAcquireTimeout()
    {
        const int timeoutMs       = 1000;
        const int turnstileHoldMs = 600;
        const int headroomMs      = 500;

        using var turnstile = new SemaphoreSlim(1, 1);
        using var sem       = new SemaphoreSlim(0, 1);
        using var gov = new PoolGovernor(
            PoolLabel.Writer, "deadline-sync", 1,
            TimeSpan.FromMilliseconds(timeoutMs),
            sharedSemaphore: sem,
            turnstile: turnstile,
            holdTurnstile: true);

        turnstile.Wait(); // hold it externally
        _ = Task.Delay(turnstileHoldMs).ContinueWith(_ => turnstile.Release());

        var sw = Stopwatch.StartNew();
        Assert.Throws<PoolSaturatedException>(() => gov.Acquire());
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < timeoutMs + headroomMs,
            $"Acquire total wait {sw.ElapsedMilliseconds} ms exceeded " +
            $"timeout {timeoutMs} ms + {headroomMs} ms headroom.");
    }

    // ── Metrics: correct timeout counter when semaphore is the bottleneck ─────
    //
    // Design note: these counter tests do NOT hold the turnstile externally.
    // The turnstile is FREE so the governor passes it immediately, then waits
    // on the exhausted semaphore and times out there.  Using a free turnstile
    // makes the counter assertion deterministic regardless of scheduler load —
    // no Task.Delay races that could make the turnstile timeout fire instead.

    /// <summary>
    /// When the semaphore (not the turnstile) times out,
    /// TotalSlotTimeouts must be 1 and TotalTurnstileTimeouts must be 0.
    /// </summary>
    [Fact]
    public async Task AcquireAsync_SemaphoreExhausted_TurnstileFree_CountedAsSlotTimeout()
    {
        using var turnstile = new SemaphoreSlim(1, 1); // free — governor passes instantly
        using var sem       = new SemaphoreSlim(0, 1); // exhausted — always fails

        using var gov = new PoolGovernor(
            PoolLabel.Writer, "slot-to-a", 1,
            TimeSpan.FromMilliseconds(50),
            sharedSemaphore: sem,
            turnstile: turnstile,
            holdTurnstile: true,
            trackMetrics: true);

        await Assert.ThrowsAsync<PoolSaturatedException>(() => gov.AcquireAsync());

        Assert.Equal(1, gov.GetSnapshot().TotalSlotTimeouts);
        Assert.Equal(0, gov.GetSnapshot().TotalTurnstileTimeouts);
    }

    [Fact]
    public void Acquire_SemaphoreExhausted_TurnstileFree_CountedAsSlotTimeout()
    {
        using var turnstile = new SemaphoreSlim(1, 1);
        using var sem       = new SemaphoreSlim(0, 1);

        using var gov = new PoolGovernor(
            PoolLabel.Writer, "slot-to-s", 1,
            TimeSpan.FromMilliseconds(50),
            sharedSemaphore: sem,
            turnstile: turnstile,
            holdTurnstile: true,
            trackMetrics: true);

        Assert.Throws<PoolSaturatedException>(() => gov.Acquire());

        Assert.Equal(1, gov.GetSnapshot().TotalSlotTimeouts);
        Assert.Equal(0, gov.GetSnapshot().TotalTurnstileTimeouts);
    }

    // ── No-turnstile path unaffected ─────────────────────────────────────────

    /// <summary>
    /// Governors without a turnstile must still throw after exactly _acquireTimeout
    /// (regression guard: the deadline change must not break the no-turnstile path).
    /// </summary>
    [Fact]
    public async Task AcquireAsync_NoTurnstile_ThrowsAfterAcquireTimeout()
    {
        const int timeoutMs = 200;

        using var gov = new PoolGovernor(
            PoolLabel.Reader, "no-ts", 1,
            TimeSpan.FromMilliseconds(timeoutMs));

        await using var held = await gov.AcquireAsync(); // fill the pool

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<PoolSaturatedException>(() => gov.AcquireAsync());
        sw.Stop();

        // Should throw at approximately timeoutMs — not much earlier (correct timeout)
        Assert.True(sw.ElapsedMilliseconds >= timeoutMs - 50,
            $"Threw after only {sw.ElapsedMilliseconds} ms; expected ≥ {timeoutMs - 50} ms.");
        // Generous upper bound: scheduler noise under parallel test load
        Assert.True(sw.ElapsedMilliseconds < timeoutMs + 500,
            $"Threw after {sw.ElapsedMilliseconds} ms; expected < {timeoutMs + 500} ms.");
    }

    // ── Deadline exhausted before semaphore attempt ───────────────────────────

    /// <summary>
    /// When the turnstile wait consumes the entire budget, the governor must
    /// throw PoolSaturatedException immediately — even though the semaphore has
    /// capacity — and record it as a TurnstileTimeout, not a SlotTimeout.
    /// </summary>
    [Fact]
    public async Task AcquireAsync_TurnstileConsumesFullBudget_ThrowsWithoutSemaphoreWait()
    {
        const int timeoutMs  = 400;
        const int headroomMs = 300;

        using var turnstile = new SemaphoreSlim(1, 1);
        // Semaphore has capacity — if we reach it we would succeed.
        // We want to prove we do NOT reach it after the deadline expires.
        using var sem = new SemaphoreSlim(1, 1);

        using var gov = new PoolGovernor(
            PoolLabel.Writer, "budget-zero", 1,
            TimeSpan.FromMilliseconds(timeoutMs),
            sharedSemaphore: sem,
            turnstile: turnstile,
            holdTurnstile: true,
            trackMetrics: true);

        await turnstile.WaitAsync(); // hold it — governor will timeout on turnstile

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<PoolSaturatedException>(() => gov.AcquireAsync());
        sw.Stop();

        // Timed out at turnstile — should be ≈ timeoutMs, definitely not 2 × timeoutMs
        Assert.True(sw.ElapsedMilliseconds < timeoutMs + headroomMs,
            $"Threw after {sw.ElapsedMilliseconds} ms; expected < {timeoutMs + headroomMs} ms.");

        Assert.Equal(1, gov.GetSnapshot().TotalTurnstileTimeouts);
        Assert.Equal(0, gov.GetSnapshot().TotalSlotTimeouts);

        turnstile.Release(); // cleanup
    }
}
