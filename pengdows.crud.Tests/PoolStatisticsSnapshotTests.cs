using System.Diagnostics;
using pengdows.crud.enums;
using pengdows.crud.metrics;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Unit tests for <see cref="PoolStatisticsSnapshot"/> computed properties.
/// </summary>
public class PoolStatisticsSnapshotTests
{
    [Fact]
    public void AverageWaitMs_IsZero_WhenNoAcquisitions()
    {
        var snapshot = MakeSnapshot(totalAcquired: 0, totalWaitTicks: 1000, totalHoldTicks: 5000);

        Assert.Equal(0d, snapshot.AverageWaitMs);
    }

    [Fact]
    public void AverageHoldMs_IsZero_WhenNoAcquisitions()
    {
        var snapshot = MakeSnapshot(totalAcquired: 0, totalWaitTicks: 1000, totalHoldTicks: 5000);

        Assert.Equal(0d, snapshot.AverageHoldMs);
    }

    [Fact]
    public void AverageWaitMs_IsComputed_FromTotalWaitTicksAndAcquisitions()
    {
        // Use a known tick amount: exactly Stopwatch.Frequency ticks = 1 second = 1000ms per acquisition
        var ticks = Stopwatch.Frequency;        // 1 second worth of ticks
        var snapshot = MakeSnapshot(totalAcquired: 1, totalWaitTicks: ticks, totalHoldTicks: 0);

        // With 1 acquisition, 1 second of wait → avg = 1000ms
        Assert.Equal(1000d, snapshot.AverageWaitMs, precision: 0);
    }

    [Fact]
    public void AverageHoldMs_IsComputed_FromTotalHoldTicksAndAcquisitions()
    {
        var ticks = Stopwatch.Frequency * 2;    // 2 seconds worth of ticks
        var snapshot = MakeSnapshot(totalAcquired: 2, totalWaitTicks: 0, totalHoldTicks: ticks);

        // With 2 acquisitions, 2 seconds of hold → avg = 1000ms per acquisition
        Assert.Equal(1000d, snapshot.AverageHoldMs, precision: 0);
    }

    [Fact]
    public void AverageWaitMs_DividesEvenly_AcrossMultipleAcquisitions()
    {
        var ticksPerMs = Stopwatch.Frequency / 1000.0;
        var totalWaitTicks = (long)(ticksPerMs * 50d * 4);   // 4 acquisitions × 50ms each
        var snapshot = MakeSnapshot(totalAcquired: 4, totalWaitTicks: totalWaitTicks, totalHoldTicks: 0);

        Assert.InRange(snapshot.AverageWaitMs, 49.5, 50.5);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PoolStatisticsSnapshot MakeSnapshot(
        long totalAcquired,
        long totalWaitTicks,
        long totalHoldTicks)
    {
        return new PoolStatisticsSnapshot(
            Label: PoolLabel.Writer,
            PoolKeyHash: "test",
            MaxSlots: 10,
            InUse: 0,
            PeakInUse: 0,
            Queued: 0,
            PeakQueued: 0,
            TurnstileQueued: 0,
            PeakTurnstileQueued: 0,
            TotalAcquired: totalAcquired,
            TotalWaitTicks: totalWaitTicks,
            TotalHoldTicks: totalHoldTicks,
            TotalSlotTimeouts: 0,
            TotalTurnstileTimeouts: 0,
            TotalCanceledWaits: 0,
            Disabled: false,
            Forbidden: false);
    }
}
