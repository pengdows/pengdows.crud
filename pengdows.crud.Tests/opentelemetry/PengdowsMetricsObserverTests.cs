using System;
using System.Collections.Generic;
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

    // ── EmitRoleDeltas dead-code removal ─────────────────────────────────
    // EmitRoleDeltas emits to _commandsExecuted with execution.role tags. Due to
    // parent-first ordering in MetricsCollector.CommandSucceeded, role counters are
    // always 0 when the event fires, so EmitRoleDeltas never actually emits — it is
    // dead code. Removing it eliminates a potential double-count if ordering changes.
    //
    // This test verifies the exact-once invariant: 1 command → total delta == 1
    // (from the aggregate path in HandleMetricsUpdated).
    [Fact]
    public async Task CommandsExecuted_ExactDeltaOfOne_PerSingleCommand()
    {
        var meterName = "test.dc." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0");
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = new DatabaseContext(MakeConfig("dc-test"), factory);

        long totalDelta = 0;
        var anySeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) => { if (inst.Meter.Name == meterName) l.EnableMeasurementEvents(inst); };
        listener.SetMeasurementEventCallback<long>((inst, val, _, _) =>
        {
            if (inst.Name == "pengdows.db.client.commands.executed")
            {
                Interlocked.Add(ref totalDelta, val);
                anySeen.TrySetResult(true);
            }
        });
        listener.Start();

        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(ctx);

        await ctx.CreateSqlContainer("SELECT 1").ExecuteScalarOrNullAsync<int>();

        await anySeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        Assert.Equal(1L, Interlocked.Read(ref totalDelta));
    }

    // ── Pool metric gauges ────────────────────────────────────────────────
    // RED: No pool instruments exist in PengdowsMetricsObserver — instrument not published.
    [Fact]
    public void PoolGauge_SlotsInUse_EmittedWithPoolLabelTagForEachRole()
    {
        var meterName = "test.pool.iu." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0");
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(MakeConfig("pool-iu-test"), factory);
        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(ctx);

        var poolLabels = new List<string?>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) => { if (inst.Meter.Name == meterName) l.EnableMeasurementEvents(inst); };
        listener.SetMeasurementEventCallback<int>((inst, _, tags, _) =>
        {
            if (inst.Name == "pengdows.db.client.pool.slots.in_use")
                poolLabels.Add(GetTagValue(tags, "pool.label"));
        });
        listener.Start();
        listener.RecordObservableInstruments();

        // One measurement per pool label (reader + writer) per tracked context
        Assert.Equal(2, poolLabels.Count);
        Assert.Contains("reader", poolLabels);
        Assert.Contains("writer", poolLabels);
    }

    [Fact]
    public void PoolGauge_SlotsQueued_EmittedForTrackedContext()
    {
        var meterName = "test.pool.q." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0");
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(MakeConfig("pool-q-test"), factory);
        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(ctx);

        var measuredCount = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) => { if (inst.Meter.Name == meterName) l.EnableMeasurementEvents(inst); };
        listener.SetMeasurementEventCallback<int>((inst, _, _, _) =>
        {
            if (inst.Name == "pengdows.db.client.pool.slots.queued")
                Interlocked.Increment(ref measuredCount);
        });
        listener.Start();
        listener.RecordObservableInstruments();

        // 2 pool roles × 1 context = 2 measurements
        Assert.Equal(2, measuredCount);
    }

    [Fact]
    public void PoolGauge_StopsEmitting_AfterUntrack()
    {
        var meterName = "test.pool.ut." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0");
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(MakeConfig("pool-ut-test"), factory);
        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(ctx);

        var measuredCount = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) => { if (inst.Meter.Name == meterName) l.EnableMeasurementEvents(inst); };
        listener.SetMeasurementEventCallback<int>((inst, _, _, _) =>
        {
            if (inst.Name == "pengdows.db.client.pool.slots.in_use")
                Interlocked.Increment(ref measuredCount);
        });
        listener.Start();

        observer.Untrack(ctx);
        listener.RecordObservableInstruments();

        Assert.Equal(0, measuredCount); // no measurements after untrack
    }

    // ── New instrument existence ──────────────────────────────────────────
    [Theory]
    // New delta counters
    [InlineData("pengdows.db.client.commands.timed_out")]
    [InlineData("pengdows.db.client.commands.cancelled")]
    [InlineData("pengdows.db.client.commands.slow")]
    [InlineData("pengdows.db.client.connections.opened")]
    [InlineData("pengdows.db.client.connections.closed")]
    [InlineData("pengdows.db.client.connections.long_lived")]
    [InlineData("pengdows.db.client.transactions.committed")]
    [InlineData("pengdows.db.client.transactions.rolled_back")]
    [InlineData("pengdows.db.client.errors.deadlocks")]
    [InlineData("pengdows.db.client.errors.serialization_failures")]
    [InlineData("pengdows.db.client.errors.constraint_violations")]
    [InlineData("pengdows.db.client.statements.prepared")]
    [InlineData("pengdows.db.client.statements.evicted")]
    [InlineData("pengdows.db.client.session.inits")]
    // New double gauges
    [InlineData("pengdows.db.client.command.duration.avg")]
    [InlineData("pengdows.db.client.command.duration.p99")]
    [InlineData("pengdows.db.client.command.failed_duration.avg")]
    [InlineData("pengdows.db.client.connections.hold_duration.avg")]
    [InlineData("pengdows.db.client.connections.open_duration.avg")]
    [InlineData("pengdows.db.client.connections.close_duration.avg")]
    [InlineData("pengdows.db.client.transactions.duration.avg")]
    [InlineData("pengdows.db.client.transactions.duration.p95")]
    [InlineData("pengdows.db.client.transactions.duration.p99")]
    [InlineData("pengdows.db.client.session.init_duration.avg")]
    [InlineData("pengdows.db.client.pool.hold_duration.avg")]
    // New int gauges
    [InlineData("pengdows.db.client.connections.peak")]
    [InlineData("pengdows.db.client.transactions.active")]
    [InlineData("pengdows.db.client.transactions.peak")]
    [InlineData("pengdows.db.client.pool.slots.peak")]
    [InlineData("pengdows.db.client.pool.turnstile.queued")]
    // New long gauge
    [InlineData("pengdows.db.client.statements.cached")]
    // New pool observable counters
    [InlineData("pengdows.db.client.pool.acquired_total")]
    [InlineData("pengdows.db.client.pool.slot_timeouts_total")]
    [InlineData("pengdows.db.client.pool.turnstile_timeouts_total")]
    [InlineData("pengdows.db.client.pool.canceled_waits_total")]
    public void NewInstrument_IsPublished(string instrumentName)
    {
        var meterName = "test.inst." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0");

        var seen = false;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, _) =>
        {
            if (inst.Meter.Name == meterName && inst.Name == instrumentName)
            {
                seen = true;
            }
        };
        listener.Start();

        using var observer = new PengdowsMetricsObserver(meter);

        Assert.True(seen, $"Instrument '{instrumentName}' was not published");
    }

    [Fact]
    public void MeterName_Constant_IsCorrect()
    {
        Assert.Equal("pengdows.crud", PengdowsMetricsObserver.MeterName);
    }

    // ── New gauge value emission on poll ──────────────────────────────────
    [Theory]
    [InlineData("pengdows.db.client.command.duration.avg")]
    [InlineData("pengdows.db.client.command.duration.p99")]
    [InlineData("pengdows.db.client.command.failed_duration.avg")]
    [InlineData("pengdows.db.client.connections.hold_duration.avg")]
    [InlineData("pengdows.db.client.connections.open_duration.avg")]
    [InlineData("pengdows.db.client.connections.close_duration.avg")]
    [InlineData("pengdows.db.client.transactions.duration.avg")]
    [InlineData("pengdows.db.client.transactions.duration.p95")]
    [InlineData("pengdows.db.client.transactions.duration.p99")]
    [InlineData("pengdows.db.client.session.init_duration.avg")]
    [InlineData("pengdows.db.client.pool.hold_duration.avg")]
    public void NewDoubleGauge_EmitsMeasurementOnPoll(string gaugeName)
    {
        var meterName = "test.dg." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0");
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(MakeConfig("dg-test"), factory);
        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(ctx);

        var count = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) =>
        {
            if (inst.Meter.Name == meterName)
            {
                l.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<double>((inst, _, _, _) =>
        {
            if (inst.Name == gaugeName)
            {
                Interlocked.Increment(ref count);
            }
        });
        listener.Start();
        listener.RecordObservableInstruments();

        Assert.True(count >= 1, $"Expected gauge '{gaugeName}' to emit at least one measurement on poll");
    }

    [Theory]
    [InlineData("pengdows.db.client.connections.peak")]
    [InlineData("pengdows.db.client.transactions.active")]
    [InlineData("pengdows.db.client.transactions.peak")]
    [InlineData("pengdows.db.client.pool.slots.peak")]
    [InlineData("pengdows.db.client.pool.turnstile.queued")]
    public void NewIntGauge_EmitsMeasurementOnPoll(string gaugeName)
    {
        var meterName = "test.ig." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0");
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(MakeConfig("ig-test"), factory);
        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(ctx);

        var count = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) => { if (inst.Meter.Name == meterName) l.EnableMeasurementEvents(inst); };
        listener.SetMeasurementEventCallback<int>((inst, _, _, _) =>
        {
            if (inst.Name == gaugeName) Interlocked.Increment(ref count);
        });
        listener.Start();
        listener.RecordObservableInstruments();

        Assert.True(count >= 1, $"Expected gauge '{gaugeName}' to emit at least one measurement on poll");
    }

    [Fact]
    public void StatementsCachedGauge_EmitsMeasurementOnPoll()
    {
        var meterName = "test.sc." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0");
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(MakeConfig("sc-test"), factory);
        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(ctx);

        var count = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) => { if (inst.Meter.Name == meterName) l.EnableMeasurementEvents(inst); };
        listener.SetMeasurementEventCallback<long>((inst, _, _, _) =>
        {
            if (inst.Name == "pengdows.db.client.statements.cached") Interlocked.Increment(ref count);
        });
        listener.Start();
        listener.RecordObservableInstruments();

        Assert.True(count >= 1, "Expected statements.cached gauge to emit at least one measurement on poll");
    }

    [Theory]
    [InlineData("pengdows.db.client.pool.acquired_total")]
    [InlineData("pengdows.db.client.pool.slot_timeouts_total")]
    [InlineData("pengdows.db.client.pool.turnstile_timeouts_total")]
    [InlineData("pengdows.db.client.pool.canceled_waits_total")]
    public void PoolObservableCounter_EmitsMeasurementOnPoll(string counterName)
    {
        var meterName = "test.poc." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0");
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(MakeConfig("poc-test"), factory);
        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(ctx);

        var count = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) => { if (inst.Meter.Name == meterName) l.EnableMeasurementEvents(inst); };
        listener.SetMeasurementEventCallback<long>((inst, _, _, _) =>
        {
            if (inst.Name == counterName) Interlocked.Increment(ref count);
        });
        listener.Start();
        listener.RecordObservableInstruments();

        // 2 pool labels × 1 context = 2 measurements
        Assert.Equal(2, count);
    }

    // ── Transaction counter deltas ────────────────────────────────────────
    [Fact]
    public async Task TransactionCommitted_Counter_EmitsDeltaAfterCommit()
    {
        var meterName = "test.txc." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0");
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = new DatabaseContext(MakeConfig("txc-test"), factory);

        long totalCommitted = 0;
        var commandSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) => { if (inst.Meter.Name == meterName) l.EnableMeasurementEvents(inst); };
        listener.SetMeasurementEventCallback<long>((inst, val, _, _) =>
        {
            if (inst.Name == "pengdows.db.client.transactions.committed")
                Interlocked.Add(ref totalCommitted, val);
            if (inst.Name == "pengdows.db.client.commands.executed")
                commandSeen.TrySetResult(true);
        });
        listener.Start();

        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(ctx);

        using (var txn = ctx.BeginTransaction())
        {
            await ctx.CreateSqlContainer("SELECT 1").ExecuteScalarOrNullAsync<int>();
            txn.Commit();
        }
        // Second command fires MetricsUpdated with the post-commit snapshot
        await ctx.CreateSqlContainer("SELECT 1").ExecuteScalarOrNullAsync<int>();

        await commandSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        Assert.True(Interlocked.Read(ref totalCommitted) >= 1,
            "Expected transactions.committed delta >= 1 after commit");
    }

    [Fact]
    public async Task TransactionRolledBack_Counter_EmitsDeltaAfterRollback()
    {
        var meterName = "test.txr." + Guid.NewGuid().ToString("N");
        using var meter = new Meter(meterName, "1.0");
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = new DatabaseContext(MakeConfig("txr-test"), factory);

        long totalRolledBack = 0;
        var commandSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) => { if (inst.Meter.Name == meterName) l.EnableMeasurementEvents(inst); };
        listener.SetMeasurementEventCallback<long>((inst, val, _, _) =>
        {
            if (inst.Name == "pengdows.db.client.transactions.rolled_back")
                Interlocked.Add(ref totalRolledBack, val);
            if (inst.Name == "pengdows.db.client.commands.executed")
                commandSeen.TrySetResult(true);
        });
        listener.Start();

        using var observer = new PengdowsMetricsObserver(meter);
        observer.Track(ctx);

        using (var txn = ctx.BeginTransaction())
        {
            await ctx.CreateSqlContainer("SELECT 1").ExecuteScalarOrNullAsync<int>();
            txn.Rollback();
        }
        await ctx.CreateSqlContainer("SELECT 1").ExecuteScalarOrNullAsync<int>();

        await commandSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        Assert.True(Interlocked.Read(ref totalRolledBack) >= 1,
            "Expected transactions.rolled_back delta >= 1 after rollback");
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
