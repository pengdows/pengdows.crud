# Metrics & Observability

`pengdows.crud` provides 25+ real-time metrics for deep operational visibility.

## Categories & Key Metrics

| Category | Metrics Tracked |
|----------|-----------------|
| **Connections** | Current/Peak, Opened/Closed, Hold Time, Avg Duration. |
| **Commands** | Total Executed/Failed, Avg Duration, P95/P99 latency. |
| **Rows** | Total rows read/affected across operations. |
| **Prepared Statements** | Total cached/evicted statements. |
| **Transactions** | Active/Max Concurrent, Avg Duration. |
| **Pool Governor** | In-use/Peak Slots, Queued Requests, Timeouts, Cancellations. |

## Event-Based Updates

Subscribe to `MetricsUpdated` to receive real-time updates without polling.

```csharp
context.MetricsUpdated += (sender, metrics) =>
{
    _logger.LogInformation("P95 Command Duration: {ms}ms", metrics.P95CommandMs);
};
```

**Rule:** Handlers must never re-enter `DatabaseContext` (avoid deadlocks).

## On-Demand Polling

Retrieve current snapshots at any time via `GetMetrics()`.

```csharp
var snapshot = context.GetMetrics();
var openConns = context.NumberOfOpenConnections;
var peakConns = context.PeakOpenConnections;
```

## Performance Hot Paths

- **ValueTask:** Used on execution methods to reduce GC allocations.
- **Compiled Accessors:** Caches property setters/getters as delegates (~5.7x faster than reflection).
- **SQL Template Caching:** Caches generated SQL templates for reuse.
- **Bounded LRU Caches:** Prevents unbounded memory growth for plans and statements.
