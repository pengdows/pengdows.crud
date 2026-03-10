# Metrics & Observability

`pengdows.crud` provides 36 real-time metrics for deep operational visibility via the `DatabaseMetrics` sealed record.

## Categories & Key Metrics

| Category | Metrics Tracked |
|----------|-----------------|
| **Connections** | Current/Peak, Opened/Closed, Hold Time, Avg Duration, long-lived count. |
| **Commands** | Total Executed/Failed/TimedOut/Cancelled, Avg Duration, P95/P99 latency. |
| **Rows** | Total rows read/affected across operations. |
| **Prepared Statements** | Total cached/evicted statements. |
| **Transactions** | Active/Max Concurrent, Committed/RolledBack, Avg/P95/P99 Duration. |
| **Errors** | Deadlocks, SerializationFailures, ConstraintViolations. |
| **Sessions** | Session init count, Avg session init time. |
| **Pool Governor** | In-use/Peak Slots, Queued Requests, Timeouts, Cancellations. |

Metrics are split into **read vs write roles** via `DatabaseRoleMetrics Read` and `DatabaseRoleMetrics Write` on the `DatabaseMetrics` record.

## Event-Based Updates

Subscribe to `MetricsUpdated` to receive real-time updates without polling.

```csharp
context.MetricsUpdated += (sender, metrics) =>
{
    _logger.LogInformation("Deadlocks: {n}, P99: {ms}ms",
        metrics.ErrorDeadlocks, metrics.P99CommandMs);
};
```

**Rule:** Handlers must never re-enter `DatabaseContext` (avoid deadlocks).

## On-Demand Polling

Retrieve current snapshots at any time via the `Metrics` property.

```csharp
var snapshot = context.Metrics;
var openConns = context.NumberOfOpenConnections;
var peakConns = context.PeakOpenConnections;
```

## Performance Hot Paths

- **ValueTask:** Used on ISqlContainer execution methods to reduce GC allocations.
- **Compiled Accessors:** Caches property setters/getters as delegates (~5.7x faster than reflection).
- **SQL Template Caching:** Caches generated SQL templates for reuse.
- **Bounded LRU Caches:** Prevents unbounded memory growth for plans and statements.
