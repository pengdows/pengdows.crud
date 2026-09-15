# Future Work

This document tracks features that have been designed or partially specified but are not yet
implemented. Items here are not roadmap commitments — they are recorded so the design thinking
is not lost and can be picked up when the need arises.

---

## Batch Operations

The current batch implementation (`TableGateway.Batch.cs`) handles chunked multi-row INSERT,
UPDATE, and UPSERT with automatic parameter-limit-aware splitting. The following extensions
were designed but not built.

### Provider-optimized bulk load

For very large datasets (tens of thousands of rows), native bulk-load protocols are 10–100×
faster than parameterized multi-row INSERT.

| Database | Mechanism | Notes |
|----------|-----------|-------|
| PostgreSQL | `COPY … FROM STDIN` (binary or CSV) | Requires `NpgsqlBinaryImporter` or `COPY` command |
| SQL Server | `SqlBulkCopy` | ADO.NET class; bypasses row-by-row binding entirely |
| DuckDB | `COPY … FROM` (CSV/Parquet/Arrow) | Analytical workloads; Arrow appender is fastest |
| Oracle | Array binding via `OracleCommand` | Set `ArrayBindCount`; avoids per-row round-trips |
| MySQL/MariaDB | `LOAD DATA INFILE` | Requires `LOCAL INFILE` server permission |
| Firebird | Batch API (Firebird 4.0+) | `FbBatchCommand`; older versions fall back to multi-row INSERT |

None of these are in scope for the current `BatchCreateAsync` / `BatchUpsertAsync` surface.
When added, they should sit behind the existing `Build`/`Execute` split so callers are not
forced to change call sites.

### `ContinueOnError` / partial-batch error handling

Currently, if any `ExecuteNonQueryAsync` call inside a batch loop throws, the exception
propagates immediately and remaining chunks are not executed. A `ContinueOnError` option
would collect per-chunk failures and return a structured result instead of throwing.

Sketch of the intended API:

```csharp
public record BatchError(int ChunkIndex, int StartRow, int EndRow, Exception Exception);

public record BatchResult(int RowsAffected, IReadOnlyList<BatchError> Errors);
```

The decision on whether to add this depends on whether callers actually need partial success
semantics. Most transactional use cases do not — a transaction wrapping the whole batch is
usually the right answer.

### Progress reporting

For long-running batches an `IProgress<BatchProgress>` callback was sketched:

```csharp
public record BatchProgress(int ChunksCompleted, int TotalChunks, int RowsAffected);
```

Would be passed as an optional parameter alongside `CancellationToken`. Low priority unless
a caller actually needs it — the cancellation token already lets the caller abort.

### Resumable / checkpointed batches

The idea: record which chunks completed successfully so a retry can skip them. Requires
stable chunk boundaries (deterministic ordering) and external state storage. Complex enough
that it probably belongs outside the library, in application code that calls `BuildBatchCreate`
and manages the resulting `IReadOnlyList<ISqlContainer>` directly.

### Streaming batch input

Accept `IAsyncEnumerable<TEntity>` instead of `IReadOnlyList<TEntity>` so callers can
generate entities lazily without materializing the full set first. Chunking would need to
buffer `N` rows at a time rather than pre-splitting the full list.

---

## Session Settings

The `SessionSettingsPreamble` property on `IDatabaseContext` is marked `[Obsolete]`.
The replacement (`GetBaseSessionSettings()` / `GetReadOnlySessionSettings()`) is implemented.
Remove `SessionSettingsPreamble` from the interface in the next major version.

Tracked usage: `benchmarks/CrudBenchmarks/ViewPerformanceBenchmarks.cs:131` (generates a
build warning today; `WarningsAsErrors` is off for that project).

---

## Oracle

### Array binding
Oracle's `OracleCommand.ArrayBindCount` allows a single `ExecuteNonQuery` to insert N rows
with array-valued parameters, avoiding multi-row VALUES syntax entirely. More efficient than
INSERT ALL for large row counts. Requires ODP.NET (Managed or Unmanaged); not available via
the generic ADO.NET `DbProviderFactory` abstraction, so would need provider-specific code
paths.

### Batch UPDATE strategy
The base `SqlDialect.SupportsBatchUpdate` returns `false` for Oracle, meaning batch updates
fall back to one `UPDATE` per entity. PostgreSQL uses `UPDATE FROM VALUES` and SQL Server
uses `MERGE`; Oracle has no direct equivalent without either a global temporary table or
PL/SQL. Design work needed before implementation.

---

## Metrics integration for batch operations

