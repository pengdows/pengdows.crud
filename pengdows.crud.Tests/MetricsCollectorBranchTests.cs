using System.Diagnostics;
using pengdows.crud.metrics;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public class MetricsCollectorBranchTests
{
    [Fact]
    public void ToMilliseconds_HandlesZeroOrNegative()
    {
        Assert.Equal(0d, MetricsCollector.ToMilliseconds(0));
        Assert.Equal(0d, MetricsCollector.ToMilliseconds(-1));
    }

    [Fact]
    public void MetricsChanged_IgnoresNullHandlers()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.MetricsChanged += null!;
        collector.MetricsChanged -= null!;
    }

    [Fact]
    public void ConnectionDurations_IgnoreNonPositiveValues()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.RecordConnectionOpenDuration(0d);
        collector.RecordConnectionCloseDuration(-1d);
        collector.ConnectionClosed(0d);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(0d, snapshot.AvgConnectionOpenMs);
        Assert.Equal(0d, snapshot.AvgConnectionCloseMs);
        Assert.Equal(0d, snapshot.AvgConnectionHoldMs);
    }

    [Fact]
    public void CommandStarted_UpdatesMaxParameters()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.CommandStarted(0);
        collector.CommandStarted(2);
        collector.CommandStarted(1);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(2, snapshot.MaxParametersObserved);
    }

    [Fact]
    public void CommandDuration_IgnoresZeroOrFutureTimestamps()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.CommandSucceeded(0, 0);

        var future = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
        collector.CommandFailed(future);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(1, snapshot.CommandsFailed);
        Assert.Equal(0d, snapshot.AvgCommandMs);
    }

    [Fact]
    public void RowsReadAndAffected_IgnoreNonPositiveCounts()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.RecordRowsRead(0);
        collector.RecordRowsAffected(-1);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(0, snapshot.RowsReadTotal);
        Assert.Equal(0, snapshot.RowsAffectedTotal);
    }

    [Fact]
    public void StatementEvicted_IgnoresNonPositive()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.RecordStatementEvicted(0);
        collector.RecordStatementEvicted(-1);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(0, snapshot.StatementsEvicted);
    }

    [Fact]
    public void TransactionCommitted_RecordsDuration()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        var start = collector.TransactionStarted();
        collector.TransactionCommitted(start);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(0, snapshot.TransactionsActive);
        Assert.True(snapshot.AvgTransactionMs >= 0d);
    }

    [Fact]
    public void PercentileRing_EmptySnapshot_ReturnsZeros()
    {
        var collector = new MetricsCollector(new MetricsOptions
        {
            EnableApproxPercentiles = true,
            PercentileWindowSize = 8
        });

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(0d, snapshot.P95CommandMs);
        Assert.Equal(0d, snapshot.P99CommandMs);
    }
    // REV-049: percentiles were memoized by snapshot *call* count and the first (empty) snapshot was
    // cached, so a consumer polling Metrics reported "unavailable"/0 for up to 31 polls after
    // commands had run, then stayed frozen for 31 more.
    private static void RunCommands(MetricsCollector collector, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var start = collector.CommandStarted(0) - System.Diagnostics.Stopwatch.Frequency / 1000;
            collector.CommandSucceeded(start, 1);
        }
    }

    private static MetricsCollector PercentileCollector() => new(new MetricsOptions
    {
        EnableApproxPercentiles = true,
        PercentileWindowSize = 128
    });

    [Fact]
    public void Percentiles_PolledBeforeAnyCommand_AreAvailableOnTheNextPollAfterCommands()
    {
        var collector = PercentileCollector();
        Assert.False(collector.CreateSnapshot().CommandPercentilesAvailable);

        RunCommands(collector, 5);

        var snapshot = collector.CreateSnapshot();
        Assert.True(snapshot.CommandPercentilesAvailable);
        Assert.True(snapshot.P95CommandMs > 0d);
    }

    [Fact]
    public void Percentiles_RecomputeAfterEnoughNewSamples_HoweverRarelyPolled()
    {
        var collector = PercentileCollector();
        RunCommands(collector, 5);
        var first = collector.CreateSnapshot();

        for (var i = 0; i < 40; i++)
        {
            var start = collector.CommandStarted(0) - System.Diagnostics.Stopwatch.Frequency; // 1 s
            collector.CommandSucceeded(start, 1);
        }

        var second = collector.CreateSnapshot();
        Assert.True(second.P95CommandMs > first.P95CommandMs * 10,
            $"P95 {first.P95CommandMs} -> {second.P95CommandMs}: the snapshot did not see the new samples");
    }
}