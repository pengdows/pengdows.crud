using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PoolAcquireTimeout is only validated as positive, so "wait as long as it takes" is spelled TimeSpan.MaxValue. The
/// governor turns the timeout into a deadline, and that conversion must neither expire at once nor wrap around: a
/// saturated pool must queue the caller and grant it when a slot frees.
/// </summary>
public class PoolGovernorLargeTimeoutTests
{
    private static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(10);

    public static TheoryData<TimeSpan> LongTimeouts => new()
    {
        TimeSpan.FromHours(1),
        TimeSpan.FromDays(60),   // past what a timer can hold
        TimeSpan.MaxValue        // the usual spelling of "wait as long as it takes"
    };

    [Theory]
    [MemberData(nameof(LongTimeouts))]
    public async Task BlockingAcquire_WithAVeryLongPoolAcquireTimeout_QueuesInsteadOfExpiring(TimeSpan timeout)
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "large-sync", 1, timeout);
        var held = governor.Acquire();

        var waiter = Task.Factory.StartNew(() => governor.Acquire(), TaskCreationOptions.LongRunning);
        await WaitUntilAsync(() => governor.QueueDepth == 1);
        await Task.Delay(300);

        Assert.False(waiter.IsCompleted, "The acquire ended at once instead of waiting for a slot.");

        held.Dispose();
        (await waiter.WaitAsync(HardTimeout)).Dispose();
        Assert.Equal(0, governor.QueueDepth);
        Assert.Equal(1, governor.AvailablePermits);
    }

    [Theory]
    [MemberData(nameof(LongTimeouts))]
    public async Task AsyncAcquire_WithAVeryLongPoolAcquireTimeout_QueuesInsteadOfExpiring(TimeSpan timeout)
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "large-async", 1, timeout);
        var held = governor.Acquire();

        var pending = governor.AcquireAsync().AsTask();
        await WaitUntilAsync(() => governor.QueueDepth == 1);
        await Task.Delay(300);

        Assert.False(pending.IsCompleted, "The acquire ended at once instead of waiting for a slot.");

        held.Dispose();
        (await pending.WaitAsync(HardTimeout)).Dispose();
        Assert.Equal(0, governor.QueueDepth);
        Assert.Equal(1, governor.AvailablePermits);
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
