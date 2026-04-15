using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using pengdows.crud.enums;
using pengdows.crud.metrics;

namespace pengdows.crud.opentelemetry;

/// <summary>
/// Observes <see cref="IDatabaseContext"/> instances and exports their metrics via System.Diagnostics.Metrics (OpenTelemetry).
/// Wire up with: <c>meterProviderBuilder.AddMeter(PengdowsMetricsObserver.MeterName)</c>
/// </summary>
public sealed class PengdowsMetricsObserver : IPengdowsMetricsObserver
{
    /// <summary>The meter name to pass to <c>MeterProviderBuilder.AddMeter()</c>.</summary>
    public const string MeterName = "pengdows.crud";

    private readonly Meter _meter;
    private readonly bool _ownsMeter;
    private readonly ConcurrentDictionary<Guid, DatabaseMetrics> _lastSnapshots = new();
    private readonly ConcurrentDictionary<Guid, IDatabaseContext> _contexts = new();

    // ── Aggregate delta counters ──────────────────────────────────────────
    private readonly Counter<long> _commandsExecuted;
    private readonly Counter<long> _commandsFailed;
    private readonly Counter<long> _commandsTimedOut;
    private readonly Counter<long> _commandsCancelled;
    private readonly Counter<long> _commandsSlow;
    private readonly Counter<long> _rowsRead;
    private readonly Counter<long> _rowsAffected;
    private readonly Counter<long> _connectionsOpened;
    private readonly Counter<long> _connectionsClosed;
    private readonly Counter<long> _connectionsLongLived;
    private readonly Counter<long> _transactionsCommitted;
    private readonly Counter<long> _transactionsRolledBack;
    private readonly Counter<long> _errorsDeadlocks;
    private readonly Counter<long> _errorsSerializationFailures;
    private readonly Counter<long> _errorsConstraintViolations;
    private readonly Counter<long> _statementsPrepared;
    private readonly Counter<long> _statementsEvicted;
    private readonly Counter<long> _sessionInits;

    // ── Observable gauges — connection ────────────────────────────────────
    private readonly ObservableGauge<int> _connectionsCurrent;
    private readonly ObservableGauge<int> _connectionsPeak;
    private readonly ObservableGauge<double> _connectionsHoldDurationAvg;
    private readonly ObservableGauge<double> _connectionsOpenDurationAvg;
    private readonly ObservableGauge<double> _connectionsCloseDurationAvg;

    // ── Observable gauges — command latency ──────────────────────────────
    private readonly ObservableGauge<double> _commandDurationP95;
    private readonly ObservableGauge<double> _commandDurationP99;
    private readonly ObservableGauge<double> _commandDurationAvg;
    private readonly ObservableGauge<double> _commandFailedDurationAvg;

    // ── Observable gauges — transactions ─────────────────────────────────
    private readonly ObservableGauge<int> _transactionsActive;
    private readonly ObservableGauge<int> _transactionsPeak;
    private readonly ObservableGauge<double> _transactionsDurationAvg;
    private readonly ObservableGauge<double> _transactionsDurationP95;
    private readonly ObservableGauge<double> _transactionsDurationP99;

    // ── Observable gauges — prepared statements ───────────────────────────
    private readonly ObservableGauge<long> _statementsCached;

    // ── Observable gauges — session ───────────────────────────────────────
    private readonly ObservableGauge<double> _sessionInitDurationAvg;

    // ── Pool gauges (per context, per role) ───────────────────────────────
    private readonly ObservableGauge<int> _poolSlotsInUse;
    private readonly ObservableGauge<int> _poolSlotsPeak;
    private readonly ObservableGauge<int> _poolSlotsQueued;
    private readonly ObservableGauge<int> _poolSlotsMax;
    private readonly ObservableGauge<int> _poolTurnstileQueued;
    private readonly ObservableGauge<double> _poolAvgWaitMs;
    private readonly ObservableGauge<double> _poolHoldDurationAvg;

    // ── Pool observable counters (cumulative) ─────────────────────────────
    private readonly ObservableCounter<long> _poolAcquiredTotal;
    private readonly ObservableCounter<long> _poolSlotTimeoutsTotal;
    private readonly ObservableCounter<long> _poolTurnstileTimeoutsTotal;
    private readonly ObservableCounter<long> _poolCanceledWaitsTotal;

