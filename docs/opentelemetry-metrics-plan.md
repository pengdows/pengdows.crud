# OpenTelemetry Metrics Adapter Plan

> **STATUS: PLANNED — Not yet implemented in 2.0**
>
> Nothing described in this document exists in the current codebase. No OpenTelemetry package,
> adapter, or instrumentation hooks have been implemented. This document records the proposed
> design for a future optional package. Do not reference any of the APIs, package names, or
> registration helpers below as if they are available — they are not.

This document records the proposed design for exposing `pengdows.crud` metrics through
OpenTelemetry in a future package. It is intentionally a plan, not a commitment.

## Goal

Provide an optional adapter package that publishes `pengdows.crud` runtime metrics to
OpenTelemetry without adding a direct OpenTelemetry dependency to `pengdows.crud` itself.

Recommended package name:

- `pengdows.crud.opentelemetry`

## Why a separate package

- Keeps the core library dependency-light.
- Avoids tying `pengdows.crud` releases to OpenTelemetry package churn.
- Allows telemetry naming and tagging policy to evolve without expanding the core API surface.
- Preserves the current internal collector and public snapshot/event model as the source of truth.

## Existing metric sources

The adapter should use existing runtime data rather than inventing a second metrics system.

- `IDatabaseContext.Metrics`
- `IDatabaseContext.MetricsUpdated`
- `DatabaseMetrics.Read`
- `DatabaseMetrics.Write`
- `PoolStatisticsSnapshot`
- `ModeContentionSnapshot`

The main metric inventory is documented in [`metrics.md`](metrics.md).

## Scope

### In scope for v1

- OpenTelemetry metrics only
- Per-context export
- Read/write role tagging
- Error-category counters
- Optional pool-governor metrics
- Optional mode-contention metrics
- DI registration helpers

### Out of scope for v1

- Tracing
- Logs
- Automatic SQL text enrichment
- High-cardinality tenant tags by default
- A one-to-one export of every pre-aggregated field as an OTel metric

## Architecture

Use a hybrid export model.

### 1. Snapshot cache

Subscribe to `MetricsUpdated` and cache the most recent snapshot for each observed context.

This cached snapshot becomes the source for observable gauges and a fallback for environments
where event frequency is lower than scrape frequency.

### 2. Delta-based counters

For monotonic values, compute deltas between the previous and current snapshot and feed those
into OpenTelemetry counters.

Examples:

- `ConnectionsOpened`
- `CommandsExecuted`
- `RowsReadTotal`
- `TransactionsCommitted`

### 3. Gauges for current state

Use observable gauges for current values that naturally go up and down.

Examples:

- `ConnectionsCurrent`
- `TransactionsActive`
- pool `InUse`
- pool `Queued`

### 4. Histograms only when raw durations are available

OpenTelemetry histograms are preferable to exporting precomputed percentiles, but they only
make sense if the adapter can observe raw duration events.

The current public surface primarily exposes pre-aggregated durations:

- `AvgCommandMs`
- `P95CommandMs`
- `P99CommandMs`
- `AvgTransactionMs`
- `P95TransactionMs`
- `P99TransactionMs`

Therefore v1 should avoid pretending these are raw histogram inputs. Two acceptable paths exist:

- Conservative v1: export counters and gauges only, plus the already-computed aggregates as
  observable gauges with explicit names that indicate they are library-computed summaries.
- Better v2: add internal hooks or a stable observer interface for raw duration events, then
  publish true OpenTelemetry histograms.

## Instrument mapping

The list below is the recommended starting point. Naming should be reviewed against the
current OpenTelemetry semantic conventions at implementation time.

### Gauges

- `pengdows.db.client.connections.current`
- `pengdows.db.client.connections.peak`
- `pengdows.db.client.transactions.active`
- `pengdows.db.client.transactions.max`
- `pengdows.db.client.command.duration.avg`
- `pengdows.db.client.command.duration.p95`
- `pengdows.db.client.command.duration.p99`
- `pengdows.db.client.transaction.duration.avg`
- `pengdows.db.client.transaction.duration.p95`
- `pengdows.db.client.transaction.duration.p99`
- `pengdows.db.client.connection.hold.duration.avg`
- `pengdows.db.client.connection.open.duration.avg`
- `pengdows.db.client.connection.close.duration.avg`
- `pengdows.db.client.session.init.duration.avg`
- `pengdows.db.client.parameters.max`
- `pengdows.db.client.pool.in_use`
- `pengdows.db.client.pool.peak_in_use`
- `pengdows.db.client.pool.queued`
- `pengdows.db.client.pool.peak_queued`
- `pengdows.db.client.pool.turnstile_queued`
- `pengdows.db.client.pool.peak_turnstile_queued`
- `pengdows.db.client.mode_contention.current_waiters`
- `pengdows.db.client.mode_contention.peak_waiters`

### Counters

