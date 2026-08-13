# pengdows.crud OpenTelemetry Metrics

> **STATUS: SHIPPED.** `pengdows.crud.opentelemetry` is a real, built, tested package
> (`pengdows.crud.opentelemetry/`, tests in `pengdows.crud.Tests/opentelemetry/`). This
> file used to be a pre-implementation plan (`opentelemetry-metrics-plan.md`) that said
> "nothing described here exists" — that framing went stale the moment the package
> shipped and was never updated, so treat anything you read elsewhere citing the old
> filename or its instrument-name list as wrong. The package's own
> [`README.md`](../pengdows.crud.opentelemetry/README.md) is the primary reference for
> installation and the metrics table; this file keeps the design rationale (why it's
> shaped the way it is) and points at source for the full, current instrument list rather
> than duplicating it — see `docs/IMPLEMENTATION_EVIDENCE.md`'s Ecosystem section for a
> point-in-time snapshot of what's shipped vs. still open.

## What it does

Bridges `IDatabaseContext.Metrics`/`MetricsUpdated` into `System.Diagnostics.Metrics`
under `Meter` name `pengdows.crud` (`PengdowsMetricsObserver.MeterName`), without adding
an OpenTelemetry dependency to the core `pengdows.crud` package. It auto-discovers both
DI-registered contexts and per-tenant contexts via `ITenantContextRegistry`'s
`ContextCreated`/`ContextRemoved` events, and supports manually tracking contexts created
outside DI via `IDatabaseContext.TrackPengdowsMetrics(IServiceProvider)`. `Track`/`Untrack`
are both idempotent.

## Setup

```bash
dotnet add package pengdows.crud.opentelemetry
```

```csharp
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("pengdows.crud")
        .AddPrometheusExporter());

services.AddPengdowsTelemetry();
```

The real registration surface is exactly this small: `AddPengdowsTelemetry()` (an
`IServiceCollection` extension registering `IPengdowsMetricsObserver` as a singleton plus
a hosted service) and `TrackPengdowsMetrics()`. There is no options class — an earlier
draft of this document proposed a `PengdowsCrudOpenTelemetryOptions` surface with
filters/tag-enrichers/per-metric-family toggles; none of that was built, and there's no
open work item to build it.

## Why a separate package

- Keeps the core library dependency-light.
- Avoids tying `pengdows.crud` releases to OpenTelemetry package churn.
- Allows telemetry naming and tagging policy to evolve without expanding the core API surface.
- Preserves the internal collector and public snapshot/event model as the source of truth.

## Architecture

This is a hybrid export model, and the design held up through implementation:

1. **Snapshot cache** — `PengdowsMetricsObserver` subscribes to each tracked context's
   `MetricsUpdated` and keeps the most recent `DatabaseMetrics` snapshot per context
   (`_lastSnapshots`, keyed by `context.RootId`).
2. **Delta-based counters** — for monotonic values (commands executed/failed, rows
   read/affected, connections opened/closed, transactions committed/rolled back, errors by
   category, statements prepared/evicted, session inits), `HandleMetricsUpdated` computes
   `current - last` against the cached snapshot and adds the delta to an OTel `Counter<long>`
   (`EmitDelta`, `PengdowsMetricsObserver.cs`).
3. **Observable gauges for current state** — connections current/peak, command/transaction
   duration EMA and approximate P95/P99, active transactions, cached prepared statements,
   and per-pool slot/queue/turnstile state are all `ObservableGauge`s read live from the
   cached snapshot and `PoolStatisticsSnapshot`, not pushed on an event.
4. **Histograms** — the original plan deferred histograms until "raw duration hooks" existed
   in `pengdows.crud`, since the public surface only exposed pre-aggregated
   EMA/P95/P99 gauges. That got solved differently than planned: rather than adding new
   hooks to `MetricsUpdated`, the shipped adapter derives a real `db.client.operation.duration`
   histogram from the `ActivitySource("pengdows.crud")` spans `SqlContainer` already emits
   for tracing, filtered to `Track()`ed contexts via a `pengdows.context_id` Activity tag.
   The `pengdows.db.client.*` percentile/EMA gauges still ship alongside it rather than being
   replaced — both naming schemes coexist so no existing consumer of the gauges breaks.

`ModeContentionSnapshot`, referenced in an earlier draft of this document as an existing
metric source, does not exist anywhere in source — mode-lock contention metrics were
planned but never built, and there's no `mode_contention.*` instrument in the shipped
observer. Don't assume it exists.

## Tags

What's actually applied, per `PengdowsMetricsObserver.cs`:

- `db.name` / `db.system` — on every command/connection/transaction/error instrument.
- `pool.label` (`reader`/`writer`) — additionally applied to pool-governor instruments only
  (`pengdows.db.client.pool.*`); command/connection/transaction counters are **not** split
  by read/write role, only by context. `docs/FUTURE_WORK.md` tracks this as a real,
  still-open gap (the OTel bridge doesn't yet expose the `DatabaseMetrics.Read`/`.Write`
  per-role split that `IDatabaseContext.GetPoolStatisticsSnapshot(PoolLabel)` already has
  internally).
- `error.type` — on trace-derived spans/exceptions only.

No `db.mode` or `execution.role` tag exists — an earlier draft proposed both; neither was
built.

## What's still open

Per `docs/FUTURE_WORK.md`'s OpenTelemetry section: the OTel semantic-convention pool-side
histograms (`create_time`/`wait_time`/`use_time`) would require new event hooks inside
`PoolGovernor`'s concurrency-critical code, deliberately deferred rather than rushed; and
command/connection/transaction counters are not yet split by read/write role in the OTel
bridge (see Tags, above). The package is also not yet published to NuGet as of this
writing — check nuget.org for current status before citing it externally.

## Test coverage

`pengdows.crud.Tests/opentelemetry/PengdowsMetricsObserverTests.cs` covers: metrics
disabled/no contexts tracked emits nothing; counters advance by correct deltas across
multiple `MetricsUpdated` events; gauges reflect the current snapshot; multiple contexts
don't interfere (keyed by `RootId`); `Track`/`Untrack` are idempotent; disposed contexts
stop contributing (`GetGauges` calls `Untrack` on `context.IsDisposed`); and pool metrics
behave correctly. Tests use `pengdows.crud.fakeDb` and an in-process OTel `MeterListener`
rather than a real exporter — the same approach this document originally recommended.
