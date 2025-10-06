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

    private static long CreateStartTimestamp(double durationMs)
    {
        var offset = (long)Math.Max(1, durationMs / 1000d * Stopwatch.Frequency);
        return Stopwatch.GetTimestamp() - offset;
    }
}

