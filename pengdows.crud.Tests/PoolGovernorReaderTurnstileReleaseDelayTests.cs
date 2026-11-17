using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// G9a-SW (CONCURRENCY.md): "in the async path, a reader's release runs in its continuation...
/// Under thread-pool starvation, a writer may wait at the turnstile behind such a touch."
///
/// This proves the *consequence* the Assumption describes deterministically, rather than trying
/// to reproduce genuine OS thread-pool starvation (inherently a timing race, and the wrong kind
/// of flaky to put in a regression suite). PoolGovernor.TestOnlyBeforeReaderTurnstileRelease hooks
/// the exact continuation the Assumption names -- the code immediately after
/// `await _turnstile.WaitAsync(...)` resumes, which runs the reader's touch-and-release -- and
/// holds it open on command. Whether a real production delay there comes from this hook or from
/// a genuinely starved thread pool is irrelevant to what actually matters: does a delayed release
/// there make a waiting writer wait correspondingly longer? That causal link is what's proven
/// below, not the specific mechanism that could produce the delay in production (ordinary,
/// well-documented ConfigureAwait(false) continuation scheduling, not something that itself needs
/// a novel proof).
///
/// Orchestrating a reader and writer governor racing for the same free turnstile permit would
/// make which one wins a genuine, undeterministic scheduling race -- the opposite of what this
/// test needs. Instead: a real writer governor's own RegisterWriterTurnstileInterest (reflection;
/// private, but this is exactly what a real writer's AcquireAsync does as its very first step)
/// sets WritersActiveOrWaiting without the writer itself contending for the turnstile at all, so
/// ShouldUseTurnstileGate correctly routes the reader through the turnstile for a reason
/// identical to production, deterministically. The test then holds the turnstile itself first
/// (forcing the reader off its own zero-wait fast path and into the awaited slow path where the
/// hook lives), releases it, and uses a second direct WaitAsync() call as the "someone else
/// waiting on the turnstile" the reader's delayed release blocks -- standing in for the real
/// writer, which in production would be queued on this exact same semaphore.
/// </summary>
public sealed class PoolGovernorReaderTurnstileReleaseDelayTests
{
    [Fact(Timeout = 10000)]
    public async Task SecondWaiterOnTheTurnstile_IsBlockedForAsLongAsAReadersReleaseContinuationIsDelayed()
    {
        var turnstile = new SemaphoreSlim(1, 1);
        using var readerGovernor = new PoolGovernor(
            PoolLabel.Reader, "turnstile-delay-reader", maxSlots: 5, acquireTimeout: TimeSpan.FromSeconds(10),
            turnstile: turnstile, holdTurnstile: false);
        using var writerGovernor = new PoolGovernor(
            PoolLabel.Writer, "turnstile-delay-writer", maxSlots: 1, acquireTimeout: TimeSpan.FromSeconds(10),
            turnstile: turnstile, holdTurnstile: true);

        // The real production trigger for ShouldUseTurnstileGate -- a writer sharing this exact
        // turnstile registering interest -- without the writer itself contending for the permit.
        RegisterWriterInterest(writerGovernor);

        // Hold the turnstile myself: the reader's own zero-timeout fast-path Wait(0) must fail,
        // so it falls into the awaited slow path -- the only place the hook lives.
        await turnstile.WaitAsync();

        var releaseGate = new ManualResetEventSlim(initialState: false);
        var hookEntered = new ManualResetEventSlim(initialState: false);
        readerGovernor.TestOnlyBeforeReaderTurnstileRelease = () =>
        {
            hookEntered.Set();
            // Synchronous block, standing in for "this continuation got delayed" -- by a starved
            // thread pool in production, by this test deterministically here.
            releaseGate.Wait();
        };

        var readerTask = readerGovernor.AcquireAsync().AsTask();
        await Task.Delay(50); // let the reader genuinely queue behind the permit I'm holding
        Assert.False(hookEntered.IsSet, "The reader should still be waiting for the turnstile I'm holding.");

        // Queues behind the reader (already waiting) for the one permit -- standing in for the
        // real writer, which in production would be queued on this exact same semaphore.
        var secondWaiterTask = turnstile.WaitAsync();

        // Release my own hold: the reader -- already queued -- gets the permit next and lands in
        // the slow path's post-await code, parking at the hook before actually releasing again.
        turnstile.Release();

        Assert.True(hookEntered.Wait(TimeSpan.FromSeconds(5)), "The reader never reached the hook.");
        Assert.False(secondWaiterTask.IsCompleted,
            "The second waiter must still be blocked while the reader's release continuation is " +
            "deliberately held open.");

        releaseGate.Set();

        var readerSlot = await readerTask;
        await secondWaiterTask; // only unblocked once the reader's delayed release finally runs
        turnstile.Release(); // give back the permit secondWaiterTask took, for a clean teardown

        await readerSlot.DisposeAsync();
    }

    private static void RegisterWriterInterest(PoolGovernor writerGovernor)
    {
        var method = typeof(PoolGovernor).GetMethod("RegisterWriterTurnstileInterest",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        object[] args = { false };
        method!.Invoke(writerGovernor, args);
        Assert.True((bool)args[0], "RegisterWriterTurnstileInterest did not register interest.");
    }
}
