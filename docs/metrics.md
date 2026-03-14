# Metrics Reference

`pengdows.crud` exposes per-context metrics through `IDatabaseContext.Metrics` and `IDatabaseContext.MetricsUpdated`.

- `Metrics` returns a `DatabaseMetrics` snapshot.
- `MetricsUpdated` pushes snapshots whenever the collector records a new observation.
- `DatabaseMetrics.Read` and `DatabaseMetrics.Write` contain role-scoped `DatabaseRoleMetrics` snapshots for read and write execution paths.

Enable metrics via `DatabaseContextConfiguration.EnableMetrics = true`.

```csharp
var config = new DatabaseContextConfiguration
{
    ConnectionString = connectionString,
    EnableMetrics = true
};

var context = new DatabaseContext(config, factory);

context.MetricsUpdated += (sender, metrics) =>
{
    Console.WriteLine($"P95 command latency: {metrics.P95CommandMs}ms");
};

var snapshot = context.Metrics;
Console.WriteLine($"Open connections: {snapshot.ConnectionsCurrent}");
```

## DatabaseMetrics

`DatabaseMetrics` currently contains 36 top-level fields plus the `Read` and `Write` role snapshots.

| Metric | Meaning |
|--------|---------|
| `ConnectionsCurrent` | Current number of open connections held by the context. |
| `PeakOpenConnections` | Highest number of concurrently open connections observed since the context was created. |
| `ConnectionsOpened` | Total number of connections opened since the context was created. |
| `ConnectionsClosed` | Total number of connections closed since the context was created. |
| `AvgConnectionHoldMs` | Exponentially weighted moving average of how long connections are held before being closed or released. |
| `AvgConnectionOpenMs` | Exponentially weighted moving average of connection-open time. This helps isolate network/authentication latency. |
| `AvgConnectionCloseMs` | Exponentially weighted moving average of connection-close time. |
| `LongLivedConnections` | Count of connections whose hold time met or exceeded `MetricsOptions.LongConnectionThreshold`. |
| `CommandsExecuted` | Total number of commands that completed successfully. |
| `CommandsFailed` | Total number of commands that failed for any reason, including timeout and cancellation. |
| `CommandsTimedOut` | Number of commands that failed specifically because of a timeout. |
| `CommandsCancelled` | Number of commands cancelled through a `CancellationToken`. |
| `AvgCommandMs` | Exponentially weighted moving average of command duration. |
| `P95CommandMs` | Approximate 95th percentile command duration from the configured percentile window. |
| `P99CommandMs` | Approximate 99th percentile command duration from the configured percentile window. |
| `MaxParametersObserved` | Largest parameter count seen on a single command. Useful for spotting oversized statements. |
| `RowsReadTotal` | Total number of rows read from data readers. |
| `RowsAffectedTotal` | Total number of rows affected by non-query work and tracked reader completions. |
| `PreparedStatements` | Number of commands successfully prepared. |
| `StatementsCached` | Number of unique prepared statement shapes added to the statement cache. |
| `StatementsEvicted` | Number of cached statement shapes evicted from the cache. |
| `TransactionsActive` | Number of currently active transactions. |
| `TransactionsMax` | Highest number of concurrent active transactions observed. |
| `AvgTransactionMs` | Exponentially weighted moving average of transaction duration. |
| `TransactionsCommitted` | Total number of transactions that committed successfully. |
| `TransactionsRolledBack` | Total number of transactions that rolled back, including disposal-triggered rollback paths. |
| `SlowCommandsTotal` | Count of commands whose duration met or exceeded `MetricsOptions.SlowCommandThreshold`. |
| `P95TransactionMs` | Approximate 95th percentile transaction duration. |
| `P99TransactionMs` | Approximate 99th percentile transaction duration. |
| `ErrorDeadlocks` | Count of database exceptions classified as deadlocks by the active dialect. |
| `ErrorSerializationFailures` | Count of database exceptions classified as serialization or snapshot-isolation conflicts. |
| `ErrorConstraintViolations` | Count of database exceptions classified as constraint violations, such as unique, FK, not-null, or check failures. |
| `SessionInitCount` | Number of physical connections on which session initialization SQL was applied. |
| `AvgSessionInitMs` | Exponentially weighted moving average of session initialization time, such as `SET` statements executed after opening a connection. |
| `Read` | `DatabaseRoleMetrics` snapshot scoped to read execution paths only. |
| `Write` | `DatabaseRoleMetrics` snapshot scoped to write execution paths only. |

## DatabaseRoleMetrics

`DatabaseRoleMetrics` mirrors the operational fields from `DatabaseMetrics` for one execution lane:

