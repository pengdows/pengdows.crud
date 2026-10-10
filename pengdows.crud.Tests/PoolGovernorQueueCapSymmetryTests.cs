using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Both acquire paths guard two queues with a depth cap: the slot queue and, when a writer turnstile is shared,
/// the turnstile queue. Each guard exists in the blocking and in the asynchronous path, and each must fail fast,
/// leave its counter exactly as it found it, and not disturb the callers already queued. These pin the two guards
/// that were exercised on only one side (the blocking slot cap and the asynchronous turnstile cap), so the two
/// paths cannot drift apart.
/// </summary>
public class PoolGovernorQueueCapSymmetryTests
{
    private static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task BlockingAcquire_OverTheSlotQueueCap_FailsFast_AndLeavesTheCounterClean()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "cap-slot-sync", 1, LongTimeout,
            trackMetrics: true, maxQueueDepth: 1);
        var held = governor.Acquire();

        // One caller may queue; it blocks on its own thread until the held slot is released.
        var occupier = Task.Factory.StartNew(() => governor.Acquire().Dispose(), TaskCreationOptions.LongRunning);
        await WaitUntilAsync(() => governor.QueueDepth == 1, occupier);

        Assert.Throws<PoolSaturatedException>(() => governor.Acquire());
        Assert.Equal(1, governor.QueueDepth); // the rejected caller took its increment back

        held.Dispose();
        await occupier.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, governor.QueueDepth);

        // A leaked increment would make this second queued caller look like an overflow and reject it.
        var heldAgain = governor.Acquire();
        var second = Task.Factory.StartNew(() => governor.Acquire().Dispose(), TaskCreationOptions.LongRunning);
        await WaitUntilAsync(() => governor.QueueDepth == 1, second);
        heldAgain.Dispose();
        await second.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, governor.QueueDepth);
        Assert.Equal(0, governor.GetSnapshot().TotalSlotTimeouts); // a rejection is not a timeout
    }

    [Fact]
    public async Task AsyncAcquire_OverTheTurnstileQueueCap_FailsFast_AndLeavesTheCounterClean()
    {
        using var turnstile = new SemaphoreSlim(1, 1);
        using var writer = new PoolGovernor(PoolLabel.Writer, "cap-turn-w", 1, LongTimeout,
            turnstile: turnstile, holdTurnstile: true);
        using var reader = new PoolGovernor(PoolLabel.Reader, "cap-turn-r", 1, LongTimeout,
            trackMetrics: true, turnstile: turnstile, holdTurnstile: false, maxQueueDepth: 1);

        // A writer holds the turnstile, so readers queue on it; one may, the next must be rejected.
        var writerSlot = writer.Acquire();
        var occupier = reader.AcquireAsync().AsTask();
        await WaitUntilAsync(() => reader.GetSnapshot().TurnstileQueued == 1, occupier);

        await Assert.ThrowsAsync<PoolSaturatedException>(() => reader.AcquireAsync().AsTask());
        Assert.Equal(1, reader.GetSnapshot().TurnstileQueued); // the occupier is undisturbed

        writerSlot.Dispose();
        (await occupier.WaitAsync(TimeSpan.FromSeconds(10))).Dispose();
        Assert.Equal(0, reader.GetSnapshot().TurnstileQueued);

        // A leaked increment would make this second queued caller look like an overflow and reject it.
        var writerSlotAgain = writer.Acquire();
        var second = reader.AcquireAsync().AsTask();
        await WaitUntilAsync(() => reader.GetSnapshot().TurnstileQueued == 1, second);
        writerSlotAgain.Dispose();
        (await second.WaitAsync(TimeSpan.FromSeconds(10))).Dispose();

        Assert.Equal(0, reader.GetSnapshot().TurnstileQueued);
        Assert.Equal(0, reader.GetSnapshot().TotalTurnstileTimeouts); // a rejection is not a timeout
    }

    [Fact]
    public async Task AsyncAcquire_OnAFullPool_ReturnsAPendingResultWithoutBlockingTheCaller()
    {
        // Both acquires share one slow path; the asynchronous one must await in it, not block. A blocking wait
        // would hold the calling thread for the whole acquire timeout and starve the thread pool under load.
        using var governor = new PoolGovernor(PoolLabel.Reader, "async-nonblocking", 1, LongTimeout);
        var held = governor.Acquire();

        var call = Task.Factory.StartNew(() => governor.AcquireAsync(), TaskCreationOptions.LongRunning);
        var returned = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(5))) == call;

        Assert.True(returned, "AcquireAsync did not return: it blocked the caller instead of awaiting.");
        var pending = await call;
        Assert.False(pending.IsCompleted);

        held.Dispose();
        (await pending).Dispose();
        Assert.Equal(0, governor.QueueDepth);
    }

    [Fact]
    public async Task BlockingAcquire_OnAFullPool_BlocksUntilASlotIsFree()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "blocking-waits", 1, LongTimeout);
        var held = governor.Acquire();

        var waiter = Task.Factory.StartNew(() => governor.Acquire(), TaskCreationOptions.LongRunning);
        await WaitUntilAsync(() => governor.QueueDepth == 1, waiter);
        Assert.False(waiter.IsCompleted);

        held.Dispose();
        (await waiter.WaitAsync(TimeSpan.FromSeconds(10))).Dispose();
        Assert.Equal(0, governor.QueueDepth);
    }

    // Polls until the condition holds. A queued caller that failed instead of queueing ends the wait with its
    // exception, so a regression reads as "the occupier was rejected" rather than as a timeout.
    private static async Task WaitUntilAsync(Func<bool> condition, Task occupier)
    {
        var deadline = Stopwatch.GetTimestamp() + (20 * Stopwatch.Frequency);
        while (!condition())
        {
            if (occupier.IsFaulted)
            {
                await occupier; // surfaces the exception
            }

            Assert.True(Stopwatch.GetTimestamp() < deadline, "The caller never reached the queue.");
            await Task.Delay(5);
        }
    }
}
