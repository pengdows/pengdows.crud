using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using pengdows.crud.enums;
using pengdows.crud.metrics;

namespace pengdows.crud.opentelemetry;

/// <summary>
/// Observes <see cref="IDatabaseContext"/> instances and exports their metrics via System.Diagnostics.Metrics (OpenTelemetry).
/// </summary>
public sealed class PengdowsMetricsObserver : IPengdowsMetricsObserver
{
    private readonly Meter _meter;
    private readonly bool _ownsMeter;
    private readonly ConcurrentDictionary<Guid, DatabaseMetrics> _lastSnapshots = new();
    private readonly ConcurrentDictionary<Guid, IDatabaseContext> _contexts = new();

    // Counters (delta-based, driven by MetricsUpdated events)
    private readonly Counter<long> _commandsExecuted;
    private readonly Counter<long> _commandsFailed;
    private readonly Counter<long> _rowsRead;
    private readonly Counter<long> _rowsAffected;

    // Observable Gauges (current values, polled on collection)
    private readonly ObservableGauge<int> _connectionsCurrent;
    private readonly ObservableGauge<double> _commandDurationP95;

    // Pool gauges (polled per-context, per-role via GetPoolStatisticsSnapshot)
    private readonly ObservableGauge<int> _poolSlotsInUse;
    private readonly ObservableGauge<int> _poolSlotsQueued;
    private readonly ObservableGauge<int> _poolSlotsMax;
    private readonly ObservableGauge<double> _poolAvgWaitMs;

    public PengdowsMetricsObserver()
        : this(new Meter("pengdows.crud",
                   typeof(PengdowsMetricsObserver).Assembly.GetName().Version?.ToString(3)), true)
    {
    }

    internal PengdowsMetricsObserver(Meter meter) : this(meter, false)
    {
    }

    private PengdowsMetricsObserver(Meter meter, bool ownsMeter)
    {
        _meter = meter;
        _ownsMeter = ownsMeter;

        _commandsExecuted = _meter.CreateCounter<long>("pengdows.db.client.commands.executed", "{command}", "Total commands executed successfully");
        _commandsFailed = _meter.CreateCounter<long>("pengdows.db.client.commands.failed", "{command}", "Total commands failed");
        _rowsRead = _meter.CreateCounter<long>("pengdows.db.client.rows.read", "{row}", "Total rows read");
        _rowsAffected = _meter.CreateCounter<long>("pengdows.db.client.rows.affected", "{row}", "Total rows affected");

        _connectionsCurrent = _meter.CreateObservableGauge("pengdows.db.client.connections.current",
            () => GetGauges(m => m.ConnectionsCurrent), "{connection}", "Current open connections");

        _commandDurationP95 = _meter.CreateObservableGauge("pengdows.db.client.command.duration.p95",
            () => GetGauges(m => (double)m.P95CommandMs), "ms", "Approximate P95 command duration");

        // Pool gauges: one measurement per (context, pool label) pair.
        // Pool stats are obtained by polling GetPoolStatisticsSnapshot on each tracked
        // context — they are not available via the MetricsUpdated event path.
        _poolSlotsInUse = _meter.CreateObservableGauge("pengdows.db.client.pool.slots.in_use",
            () => GetPoolGauges(s => s.InUse), "{slot}", "Pool slots currently in use");

        _poolSlotsQueued = _meter.CreateObservableGauge("pengdows.db.client.pool.slots.queued",
            () => GetPoolGauges(s => s.Queued), "{slot}", "Pool slots currently queued (waiting for a slot)");

        _poolSlotsMax = _meter.CreateObservableGauge("pengdows.db.client.pool.slots.max",
            () => GetPoolGauges(s => s.MaxSlots), "{slot}", "Configured maximum pool slots");

        _poolAvgWaitMs = _meter.CreateObservableGauge("pengdows.db.client.pool.wait_duration_avg",
            () => GetPoolGaugesDouble(s => s.AverageWaitMs), "ms", "Average time waiting to acquire a pool slot");
    }

    public void Track(IDatabaseContext context)
    {
        if (!_contexts.TryAdd(context.RootId, context))
            return;

        _lastSnapshots[context.RootId] = context.Metrics;
        context.MetricsUpdated += HandleMetricsUpdated;
    }