- `pengdows.db.client.connections.opened`
- `pengdows.db.client.connections.closed`
- `pengdows.db.client.connections.long_lived`
- `pengdows.db.client.commands.executed`
- `pengdows.db.client.commands.failed`
- `pengdows.db.client.commands.timed_out`
- `pengdows.db.client.commands.cancelled`
- `pengdows.db.client.commands.slow`
- `pengdows.db.client.rows.read`
- `pengdows.db.client.rows.affected`
- `pengdows.db.client.statements.prepared`
- `pengdows.db.client.statements.cached`
- `pengdows.db.client.statements.evicted`
- `pengdows.db.client.transactions.committed`
- `pengdows.db.client.transactions.rolled_back`
- `pengdows.db.client.errors`
- `pengdows.db.client.session.init.count`
- `pengdows.db.client.pool.acquired`
- `pengdows.db.client.pool.slot_timeouts`
- `pengdows.db.client.pool.turnstile_timeouts`
- `pengdows.db.client.pool.canceled_waits`
- `pengdows.db.client.mode_contention.waits`
- `pengdows.db.client.mode_contention.timeouts`

### Deferred histograms

Only add these if raw duration samples can be observed safely:

- `pengdows.db.client.command.duration`
- `pengdows.db.client.transaction.duration`
- `pengdows.db.client.connection.hold.duration`
- `pengdows.db.client.connection.open.duration`
- `pengdows.db.client.connection.close.duration`
- `pengdows.db.client.session.init.duration`
- `pengdows.db.client.pool.wait.duration`
- `pengdows.db.client.pool.hold.duration`
- `pengdows.db.client.mode_contention.wait.duration`

## Tags

Use a minimal, stable, low-cardinality tag set.

Recommended tags:

- `db.system`
- `db.mode`
- `execution.role=read|write`
- `pool.label=reader|writer`
- `db.error.category=deadlock|serialization_failure|constraint_violation`

Optional tags:

- `db.name` only when low-cardinality and explicitly enabled
- adapter-specific environment tags added by caller configuration

Avoid by default:

- raw connection strings
- tenant ids
- SQL text
- pool key hashes
- unbounded custom tags

## Registration model

Possible public API:

```csharp
services.AddPengdowsCrudOpenTelemetry(options =>
{
    options.IncludePoolMetrics = true;
    options.IncludeModeContentionMetrics = true;
    options.IncludeDatabaseName = false;
});
```

Possible options surface:

```csharp
public sealed class PengdowsCrudOpenTelemetryOptions
{
    public bool IncludePoolMetrics { get; set; } = true;
    public bool IncludeModeContentionMetrics { get; set; } = true;
    public bool IncludeDatabaseName { get; set; }
    public bool IncludeRoleMetrics { get; set; } = true;
    public Func<IDatabaseContext, bool>? Filter { get; set; }
    public Func<IDatabaseContext, TagList>? TagEnricher { get; set; }
}
```

Observation helpers should likely include:

- observing a single `IDatabaseContext`
- observing contexts created through DI
- observing contexts from `ITenantContextRegistry`

## Implementation phases

### Phase 1: design validation

- Confirm package name and target frameworks
- Review instrument names against active OpenTelemetry conventions
- Decide whether pre-aggregated averages/percentiles should be exported in v1

### Phase 2: minimal adapter

- Create package skeleton
- Add `Meter`
- Add context registration and deduplication
- Export counters and gauges from `DatabaseMetrics`
- Add unit tests for delta handling and duplicate-registration prevention

### Phase 3: extended diagnostics

- Add pool governor metrics
- Add mode contention metrics
- Add optional read/write role instruments or tags
- Add tests covering counter monotonicity and disposal behavior

### Phase 4: histogram support

- Evaluate whether raw duration hooks are needed in `pengdows.crud`
- If yes, design a minimal non-invasive hook
- Add histograms only after raw events are available and semantics are clear

## Key risks

- Double registration could double-count counters.
- Recreated contexts could look like counter resets if identity is not handled carefully.
- High-cardinality tags could make the adapter unsafe in multi-tenant production systems.
- `MetricsUpdated` handlers must not call back into the same `DatabaseContext`.
- Exporting both precomputed percentiles and histograms without careful naming would create
  confusing duplicate telemetry.

## Testing strategy

When implemented, testing should cover:

- metrics disabled: adapter emits nothing
- metrics enabled: counters advance by correct deltas
- gauges reflect current snapshot values
- multiple contexts do not interfere with each other
- duplicate registration is ignored or rejected
- disposed contexts stop contributing
- pool and contention metrics behave correctly when enabled
- tags remain bounded and stable

Use `pengdows.crud.fakeDb` for unit tests. Avoid real exporters in tests; inspect emitted
measurements through test meters/readers.

## Decision summary

Recommended future direction:

- Build an OpenTelemetry adapter
- Keep it out of the core package
- Start with counters and gauges
- Defer true histograms until raw duration events are available
- Keep tags conservative by default
