using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.metrics;
using pengdows.crud.opentelemetry;
using Xunit;

namespace pengdows.crud.Tests.opentelemetry;

public class PengdowsMetricsObserverTests
{
    [Fact]
    public async Task Track_RegistersContextAndExportsMetrics()
    {
        var meterName = "pengdows.crud.test." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0.0");

        // 1. Setup Context with Metrics enabled
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            EnableMetrics = true,
            ApplicationName = "TestDB"
        };
        await using var context = new DatabaseContext(config, factory);

        // 3. Setup Listener FIRST
        var exportedTcs = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == meterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "pengdows.db.client.commands.executed")
            {
                exportedTcs.TrySetResult(measurement);
            }
        });

        listener.Start();

        // 2. Create observer with our specific meter
        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(context);

        // 4. Execute a command to generate metrics
        var container = context.CreateSqlContainer("SELECT 1");
        await container.ExecuteScalarOrNullAsync<int>();

        // 5. Wait for the listener callback
        var result = await exportedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // 6. Assert
        Assert.True(result >= 1, $"Expected at least 1 command in OTel, got {result}.");
    }

    [Fact]
    public void AddPengdowsTelemetry_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddPengdowsTelemetry();
        var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<IPengdowsMetricsObserver>());
        var hostedServices = sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
        Assert.Contains(hostedServices, s => s is PengdowsTelemetryService);
    }
}
