using System;
using System.Diagnostics;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Deadlines and the metrics' durations convert ticks with Stopwatch.Frequency, so the clock a pool governor, tracked
/// reader or tracked connection is given must tick at that frequency. One that does not would make every deadline and
/// duration wrong without any error, so it is refused when the object is built.
/// </summary>
public class TimeProviderFrequencyTests
{
    private sealed class MillisecondClock : TimeProvider
    {
        public override long TimestampFrequency => 1_000;

        public override long GetTimestamp() => Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1_000);
    }

    private sealed class NoopDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void APoolGovernor_RefusesAClockWithAnotherFrequency()
    {
        var thrown = Assert.Throws<ArgumentException>(() =>
            new PoolGovernor(PoolLabel.Reader, "frequency", 1, TimeSpan.FromSeconds(5),
                timeProvider: new MillisecondClock()));

        Assert.Equal("timeProvider", thrown.ParamName);
    }

    [Fact]
    public void ATrackedReader_RefusesAClockWithAnotherFrequency()
    {
        var thrown = Assert.Throws<ArgumentException>(() =>
            new TrackedReader(new fakeDbDataReader(), new TrackedConnection(new fakeDbConnection()),
                new NoopDisposable(), false, timeProvider: new MillisecondClock()));

        Assert.Equal("timeProvider", thrown.ParamName);
    }

    [Fact]
    public void ATrackedConnection_RefusesAClockWithAnotherFrequency()
    {
        var thrown = Assert.Throws<ArgumentException>(() =>
            new TrackedConnection(new fakeDbConnection(), timeProvider: new MillisecondClock()));

        Assert.Equal("timeProvider", thrown.ParamName);
    }

    [Fact]
    public void TheSystemClock_AndAClockAtTheStopwatchFrequency_AreAccepted()
    {
        using var viaDefault = new PoolGovernor(PoolLabel.Reader, "default", 1, TimeSpan.FromSeconds(5));
        using var viaSystem = new PoolGovernor(PoolLabel.Reader, "system", 1, TimeSpan.FromSeconds(5),
            timeProvider: TimeProvider.System);
        using var viaCounting = new PoolGovernor(PoolLabel.Reader, "counting", 1, TimeSpan.FromSeconds(5),
            timeProvider: new CountingClock());

        Assert.Equal(1, viaDefault.AvailablePermits);
        Assert.Equal(1, viaSystem.AvailablePermits);
        Assert.Equal(1, viaCounting.AvailablePermits);
    }
}
