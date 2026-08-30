# Observability Guide: Diagnosing Operational Questions

`docs/metrics.md`, `docs/tracing.md`, and `docs/opentelemetry-metrics.md` document *what fields
exist*. This doc maps *operational questions* to *which of those fields actually answers them* —
a decision tree, not another field list.

## "My app feels slow — is it the database, the pool, or a lock?"

Compare three independent numbers before assuming it's the database:

| Symptom | Look at | What it means |
|---|---|---|
| Command itself is slow | `DatabaseMetrics.AvgCommandMs` / `P95CommandMs` / `P99CommandMs` (gated by `CommandPercentilesAvailable`) | The database is actually slow to execute this query — an index/query-plan problem, not a pengdows problem. |
| Request queued before it could even run | `PoolStatisticsSnapshot.AverageWaitMs` (via `context.GetPoolStatisticsSnapshot(PoolLabel.Reader/Writer)`) or `DatabaseMetrics.Read/WritePoolWaits`/`...PoolTimeouts` | The context's own admission control (`PoolGovernor`) is the bottleneck — you're asking for more concurrent connections than `MaxConcurrentReads`/`MaxConcurrentWrites` allows. Raise the limit or reduce concurrent demand; this is not a database-side slowdown. |
| Request queued behind a serialization lock | `DatabaseMetrics.ModeWaits`/`ModeTimeouts`, or a caught `ModeContentionException.Snapshot` (`ModeContentionSnapshot`: `CurrentWaiters`, `PeakWaiters`, `TotalWaits`, `TotalTimeouts`, `TotalWaitTimeTicks`, `AverageWaitTimeTicks`) | Only relevant under `DbMode.SingleWriter`/`SingleConnection` — the mode lock, not the pool or the database, is serializing work. **`ModeContentionException` extends `TimeoutException` directly, not `DatabaseException`** — a `catch (DatabaseException)` block will silently miss it; catch it explicitly if you need to detect this case. |
| Connection open/close itself is slow | `AvgConnectionOpenMs`/`AvgConnectionCloseMs` vs. `AvgSessionInitMs` | Separates raw TCP/auth cost from the cost of the dialect's own post-open `SET`/session-settings statements — a slow `AvgSessionInitMs` with fast `AvgConnectionOpenMs` points at the dialect's session-settings SQL, not network/auth. |

`AvgCommandMs` and `AvgFailedCommandMs` are tracked **separately** — a spike in failed-command
latency (timeouts, cancellations, errors) never contaminates the success-path
`Avg`/`P95`/`P99` you'd use for an SLO.

## "Why is this specific reader slow — the database, or my own consumption loop?"

Three EWMA fields on `DatabaseMetrics`/`DatabaseRoleMetrics` decompose a reader's total lease
into segments that would otherwise look like one number:

- `AvgReaderTimeToFirstRowMs` — reader acquisition until the first row is available. Slow here
  means the database's query execution itself is slow.
- `AvgReaderConsumptionMs` — first row until the reader is disposed. Slow here means **your own
  code** is slow to iterate/process rows (network round-trips for large result sets, slow
  per-row processing, or simply not disposing the reader promptly).
- `AvgReaderLeaseMs` — the complete lease, acquisition through disposal. This is what actually
  holds a pool permit; a large gap between this and `AvgConnectionHoldMs` for the same workload
  usually means readers are lingering rather than being disposed promptly.

## "Which tenant/request does this trace/log line belong to?"

