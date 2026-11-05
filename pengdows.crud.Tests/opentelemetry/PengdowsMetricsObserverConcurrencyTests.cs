using System;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.metrics;
using pengdows.crud.opentelemetry;
using Xunit;

namespace pengdows.crud.Tests.opentelemetry;

// MetricsUpdated handlers can run on several threads at once, and each one reads the last exported
// snapshot, emits the difference, and writes its own snapshot back. An older snapshot that lands
// after a newer one must not move the baseline backwards, or the next event re-emits the gap.
public class PengdowsMetricsObserverConcurrencyTests
{
    [Fact]
    public async Task StaleSnapshotArrivingLate_DoesNotReEmitCommandsAlreadyExported()
    {
        var meterName = "pengdows.crud.test.stale." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0.0");

        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            EnableMetrics = true,
            ApplicationName = "StaleDB"
        };
        await using var context = new DatabaseContext(config, factory);

        var baseline = context.Metrics.CommandsExecuted;
        long exported = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == meterName && instrument.Name == "pengdows.db.client.commands.executed")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => exported += measurement);
        listener.Start();

        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(context);

        await using (var first = context.CreateSqlContainer("SELECT 1"))
        {
            await first.ExecuteScalarOrNullAsync<int>();
        }
        var older = context.Metrics;

        await using (var second = context.CreateSqlContainer("SELECT 1"))
        {
            await second.ExecuteScalarOrNullAsync<int>();
        }
        var newer = context.Metrics;

        Assert.Equal(older.CommandsExecuted + 1, newer.CommandsExecuted);
        Assert.Equal(newer.CommandsExecuted - baseline, exported);

        // The older snapshot is delivered last, as a racing handler could do.
        var handler = typeof(PengdowsMetricsObserver).GetMethod(
            "HandleMetricsUpdated", BindingFlags.NonPublic | BindingFlags.Instance)!;
        handler.Invoke(observer, new object[] { context, older });
        handler.Invoke(observer, new object[] { context, newer });

        Assert.Equal(context.Metrics.CommandsExecuted, exported);
    }
}