    public void Untrack(IDatabaseContext context)
    {
        if (!_contexts.TryRemove(context.RootId, out _))
            return; // not tracked — idempotent

        _lastSnapshots.TryRemove(context.RootId, out _);
        context.MetricsUpdated -= HandleMetricsUpdated;
    }

    /// <summary>
    /// Returns true if this observer is currently tracking the given context.
    /// Intended for test assertions only.
    /// </summary>
    internal bool IsTracking(IDatabaseContext context) =>
        _contexts.ContainsKey(context.RootId);

    private void HandleMetricsUpdated(object? sender, DatabaseMetrics e)
    {
        if (sender is not IDatabaseContext context)
        {
            return;
        }

        if (_lastSnapshots.TryGetValue(context.RootId, out var last))
        {
            var tags = new TagList
            {
                { "db.name", context.Name },
                { "db.system", context.Product.ToString().ToLowerInvariant() }
            };

            // Emit aggregate (context-level) deltas. Role-specific data is not reliable
            // here: MetricsCollector propagates child→parent before incrementing the child
            // counter, so DatabaseMetrics.Read/Write counters are always stale by one
            // command when this event fires. Pool metrics are handled separately via
            // GetPoolGauges polling in the ObservableGauge callbacks.
            EmitDelta(_commandsExecuted, e.CommandsExecuted, last.CommandsExecuted, tags);
            EmitDelta(_commandsFailed, e.CommandsFailed, last.CommandsFailed, tags);
            EmitDelta(_rowsRead, e.RowsReadTotal, last.RowsReadTotal, tags);
            EmitDelta(_rowsAffected, e.RowsAffectedTotal, last.RowsAffectedTotal, tags);
        }

        _lastSnapshots[context.RootId] = e;
    }

    private static void EmitDelta(Counter<long> counter, long current, long last, TagList tags)
    {
        var delta = current - last;
        if (delta > 0)
        {
            counter.Add(delta, tags);
        }
    }

    private IEnumerable<Measurement<T>> GetGauges<T>(Func<DatabaseMetrics, T> selector) where T : struct
    {
        foreach (var kv in _lastSnapshots)
        {
            if (!_contexts.TryGetValue(kv.Key, out var context))
                continue;

            // Skip and lazily evict disposed contexts. A disposed context can appear
            // here when it was invalidated by TenantContextRegistry without an explicit
            // Untrack call (e.g. during Dispose of the registry itself).
            if (context.IsDisposed)
            {
                Untrack(context);
                continue;
            }

            var tags = new TagList { { "db.name", context.Name }, { "db.system", context.Product.ToString().ToLowerInvariant() } };
            yield return new Measurement<T>(selector(kv.Value), tags);
        }
    }

    private IEnumerable<Measurement<int>> GetPoolGauges(Func<PoolStatisticsSnapshot, int> selector)
    {
        foreach (var kv in _contexts)
        {
            var context = kv.Value;
            if (context.IsDisposed)
            {
                Untrack(context);
                continue;
            }

            foreach (var label in new[] { PoolLabel.Reader, PoolLabel.Writer })
            {
                var snapshot = context.GetPoolStatisticsSnapshot(label);
                var tags = new TagList
                {
                    { "db.name", context.Name },
                    { "db.system", context.Product.ToString().ToLowerInvariant() },
                    { "pool.label", label.ToString().ToLowerInvariant() }
                };
                yield return new Measurement<int>(selector(snapshot), tags);
            }
        }
    }

    private IEnumerable<Measurement<double>> GetPoolGaugesDouble(Func<PoolStatisticsSnapshot, double> selector)
    {
        foreach (var kv in _contexts)
        {
            var context = kv.Value;
            if (context.IsDisposed)
            {
                Untrack(context);
                continue;
            }

            foreach (var label in new[] { PoolLabel.Reader, PoolLabel.Writer })
            {
                var snapshot = context.GetPoolStatisticsSnapshot(label);
                var tags = new TagList
                {
                    { "db.name", context.Name },
                    { "db.system", context.Product.ToString().ToLowerInvariant() },
                    { "pool.label", label.ToString().ToLowerInvariant() }
                };
                yield return new Measurement<double>(selector(snapshot), tags);
            }
        }
    }

    public void Dispose()
    {
        foreach (var context in _contexts.Values)
        {
            context.MetricsUpdated -= HandleMetricsUpdated;
        }

        if (_ownsMeter)
        {
            _meter.Dispose();
        }
    }
}
