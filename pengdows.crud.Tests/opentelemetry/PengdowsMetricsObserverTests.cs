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

    // =========================================================================
    // Multi-tenant safety — RED tests (TDD)
    // =========================================================================

    private static DatabaseContextConfiguration MakeConfig(string appName) =>
        new() { ConnectionString = "Data Source=:memory:", EnableMetrics = true, ApplicationName = appName };

    private static MeterListener MakeGaugeListener(string meterName, List<string> seenDbNames)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) =>
        {
            if (inst.Meter.Name == meterName) l.EnableMeasurementEvents(inst);
        };
        listener.SetMeasurementEventCallback<int>((inst, _, tags, _) =>
        {
            if (inst.Name == "pengdows.db.client.connections.current")
                seenDbNames.Add(GetTagValue(tags, "db.name") ?? "");
        });
        listener.Start();
        return listener;
    }

    // ── Gauge skips disposed contexts ─────────────────────────────────────
    // RED: GetGauges has no IsDisposed guard — disposed context still appears.
    // Note: context.Name returns the database product name ("SQLite"), not the
    // ApplicationName from config. Assertions use NotEmpty/Empty to avoid coupling
    // to the internal name format.
    [Fact]
    public void Gauge_SkipsDisposedContext()
    {
        var meterName = "test.disp." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0");
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        var ctx = new DatabaseContext(MakeConfig("disposed-tenant"), factory);
        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(ctx);

        var seen = new List<string>();
        using var listener = MakeGaugeListener(meterName, seen);

        listener.RecordObservableInstruments();
        Assert.NotEmpty(seen); // context is tracked — at least one gauge measurement
        seen.Clear();

        ctx.Dispose();

        listener.RecordObservableInstruments();
        Assert.Empty(seen); // disposed context must not appear in gauge
    }

    // ── Untrack removes context from gauge ────────────────────────────────
    // RED: IPengdowsMetricsObserver.Untrack does not exist — compile error.
    // Note: db.name tag = database product name ("SQLite"), not ApplicationName.
    // Assertions use NotEmpty/Empty to avoid coupling to the internal name format.
    [Fact]
    public void Untrack_StopsGaugeReporting()
    {
        var meterName = "test.ug." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0");
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        using var ctx = new DatabaseContext(MakeConfig("ctx-a"), factory);
        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(ctx);

        var seen = new List<string>();
        using var listener = MakeGaugeListener(meterName, seen);

        listener.RecordObservableInstruments();
        Assert.NotEmpty(seen); // context is tracked — at least one gauge measurement
        seen.Clear();

        observer.Untrack(ctx);

        listener.RecordObservableInstruments();
        Assert.Empty(seen); // untracked context must not appear in gauge
    }

    // ── Untrack stops delta counter emission ──────────────────────────────
    // RED: Untrack does not exist — compile error.
    [Fact]
    public async Task Untrack_StopsDeltaEmission()
    {
        var meterName = "test.ud." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0");
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        await using var ctx = new DatabaseContext(MakeConfig("ctx-b"), factory);
        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(ctx);

        long deltaAfterUntrack = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) => { if (inst.Meter.Name == meterName) l.EnableMeasurementEvents(inst); };
        listener.SetMeasurementEventCallback<long>((inst, val, tags, _) =>
        {
            if (inst.Name == "pengdows.db.client.commands.executed")
                Interlocked.Add(ref deltaAfterUntrack, val);
        });
        listener.Start();

        observer.Untrack(ctx);

        await ctx.CreateSqlContainer("SELECT 1").ExecuteScalarOrNullAsync<int>();
        await ctx.CreateSqlContainer("SELECT 1").ExecuteScalarOrNullAsync<int>();

        Assert.Equal(0, Interlocked.Read(ref deltaAfterUntrack));
    }

    // ── Untrack is idempotent ──────────────────────────────────────────────
    // RED: Untrack does not exist — compile error.
    [Fact]
    public void Untrack_IsIdempotent_DoesNotThrow()
    {
        using var meter = new Meter("test.idm." + Guid.NewGuid().ToString("N"), "1.0");
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(MakeConfig("idm"), factory);
        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(ctx);

        observer.Untrack(ctx);
        observer.Untrack(ctx); // second call must not throw
    }

    // ── Untrack on already-disposed context is safe ───────────────────────
    // RED: Untrack does not exist — compile error.
    [Fact]
    public void Untrack_DisposedContext_DoesNotThrow()
    {
        using var meter = new Meter("test.ud2." + Guid.NewGuid().ToString("N"), "1.0");
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var ctx = new DatabaseContext(MakeConfig("dispose-before-untrack"), factory);
        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(ctx);
        ctx.Dispose();

        observer.Untrack(ctx); // must not throw even though ctx is already disposed
    }

    // ── Two contexts tracked independently ────────────────────────────────
    // Regression guard: delta from ctxA must not bleed into ctxB tags.
    // Uses Sqlite + DuckDB so that db.name tags differ ("SQLite" vs "DuckDB"),
    // giving distinct measurements rather than SDK-aggregated identical ones.
    [Fact]
    public void Track_TwoContexts_ReportedWithSeparateTags()
    {
        var meterName = "test.two." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0");
        var sqliteFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var duckDbFactory = new fakeDbFactory(SupportedDatabase.DuckDB);

        using var ctxA = new DatabaseContext(MakeConfig("alpha"), sqliteFactory);
        using var ctxB = new DatabaseContext(MakeConfig("beta"), duckDbFactory);
        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(ctxA);
        observer.Track(ctxB);

        var seen = new List<string>();
        using var listener = MakeGaugeListener(meterName, seen);
        listener.RecordObservableInstruments();

        // db.name = product name (e.g. "SQLite", "DuckDB") — both must appear
        Assert.Contains("SQLite", seen, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("DuckDb", seen, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, seen.Count); // exactly one measurement per tracked context
    }

    // ── Default meter uses assembly version, not a hardcoded string ───────
    // RED: constructor hardcodes "2.0.1" — mismatch with assembly version.
    [Fact]
    public void DefaultMeter_UsesAssemblyVersion()
    {
        var expected = typeof(PengdowsMetricsObserver).Assembly.GetName().Version?.ToString(3)
                       ?? throw new InvalidOperationException("Assembly has no version");

        string? seen = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, _) =>
        {
            if (inst.Meter.Name == "pengdows.crud") seen = inst.Meter.Version;
        };
        listener.Start();

        using var observer = new PengdowsMetricsObserver();
        listener.RecordObservableInstruments();

        Assert.Equal(expected, seen);
    }

    // ── TelemetryService subscribes to ITenantContextRegistry.ContextCreated ──
    // RED: ITenantContextRegistry has no ContextCreated event — compile error.
    [Fact]
    public async Task TelemetryService_AutoTracks_ContextCreatedFromRegistry()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var fakeRegistry = new FakeTenantContextRegistry();
        var meterName = "test.svc.c." + Guid.NewGuid().ToString("N");

        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ITenantContextRegistry>(fakeRegistry)
            .AddSingleton<IPengdowsMetricsObserver>(_ => new PengdowsMetricsObserver(new Meter(meterName, "1.0")))
            .AddHostedService<PengdowsTelemetryService>()
            .BuildServiceProvider();

        foreach (var svc in sp.GetServices<IHostedService>())
            await svc.StartAsync(CancellationToken.None);

        using var ctx = new DatabaseContext(MakeConfig("created-tenant"), factory);
        fakeRegistry.SimulateContextCreated(ctx);

        var observer = (PengdowsMetricsObserver)sp.GetRequiredService<IPengdowsMetricsObserver>();
        Assert.True(observer.IsTracking(ctx));

        foreach (var svc in sp.GetServices<IHostedService>().Reverse())
            await svc.StopAsync(CancellationToken.None);
    }

    // ── TelemetryService subscribes to ITenantContextRegistry.ContextRemoved ─
    // RED: ITenantContextRegistry has no ContextRemoved event — compile error.
    [Fact]
    public async Task TelemetryService_AutoUntracks_ContextRemovedFromRegistry()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var fakeRegistry = new FakeTenantContextRegistry();
        var meterName = "test.svc.r." + Guid.NewGuid().ToString("N");

        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ITenantContextRegistry>(fakeRegistry)
            .AddSingleton<IPengdowsMetricsObserver>(_ => new PengdowsMetricsObserver(new Meter(meterName, "1.0")))
            .AddHostedService<PengdowsTelemetryService>()
            .BuildServiceProvider();

        foreach (var svc in sp.GetServices<IHostedService>())
            await svc.StartAsync(CancellationToken.None);

        using var ctx = new DatabaseContext(MakeConfig("removed-tenant"), factory);
        fakeRegistry.SimulateContextCreated(ctx);

        var observer = (PengdowsMetricsObserver)sp.GetRequiredService<IPengdowsMetricsObserver>();
        Assert.True(observer.IsTracking(ctx));

        fakeRegistry.SimulateContextRemoved(ctx);
        Assert.False(observer.IsTracking(ctx));

        foreach (var svc in sp.GetServices<IHostedService>().Reverse())
            await svc.StopAsync(CancellationToken.None);
    }

    // ── TelemetryService unsubscribes from registry on stop ──────────────
    // RED: service does not subscribe to registry events at all.
    [Fact]
    public async Task TelemetryService_UnsubscribesFromRegistry_OnStop()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var fakeRegistry = new FakeTenantContextRegistry();
        var meterName = "test.svc.s." + Guid.NewGuid().ToString("N");

        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ITenantContextRegistry>(fakeRegistry)
            .AddSingleton<IPengdowsMetricsObserver>(_ => new PengdowsMetricsObserver(new Meter(meterName, "1.0")))
            .AddHostedService<PengdowsTelemetryService>()
            .BuildServiceProvider();

        foreach (var svc in sp.GetServices<IHostedService>())
            await svc.StartAsync(CancellationToken.None);
        foreach (var svc in sp.GetServices<IHostedService>().Reverse())
            await svc.StopAsync(CancellationToken.None);

        // After stop, ContextCreated must no longer reach the observer
        using var ctx = new DatabaseContext(MakeConfig("after-stop-tenant"), factory);
        fakeRegistry.SimulateContextCreated(ctx);

        var observer = (PengdowsMetricsObserver)sp.GetRequiredService<IPengdowsMetricsObserver>();
        Assert.False(observer.IsTracking(ctx));
    }

    // ── Test double for ITenantContextRegistry ────────────────────────────
    // ContextCreated/ContextRemoved mirror the events on TenantContextRegistry
    // that are not yet on the interface (will be added in green phase).
    private sealed class FakeTenantContextRegistry : ITenantContextRegistry
    {
        public event Action<IDatabaseContext>? ContextCreated;
        public event Action<IDatabaseContext>? ContextRemoved;

        public IDatabaseContext GetContext(string tenant) => throw new NotSupportedException();
        public void Invalidate(string tenant) => throw new NotSupportedException();
        public void InvalidateAll() => throw new NotSupportedException();

        public void SimulateContextCreated(IDatabaseContext ctx) => ContextCreated?.Invoke(ctx);
        public void SimulateContextRemoved(IDatabaseContext ctx) => ContextRemoved?.Invoke(ctx);
    }
}
