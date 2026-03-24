using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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

    // Counters
    private readonly Counter<long> _commandsExecuted;
    private readonly Counter<long> _commandsFailed;
    private readonly Counter<long> _rowsRead;
    private readonly Counter<long> _rowsAffected;

    // Observable Gauges (for values that don't need delta calculation)
    private readonly ObservableGauge<int> _connectionsCurrent;
    private readonly ObservableGauge<double> _commandDurationP95;

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

            // Emit deltas for counters
            EmitDelta(_commandsExecuted, e.CommandsExecuted, last.CommandsExecuted, tags);
            EmitDelta(_commandsFailed, e.CommandsFailed, last.CommandsFailed, tags);
            EmitDelta(_rowsRead, e.RowsReadTotal, last.RowsReadTotal, tags);
            EmitDelta(_rowsAffected, e.RowsAffectedTotal, last.RowsAffectedTotal, tags);

            // Role deltas
            EmitRoleDeltas(context, e, last);
        }

        _lastSnapshots[context.RootId] = e;
    }

    private void EmitRoleDeltas(IDatabaseContext context, DatabaseMetrics current, DatabaseMetrics last)
    {
        var readTags = new TagList { { "db.name", context.Name }, { "db.system", context.Product.ToString().ToLowerInvariant() }, { "execution.role", "read" } };
        EmitDelta(_commandsExecuted, current.Read.CommandsExecuted, last.Read.CommandsExecuted, readTags);

        var writeTags = new TagList { { "db.name", context.Name }, { "db.system", context.Product.ToString().ToLowerInvariant() }, { "execution.role", "write" } };
        EmitDelta(_commandsExecuted, current.Write.CommandsExecuted, last.Write.CommandsExecuted, writeTags);
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
