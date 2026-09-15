using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// AcquireAsync/TryAcquireAsync duplicate their own admission-control logic independently of the
/// synchronous Acquire/TryAcquire methods (see PoolGovernorSyncAcquireTests.cs's class remarks) —
/// this covers the async-side turnstile fast-path/slow-path release and queue-depth branches that
/// were previously exercised only on the sync side, if at all.
/// </summary>
public sealed class PoolGovernorAsyncAcquireGapTests
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
    /// See PoolGovernorSyncAcquireTests.SimulateWriterInterestRegistered's remarks — identical
    /// rationale applies to the async entry points.
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
    public async Task AcquireAsync_ReaderFastPath_WithWriterInterestAlreadyRegistered_ReleasesTurnstileImmediately()
    {
        using var turnstile = new SemaphoreSlim(1, 1);
        using var writer = new PoolGovernor(PoolLabel.Writer, "async-fastpath-w", 1,
            TimeSpan.FromSeconds(10), turnstile: turnstile, holdTurnstile: true);
        using var reader = new PoolGovernor(PoolLabel.Reader, "async-fastpath-r", 1,
            TimeSpan.FromSeconds(10), turnstile: turnstile, holdTurnstile: false);

        var restore = SimulateWriterInterestRegistered(turnstile);
        try
        {
            Assert.Equal(1, turnstile.CurrentCount);

            await using var readerSlot = await reader.AcquireAsync();

            Assert.Equal(1, reader.GetSnapshot().InUse);
            Assert.Equal(1, turnstile.CurrentCount);
        }
        finally
        {
            restore();
        }
    }

    [Fact]
    public async Task TryAcquireAsync_ReaderFastPath_WithWriterInterestAlreadyRegistered_ReleasesTurnstileImmediately()
    {
        using var turnstile = new SemaphoreSlim(1, 1);
        using var writer = new PoolGovernor(PoolLabel.Writer, "async-tryfastpath-w", 1,
            TimeSpan.FromSeconds(10), turnstile: turnstile, holdTurnstile: true);
        using var reader = new PoolGovernor(PoolLabel.Reader, "async-tryfastpath-r", 1,
            TimeSpan.FromSeconds(10), turnstile: turnstile, holdTurnstile: false);

        var restore = SimulateWriterInterestRegistered(turnstile);
        try
        {
            var (success, slot) = await reader.TryAcquireAsync();

            Assert.True(success);
            Assert.Equal(1, reader.GetSnapshot().InUse);
            Assert.Equal(1, turnstile.CurrentCount);

            await slot.DisposeAsync();
        }
        finally
        {
            restore();
        }
    }

    // Async mirror of PoolGovernorSyncAcquireTests.Acquire_ReaderWaitsOutBusyTurnstileThenSucceeds_
    // ReleasesTurnstileAndAcquiresFreeSlotImmediately.
    [Fact]
    public async Task AcquireAsync_ReaderWaitsOutBusyTurnstileThenSucceeds_ReleasesTurnstileAndAcquiresFreeSlotImmediately()
    {
        using var turnstile = new SemaphoreSlim(1, 1);
        using var writer = new PoolGovernor(PoolLabel.Writer, "async-slow-path-w", 1,
            TimeSpan.FromSeconds(30), turnstile: turnstile, holdTurnstile: true);
        using var reader = new PoolGovernor(PoolLabel.Reader, "async-slow-path-r", 5,
            TimeSpan.FromSeconds(10), trackMetrics: true, turnstile: turnstile, holdTurnstile: false);

        var writerSlot = await writer.AcquireAsync();

        var readerTask = reader.AcquireAsync().AsTask();

        await WaitUntilAsync(() => reader.GetSnapshot().TurnstileQueued >= 1);
        Assert.Equal(1, reader.GetSnapshot().TurnstileQueued);

        await writerSlot.DisposeAsync();

        var readerSlot = await readerTask.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            Assert.Equal(1, reader.GetSnapshot().InUse);
            Assert.True(turnstile.Wait(0), "Turnstile should be free after the reader's touch-and-release.");
            turnstile.Release();
        }
        finally
        {
            await readerSlot.DisposeAsync();
        }
    }

    // Async mirror of PoolGovernorSyncAcquireTests.
    // Acquire_ZeroAcquireTimeoutWithBusySlot_ThrowsPoolSaturatedWithoutRealTimedWait — see its
    // remarks for why a near-zero acquire timeout reliably (not just probabilistically) reaches
    // GetRemainingTimeout's TimeSpan.Zero branch without needing a real inter-thread race.
    [Fact]
    public async Task AcquireAsync_ZeroAcquireTimeoutWithBusySlot_ThrowsPoolSaturatedWithoutRealTimedWait()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "async-zero-timeout-key", 1, TimeSpan.Zero);
        await using var held = await governor.AcquireAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<PoolSaturatedException>(async () => await governor.AcquireAsync());
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Expected an essentially-immediate PoolSaturatedException, took {sw.ElapsedMilliseconds}ms.");
    }

    // No turnstile — async mirror of PoolGovernorSyncAcquireTests.
    // Acquire_SlotBusyThenReleasedWithinTimeout_SucceedsViaTimedSemaphoreWait.
    [Fact]
    public async Task AcquireAsync_SlotBusyThenReleasedWithinTimeout_SucceedsViaTimedSemaphoreWait()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "async-timed-wait-key", 1,
            TimeSpan.FromSeconds(10));

        var holderSlot = await governor.AcquireAsync();

        var releaseAfter = Task.Run(async () =>
        {
            await Task.Delay(100);
            await holderSlot.DisposeAsync();
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await using var acquiredSlot = await governor.AcquireAsync();
        sw.Stop();

        await releaseAfter;

        Assert.True(sw.ElapsedMilliseconds >= 80,
            $"Expected AcquireAsync() to have genuinely waited for the release (~100ms), but it returned after {sw.ElapsedMilliseconds}ms.");
        Assert.Equal(1, governor.GetSnapshot().InUse);
    }
}
