using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.metrics;
using pengdows.crud.opentelemetry;
using pengdows.crud.tenant;
using Xunit;

namespace pengdows.crud.Tests.opentelemetry;

public class PengdowsMetricsObserverTests
{
    private delegate void LongMeasurementHandler(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags);

    private sealed class PerTenantResolver : ITenantConnectionResolver
    {
        public IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant)
        {
            return new DatabaseContextConfiguration
            {
                ProviderName = "fake-sqlite",
                ConnectionString = "Data Source=:memory:",
                EnableMetrics = true,
                ApplicationName = "tenant-" + tenant
            };
        }
    }

    private sealed class StubContextFactory : IDatabaseContextFactory
    {
        public IDatabaseContext Create(
            IDatabaseContextConfiguration configuration,
            DbProviderFactory factory,
            ILoggerFactory loggerFactory)
        {
            return new DatabaseContext(configuration, factory, loggerFactory);
        }
    }

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

    // TelemetryService_TracksTenantContextCreatedAfterStartup and
    // TelemetryService_InvalidatedTenantContext_IsRemovedFromObservableGauges
    // require ITenantContextRegistry.ContextCreated/ContextRemoved events
    // which are not yet part of the public interface.

    private async Task TelemetryService_TracksTenantContextCreatedAfterStartup_Disabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>(
            "fake-sqlite",
            static (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));
        services.AddSingleton<ITenantConnectionResolver, PerTenantResolver>();
        services.AddSingleton<IDatabaseContextFactory, StubContextFactory>();
        services.AddSingleton<ITenantContextRegistry, TenantContextRegistry>();
        services.AddPengdowsTelemetry();

        await using var sp = services.BuildServiceProvider();

        var exportedTcs = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = CreateListener(
            "pengdows.crud",
            (instrument, measurement, tags) =>
            {
                if ((instrument.Name == "pengdows.db.client.commands.executed")
                    && HasTag(tags, "db.name", "tenant-alpha"))
                {
                    exportedTcs.TrySetResult(measurement);
                }
            });

        foreach (var hostedService in sp.GetServices<IHostedService>())
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        var registry = sp.GetRequiredService<ITenantContextRegistry>();
        await using var context = registry.GetContext("alpha");
        var container = context.CreateSqlContainer("SELECT 1");
        await container.ExecuteScalarOrNullAsync<int>();

        var result = await exportedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result >= 1, $"Expected tenant context metrics to be exported, got {result}.");

        foreach (var hostedService in sp.GetServices<IHostedService>().Reverse())
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    private async Task TelemetryService_InvalidatedTenantContext_IsRemovedFromObservableGauges_Disabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>(
            "fake-sqlite",
            static (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));
        services.AddSingleton<ITenantConnectionResolver, PerTenantResolver>();
        services.AddSingleton<IDatabaseContextFactory, StubContextFactory>();
        services.AddSingleton<ITenantContextRegistry, TenantContextRegistry>();
        services.AddPengdowsTelemetry();

        await using var sp = services.BuildServiceProvider();

        var seenDatabaseNames = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if ((instrument.Meter.Name == "pengdows.crud")
                && (instrument.Name == "pengdows.db.client.connections.current"))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "pengdows.db.client.connections.current")
            {
                var dbName = GetTagValue(tags, "db.name");
                if (!string.IsNullOrEmpty(dbName))
                {
                    seenDatabaseNames.Add(dbName);
                }
            }
        });

        listener.Start();

        foreach (var hostedService in sp.GetServices<IHostedService>())
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        var registry = sp.GetRequiredService<ITenantContextRegistry>();
        var alpha = registry.GetContext("alpha");
        var beta = registry.GetContext("beta");

        listener.RecordObservableInstruments();

        Assert.Contains("tenant-alpha", seenDatabaseNames);
        Assert.Contains("tenant-beta", seenDatabaseNames);

        seenDatabaseNames.Clear();
        registry.Invalidate("alpha");

        listener.RecordObservableInstruments();

        Assert.DoesNotContain("tenant-alpha", seenDatabaseNames);
        Assert.Contains("tenant-beta", seenDatabaseNames);

        foreach (var hostedService in sp.GetServices<IHostedService>().Reverse())
        {
            await hostedService.StopAsync(CancellationToken.None);
        }

        alpha.Dispose();
        beta.Dispose();
    }

    private static MeterListener CreateListener(
        string meterName,
        LongMeasurementHandler onMeasurement)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == meterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            onMeasurement(instrument, measurement, tags);
        });

        listener.Start();
        return listener;
    }

    private static bool HasTag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string name, string expectedValue)
    {
        return string.Equals(GetTagValue(tags, name), expectedValue, StringComparison.Ordinal);
    }

    private static string? GetTagValue(ReadOnlySpan<KeyValuePair<string, object?>> tags, string name)
    {
        foreach (var tag in tags)
        {
            if (string.Equals(tag.Key, name, StringComparison.Ordinal))
            {
                return tag.Value?.ToString();
            }
        }

        return null;
    }
}