    public PengdowsMetricsObserver()
        : this(new Meter(MeterName,
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

        // Delta counters
        _commandsExecuted = _meter.CreateCounter<long>("pengdows.db.client.commands.executed", "{command}", "Total commands executed successfully");
        _commandsFailed = _meter.CreateCounter<long>("pengdows.db.client.commands.failed", "{command}", "Total commands failed");
        _commandsTimedOut = _meter.CreateCounter<long>("pengdows.db.client.commands.timed_out", "{command}", "Total commands that timed out");
        _commandsCancelled = _meter.CreateCounter<long>("pengdows.db.client.commands.cancelled", "{command}", "Total commands cancelled by caller");
        _commandsSlow = _meter.CreateCounter<long>("pengdows.db.client.commands.slow", "{command}", "Total commands exceeding slow-command threshold");
        _rowsRead = _meter.CreateCounter<long>("pengdows.db.client.rows.read", "{row}", "Total rows read");
        _rowsAffected = _meter.CreateCounter<long>("pengdows.db.client.rows.affected", "{row}", "Total rows affected");
        _connectionsOpened = _meter.CreateCounter<long>("pengdows.db.client.connections.opened", "{connection}", "Total connections opened");
        _connectionsClosed = _meter.CreateCounter<long>("pengdows.db.client.connections.closed", "{connection}", "Total connections closed");
        _connectionsLongLived = _meter.CreateCounter<long>("pengdows.db.client.connections.long_lived", "{connection}", "Total connections held longer than configured threshold");
        _transactionsCommitted = _meter.CreateCounter<long>("pengdows.db.client.transactions.committed", "{transaction}", "Total transactions successfully committed");
        _transactionsRolledBack = _meter.CreateCounter<long>("pengdows.db.client.transactions.rolled_back", "{transaction}", "Total transactions rolled back");
        _errorsDeadlocks = _meter.CreateCounter<long>("pengdows.db.client.errors.deadlocks", "{error}", "Total deadlock errors detected");
        _errorsSerializationFailures = _meter.CreateCounter<long>("pengdows.db.client.errors.serialization_failures", "{error}", "Total serialization conflict errors");
        _errorsConstraintViolations = _meter.CreateCounter<long>("pengdows.db.client.errors.constraint_violations", "{error}", "Total constraint violation errors");
        _statementsPrepared = _meter.CreateCounter<long>("pengdows.db.client.statements.prepared", "{statement}", "Total commands successfully prepared");
        _statementsEvicted = _meter.CreateCounter<long>("pengdows.db.client.statements.evicted", "{statement}", "Total prepared statement shapes evicted from cache");
        _sessionInits = _meter.CreateCounter<long>("pengdows.db.client.session.inits", "{connection}", "Total physical connections on which session settings were applied");

        // Connection gauges
        _connectionsCurrent = _meter.CreateObservableGauge("pengdows.db.client.connections.current",
            () => GetGauges(m => m.ConnectionsCurrent), "{connection}", "Current open connections");
        _connectionsPeak = _meter.CreateObservableGauge("pengdows.db.client.connections.peak",
            () => GetGauges(m => m.PeakOpenConnections), "{connection}", "Peak open connections observed");
        _connectionsHoldDurationAvg = _meter.CreateObservableGauge("pengdows.db.client.connections.hold_duration.avg",
            () => GetGauges(m => m.AvgConnectionHoldMs), "ms", "EMA of connection hold duration");
        _connectionsOpenDurationAvg = _meter.CreateObservableGauge("pengdows.db.client.connections.open_duration.avg",
            () => GetGauges(m => m.AvgConnectionOpenMs), "ms", "EMA of time to open a connection");
        _connectionsCloseDurationAvg = _meter.CreateObservableGauge("pengdows.db.client.connections.close_duration.avg",
            () => GetGauges(m => m.AvgConnectionCloseMs), "ms", "EMA of time to close a connection");

        // Command latency gauges
        _commandDurationP95 = _meter.CreateObservableGauge("pengdows.db.client.command.duration.p95",
            () => GetGauges(m => (double)m.P95CommandMs), "ms", "Approximate P95 command duration");
        _commandDurationP99 = _meter.CreateObservableGauge("pengdows.db.client.command.duration.p99",
            () => GetGauges(m => m.P99CommandMs), "ms", "Approximate P99 command duration");
        _commandDurationAvg = _meter.CreateObservableGauge("pengdows.db.client.command.duration.avg",
            () => GetGauges(m => m.AvgCommandMs), "ms", "EMA of command duration");
        _commandFailedDurationAvg = _meter.CreateObservableGauge("pengdows.db.client.command.failed_duration.avg",
            () => GetGauges(m => m.AvgFailedCommandMs), "ms", "EMA of failed command duration");

        // Transaction gauges
        _transactionsActive = _meter.CreateObservableGauge("pengdows.db.client.transactions.active",
            () => GetGauges(m => m.TransactionsActive), "{transaction}", "Currently active transactions");
        _transactionsPeak = _meter.CreateObservableGauge("pengdows.db.client.transactions.peak",
            () => GetGauges(m => m.TransactionsMax), "{transaction}", "Peak concurrent transactions observed");
        _transactionsDurationAvg = _meter.CreateObservableGauge("pengdows.db.client.transactions.duration.avg",
            () => GetGauges(m => m.AvgTransactionMs), "ms", "EMA of transaction duration");
        _transactionsDurationP95 = _meter.CreateObservableGauge("pengdows.db.client.transactions.duration.p95",
            () => GetGauges(m => m.P95TransactionMs), "ms", "Approximate P95 transaction duration");
        _transactionsDurationP99 = _meter.CreateObservableGauge("pengdows.db.client.transactions.duration.p99",
            () => GetGauges(m => m.P99TransactionMs), "ms", "Approximate P99 transaction duration");

        // Statement / session gauges
        _statementsCached = _meter.CreateObservableGauge("pengdows.db.client.statements.cached",
            () => GetGauges(m => m.StatementsCached), "{statement}", "Cached prepared statement shapes");
        _sessionInitDurationAvg = _meter.CreateObservableGauge("pengdows.db.client.session.init_duration.avg",
            () => GetGauges(m => m.AvgSessionInitMs), "ms", "EMA of session settings application time");

        // Pool gauges
        _poolSlotsInUse = _meter.CreateObservableGauge("pengdows.db.client.pool.slots.in_use",
            () => GetPoolGauges(s => s.InUse), "{slot}", "Pool slots currently in use");
        _poolSlotsPeak = _meter.CreateObservableGauge("pengdows.db.client.pool.slots.peak",
            () => GetPoolGauges(s => s.PeakInUse), "{slot}", "Peak pool slots in use");
        _poolSlotsQueued = _meter.CreateObservableGauge("pengdows.db.client.pool.slots.queued",
            () => GetPoolGauges(s => s.Queued), "{slot}", "Pool slots currently queued");
        _poolSlotsMax = _meter.CreateObservableGauge("pengdows.db.client.pool.slots.max",
            () => GetPoolGauges(s => s.MaxSlots), "{slot}", "Configured maximum pool slots");
        _poolTurnstileQueued = _meter.CreateObservableGauge("pengdows.db.client.pool.turnstile.queued",
            () => GetPoolGauges(s => s.TurnstileQueued), "{slot}", "Operations waiting for fairness turnstile");
        _poolAvgWaitMs = _meter.CreateObservableGauge("pengdows.db.client.pool.wait_duration_avg",
            () => GetPoolGaugesDouble(s => s.AverageWaitMs), "ms", "Average time waiting to acquire a pool slot");
        _poolHoldDurationAvg = _meter.CreateObservableGauge("pengdows.db.client.pool.hold_duration.avg",
            () => GetPoolGaugesDouble(s => s.AverageHoldMs), "ms", "Average time a pool slot was held");

        // Pool observable counters (cumulative — backends compute rate via rate())
        _poolAcquiredTotal = _meter.CreateObservableCounter("pengdows.db.client.pool.acquired_total",
            () => GetPoolCounters(s => s.TotalAcquired), "{acquisition}", "Total pool slot acquisitions");
        _poolSlotTimeoutsTotal = _meter.CreateObservableCounter("pengdows.db.client.pool.slot_timeouts_total",
            () => GetPoolCounters(s => s.TotalSlotTimeouts), "{timeout}", "Total slot acquisition timeouts");
        _poolTurnstileTimeoutsTotal = _meter.CreateObservableCounter("pengdows.db.client.pool.turnstile_timeouts_total",
            () => GetPoolCounters(s => s.TotalTurnstileTimeouts), "{timeout}", "Total turnstile acquisition timeouts");
        _poolCanceledWaitsTotal = _meter.CreateObservableCounter("pengdows.db.client.pool.canceled_waits_total",
            () => GetPoolCounters(s => s.TotalCanceledWaits), "{cancellation}", "Total canceled pool wait operations");
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
            return;

        _lastSnapshots.TryRemove(context.RootId, out _);
        context.MetricsUpdated -= HandleMetricsUpdated;
    }

    internal bool IsTracking(IDatabaseContext context) =>
        _contexts.ContainsKey(context.RootId);

    private void HandleMetricsUpdated(object? sender, DatabaseMetrics e)
    {
        if (sender is not IDatabaseContext context)
            return;

        if (!_lastSnapshots.TryGetValue(context.RootId, out var last))
            return;

        var tags = new TagList
        {
            { "db.name", context.Name },
            { "db.system", context.Product.ToString().ToLowerInvariant() }
        };

        // Commands
        EmitDelta(_commandsExecuted, e.CommandsExecuted, last.CommandsExecuted, tags);
        EmitDelta(_commandsFailed, e.CommandsFailed, last.CommandsFailed, tags);
        EmitDelta(_commandsTimedOut, e.CommandsTimedOut, last.CommandsTimedOut, tags);
        EmitDelta(_commandsCancelled, e.CommandsCancelled, last.CommandsCancelled, tags);
        EmitDelta(_commandsSlow, e.SlowCommandsTotal, last.SlowCommandsTotal, tags);

        // Rows
        EmitDelta(_rowsRead, e.RowsReadTotal, last.RowsReadTotal, tags);
        EmitDelta(_rowsAffected, e.RowsAffectedTotal, last.RowsAffectedTotal, tags);

        // Connections
        EmitDelta(_connectionsOpened, e.ConnectionsOpened, last.ConnectionsOpened, tags);
        EmitDelta(_connectionsClosed, e.ConnectionsClosed, last.ConnectionsClosed, tags);
        EmitDelta(_connectionsLongLived, e.LongLivedConnections, last.LongLivedConnections, tags);

        // Transactions
        EmitDelta(_transactionsCommitted, e.TransactionsCommitted, last.TransactionsCommitted, tags);
        EmitDelta(_transactionsRolledBack, e.TransactionsRolledBack, last.TransactionsRolledBack, tags);

        // Errors
        EmitDelta(_errorsDeadlocks, e.ErrorDeadlocks, last.ErrorDeadlocks, tags);
        EmitDelta(_errorsSerializationFailures, e.ErrorSerializationFailures, last.ErrorSerializationFailures, tags);
        EmitDelta(_errorsConstraintViolations, e.ErrorConstraintViolations, last.ErrorConstraintViolations, tags);

        // Statements / session
        EmitDelta(_statementsPrepared, e.PreparedStatements, last.PreparedStatements, tags);
        EmitDelta(_statementsEvicted, e.StatementsEvicted, last.StatementsEvicted, tags);
        EmitDelta(_sessionInits, e.SessionInitCount, last.SessionInitCount, tags);

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

            if (context.IsDisposed)
            {
                Untrack(context);
                continue;
            }

            var tags = new TagList
            {
                { "db.name", context.Name },
                { "db.system", context.Product.ToString().ToLowerInvariant() }
            };
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

    private IEnumerable<Measurement<long>> GetPoolCounters(Func<PoolStatisticsSnapshot, long> selector)
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
                yield return new Measurement<long>(selector(snapshot), tags);
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
