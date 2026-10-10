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
        await WaitUntilAsync(() => gate.QueueCount == 1);
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
        await WaitUntilAsync(() => gate.QueueCount == 1);
        await Task.Delay(300);

        Assert.False(waiter.IsCompleted, "The wait ended at once instead of lasting the timeout the caller asked for.");

        gate.Release(held);
        gate.Release(await waiter.WaitAsync(HardTimeout));
        Assert.Equal(0, gate.ActiveCount);
    }

    // CancellationTokenSource.CancelAfter cannot hold more than about 49.7 days, which the asynchronous wait used to
    // arm unconditionally: a longer timeout threw ArgumentOutOfRangeException instead of waiting.
    [Theory]
    [InlineData(60)]
    [InlineData(-1)]
    public async Task AsyncAcquire_WithATimeoutLongerThanATimerCanHold_WaitsForThePermit(int days)
    {
        var timeout = days < 0 ? TimeSpan.MaxValue : TimeSpan.FromDays(days);
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();

        var pending = gate.AcquireAsync(timeout).AsTask();
        await WaitUntilAsync(() => gate.QueueCount == 1);
        await Task.Delay(200);

        Assert.False(pending.IsCompleted, "The wait ended at once instead of lasting the timeout the caller asked for.");

        gate.Release(held);
        gate.Release(await pending.WaitAsync(HardTimeout));
        Assert.Equal(0, gate.ActiveCount);
        Assert.Equal(0, gate.QueueCount);
    }

    [Fact]
    public void ABlockedThread_InterruptedAgainWhileItLeavesTheQueue_StillLeavesTheQueue()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();
        Exception? thrown = null;
        using var inAbandon = new ManualResetEventSlim(false);
        gate.TestOnlyBeforeInterruptedWaitIsAbandoned = inAbandon.Set;

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
        Assert.True(SpinWait.SpinUntil(() => gate.QueueCount == 1, HardTimeout));

        // Holding the gate's lock makes the leaving thread block entering it, which is where a second interrupt lands.
        Monitor.Enter(gate.TestOnlySyncRoot);
        try
        {
            blocked.Interrupt();
            Assert.True(inAbandon.Wait(HardTimeout), "The thread never started leaving the queue.");
            Assert.True(SpinWait.SpinUntil(() => (blocked.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, HardTimeout),
                "The thread never blocked on the gate's lock.");
            blocked.Interrupt();
        }
        finally
        {
            Monitor.Exit(gate.TestOnlySyncRoot);
        }

        Assert.True(blocked.Join(HardTimeout), "The interrupted thread never came back.");
        Assert.IsType<ThreadInterruptedException>(thrown);
        Assert.Equal(0, gate.QueueCount); // a second interrupt must not leave the caller queued

        gate.Release(held);
        Assert.Equal(0, gate.ActiveCount);
        Assert.True(gate.TryAcquire(out var permit), "The permit was lost to the interrupted waiter.");
        gate.Release(permit);
    }

    [Fact]
    public void ABlockedThread_GrantedAPermitAndInterruptedAgain_GivesThePermitBack()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();
        Exception? thrown = null;
        using var granted = new ManualResetEventSlim(false);
        using var lockHeld = new ManualResetEventSlim(false);
        gate.TestOnlyBeforeInterruptedWaitIsAbandoned = () =>
        {
            gate.Release(held); // the permit is granted to this thread as it gives up
            granted.Set();
            lockHeld.Wait(HardTimeout);
        };

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
        Assert.True(SpinWait.SpinUntil(() => gate.QueueCount == 1, HardTimeout));
        blocked.Interrupt();
        Assert.True(granted.Wait(HardTimeout), "The grant never happened.");

        Monitor.Enter(gate.TestOnlySyncRoot);
        try
        {
            lockHeld.Set();
            Assert.True(SpinWait.SpinUntil(() => (blocked.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, HardTimeout),
                "The thread never blocked on the gate's lock.");
            blocked.Interrupt();
        }
        finally
        {
            Monitor.Exit(gate.TestOnlySyncRoot);
        }

        Assert.True(blocked.Join(HardTimeout), "The interrupted thread never came back.");
        Assert.IsType<ThreadInterruptedException>(thrown);
        Assert.Equal(0, gate.QueueCount);
        Assert.Equal(0, gate.ActiveCount); // the granted permit was given back, not kept by the departed caller
        Assert.True(gate.TryAcquire(out var permit), "The granted permit was lost with the interrupted caller.");
        gate.Release(permit);
    }

    [Fact]
    public void ABlockedThread_GrantedAPermitAndInterruptedWhileGivingItBack_StillGivesItBack()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();
        Exception? thrown = null;
        using var atGiveBack = new ManualResetEventSlim(false);
        using var lockHeld = new ManualResetEventSlim(false);

        // The grant lands as the thread gives up its place; the lock is then held just as it goes to give the permit back.
        gate.TestOnlyBeforeInterruptedWaitIsAbandoned = () => gate.Release(held);
        gate.TestOnlyBeforeAGrantIsGivenBack = () =>
        {
            atGiveBack.Set();
            lockHeld.Wait(HardTimeout);
        };

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
        Assert.True(SpinWait.SpinUntil(() => gate.QueueCount == 1, HardTimeout));
        blocked.Interrupt();
        Assert.True(atGiveBack.Wait(HardTimeout), "The thread never reached the point of giving the permit back.");

        Monitor.Enter(gate.TestOnlySyncRoot);
        try
        {
            lockHeld.Set();
            Assert.True(SpinWait.SpinUntil(() => (blocked.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, HardTimeout),
                "The thread never blocked on the gate's lock.");
            blocked.Interrupt();
        }
        finally
        {
            Monitor.Exit(gate.TestOnlySyncRoot);
        }

        Assert.True(blocked.Join(HardTimeout), "The interrupted thread never came back.");
        Assert.IsType<ThreadInterruptedException>(thrown);
        Assert.Equal(0, gate.ActiveCount); // the permit was given back although the thread was interrupted again
        Assert.True(gate.TryAcquire(out var permit), "The granted permit was lost.");
        gate.Release(permit);
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
