# Metrics

Each `IDatabaseContext` exposes a `DatabaseMetrics` snapshot through `Metrics` and raises `MetricsUpdated` when the collector records new activity.

## Access Pattern

```csharp
DatabaseMetrics snapshot = context.Metrics;

context.MetricsUpdated += OnMetricsUpdated;
```

Treat the event as an observer feed. Handlers should not call back into the same context.

## What `DatabaseMetrics` Contains

The current record includes:

- aggregate read and write role snapshots
- connection counts and latency averages
- command counts, latency averages, and percentile estimates
- row counts and parameter observations
- prepared-statement cache counters
- transaction counts and latency estimates
- error attribution counters
- session-initialization counters

Avoid hard-coding a metric-count claim in docs. The authoritative shape is the `DatabaseMetrics` record in `pengdows.crud.abstractions`.
