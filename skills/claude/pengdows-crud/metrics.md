# Telemetry & Performance Metrics

`pengdows.crud` provides deep, built-in operational observability by tracking 36 granular metrics directly inside the execution pipeline.

---

## 1. Internal Metrics Architecture

Metrics are captured with thread-safe atomic operations and coordinated by `DatabaseContext`:
- **Connection Lifecycle**: Tracks connection open/close counts, lifetimes, and peak concurrent connections.
- **PoolGovernor Telemetry**: Tracks reader/writer slot acquisition wait durations, slot hold times, contention spikes, and turnstile fairness stats.
- **Command Timings**: Tracks execution latency across scalars, readers, and non-query statements.
- **Transaction Metrics**: Tracks active transactions, commit/rollback counts, savepoint counts, and open transaction durations.
- **Error Attribution**: Categorizes database failures (`DeadlockException`, `UniqueConstraintViolationException`, `ConcurrencyConflictException`, etc.) to isolate environmental vs application issues.

---

## 2. Accessing Metrics Snapshots

```csharp
DatabaseMetrics snapshot = context.Metrics;

long activeConns = snapshot.ActiveConnections;
long peakConns   = snapshot.PeakConnections;
long totalCmds   = snapshot.TotalCommandsExecuted;
TimeSpan avgWait = snapshot.AveragePoolWaitDuration;
```

---

## 3. Subscribing to Live Updates

`DatabaseContext` raises `MetricsUpdated` whenever new metrics observations are recorded:

```csharp
context.MetricsUpdated += (sender, metrics) =>
{
    logger.LogInformation("Active Connections: {Active}, Commands/sec: {Rate}",
        metrics.ActiveConnections,
        metrics.CommandsPerSecond);
};
```

> [!WARNING]
> **Event Handler Rule**: Handlers subscribed to `MetricsUpdated` must NEVER call back into the `DatabaseContext` (no queries, no transactions) and must be unsubscribed when the listener is disposed to avoid memory leaks on singleton contexts.

---

## 4. OpenTelemetry Integration

`pengdows.crud` metrics can be exported directly to standard OpenTelemetry meters (`pengdows.crud`) for integration with Prometheus, Datadog, or Grafana dashboards.