- Connections: `ConnectionsCurrent`, `PeakOpenConnections`, `ConnectionsOpened`, `ConnectionsClosed`, `AvgConnectionHoldMs`, `AvgConnectionOpenMs`, `AvgConnectionCloseMs`, `LongLivedConnections`
- Commands: `CommandsExecuted`, `CommandsFailed`, `CommandsTimedOut`, `CommandsCancelled`, `AvgCommandMs`, `P95CommandMs`, `P99CommandMs`, `MaxParametersObserved`
- Rows and statements: `RowsReadTotal`, `RowsAffectedTotal`, `PreparedStatements`, `StatementsCached`, `StatementsEvicted`
- Transactions: `TransactionsActive`, `TransactionsMax`, `AvgTransactionMs`, `TransactionsCommitted`, `TransactionsRolledBack`, `SlowCommandsTotal`, `P95TransactionMs`, `P99TransactionMs`
- Errors: `ErrorDeadlocks`, `ErrorSerializationFailures`, `ErrorConstraintViolations`

Use these when you need to distinguish read pressure from write pressure. For example, a high `Read.P95CommandMs` with a low `Write.P95CommandMs` usually points to reader contention, query shape, or pool pressure isolated to the read path.

## Pool Governor Metrics

`DatabaseContext` also exposes pool snapshots internally and in diagnostic paths through `PoolStatisticsSnapshot`.

| Metric | Meaning |
|--------|---------|
| `Label` | Which governor the snapshot came from, typically reader or writer. |
| `PoolKeyHash` | Stable hash of the pool key used for diagnostic correlation without exposing the raw connection string. |
| `MaxSlots` | Configured maximum number of slots in the governor. |
| `InUse` | Current number of acquired slots. |
| `PeakInUse` | Highest number of concurrently acquired slots observed. |
| `Queued` | Number of operations currently waiting for a slot. |
| `PeakQueued` | Highest slot-wait queue depth observed. |
| `TurnstileQueued` | Number of operations currently waiting on the fairness turnstile. |
| `PeakTurnstileQueued` | Highest turnstile queue depth observed. |
| `TotalAcquired` | Lifetime count of successful slot acquisitions. |
| `TotalWaitTicks` | Cumulative slot wait time in `Stopwatch` ticks. |
| `TotalHoldTicks` | Cumulative slot hold time in `Stopwatch` ticks. |
| `TotalSlotTimeouts` | Number of waits that timed out while waiting for a slot. |
| `TotalTurnstileTimeouts` | Number of waits that timed out while waiting at the fairness turnstile. |
| `TotalCanceledWaits` | Number of waits cancelled before acquisition completed. |
| `Disabled` | Indicates that the governor is disabled and not actively limiting concurrency. |
| `Forbidden` | Indicates that acquisition is forbidden, such as a zero-slot configuration. |

## Mode Contention Metrics

Single-writer and single-connection modes can surface lock-contention diagnostics through `ModeContentionSnapshot`.

| Metric | Meaning |
|--------|---------|
| `CurrentWaiters` | Number of operations currently waiting for the mode lock. |
| `PeakWaiters` | Highest number of concurrent mode-lock waiters observed. |
| `TotalWaits` | Lifetime count of waits for the mode lock. |
| `TotalTimeouts` | Lifetime count of mode-lock waits that timed out. |
| `TotalWaitTimeTicks` | Cumulative wait time spent blocked on the mode lock. |
| `AverageWaitTimeTicks` | Average wait time per recorded mode-lock wait. |

## Internal Attribution Metrics

The runtime also tracks internal request-attribution counters used for diagnostics and testing. These are not part of the main public `DatabaseMetrics` contract.

| Metric | Meaning |
|--------|---------|
| `ReadRequests` | Total operations attributed to the read path. |
| `WriteRequests` | Total operations attributed to the write path. |
| `ReadGovernorWaits` | Read operations that had to wait on the pool governor. |
| `WriteGovernorWaits` | Write operations that had to wait on the pool governor. |
| `ReadGovernorTimeouts` | Read operations that timed out waiting on the pool governor. |
| `WriteGovernorTimeouts` | Write operations that timed out waiting on the pool governor. |
| `ReadModeWaits` | Read operations that had to wait on mode contention. |
| `WriteModeWaits` | Write operations that had to wait on mode contention. |

## Configuration

Metrics behavior is controlled through `MetricsOptions`:

- `LongConnectionThreshold`: hold-time threshold used by `LongLivedConnections`
- `SlowCommandThreshold`: duration threshold used by `SlowCommandsTotal`
- `EnableApproxPercentiles`: enables the bounded percentile windows used for P95/P99 metrics
- `PercentileWindowSize`: size of the percentile sampling window

## Usage Notes

- `MetricsUpdated` should be treated as a non-blocking observer callback. Do not call back into the same `DatabaseContext` from inside the handler.
- Percentiles are approximate by design; they trade exactness for bounded overhead.
- All metrics are per-context. In a multi-tenant system, each tenant context has its own independent metric stream.
