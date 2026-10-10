using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The blocking wait at the edges of what a caller can ask for: a timeout longer than any test would sit through, a
/// thread interrupted while it is blocked, and a cancellation callback still running as the wait ends. At each edge
/// the gate must keep every permit, leave no waiter behind, and do exactly what the caller asked.
/// </summary>
public class PoolGovernorConcurrencyGateBlockingWaitBoundaryTests
{
    private static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(10);

    // The deadline used to be computed as Ticks * Stopwatch.Frequency in a long, which overflows above 15 minutes
    // where the frequency is 1 GHz (Linux, macOS) and above 25 hours where it is 10 MHz (Windows), and then reads as
    // "already expired". 2,200,000 seconds is past int.MaxValue milliseconds, the most one Monitor.Wait can take.
    [Theory]
    [InlineData(960)]
    [InlineData(1_800)]
    [InlineData(3_600)]
    [InlineData(86_400)]
    [InlineData(2_200_000)]
    public async Task BlockingAcquire_WithATimeoutPastTheOldOverflowPoint_WaitsForThePermit(double seconds)
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();

        var waiter = Task.Factory.StartNew(() => gate.Acquire(TimeSpan.FromSeconds(seconds)),
            TaskCreationOptions.LongRunning);
        await Task.Delay(300);

        Assert.False(waiter.IsCompleted, "The wait ended at once instead of lasting the timeout the caller asked for.");

        gate.Release(held);
        var permit = await waiter.WaitAsync(HardTimeout);
        gate.Release(permit);

        Assert.Equal(0, gate.ActiveCount);
        Assert.Equal(0, gate.QueueCount);
    }

    [Fact]
    public async Task BlockingAcquire_WithTheLargestTimeoutThereIs_WaitsForThePermit()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();

        var waiter = Task.Factory.StartNew(() => gate.Acquire(TimeSpan.MaxValue), TaskCreationOptions.LongRunning);
        await Task.Delay(300);

        Assert.False(waiter.IsCompleted, "The wait ended at once instead of lasting the timeout the caller asked for.");

        gate.Release(held);
        gate.Release(await waiter.WaitAsync(HardTimeout));
        Assert.Equal(0, gate.ActiveCount);
    }

    [Fact]
    public async Task ABlockedThread_WhenInterrupted_LeavesTheQueue_AndNoPermitIsLost()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();
        Exception? thrown = null;

        var blocked = new Thread(() =>
        {
            try
            {
                gate.Acquire(TimeSpan.FromSeconds(30));
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        }) { IsBackground = true };
        blocked.Start();
        await WaitUntilAsync(() => gate.QueueCount == 1);

        blocked.Interrupt();
        Assert.True(blocked.Join(HardTimeout), "The interrupted thread never came back.");

        Assert.IsType<ThreadInterruptedException>(thrown);
        Assert.Equal(0, gate.QueueCount); // the interrupted caller is no longer waiting

        // A permit handed to a caller that is gone would be lost for good. It must come back to the gate.
        gate.Release(held);
        Assert.Equal(0, gate.ActiveCount);
        Assert.True(gate.TryAcquire(out var permit), "The permit was lost to the interrupted waiter.");
        gate.Release(permit);
    }

    [Fact]
    public async Task ABlockedThread_InterruptedAsAPermitIsGrantedToIt_GivesThePermitBack()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();
        Exception? thrown = null;

        // The grant lands after the interrupt has been raised but before the thread has given up its place.
        gate.TestOnlyBeforeInterruptedWaitIsAbandoned = () => gate.Release(held);

        var blocked = new Thread(() =>
        {
            try
            {
                gate.Acquire(TimeSpan.FromSeconds(30));
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        }) { IsBackground = true };
        blocked.Start();
        await WaitUntilAsync(() => gate.QueueCount == 1);

        blocked.Interrupt();
        Assert.True(blocked.Join(HardTimeout), "The interrupted thread never came back.");

        Assert.IsType<ThreadInterruptedException>(thrown);
        Assert.Equal(0, gate.QueueCount);
        Assert.Equal(0, gate.ActiveCount); // the permit granted to the interrupted caller was not kept
        Assert.True(gate.TryAcquire(out var permit), "The granted permit was lost with the interrupted caller.");
        gate.Release(permit);
    }

    [Fact]
    public async Task BlockingAcquire_ReturnsOnlyAfterTheCancellationCallbackThatCompletedItHasFinished()
    {
        // A cached waiter is reused for the thread's next wait, so a cancellation callback still running against it
        // when Acquire returns could complete that next wait. Acquire must wait the callback out.
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();
        using var cancellation = new CancellationTokenSource();
        using var inCallback = new ManualResetEventSlim(false);
        using var proceed = new ManualResetEventSlim(false);
        gate.TestOnlyAfterCancelCompleted = () =>
        {
            inCallback.Set();
            proceed.Wait(HardTimeout);
        };

        var waiter = Task.Factory.StartNew(() => gate.Acquire(Timeout.InfiniteTimeSpan, cancellation.Token),
            TaskCreationOptions.LongRunning);
        await WaitUntilAsync(() => gate.QueueCount == 1);

        var canceller = Task.Factory.StartNew(() => cancellation.Cancel(), TaskCreationOptions.LongRunning);
        Assert.True(inCallback.Wait(HardTimeout), "The cancellation callback never ran.");
        await Task.Delay(300);

        Assert.False(waiter.IsCompleted,
            "Acquire returned while the cancellation callback that completed it was still running against its waiter.");

        proceed.Set();
        await Assert.ThrowsAsync<OperationCanceledException>(() => waiter.WaitAsync(HardTimeout));
        await canceller.WaitAsync(HardTimeout);

        gate.Release(held);
        Assert.Equal(0, gate.ActiveCount);
        Assert.Equal(0, gate.QueueCount);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = Stopwatch.GetTimestamp() + (HardTimeout.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond);
        while (!condition())
        {
            Assert.True(Stopwatch.GetTimestamp() < deadline, "The condition was never met.");
            await Task.Delay(2);
        }
    }
}