`IDatabaseContext.RootId` (a `Guid`, stable for the context's lifetime) is the correlation key
used everywhere:

- Every `ActivitySource("pengdows.crud")` span carries it as the `pengdows.context_id` tag (see
  `docs/tracing.md`) — filter or group traces by this tag to isolate one context's (e.g. one
  tenant's) activity in a multi-context application.
- `pengdows.crud.opentelemetry`'s `PengdowsMetricsObserver` keys its per-context snapshot cache
  by the same `RootId` (see `docs/opentelemetry-metrics.md`'s Architecture section) — multiple
  concurrently-tracked contexts never interfere with each other's counters/gauges.
- For multi-tenant deployments specifically: `ITenantContextRegistry.ContextCreated`/
  `ContextRemoved` fire with the `IDatabaseContext` instance itself (no separate tenant-ID field
  — see `docs/connection/multitenancy.md`'s Lifecycle Events section). Log `ctx.RootId` and
  `ctx.Name` from these handlers at the point you already know which tenant string produced that
  context, to build your own tenant↔`RootId` correlation table for later log/trace lookups — the
  library does not persist that mapping for you.

## "Is my P95/P99 SLO actually regressing, or do I just have no data?"

Percentile tracking is **opt-in** (`MetricsOptions.EnableApproxPercentiles`, default `false`) —
a `P95CommandMs`/`P99CommandMs` of `0` is ambiguous between "genuinely fast" and "tracking is
off, or no samples yet." Always gate on the paired availability flag before trusting a percentile
value:

```csharp
if (metrics.CommandPercentilesAvailable)
{
    // metrics.P95CommandMs / P99CommandMs are real
}
```

Same pattern for `TransactionPercentilesAvailable` / `P95TransactionMs`/`P99TransactionMs`. If
both are consistently `false` in production, percentile tracking isn't enabled — that's a
configuration gap, not evidence the database is fast.

## "Are connections piling up, or leaking?"

- `ConnectionsCurrent` vs. `PeakOpenConnections` — a `ConnectionsCurrent` that never returns to
  baseline between bursts, while `PeakOpenConnections` keeps climbing, suggests something isn't
  disposing connections/readers/transactions promptly rather than a load spike.
- `LongLivedConnections` — count of connections held longer than the configured threshold
  (`MetricsOptions.LongConnectionThreshold`) — a nonzero, growing count is a more direct signal
  than watching `ConnectionsCurrent` drift, since it's specifically counting outliers, not the
  aggregate.
- `SlowCommandsTotal` — similarly, count of commands that exceeded `MetricsOptions.SlowCommandThreshold`
  — use this to spot a growing tail of slow outliers even while `AvgCommandMs` still looks fine.

## "How do I wire this into Grafana/Datadog/Prometheus/etc.?"

Two independent, separately-installed pieces — you don't need both:

- **Traces**: zero extra package. `services.AddOpenTelemetry().WithTracing(b => b.AddSource("pengdows.crud"))`
  is the entire setup (`docs/tracing.md`). Layer `AddSqlClientInstrumentation()`/
  `AddNpgsqlInstrumentation()`/etc. alongside it for provider-level connection detail pengdows.crud
  itself doesn't expose.
- **Metrics**: install `pengdows.crud.opentelemetry`, call `services.AddPengdowsTelemetry()`, and
  add `.AddMeter("pengdows.crud")` to your `WithMetrics(...)` builder (`docs/opentelemetry-metrics.md`).
  It auto-discovers DI-registered and per-tenant (`ITenantContextRegistry`) contexts; call
  `IDatabaseContext.TrackPengdowsMetrics(serviceProvider)` explicitly for a context you construct
  outside DI. Known current gap: command/connection/transaction counters aren't yet split by
  read/write role in the OTel bridge (pool-governor instruments are); don't expect a `pool.label`
  tag on those three families yet.

## "A command failed — is it worth retrying?"

Not a metrics question, but adjacent enough to belong here: `ISqlDialect.AnalyzeException(ex)`
returns a provider-neutral `DbExceptionInfo` (`Category`, `ConstraintKind`, `IsTransient`,
`IsRetryable`, `ProviderErrorCode`, `SqlState`) from any caught exception — check `IsRetryable`
instead of pattern-matching provider-specific error codes/messages yourself. See
`docs/planning/future-work.md`'s DOC-023 entry for the dedicated write-up of this API (still
open as of this writing) if you need the full taxonomy and provider-coverage matrix.