The existing `MetricsCollector` tracks per-command parameter counts and execution times.
Batch operations currently show up as N individual command records. A batch-aware metrics
event (total rows, chunk count, total duration) would make the dashboards more useful for
diagnosing batch throughput.

---

## Analyzer improvements: multi-tenancy footguns

Motivated by a real integration (a multi-tenant wiki app wiring up `AddMultiTenancy` +
`ITenantContextRegistry` for the first time): the existing analyzers caught one real mistake
(`PGC001`, tried to register `IDatabaseContext` as scoped so it could be resolved per-request)
but only after the wrong design was already half-built, and didn't catch a second, more
dangerous mistake at all (application code calling gateway methods without passing the
per-request `IDatabaseContext`, silently falling back to whatever context the gateway singleton
was constructed with — the bootstrap tenant). `PGC025` (`GatewayMethodContextParameterAnalyzer`)
already enforces `ctx = contextArg ?? Context` threading *inside* gateway subclasses, but has no
opinion about *callers* of those gateways.

Three ideas, roughly in order of how early each would have caught the actual mistakes made:

### 1. Flag `IDatabaseContext` as a direct injection point in multi-tenant projects

The root mistake wasn't the scoped registration — it was designing endpoints/controllers to
take `IDatabaseContext` as a constructor/action/delegate parameter at all, once multi-tenancy is
in play. A directly-injected `IDatabaseContext` always resolves to whatever was registered at
startup; it can never vary per request.

Sketch: a compilation-wide analyzer that, when it detects a call to `AddMultiTenancy` anywhere
in the project, flags any minimal-API delegate parameter, MVC controller constructor/action
parameter, or `[FromServices]` property of type `IDatabaseContext`. Message: *"IDatabaseContext
should not be injected directly in a multi-tenant app — resolve it via
ITenantContextRegistry.GetContext(tenantKey) per request instead."*

This is the earliest possible intervention — it would fire on the very first draft, before
anyone gets as far as writing a (bad) DI registration for it.

### 2. Flag gateway calls that omit the optional context argument, project-wide

This is the dangerous one: omitting `contextArg` at a call site doesn't throw, it silently runs
against the wrong tenant's database. `PGC025` already has all the machinery to recognize
"gateway method with an optional trailing `IDatabaseContext? contextArg = null`" — it just
restricts its search to methods declared *inside* gateway-derived types (`IsGatewayType`).
Extending the same detection to arbitrary call sites (any invocation of one of those methods,
regardless of the containing type) — gated on `AddMultiTenancy` being present in the project, so
single-tenant apps aren't forced into always passing context explicitly — would have caught
every one of the ~20 call sites that needed fixing by hand in `Program.cs`, at compile time
instead of via manual grep.

Open question: whether to make this unconditional (flag *any* omitted context argument,
tenancy or not) since even in a single-tenant app it's evidence the call is relying on a
gateway's constructor-bound default rather than an explicit transaction/context — that's
arguably always worth a lint, just lower severity than the multi-tenant case.

### 3. Make `PGC001`'s message tenancy-aware

Right now `PGC001` says *"...must not be registered as scoped or transient"* and stops there —
correct, but it doesn't point anywhere. If `AddMultiTenancy` is also present in the compilation,
append: *"...for per-request tenant resolution, use ITenantContextRegistry.GetContext(tenantKey)
instead."* A code-fix provider that rewrites the offending `AddScoped<IDatabaseContext>(...)`
factory into a `ITenantContextRegistry`-based resolver would go further, but even just the
improved message answers the question the person was actually trying to solve when they reached
for `AddScoped` in the first place.

None of these three replace integration testing — they catch DI-shape/call-site mistakes at
compile time, but "does Host-header-based tenant routing actually land on the right tenant" is
a runtime behavior that still needs an end-to-end test (see the wiki app's
`MultiTenancyEndpointTests` for the pattern: boot the real app via `WebApplicationFactory`,
send requests with different `Host` headers, assert against `ITenantContextRegistry.GetContext`
directly).

---

## OpenTelemetry metrics adapter

`pengdows.crud` currently exposes metrics through `IDatabaseContext.Metrics`,
`IDatabaseContext.MetricsUpdated`, and related diagnostic snapshots. A future adapter package
could bridge those metrics into OpenTelemetry without adding an OpenTelemetry dependency to
the core library.

Recommended direction:

- package name: `pengdows.crud.opentelemetry`
- keep the core package free of direct OpenTelemetry dependencies
- start with counters and gauges
- defer true histograms until raw duration samples are available through a stable hook
- keep tags low-cardinality by default

See [`opentelemetry-metrics-plan.md`](opentelemetry-metrics-plan.md) for the full design and
implementation plan.
