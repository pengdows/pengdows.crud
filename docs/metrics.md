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
- read/write request counts, pool waits/timeouts, and mode-lock waits/timeouts

Avoid hard-coding a metric-count claim in docs. The authoritative shape is the `DatabaseMetrics` record in `pengdows.crud.abstractions`.

## Mode contention (`SingleWriter`/`SingleConnection`)

Separate from `DatabaseMetrics`, waiting on the mode lock in `SingleWriter`/`SingleConnection` is tracked by an internal `ModeContentionStats` collector (`pengdows.crud/metrics/ModeContentionStats.cs`) and surfaced publicly only when it times out: a failed wait throws `ModeContentionException` (`pengdows.crud.exceptions`) carrying a public `Snapshot` property (`ModeContentionSnapshot`: `CurrentWaiters`, `PeakWaiters`, `TotalWaits`, `TotalTimeouts`, `TotalWaitTimeTicks`, `AverageWaitTimeTicks`). Note `ModeContentionException` extends `TimeoutException` directly — it is **not** part of the `DatabaseException` hierarchy described in `CLAUDE.md`, so a `catch (DatabaseException)` block will not catch it.

The aggregate snapshot exposes cumulative read/write request counts, pool wait and timeout
counts, and mode-lock wait and timeout counts. Pool counts come from the authoritative pool
governors; mode counts come from the mode-lock collector. These values help explain pressure
visible in the latency metrics.

## Percentile tracking is opt-in

`DatabaseMetrics` and each `DatabaseRoleMetrics` snapshot expose
`CommandPercentilesAvailable` and `TransactionPercentilesAvailable`. These flags are true
only when the corresponding P95/P99 values contain data. They are false when percentile
tracking is disabled or when no samples have been recorded, so a consumer never has to
interpret a zero percentile as a valid measurement.

Percentile tracking is enabled with `MetricsOptions.EnableApproxPercentiles`
(`pengdows.crud.metrics`, public, `init`-only), which defaults to `false`. When disabled,
the collector does not allocate percentile ring buffers. `MetricsOptions.PercentileWindowSize`
(default 2048, must be a power of two) controls the sliding window size once enabled. All
"average" fields (unaffected by this flag) use an EWMA (exponentially weighted moving average)
with per-metric window sizes rather than a true running mean.

## Long-connection and slow-command thresholds

Two more `MetricsOptions` (`pengdows.crud.metrics`, public, `init`-only) controls classify
individual connections and commands as outliers, independent of the percentile/EWMA latency
tracking above:

- `LongConnectionThreshold` (`TimeSpan`, defaults to 30 seconds, must be positive) — when a
  connection closes, its hold duration is compared against this threshold. A hold duration at
  or above the threshold increments `LongLivedConnections` on the `DatabaseMetrics`/
  `DatabaseRoleMetrics` snapshot.
- `SlowCommandThreshold` (`TimeSpan`, defaults to 1 second, must be positive) — when a command
  finishes (success or failure), its elapsed duration is compared against this threshold. An
  elapsed duration at or above the threshold increments `SlowCommandsTotal` on the same
  snapshots.

Both counters are cumulative totals, not gauges — they only grow, the same as
`ConnectionsOpened` or `CommandsExecuted`. For example, with
`LongConnectionThreshold = TimeSpan.FromMilliseconds(1)`, a connection held for 5ms increments
`LongLivedConnections` by 1 when it closes; with `SlowCommandThreshold = TimeSpan.FromMilliseconds(10)`,
a command that takes 50ms — whether it ultimately succeeds or fails — increments
`SlowCommandsTotal` by 1.
