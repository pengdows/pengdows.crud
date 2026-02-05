// Tests for the turnstile-fairness paths in PoolGovernor.
// A writer (holdTurnstile=true) holds the turnstile for the duration of its
// permit; readers (holdTurnstile=false) touch-and-release it.  This prevents
// reader floods from starving writers.

using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PoolGovernorTurnstileTests
{
    // ── Writer holds turnstile → reader blocked ──────────────────────────

    [Fact]
    public void Acquire_WriterHoldsTurnstile_ReaderTimesOutOnTurnstile()
    {
        using var turnstile = new SemaphoreSlim(1, 1);

        using var writer = new PoolGovernor(PoolLabel.Writer, "ts-w", 1,
            TimeSpan.FromMilliseconds(50),
            turnstile: turnstile, holdTurnstile: true);

        using var reader = new PoolGovernor(PoolLabel.Reader, "ts-r", 1,
            TimeSpan.FromMilliseconds(50),
            turnstile: turnstile, holdTurnstile: false);

        using var wp = writer.Acquire();   // holds turnstile

        Assert.Throws<PoolSaturatedException>(() => reader.Acquire());
        Assert.Equal(1, reader.GetSnapshot().TotalTimeouts);
    }

    [Fact]
    public async Task AcquireAsync_WriterHoldsTurnstile_ReaderTimesOutOnTurnstile()
    {
        using var turnstile = new SemaphoreSlim(1, 1);

        using var writer = new PoolGovernor(PoolLabel.Writer, "ts-wa", 1,
            TimeSpan.FromMilliseconds(50),
            turnstile: turnstile, holdTurnstile: true);

        using var reader = new PoolGovernor(PoolLabel.Reader, "ts-ra", 1,
            TimeSpan.FromMilliseconds(50),
            turnstile: turnstile, holdTurnstile: false);

        var wp = await writer.AcquireAsync();

        await Assert.ThrowsAsync<PoolSaturatedException>(() => reader.AcquireAsync());
        Assert.Equal(1, reader.GetSnapshot().TotalTimeouts);

        wp.Dispose();
    }

    // ── Readers touch-and-release → concurrent reads succeed ──────────────

    [Fact]
    public void Acquire_ReadersTouchAndRelease_ConcurrentSucceeds()
    {
        using var turnstile = new SemaphoreSlim(1, 1);

        // capacity 2: both readers can each grab a slot after releasing the turnstile
        using var reader = new PoolGovernor(PoolLabel.Reader, "ts-conc", 2,
            TimeSpan.FromMilliseconds(50),
            turnstile: turnstile, holdTurnstile: false);

        using var p1 = reader.Acquire();
        using var p2 = reader.Acquire();

        Assert.Equal(2, reader.GetSnapshot().InUse);
    }

    // ── TryAcquire / TryAcquireAsync with turnstile blocked ──────────────

    [Fact]
    public void TryAcquire_WriterHoldsTurnstile_ReaderReturnsFalse()
    {
        using var turnstile = new SemaphoreSlim(1, 1);

        using var writer = new PoolGovernor(PoolLabel.Writer, "try-w", 1,
            TimeSpan.FromMilliseconds(50),
            turnstile: turnstile, holdTurnstile: true);

        using var reader = new PoolGovernor(PoolLabel.Reader, "try-r", 1,
            TimeSpan.FromMilliseconds(50),
            turnstile: turnstile, holdTurnstile: false);

        using var wp = writer.Acquire();   // holds turnstile

        var success = reader.TryAcquire(out var rp);

        Assert.False(success);
        Assert.Equal(default, rp);
        Assert.Equal(0, reader.GetSnapshot().TotalTimeouts); // TryAcquire never counts timeouts
    }

    [Fact]
    public async Task TryAcquireAsync_WriterHoldsTurnstile_ReaderReturnsFalse()
    {
        using var turnstile = new SemaphoreSlim(1, 1);

        using var writer = new PoolGovernor(PoolLabel.Writer, "trya-w", 1,
            TimeSpan.FromMilliseconds(50),
            turnstile: turnstile, holdTurnstile: true);

        using var reader = new PoolGovernor(PoolLabel.Reader, "trya-r", 1,
            TimeSpan.FromMilliseconds(50),
            turnstile: turnstile, holdTurnstile: false);

        var wp = await writer.AcquireAsync();

        var result = await reader.TryAcquireAsync();

        Assert.False(result.Success);
        Assert.Equal(0, reader.GetSnapshot().TotalTimeouts);

        wp.Dispose();
    }

    // ── Second writer times out on turnstile ──────────────────────────────

    [Fact]
    public void Acquire_SecondWriterTimesOutOnTurnstile()
    {
        using var turnstile = new SemaphoreSlim(1, 1);

        using var w1 = new PoolGovernor(PoolLabel.Writer, "ts-w1", 1,
            TimeSpan.FromMilliseconds(30), turnstile: turnstile, holdTurnstile: true);

        using var w2 = new PoolGovernor(PoolLabel.Writer, "ts-w2", 1,
            TimeSpan.FromMilliseconds(30), turnstile: turnstile, holdTurnstile: true);

        using var p1 = w1.Acquire();   // holds turnstile

        Assert.Throws<PoolSaturatedException>(() => w2.Acquire());
        Assert.Equal(1, w2.GetSnapshot().TotalTimeouts);
    }

    [Fact]
    public async Task AcquireAsync_SecondWriterTimesOutOnTurnstile()
    {
        using var turnstile = new SemaphoreSlim(1, 1);

        using var w1 = new PoolGovernor(PoolLabel.Writer, "ts-aw1", 1,
            TimeSpan.FromMilliseconds(30), turnstile: turnstile, holdTurnstile: true);

        using var w2 = new PoolGovernor(PoolLabel.Writer, "ts-aw2", 1,
            TimeSpan.FromMilliseconds(30), turnstile: turnstile, holdTurnstile: true);

        var p1 = await w1.AcquireAsync();

        await Assert.ThrowsAsync<PoolSaturatedException>(() => w2.AcquireAsync());
        Assert.Equal(1, w2.GetSnapshot().TotalTimeouts);

        p1.Dispose();
    }

    // ── Turnstile released on slot-acquisition failure ────────────────────
    // Writer acquires turnstile, then fails on the exhausted slot semaphore.
    // The catch handler must release the turnstile so the next caller can proceed.

    [Fact]
    public void Acquire_SlotSaturated_WriterReleasesTurnstileOnFailure()
    {
        using var turnstile = new SemaphoreSlim(1, 1);
        using var exhaustedSem = new SemaphoreSlim(0, 1); // zero available

        using var writer = new PoolGovernor(PoolLabel.Writer, "fail-ts", 1,
            TimeSpan.FromMilliseconds(20),
            sharedSemaphore: exhaustedSem,
            turnstile: turnstile, holdTurnstile: true);

        Assert.Throws<PoolSaturatedException>(() => writer.Acquire());

        // Turnstile must have been released by the catch handler
        Assert.True(turnstile.Wait(0), "Turnstile was not released after slot acquisition failure.");
    }

    [Fact]
    public async Task AcquireAsync_SlotSaturated_WriterReleasesTurnstileOnFailure()
    {
        using var turnstile = new SemaphoreSlim(1, 1);
        using var exhaustedSem = new SemaphoreSlim(0, 1);

        using var writer = new PoolGovernor(PoolLabel.Writer, "fail-ts-a", 1,
            TimeSpan.FromMilliseconds(20),
            sharedSemaphore: exhaustedSem,
            turnstile: turnstile, holdTurnstile: true);

        await Assert.ThrowsAsync<PoolSaturatedException>(() => writer.AcquireAsync());

        Assert.True(turnstile.Wait(0), "Turnstile was not released after async slot acquisition failure.");
    }

    [Fact]
    public void TryAcquire_SlotSaturated_WriterReleasesTurnstileOnFallthrough()
    {
        using var turnstile = new SemaphoreSlim(1, 1);
        using var exhaustedSem = new SemaphoreSlim(0, 1);

        using var writer = new PoolGovernor(PoolLabel.Writer, "try-fail-ts", 1,
            TimeSpan.FromMilliseconds(10),
            sharedSemaphore: exhaustedSem,
            turnstile: turnstile, holdTurnstile: true);

        var success = writer.TryAcquire(out var permit);

        Assert.False(success);
        Assert.True(turnstile.Wait(0), "Turnstile was not released after TryAcquire slot miss.");
    }

    [Fact]
    public async Task TryAcquireAsync_SlotSaturated_WriterReleasesTurnstileOnFallthrough()
    {
        using var turnstile = new SemaphoreSlim(1, 1);
        using var exhaustedSem = new SemaphoreSlim(0, 1);

        using var writer = new PoolGovernor(PoolLabel.Writer, "trya-fail-ts", 1,
            TimeSpan.FromMilliseconds(10),
            sharedSemaphore: exhaustedSem,
            turnstile: turnstile, holdTurnstile: true);

        var result = await writer.TryAcquireAsync();

        Assert.False(result.Success);
        Assert.True(turnstile.Wait(0), "Turnstile was not released after async TryAcquire slot miss.");
    }

    // ── Writer permit disposal releases turnstile ─────────────────────────

    [Fact]
    public void Dispose_WriterPermit_ReleasesTurnstile()
    {
        using var turnstile = new SemaphoreSlim(1, 1);

        using var writer = new PoolGovernor(PoolLabel.Writer, "perm-ts", 1,
            TimeSpan.FromMilliseconds(50),
            turnstile: turnstile, holdTurnstile: true);

        var permit = writer.Acquire();
        Assert.False(turnstile.Wait(0)); // turnstile held

        permit.Dispose();                // releases turnstile
        Assert.True(turnstile.Wait(0));  // turnstile free
    }

    // ── Drain on disabled governor ─────────────────────────────────────────

    [Fact]
    public async Task WaitForDrainAsync_Disabled_ReturnsImmediately()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "drain-dis", 1,
            TimeSpan.FromMilliseconds(10), disabled: true);

        await governor.WaitForDrainAsync(); // must return without blocking
    }

    [Fact]
    public async Task WaitForDrainAsync_AlreadyDrained_ReturnsImmediately()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "drain-0", 1,
            TimeSpan.FromMilliseconds(10));

        // Never acquired anything — inUse == 0 fast path
        await governor.WaitForDrainAsync();
    }

    // ── Turnstile ownership on dispose ─────────────────────────────────────

    [Fact]
    public void Dispose_OwnsTurnstile_DisposesIt()
    {
        var turnstile = new SemaphoreSlim(1, 1);
        var governor = new PoolGovernor(PoolLabel.Writer, "own-ts", 1,
            TimeSpan.FromMilliseconds(10),
            turnstile: turnstile, holdTurnstile: true, ownsTurnstile: true);

        governor.Dispose();

        Assert.Throws<ObjectDisposedException>(() => turnstile.Wait(0));
    }

    [Fact]
    public void Dispose_DoesNotOwnTurnstile_PreservesIt()
    {
        using var turnstile = new SemaphoreSlim(1, 1);
        var governor = new PoolGovernor(PoolLabel.Writer, "borrow-ts", 1,
            TimeSpan.FromMilliseconds(10),
            turnstile: turnstile, holdTurnstile: true, ownsTurnstile: false);

        governor.Dispose();

        Assert.True(turnstile.Wait(0)); // turnstile still alive
    }

    // ── TryAcquireAsync disabled path ─────────────────────────────────────

    [Fact]
    public async Task TryAcquireAsync_Disabled_ReturnsImmediately()
    {
        using var gov = new PoolGovernor(PoolLabel.Reader, "dis", 1,
            TimeSpan.FromMilliseconds(10), disabled: true);

        var (success, permit) = await gov.TryAcquireAsync();

        Assert.True(success);
        Assert.Equal(default, permit);
    }

    // ── TryAcquireAsync reader touch-and-release succeeds ─────────────────
    // Reader with a free turnstile: touches it, releases it, then acquires the slot.

    [Fact]
    public async Task TryAcquireAsync_ReaderTouchAndRelease_Succeeds()
    {
        using var turnstile = new SemaphoreSlim(1, 1);

        using var reader = new PoolGovernor(PoolLabel.Reader, "tac-r", 1,
            TimeSpan.FromMilliseconds(50),
            turnstile: turnstile, holdTurnstile: false);

        var (success, permit) = await reader.TryAcquireAsync();

        Assert.True(success);
        // Turnstile must still be available (reader released it after touching)
        Assert.True(turnstile.Wait(0));
        turnstile.Release();

        permit.Dispose();
    }

    // ── TryAcquire disabled path (sync counterpart) ───────────────────────

    [Fact]
    public void TryAcquire_Disabled_ReturnsImmediately()
    {
        using var gov = new PoolGovernor(PoolLabel.Reader, "dis-s", 1,
            TimeSpan.FromMilliseconds(10), disabled: true);

        var success = gov.TryAcquire(out var permit);

        Assert.True(success);
        Assert.Equal(default, permit);
    }

    // ── Constructor validation ─────────────────────────────────────────────

    [Fact]
    public void Constructor_ZeroPermits_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PoolGovernor(PoolLabel.Reader, "z", 0, TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public void Constructor_NegativePermits_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PoolGovernor(PoolLabel.Reader, "n", -5, TimeSpan.FromMilliseconds(10)));
    }
}
