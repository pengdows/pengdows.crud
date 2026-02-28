// =============================================================================
// FILE: ModeContentionStats.cs
// PURPOSE: Tracks contention statistics for SingleWriter/SingleConnection modes.
//
// AI SUMMARY:
// - Internal metrics for mode lock contention tracking.
// - Thread-safe: all counters use Interlocked operations.
// - Tracked metrics:
//   * CurrentWaiters: Operations currently waiting for lock
//   * PeakWaiters: Maximum concurrent waiters observed
//   * TotalWaits: Lifetime wait count
//   * TotalTimeouts: Lock acquisition timeout count
//   * TotalWaitTimeTicks: Cumulative wait time in ticks
// - RecordWaitStart(): Increments waiters, updates peak
// - RecordWaitEnd(ticks): Decrements waiters, adds wait time
// - RecordTimeout(ticks): Increments timeouts, calls RecordWaitEnd
// - GetSnapshot(): Returns ModeContentionSnapshot with averages.
// - ModeContentionSnapshot: Public readonly record struct for exception context.
// =============================================================================

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
            (int)Math.Clamp(Interlocked.Read(ref _currentWaiters), 0L, int.MaxValue),
            (int)Math.Clamp(Interlocked.Read(ref _peakWaiters), 0L, int.MaxValue),
            waits,
            Interlocked.Read(ref _totalTimeouts),
            totalTicks,
            avgTicks);
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