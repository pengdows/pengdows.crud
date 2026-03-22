# Metrics & Observability

`pengdows.crud` provides 36+ real-time metrics for deep operational visibility via the `DatabaseMetrics` sealed record.

## Categories & Key Metrics

| Category | Key Fields |
|----------|------------|
| **Connections** | `TotalOpened`, `TotalClosed`, `PeakConcurrent`, `AvgHoldMs`, `MaxHoldMs` |
| **Commands** | `TotalExecuted`, `TotalFailed`, `TotalTimeouts`, `P95LatencyMs`, `P99LatencyMs` |
| **Rows** | `TotalRead`, `TotalAffected` |
| **Transactions** | `TotalStarted`, `TotalCommitted`, `TotalRolledBack`, `ActiveCount` |
| **Errors** | `Deadlocks`, `SerializationFailures`, `ConstraintViolations`, `OtherErrors` |
| **Prepared Statement Cache** | `CacheSize`, `CacheHits`, `CacheMisses`, `Evictions` |

Metrics are split into **read vs write roles** via `DatabaseRoleMetrics Read` and `DatabaseRoleMetrics Write` on the `DatabaseMetrics` record.

## Event-Based Updates

Subscribe to `MetricsUpdated` to receive real-time updates without polling.

```csharp
context.MetricsUpdated += (sender, metrics) =>
{
    _logger.LogInformation("Deadlocks: {n}, P99: {ms}ms",
        metrics.Deadlocks, metrics.P99LatencyMs);
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
