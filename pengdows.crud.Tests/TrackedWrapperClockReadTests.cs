using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.@internal;
using pengdows.crud.fakeDb;
using pengdows.crud.metrics;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Every reader and every connection close used to read the clock whether or not anything was listening: the
/// readings only feed the metrics collector, and a clock read costs about 19 ns. With no collector the tracked
/// wrappers must not read it at all; with one, they must still time what they report.
/// </summary>
public class TrackedWrapperClockReadTests
{
    private static fakeDbDataReader TwoRows() => new(new[]
    {
        new Dictionary<string, object> { ["id"] = 1 },
        new Dictionary<string, object> { ["id"] = 2 }
    });

    private sealed class NoopDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // The reader's connection is a real tracked connection over fakeDb; it is not closed with the reader and it has
    // no clock of its own to count, so it adds no readings to the reader's.
    private static TrackedReader NewReader(CountingClock clock, MetricsCollector? collector) =>
        new(TwoRows(), new TrackedConnection(new fakeDbConnection()), new NoopDisposable(), false,
            metricsCollector: collector, timeProvider: clock);

    [Fact]
    public void AReaderWithNoCollector_NeverReadsTheClock()
    {
        var clock = new CountingClock();
        var reader = NewReader(clock, null);

        while (reader.Read())
        {
        }

        reader.Dispose();

        Assert.Equal(0, clock.Reads);
    }

    [Fact]
    public async Task AnAsyncReaderWithNoCollector_NeverReadsTheClock()
    {
        var clock = new CountingClock();
        var reader = NewReader(clock, null);

        while (await reader.ReadAsync())
        {
        }

        await reader.DisposeAsync();

        Assert.Equal(0, clock.Reads);
    }

    [Fact]
    public void AReaderWithACollector_ReadsTheClockForItsLeaseItsFirstRowAndItsEnd_AndStillReportsBoth()
    {
        var clock = new CountingClock();
        var collector = new MetricsCollector(MetricsOptions.Default);
        var reader = NewReader(clock, collector);

        Thread.Sleep(20); // the lease is open for a while before the first row is read
        while (reader.Read())
        {
        }

        reader.Dispose();

        // The lease start, the first row, and the moment the lease ended; the second row reads nothing.
        Assert.Equal(3, clock.Reads);
        var snapshot = collector.CreateSnapshot();
        Assert.True(snapshot.AvgReaderTimeToFirstRowMs >= 10, $"first row {snapshot.AvgReaderTimeToFirstRowMs} ms");
        Assert.True(snapshot.AvgReaderLeaseMs >= 10, $"lease {snapshot.AvgReaderLeaseMs} ms");
    }

    // Same frequency as the Stopwatch but an origin of its own, as any injected clock would have.
    private sealed class OffsetClock : TimeProvider
    {
        public override long GetTimestamp() => Stopwatch.GetTimestamp() + 7_000_000_000_000_000L;
    }

    [Fact]
    public void AReaderTimedByAClockWithAnotherOrigin_StillReportsSaneDurations()
    {
        var collector = new MetricsCollector(MetricsOptions.Default);
        var reader = new TrackedReader(TwoRows(), new TrackedConnection(new fakeDbConnection()), new NoopDisposable(),
            false, metricsCollector: collector, timeProvider: new OffsetClock());

        Thread.Sleep(20);
        while (reader.Read())
        {
        }

        reader.Dispose();

        // Every reading comes from the injected clock; mixing in the Stopwatch's own origin makes these nonsense.
        var snapshot = collector.CreateSnapshot();
        Assert.InRange(snapshot.AvgReaderLeaseMs, 10, 5_000);
        Assert.InRange(snapshot.AvgReaderTimeToFirstRowMs, 10, 5_000);
    }

    [Fact]
    public void ClosingAConnectionWithNoCollector_NeverReadsTheClock()
    {
        var clock = new CountingClock();
        using var connection = new TrackedConnection(new fakeDbConnection(), timeProvider: clock);

        connection.Open();
        connection.Close();

        Assert.Equal(0, clock.Reads);
    }

    [Fact]
    public async Task OpeningAndDisposingAConnectionAsync_WithNoCollector_NeverReadsTheClock()
    {
        var clock = new CountingClock();
        var connection = new TrackedConnection(new fakeDbConnection(), timeProvider: clock);

        await connection.OpenAsync();
        await connection.DisposeAsync();

        Assert.Equal(0, clock.Reads);
    }

    [Fact]
    public void ClosingAConnectionWithACollector_StillReportsTheCloseDuration()
    {
        var clock = new CountingClock();
        var collector = new MetricsCollector(MetricsOptions.Default);
        using var connection = new TrackedConnection(new fakeDbConnection(), metricsCollector: collector,
            timeProvider: clock);

        connection.Open();
        connection.Close();

        Assert.True(clock.Reads > 0);
        Assert.True(collector.CreateSnapshot().AvgConnectionCloseMs > 0);
    }
}
