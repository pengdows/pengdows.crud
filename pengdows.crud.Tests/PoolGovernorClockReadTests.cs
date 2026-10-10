using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Reading the clock costs about as much as the whole rest of an uncontended acquire, and the readings only feed the
/// wait and hold metrics. With metrics off nothing may read the clock on the fast path; with metrics on, one reading
/// when the slot is taken (it is both the start of the wait and the moment of acquisition when nothing waited) and
/// one when it is given back are all the metrics need.
/// </summary>
public class PoolGovernorClockReadTests
{
    private sealed class CountingClock : TimeProvider
    {
        private int _reads;

        internal int Reads => Volatile.Read(ref _reads);

        public override long GetTimestamp()
        {
            Interlocked.Increment(ref _reads);
            return Stopwatch.GetTimestamp();
        }
    }

    public static TheoryData<bool, bool, string> Cases()
    {
        var data = new TheoryData<bool, bool, string>();
        foreach (var turnstile in new[] { false, true })
        {
            foreach (var path in new[] { "Acquire", "AcquireAsync", "TryAcquire", "TryAcquireAsync" })
            {
                data.Add(false, turnstile, path);
                data.Add(true, turnstile, path);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task AnUncontendedAcquireAndRelease_ReadsTheClockOnlyAsMuchAsTheMetricsNeed(
        bool trackMetrics, bool withTurnstile, string path)
    {
        var clock = new CountingClock();
        using var turnstile = withTurnstile ? new SemaphoreSlim(1, 1) : null;
        using var governor = withTurnstile
            ? new PoolGovernor(PoolLabel.Writer, "clock", 1, TimeSpan.FromSeconds(5), trackMetrics: trackMetrics,
                turnstile: turnstile, holdTurnstile: true, timeProvider: clock)
            : new PoolGovernor(PoolLabel.Reader, "clock", 1, TimeSpan.FromSeconds(5), trackMetrics: trackMetrics,
                timeProvider: clock);

        PoolSlot slot;
        switch (path)
        {
            case "Acquire":
                slot = governor.Acquire();
                break;
            case "AcquireAsync":
                slot = await governor.AcquireAsync();
                break;
            case "TryAcquire":
                Assert.True(governor.TryAcquire(out slot));
                break;
            default:
                var attempt = await governor.TryAcquireAsync();
                Assert.True(attempt.Success);
                slot = attempt.Permit;
                break;
        }

        slot.Dispose();

        Assert.Equal(trackMetrics ? 2 : 0, clock.Reads);
        Assert.Equal(1, governor.AvailablePermits);
    }

    [Fact]
    public void WithMetricsOn_AnUncontendedAcquire_RecordsNoWait_AndAHoldOnlyWhenTimePassed()
    {
        var clock = new CountingClock();
        using var governor = new PoolGovernor(PoolLabel.Reader, "clock-metrics", 1, TimeSpan.FromSeconds(5),
            trackMetrics: true, timeProvider: clock);

        var slot = governor.Acquire();
        Thread.Sleep(20);
        slot.Dispose();

        var snapshot = governor.GetSnapshot();
        Assert.Equal(0, snapshot.TotalWaitTicks); // nothing waited: the wait started when the slot was taken
        Assert.True(snapshot.TotalHoldTicks >= Stopwatch.Frequency / 1000 * 15,
            $"The 20 ms hold was recorded as {snapshot.TotalHoldTicks} ticks.");
    }

    [Fact]
    public async Task WithMetricsOn_ACallerThatWaited_StillRecordsItsWait()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "clock-wait", 1, TimeSpan.FromSeconds(30),
            trackMetrics: true);
        var held = governor.Acquire();

        var waiter = Task.Factory.StartNew(() => governor.Acquire(), TaskCreationOptions.LongRunning);
        while (governor.QueueDepth == 0)
        {
            await Task.Delay(2);
        }

        await Task.Delay(30);
        held.Dispose();
        (await waiter.WaitAsync(TimeSpan.FromSeconds(10))).Dispose();

        Assert.True(governor.GetSnapshot().TotalWaitTicks >= Stopwatch.Frequency / 1000 * 20,
            "A caller that waited about 30 ms must show that wait in the metrics.");
    }
}
