using System;
using System.Diagnostics;
using pengdows.crud.@internal;
using pengdows.crud.metrics;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Verifies that AvgCommandMs / P95 / P99 all describe the same population
/// (successful commands only) and that failed commands are tracked separately
/// in AvgFailedCommandMs.
/// </summary>
public class MetricsCommandSeparationTests
{
    // ── Success path feeds AvgCommandMs ──────────────────────────────────────

    [Fact]
    public void RecordCommandDuration_SuccessOnly_FeedsAvgCommandMs()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.CommandSucceeded(CreateTimestamp(100d), 0);

        var snapshot = collector.CreateSnapshot();

        Assert.True(snapshot.AvgCommandMs > 0d,
            "AvgCommandMs must be > 0 after a successful command");
    }

    [Fact]
    public void RecordCommandDuration_Success_DoesNotFeedAvgFailedCommandMs()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.CommandSucceeded(CreateTimestamp(100d), 0);

        var snapshot = collector.CreateSnapshot();

        Assert.Equal(0d, snapshot.AvgFailedCommandMs);
    }

    // ── Failure path does NOT contaminate AvgCommandMs ───────────────────────

    [Fact]
    public void RecordCommandDuration_FailureOnly_DoesNotAffectAvgCommandMs()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.CommandFailed(CreateTimestamp(100d));

        var snapshot = collector.CreateSnapshot();

        // Before fix: AvgCommandMs would be ~100ms (bug).
        // After fix:  AvgCommandMs must be 0 (never fed by failures).
        Assert.Equal(0d, snapshot.AvgCommandMs);
    }

    [Fact]
    public void RecordCommandDuration_FailureOnly_FeedsAvgFailedCommandMs()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.CommandFailed(CreateTimestamp(100d));

        var snapshot = collector.CreateSnapshot();

        Assert.True(snapshot.AvgFailedCommandMs > 0d,
            "AvgFailedCommandMs must be > 0 after a failed command");
    }

    [Fact]
    public void RecordCommandDuration_TimedOut_DoesNotAffectAvgCommandMs()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.CommandTimedOut(CreateTimestamp(200d));

        var snapshot = collector.CreateSnapshot();

        Assert.Equal(0d, snapshot.AvgCommandMs);
        Assert.True(snapshot.AvgFailedCommandMs > 0d);
    }

    [Fact]
    public void RecordCommandDuration_Cancelled_DoesNotAffectAvgCommandMs()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.CommandCancelled(CreateTimestamp(50d));

        var snapshot = collector.CreateSnapshot();

        Assert.Equal(0d, snapshot.AvgCommandMs);
        Assert.True(snapshot.AvgFailedCommandMs > 0d);
    }

    // ── P95/P99 still not fed by failures ────────────────────────────────────

    [Fact]
    public void PercentileRing_NotFedByFailedCommands()
    {
        var collector = new MetricsCollector(new MetricsOptions
        {
            EnableApproxPercentiles = true,
            PercentileWindowSize = 8
        });
        collector.CommandFailed(CreateTimestamp(500d));

        var snapshot = collector.CreateSnapshot();

        Assert.Equal(0d, snapshot.P95CommandMs);
        Assert.Equal(0d, snapshot.P99CommandMs);
    }

    // ── Mixed: success + failure both tracked independently ──────────────────

    [Fact]
    public void MixedOutcomes_BothMetricsPopulated()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.CommandSucceeded(CreateTimestamp(10d), 0);
        collector.CommandFailed(CreateTimestamp(200d));

        var snapshot = collector.CreateSnapshot();

        Assert.True(snapshot.AvgCommandMs > 0d);
        Assert.True(snapshot.AvgFailedCommandMs > 0d);
        // AvgCommandMs and AvgFailedCommandMs track different populations;
        // they are not required to be equal.
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static long CreateTimestamp(double durationMs)
    {
        var offset = (long)Math.Max(1, durationMs / 1000d * Stopwatch.Frequency);
        return Stopwatch.GetTimestamp() - offset;
    }
}
