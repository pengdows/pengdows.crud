using System;
using System.Diagnostics;
using pengdows.crud.metrics;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public class MetricsCollectorTests
{
    [Fact]
    public void CommandSucceeded_UpdatesExecutionMetrics()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.CommandStarted(3);
        collector.CommandSucceeded(CreateStartTimestamp(20d), 5);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(1, snapshot.CommandsExecuted);
        Assert.Equal(5, snapshot.RowsAffectedTotal);
        Assert.Equal(3, snapshot.MaxParametersObserved);
        Assert.True(snapshot.AvgCommandMs >= 0d);
    }

    [Fact]
    public void CommandTimedOut_IncrementsFailureCounters()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.CommandTimedOut(CreateStartTimestamp(15d));

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(1, snapshot.CommandsTimedOut);
        Assert.Equal(1, snapshot.CommandsFailed);
        Assert.True(snapshot.AvgCommandMs >= 0d);
    }

    [Fact]
    public void ConnectionClosed_TracksLongLivedConnections()
    {
        var collector = new MetricsCollector(new MetricsOptions
        {
            LongConnectionThreshold = TimeSpan.FromMilliseconds(1)
        });

        collector.ConnectionOpened();
        collector.ConnectionClosed(5d);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(1, snapshot.ConnectionsOpened);
        Assert.Equal(1, snapshot.ConnectionsClosed);
        Assert.Equal(1, snapshot.LongLivedConnections);
        Assert.True(snapshot.AvgConnectionHoldMs >= 5d);
    }

    [Fact]
    public void PrepareMetrics_RecordCacheAndEvictions()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);

        collector.RecordPreparedStatement();
        collector.RecordStatementCached();
        collector.RecordStatementEvicted(2);
        collector.RecordStatementEvicted(0); // ignored

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(1, snapshot.PreparedStatements);
        Assert.Equal(1, snapshot.StatementsCached);
        Assert.Equal(2, snapshot.StatementsEvicted);
    }

    [Fact]
    public void PercentileWindow_ComputesApproximateValues()
    {
        var collector = new MetricsCollector(new MetricsOptions
        {
            EnableApproxPercentiles = true,
            PercentileWindowSize = 8
        });

        var durations = new[] { 5d, 10d, 20d, 40d, 80d };
        foreach (var duration in durations)
        {
            collector.CommandSucceeded(CreateStartTimestamp(duration), 0);
        }

        var snapshot = collector.CreateSnapshot();
        Assert.True(snapshot.P95CommandMs > 0d);
        Assert.True(snapshot.P99CommandMs >= snapshot.P95CommandMs);
    }

    [Fact]
    public void TransactionCommitted_IncrementsCommittedCounter()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        var ts = collector.TransactionStarted();
        collector.TransactionCommitted(ts);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(1, snapshot.TransactionsCommitted);
        Assert.Equal(0, snapshot.TransactionsRolledBack);
        Assert.Equal(0, snapshot.TransactionsActive);
    }

    [Fact]
    public void TransactionRolledBack_IncrementsRolledBackCounter()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        var ts = collector.TransactionStarted();
        collector.TransactionRolledBack(ts);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(0, snapshot.TransactionsCommitted);
        Assert.Equal(1, snapshot.TransactionsRolledBack);
        Assert.Equal(0, snapshot.TransactionsActive);
    }

    [Fact]
    public void MultipleOutcomes_EachCounterAccumulatesIndependently()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);

        // Two commits, one rollback
        collector.TransactionCommitted(collector.TransactionStarted());
        collector.TransactionCommitted(collector.TransactionStarted());
        collector.TransactionRolledBack(collector.TransactionStarted());

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(2, snapshot.TransactionsCommitted);
        Assert.Equal(1, snapshot.TransactionsRolledBack);
    }

    [Fact]
    public void SlowCommand_ExceedsThreshold_IncrementsSlowCommandsTotal()
    {
        var collector = new MetricsCollector(new MetricsOptions
        {
            SlowCommandThreshold = TimeSpan.FromMilliseconds(10)
        });

        // Command that took 50ms (above 10ms threshold)
        collector.CommandSucceeded(CreateStartTimestamp(50d), 0);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(1, snapshot.SlowCommandsTotal);
    }

    [Fact]
    public void SlowCommand_BelowThreshold_DoesNotIncrementSlowCommandsTotal()
    {
        var collector = new MetricsCollector(new MetricsOptions
        {
            SlowCommandThreshold = TimeSpan.FromSeconds(10)
        });

        // Command that took 5ms (below 10s threshold)
        collector.CommandSucceeded(CreateStartTimestamp(5d), 0);

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(0, snapshot.SlowCommandsTotal);
    }

    [Fact]
    public void SlowCommand_FailedCommandAboveThreshold_AlsoIncrements()
    {
        var collector = new MetricsCollector(new MetricsOptions
        {
            SlowCommandThreshold = TimeSpan.FromMilliseconds(10)
        });

        collector.CommandFailed(CreateStartTimestamp(50d));

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(1, snapshot.SlowCommandsTotal);
    }

    [Fact]
    public void TransactionPercentile_ComputesApproxValues()
    {
        var collector = new MetricsCollector(new MetricsOptions
        {
            EnableApproxPercentiles = true,
            PercentileWindowSize = 8
        });

        var durations = new[] { 5d, 10d, 20d, 40d, 80d };
        foreach (var duration in durations)
        {
            var ts = CreateStartTimestamp(duration);
            collector.TransactionCommitted(ts);
        }

        var snapshot = collector.CreateSnapshot();
        Assert.True(snapshot.P95TransactionMs > 0d);
        Assert.True(snapshot.P99TransactionMs >= snapshot.P95TransactionMs);
    }

    [Fact]
    public void TransactionPercentile_NoPercentileTracking_ReturnsZero()
    {
        // EnableApproxPercentiles = false (default)
        var collector = new MetricsCollector(MetricsOptions.Default);
        collector.TransactionCommitted(collector.TransactionStarted());

        var snapshot = collector.CreateSnapshot();
        Assert.Equal(0d, snapshot.P95TransactionMs);
        Assert.Equal(0d, snapshot.P99TransactionMs);
    }

    private static long CreateStartTimestamp(double durationMs)
    {
        var offset = (long)Math.Max(1, durationMs / 1000d * Stopwatch.Frequency);
        return Stopwatch.GetTimestamp() - offset;
    }
}