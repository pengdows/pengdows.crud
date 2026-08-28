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

## Mode contention (`SingleWriter`/`SingleConnection`)

Separate from `DatabaseMetrics`, waiting on the mode lock in `SingleWriter`/`SingleConnection` is tracked by an internal `ModeContentionStats` collector (`pengdows.crud/metrics/ModeContentionStats.cs`) and surfaced publicly only when it times out: a failed wait throws `ModeContentionException` (`pengdows.crud.exceptions`) carrying a public `Snapshot` property (`ModeContentionSnapshot`: `CurrentWaiters`, `PeakWaiters`, `TotalWaits`, `TotalTimeouts`, `TotalWaitTimeTicks`, `AverageWaitTimeTicks`). Note `ModeContentionException` extends `TimeoutException` directly — it is **not** part of the `DatabaseException` hierarchy described in `CLAUDE.md`, so a `catch (DatabaseException)` block will not catch it.

There is also an internal `AttributionStats` collector (read/write request counts, governor-wait/timeout counts) that records `ReadRequests`/`WriteRequests` per operation but has no public accessor today — its governor-wait and mode-wait counters are declared but never incremented, and its snapshot is never read anywhere. It exists in source but isn't a usable feature yet.

## Percentile tracking is opt-in

`DatabaseMetrics`'s `P95`/`P99` fields only get real data if percentile tracking is explicitly enabled: `MetricsOptions.EnableApproxPercentiles` (`pengdows.crud.metrics`, public, `init`-only) defaults to `false`. When left at the default, `MetricsCollector` never constructs its `PercentileRing` circular buffer at all, so `P95`/`P99` stay at their default value with no indication why — a consumer reading them without first setting `EnableApproxPercentiles = true` gets numbers that look valid but aren't measuring anything. `MetricsOptions.PercentileWindowSize` (default 2048, must be a power of two) controls the sliding window size once enabled. All "average" fields (unaffected by this flag) use an EWMA (exponentially weighted moving average) with per-metric window sizes rather than a true running mean.
