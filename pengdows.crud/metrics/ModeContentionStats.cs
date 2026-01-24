using System;
using System.Threading;

namespace pengdows.crud.metrics;

internal sealed class ModeContentionStats
{
    private long _currentWaiters;
    private long _peakWaiters;
    private long _totalWaits;
    private long _totalTimeouts;
    private long _totalWaitTimeTicks;

    public void RecordWaitStart()
    {
        var current = Interlocked.Increment(ref _currentWaiters);
        Interlocked.Increment(ref _totalWaits);
        UpdatePeak(ref _peakWaiters, current);
    }

    public void RecordWaitEnd(long waitTicks)
    {
        if (waitTicks > 0)
        {
            Interlocked.Add(ref _totalWaitTimeTicks, waitTicks);
        }

        Interlocked.Decrement(ref _currentWaiters);
    }

    public void RecordTimeout(long waitTicks)
    {
        Interlocked.Increment(ref _totalTimeouts);
        RecordWaitEnd(waitTicks);
    }

    public ModeContentionSnapshot GetSnapshot()
    {
        var waits = Interlocked.Read(ref _totalWaits);
        var totalTicks = Interlocked.Read(ref _totalWaitTimeTicks);
        var avgTicks = waits == 0 ? 0 : totalTicks / waits;

        return new ModeContentionSnapshot(
            CurrentWaiters: (int)Math.Clamp(Interlocked.Read(ref _currentWaiters), 0L, int.MaxValue),
            PeakWaiters: (int)Math.Clamp(Interlocked.Read(ref _peakWaiters), 0L, int.MaxValue),
            TotalWaits: waits,
            TotalTimeouts: Interlocked.Read(ref _totalTimeouts),
            TotalWaitTimeTicks: totalTicks,
            AverageWaitTimeTicks: avgTicks);
    }

    private static void UpdatePeak(ref long peak, long current)
    {
        while (true)
        {
            var existing = Interlocked.Read(ref peak);
            if (current <= existing)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref peak, current, existing) == existing)
            {
                return;
            }
        }
    }
}

public readonly record struct ModeContentionSnapshot(
    int CurrentWaiters,
    int PeakWaiters,
    long TotalWaits,
    long TotalTimeouts,
    long TotalWaitTimeTicks,
    long AverageWaitTimeTicks);
