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

## OpenTelemetry metrics adapter

`pengdows.crud.opentelemetry` bridges `IDatabaseContext.Metrics`/`MetricsUpdated` into
OpenTelemetry without adding an OpenTelemetry dependency to the core package. Built.

Still open: it exports approximate P95/P99 as gauges rather than raw duration
samples/histograms — see the review below.

---

## Open items from architecture/DAL-comparison review (2026-08-12)

A broader review (isolation semantics, session init, pool governance, audit/lifecycle,
detection, metrics, locking, benchmarking) surfaced a longer list of findings. Everything at P0,
and most of P1, was fixed directly in the same session: isolation `Degraded` truthfulness for
TiDB/Snowflake, SingleWriter turnstile fairness (was gated on a pool-identity hash that never
matched in the common case), session-init fail-closed (`SessionInitializationFailureMode`),
`TenantContextRegistry.MaxTenantCount` TOCTOU race, `PoolGovernor` bounded queue depth
(`MaxQueuedReads`/`MaxQueuedWrites`), audit setters ignoring `[NonUpdateable]`/`[NonInsertable]`,
`AuditCreationPolicy`, `DatabaseDetectionResult` evidence, `IDataSourceInformation.ParsedVersion`,
`ParameterMarkerPattern` (was always empty), an opt-in `EnforceUniqueConnectionString` guard
against two contexts sharing a physical pool, and a transaction reader-lock-lifetime bug found
while tracing the turnstile fix (a `TransactionContext`'s serialization lock was released while
a reader it had opened was still active).

One claimed item checked out as already false against current `main` and needed no fix: the
review's "metrics avg-vs-P95/P99 population mismatch" — `MetricsCollector.RecordCommandDuration`
already feeds the average and the percentile ring from the same success-only population, with a
separate `_failedCommandDuration` EWMA for failures, specifically to keep `Avg`/`P95`/`P99`
describing one consistent population. Also fixed while auditing the locking architecture: stale
comments in `RealAsyncLocker.cs`/`TrackedConnection.cs` that still described `RealAsyncLocker` as
used for SingleWriter mode — it isn't; SingleWriter serializes writes via `PoolGovernor` over
ephemeral, NoOp-locked connections, with no persistent connection to lock.

A second item ("TypeMapRegistry staleness / schema-invalidation policy") turned out to be a
non-issue on closer inspection and was removed rather than listed as open: `TypeMapRegistry`
caches `TableInfo` keyed by the .NET `Type` object and builds it purely from that type's
attributes via reflection — it never reads live database schema, so there's nothing for it to
go stale relative to. Compiled attributes are immutable for a `Type`'s lifetime, so a cache hit
is correct forever; changing a POCO's attributes requires recompiling, which produces a new
process with fresh types anyway. The only way to defeat this is deliberately minting a *new*
`Type` for the *same* logical table repeatedly at runtime (hot-reload/dynamic codegen) instead
of reusing one — a self-inflicted anti-pattern, not a library gap — and even then the cache size
is bounded by how many tables the app actually uses.

What's left:

### P1

- **Detection probes are still synchronous — partially fixed.** Split into three phases by risk:
  - Phase 1 (done): `DatabaseDetectionService` now has genuine async twins
    (`DetectProductAsync`/`DetectFromConnectionAsync`/`DetectFromConnectionWithDetailAsync`/
    `DetectFlavorWithDetailAsync`) using `DbCommand.ExecuteScalarAsync` for the round-trip probes
    (Aurora/TiDB/Yugabyte/Cockroach), with `OperationCanceledException` propagating un-wrapped per
    the project's exception-hierarchy convention. Covered by
    `DatabaseDetectionServiceAsyncTests.cs`, which proves the async path is genuinely used (not a
    sync fallback) via a command double whose `ExecuteScalar()` throws and whose
    `ExecuteScalarAsync()` answers the probes. `GetSchema()` stays sync — no true async ADO.NET
    equivalent exists.
  - Phase 2 (done, narrowed scope): fixed the two spots where an already-async method still
    called the synchronous `DatabaseDetectionService.DetectProduct` instead of its `...Async`
    twin — `SqlDialectFactory.CreateDialectAsync` (outer product identification) and
    `SqlDialect.DetectDatabaseInfoAsync`'s own fallback when name/version inference can't beat
    the dialect's already-assumed base type. Both covered by
    `SqlDialectFactoryAsyncDetectionTests.cs` via a decorator connection whose sync
    `ExecuteScalar()` throws, forcing the Aurora-vs-plain-MySQL flavor probe through
    `ExecuteScalarAsync()` to resolve correctly.

    First attempt at this fix was wrong and caught by review before merging: `CreateDialectAsync`
    is not only reachable from the genuinely-async `DataSourceInformation.CreateAsync` — both
    synchronous `SqlDialectFactory.CreateDialect(...)` overloads (called by every
    `IConnectionStrategy.HandleDialectDetection` implementation, i.e. every ordinary, fully-sync
    `DatabaseContext` construction) delegated to `CreateDialectAsync(...).GetAwaiter().GetResult()`.
    Naively swapping the sync detection call for the async one inside `CreateDialectAsync` would
    have made every synchronous `DatabaseContext` construction start blocking on real async I/O it
    never previously touched for that step — sync-over-async with no benefit, introduced silently.
    Fixed by giving `CreateDialect(ITrackedConnection, DbProviderFactory, ILoggerFactory)` its own
    independent body that calls the sync `DetectProduct` directly instead of delegating to
    `CreateDialectAsync`. `DetectDatabaseInfoAsync` is still blocked via `.GetAwaiter().GetResult()`
    from that sync path exactly as before — it has no genuine sync implementation at all
    (`SqlDialect.GetDatabaseVersion` is itself just
    `GetDatabaseVersionAsync(...).GetAwaiter().GetResult()`), so that part was already
    sync-over-async prior to this change and splitting it further wasn't possible without a much
    larger rewrite. `SqlDialectFactoryAsyncDetectionTests.cs` locks the corrected contract down
    with a third test using the inverse decorator (blocks `ExecuteScalarAsync` only for the
    identification probe): `CreateDialect(...)` must still resolve Aurora MySQL using only the sync
    probe, proving the sync entry point never starts depending on the async overload.

    Known gap, deliberately not addressed here: neither `SqlDialectFactory.CreateDialectAsync` nor
    `DataSourceInformation.CreateAsync` accept a `CancellationToken`, even though the
    `DetectProductAsync` primitives they now call support one. Phase 1 built genuinely
    asynchronous, cancellation-capable detection; this call chain doesn't yet expose that
    cancellation to callers. Threading a token through both signatures is straightforward but was
    left out to avoid scope creep into more call sites right before release — revisit alongside
    Phase 3.
  - Phase 3 (not started, the risky part): expose it via a `DatabaseContext.CreateAsync` factory.
    Requires rewriting the ~400-line private constructor into a shared async core the sync
    constructor also routes through, making the full test suite the regression gate for that
    rewrite. `IConnectionStrategy.HandleDialectDetectionAsync` (an async twin of
    `HandleDialectDetection`) belongs here, not in Phase 2 — it would have no caller until this
    factory exists. Deliberately deferred on its own merits, independent of Phase 2 being done.

### P2

- **SQL Server pays a live session-settings SET round trip on every operation under
  `DbMode.Standard`, unlike PostgreSQL.** Quantified by
  `benchmarks/CrudBenchmarks/results/sqlserver-equal-footing-run-2026-08-13.md`: under
  `DbMode.Standard` (a fresh ephemeral connection per operation), pengdows is consistently
  ~1.4-2.0x slower than Dapper (and, unusually, slower than EF Core too) against SQL
  Server, while PostgreSQL and SQLite show parity or better. Root cause traced end to end:
  `TrackedConnection`'s session-settings callback is gated by a per-*wrapper-instance* flag
  (`_wasOpened`), not a per-*physical-connection* flag, so in `DbMode.Standard` every
  logical checkout re-triggers it — regardless of whether the ADO.NET pool handed back a
  warm connection. PostgreSQL avoids this entirely via
  `PostgreSqlDialect.PrepareConnectionStringForDataSource`, which bakes the same settings
  into the Npgsql `NpgsqlDataSource`'s startup `Options` as GUC defaults that `RESET ALL`
  restores automatically on pool return — `SqlServerDialect` has no equivalent override,
  so the bake-and-skip path (`_rwSettingsBakedIntoDataSource`) never applies to it.

  **Important scoping, confirmed by a follow-up benchmark**
  (`sqlserver-hydration-hotpath-run-2026-08-13.md`): the 1.4-2.0x figure is close to a
  worst case for amortization — many small, independent, ephemeral-connection operations,
  each re-paying the full tax. With the session-init cost paid once instead of per
  operation (`DbMode.SingleConnection`, mirroring `HydrationHotPathBenchmarks.cs`'s
  SQLite normalization), the gap drops to 1.18x at 100 rows and **1.025x at 5,000 rows** —
  pengdows's actual row-materialization work is close to Dapper's; the large multiplier is
  specifically a property of `DbMode.Standard` under a workload of small, independent
  operations, not a general statement about the SQL Server execution path. Whether an
  equivalent bake-in is even possible for `DbMode.Standard` on SQL Server (TDS/`SqlClient`
  has no direct analog to Postgres's arbitrary `Options=-c key=value` mechanism) is a
  narrower, lower-priority question than "SQL Server is broadly slower" made it look.

  **Decision: not pursuing a default-behavior change here.** Always-reapply exists because
  a connection from the pool — including one pengdows itself used a moment ago for a
  different, unrelated operation — can arrive with drifted session state, and the cost of
  getting this wrong is correctness, not just consistency: `QUOTED_IDENTIFIER ON` is the
  specific setting that makes the framework's own ANSI double-quote identifier quoting
  (`WrapObjectName`) parse at all — e.g. `SELECT "col 1" FROM "name space"."table name"`.
  Without it, that's not subtly different behavior, it's broken SQL (the quotes are read as
  a string literal, not an identifier delimiter). A few hundred microseconds against that
  failure mode is not a trade worth taking as the default. If a lower-cost path is ever
  built (batching was rejected for making SQL Server logs unreadable — see the design
  conversation this entry is drawn from), it should be an explicit, off-by-default opt-in
  requiring the caller to assert exclusive ownership of the connection string's pool, never
  a change to the default correctness-first behavior.
- **Reader latency doesn't distinguish database time from consumer time.** `ExecuteReaderAsync`
  metrics treat the command as complete once the provider returns the reader; time spent by the
  caller consuming rows isn't separated out. Proposed: execute→first-row, first-row→dispose, and
  total reader lease as three distinct timings.
- **Metric cardinality policy for dynamic multi-tenancy.** No deliberate policy yet for
  context/tenant-derived tags (e.g. `db.name`) that could become high-cardinality.
- **No multi-result-set support.** `TrackedReader.NextResult()` throws `NotSupportedException`
  by design; a real feature gap relative to some competitors, not a correctness bug.
- **Stored-procedure multi-result/OUT parameter handling** is less complete than the best
  specialized competitors.
- **Provider driver-version compatibility matrix.** Database-engine coverage is strong; testing
  across multiple meaningful driver releases (Npgsql, SqlClient, MySqlConnector/MySql.Data,
  Oracle providers, etc.) is not.
- **More mutation/fuzz/state-machine testing**, particularly around parameter rendering,
  connection lifecycle, transactions, cancellation, and mapping/coercion.
- **SingleWriter fairness torture test.** The turnstile activation bug is fixed and covered by a
  unit test (`SingleWriterTurnstileActivationTests.cs`), but there's no long-running stress test
  proving writers don't starve under continuous concurrent readers against a real SQLite file.
- **Broader transaction concurrency stress testing.** The specific reader-lock-lifetime gap is
  now covered (`TransactionReaderLockLifetimeTests.cs`), but general multi-threaded torture
  testing of the no-op/real/reusable locker architecture doesn't exist yet.

### P3

- **One immutable capability snapshot.** Capability truth is still spread across
  dialect/detection/context structures rather than one typed, inspectable surface.
- **Benchmark process issues** (misleading `Fails=0` reporting under contention, correctness
  sidecar files not surviving BenchmarkDotNet artifact cleanup) — lives in `benchmarks/`,
  separate from the core library, not touched by this review.
- **Documentation lag** — connection-mode semantics (Standard/SingleWriter/SingleConnection/
  KeepAlive), generated/tested capability tables, and the `crud`-naming/positioning problem (the
  name undersells that this is also an execution-policy/runtime layer).
- ~~**TiDB/MySql.Data prepare workaround** lacks a version number or upstream issue
  reference in its source comment~~ — fixed 2026-08-13: `TiDbDialect.cs` now names the
  tested `MySql.Data` version (9.3.0) and the exact mechanism (text-protocol backslash
  escaping corrupting string parameters). No public upstream issue could be found matching
  this exact bug despite a targeted search — it was apparently found empirically via this
  project's own TiDB integration testing, not from a tracked report. The comment now says
  so explicitly and flags that this should be re-verified against newer `MySql.Data`
  releases rather than assumed permanent.

---

## Fixed: multitenancy dialect-cache identity collision (2026-08-14)

**Symptom reported by user:** "when using multitenancy I can't use multiple versions of the same
database" — e.g. two tenants both on MySQL, one at 8.0.18 and one at 8.0.21, interfering with each
other's generated SQL.

**Root cause:** `TableGateway<TEntity,TRowID>`, `PrimaryKeyTableGateway<TEntity>`, and
`BaseTableGateway<TEntity>` are documented singletons deliberately shared across tenant contexts
(`gateway.Method(entity, tenantCtx)` — see this file's multi-tenancy guidance and
`pengdows.crud/CLAUDE.md`). Each tenant's `DatabaseContext` correctly gets its own `ISqlDialect`
instance with its own detected `ProductInfo.ParsedVersion` — that part always worked. The bug was
one layer up: eight SQL-template/binder/query cache fields across the three gateway classes keyed
on `ISqlDialect.DatabaseType` (the `SupportedDatabase` enum) instead of the dialect instance:

- `TableGateway.Core.cs`: `_insertBinders`, `_upsertBinders`, `_updateBinders`,
  `_templatesByDialect`, `_containersByDialect`
- `PrimaryKeyTableGateway.Core.cs`: `_pkTemplatesByDialect`
- `BaseTableGateway.Core.cs`: `_queryCache`, `_whereParameterNames`

`ConcurrentDictionary.GetOrAdd` only builds a cached value once per key — whichever tenant's
dialect hit a given cache first for, say, `SupportedDatabase.MySql`, locked that cached SQL
fragment in for every other MySQL tenant on the same gateway singleton, permanently, regardless of
actual server version. Concretely demonstrated via `MySqlDialect.UpsertIncomingAlias`
(`MySqlDialect.cs`), which gates UPSERT syntax on `ProductInfo.ParsedVersion >= new Version(8,0,20)`:
whichever tenant called `UpsertAsync`/`BuildUpsert` first decided the `ON DUPLICATE KEY UPDATE`
syntax for every other same-enum tenant afterward — the loser got either a SQL syntax error or
silently wrong UPSERT behavior on their real server. Order-dependent (whichever tenant's dialect
populated the cache first "won"), which is what made it "subtle" rather than immediately obvious
in testing.

Telling detail: `BaseTableGateway.Core.cs` already had a *correctly*-instance-keyed cache
(`_wrappedTableNameCache`, `ConcurrentDictionary<ISqlDialect, string>`) sitting right next to the
enum-keyed ones — so the instance-keying pattern already existed in the codebase, just wasn't
applied consistently to the heavier SQL-template/binder/container caches.

**First fix (correctness):** all eight caches keyed on the `ISqlDialect` instance rather than
`DatabaseType`, using `ConditionalWeakTable<ISqlDialect, ...>` instead of
`ConcurrentDictionary<SupportedDatabase, ...>`. `ConditionalWeakTable` (not a plain instance-keyed
`ConcurrentDictionary`) specifically so a tenant's cached artifacts are reclaimed once its
`DatabaseContext`/dialect is no longer referenced — `TenantContextRegistry.ContextRemoved` already
exists for tenant offboarding, and a plain strong-reference dictionary keyed by instance would
silently convert "bounded but wrong" (the old bug, ~15 possible enum values) into "correct but
unbounded" (every dialect this gateway singleton has ever seen, pinned for the process lifetime).

Covered by `pengdows.crud.Tests/TableGatewayMultiTenantDialectCacheTests.cs`: one `TableGateway`
singleton, two tenant contexts (MySQL 8.0.19 vs 8.0.33) via a dialect-override `IDatabaseContext`
decorator, asserting each tenant's `BuildUpsert` produces its own version-correct SQL — in both
call orders (legacy-then-modern and modern-then-legacy), since the bug was order-dependent and a
single-order test wouldn't have proven the cache was actually fixed rather than just "the first
caller happened to be right this time."

**Follow-up (space efficiency) — fingerprint-keying for the pure-SQL-text caches:**
identity-keying is correct but doesn't dedupe tenants on the *identical* engine+version, which is
common in practice (e.g. a managed fleet standardized on one version, with an occasional
un-upgraded or newer outlier). A full audit of every property each of the eight caches' build
functions reads from the dialect (transitively, including helper calls) found the risk splits
cleanly in two:

- `_templatesByDialect`, `_pkTemplatesByDialect`, `_queryCache`/`_whereParameterNames` build pure
  SQL text/metadata — no `DbParameter` construction happens in them. Every property they read is
  either constant per `DatabaseType` or a pure function of `ProductInfo.ParsedVersion` (verified
  exhaustively, including the exact `MySqlDialect`/`TiDbDialect` `>= 8.0.20` upsert-alias
  threshold). These four are **now fingerprint-keyed**: `IInternalSqlDialect.CacheFingerprint`
  (default impl on `SqlDialect`: `"{DatabaseType}|{ParsedVersion}"`) replaces the dialect instance
  as the key, via `ConcurrentDictionary<string, ...>` instead of
  `ConditionalWeakTable<ISqlDialect, ...>`. Many same-version tenants now share one entry; cache
  cardinality is bounded by distinct engine+version combinations ever seen, not tenant count.
  Covered by `TableGatewayMultiTenantDialectCacheTests.BuildUpsert_TwoTenantsOnSameMySqlVersion_ShareOneCacheEntry`
  (two distinct dialect instances, same version → one cache entry) alongside the original
  different-version correctness tests (still passing, now against the fingerprint
  implementation).
- `_containersByDialect` and the three binder caches (`_insertBinders`/`_upsertBinders`/
  `_updateBinders`) all bake actual `DbParameter` construction permanently into the cached
  artifact — `CompiledBinderFactory` closes over the dialect instance itself via
  `Expression.Constant(dialect)` and keeps calling `dialect.CreateDbParameter(...)` for the
  cached delegate's entire lifetime. `CreateDbParameter`'s behavior depends on two things a
  version-only fingerprint can't see:
  1. **`FirebirdDialect.GuidStorageMode`** — an `init`-only, per-instance-configurable property
     (defaults to `Binary`), independent of server version, that changes GUID wire format via
     `GuidFormat`. Two Firebird tenants on the identical version but different
     `GuidStorageMode` would silently collapse under a version-only fingerprint and corrupt each
     other's GUID parameters — the direct Firebird analogue of the MySQL bug this entry started
     with.
  2. **The live `DbProviderFactory` instance** (`GetPooledParameter` → `Factory.CreateParameter()`)
     — not exposed anywhere on `ISqlDialect`, so no fingerprint built from today's interface
     surface can verify "same driver package" even in principle.

  These four caches are **still identity-keyed** (`ConditionalWeakTable<ISqlDialect, ...>`,
  unchanged from the first fix) rather than fingerprint-keyed, deliberately: extending the
  fingerprint to cover them safely needs (a) folding `GuidStorageMode` into
  `FirebirdDialect.CacheFingerprint` (mechanically straightforward, not yet done since nothing
  exercises it while these caches stay instance-keyed) and (b) a deliberate decision about the
  `DbProviderFactory`-identity gap — either accept it as a documented assumption ("tenants sharing
  a fingerprint are assumed to use the same driver package for that engine," realistic in
  practice — nobody mixes two different ADO.NET providers for one engine across tenants in the
  same app) or add a way to source a stable factory identity onto `ISqlDialect`. Pick this up with
  its own dedicated TDD pass (a Firebird `GuidStorageMode`-collision red test, mirroring
  `TableGatewayMultiTenantDialectCacheTests`) before converting these four.

---

## Fixed: audit fields no longer claim a write that never persisted (2026-08-14)

**What was wrong:** `SetAuditFields` mutates `LastUpdatedOn`/`LastUpdatedBy` (and
`CreatedOn`/`CreatedBy` on create) as a side effect of `Build*` — before any SQL executes. If
`Execute*` then fails, or (for `[Version]`-column entities) succeeds but affects 0 rows, the
entity's audit fields were left claiming a write that never happened. Concretely: an
optimistic-concurrency conflict (a normal, expected, already-documented outcome — see
`ConcurrencyConflictException`) would leave `entity.LastUpdatedOn` showing "just now" even though
the row in the database was untouched.

**Revised severity, reached by working through it with the user rather than accepting the
original filing at face value:** this is narrower than a blanket "always matters" defect. In the
two most common usage patterns it's actually harmless:
- **Retry with the same object until it succeeds** — each retry re-stamps a fresh value; the
  final persisted state matches the final stamped state.
- **Discard-and-reload on `ConcurrencyConflictException`** (the idiomatic response — refetch, let
  the caller retry the business operation) — the wrongly-mutated object gets thrown away before
  anyone observes it.

It has real, non-self-correcting impact in two narrower cases: (1) the Build tier's own
documented contract (`pengdows.crud/CLAUDE.md`: Build methods return an `ISqlContainer` "you
inspect, modify, or execute yourself" — dry-run/inspect-without-executing is an explicitly
supported use case, and there's no retry to paper over a write that was never attempted at all),
and (2) anything that inspects the entity immediately after a caught failure without reloading
(logging being the realistic example — reporting a timestamp that says "just now" for a write
that was rejected).

**Fix:** `BaseTableGateway.Audit.cs` gained `SnapshotAuditFields`/`RestoreAuditFields` — capture
the entity's audit-column values immediately before `SetAuditFields` mutates them, restore them
in a catch block (or before manually raising `ConcurrencyConflictException` on a 0-rows-affected
result) whenever the write doesn't actually succeed. Zero added round trips — pure in-memory
bookkeeping, consistent with this project's stance against paying round-trip costs for
correctness that don't need them (see the SQL Server session-settings entry above for the same
stance applied elsewhere).

**Scope: single-entity convenience methods only.** Wired into all 8 call sites that mutate audit
fields and execute in the same method body:
- `TableGateway<TEntity,TRowID>`: both `CreateAsync` overloads (`TableGateway.Core.cs`),
  `UpdateAsync(entity, loadOriginal, ...)` (`TableGateway.Core.cs`), `UpsertAsync`
  (`TableGateway.Upsert.cs`)
- `PrimaryKeyTableGateway<TEntity>`: `CreateAsync` (`PrimaryKeyTableGateway.Core.cs`), both
  `UpdateAsync` overloads (`PrimaryKeyTableGateway.Update.cs`), `UpsertAsync`
  (`PrimaryKeyTableGateway.Upsert.cs`)

**Follow-up round (same day): a `catch` block can't see a plain `return false`.** External review
of the first push correctly caught that the fix above only restored on a *thrown* exception. Several
of the 8 methods can signal an unsuccessful write without throwing at all — `ExecuteNonQueryAsync`
affecting 0 rows and the method just returning `false`/`0` (no exception, so the `catch` block
never runs):
- `TableGateway.CreateAsync` (both overloads) — the "default path" `return rowsAffected == 1;`,
  the PREFETCH branch's equivalent, and the CORRELATION TOKEN branch's explicit `return false;`
- `PrimaryKeyTableGateway.CreateAsync` — same shape, single branch
- `TableGateway.UpdateAsync` / `PrimaryKeyTableGateway.UpdateAsync` — 0 rows affected on an
  *unversioned* entity doesn't throw `ConcurrencyConflictException` (that only fires when
  `_versionColumn != null`); it just returns `0`, and the audit fields were never restored for
  that case
- `TableGateway.UpsertAsync` / `PrimaryKeyTableGateway.UpsertAsync` — same gap for unversioned
  entities, or versioned entities on a dialect that can't detect the conflict (MySQL/MariaDB
  `ON DUPLICATE KEY`, Firebird, non-`WHERE` `ON CONFLICT`)

Fixed by adding `RestoreAuditFieldsIfFailed` (`BaseTableGateway.Audit.cs`) — the same restore,
called explicitly at every point a method observes an unsuccessful result and is about to
`return` normally, not only from the `catch` blocks. `UpdateAsync`/`UpsertAsync` now restore
unconditionally on `rowsAffected == 0` before the (still-conditional) `ConcurrencyConflictException`
throw, rather than only when a version column made that throw happen.

Covered by 6 additional tests (13 total in `AuditFieldRestoreOnFailureTests.cs`) forcing
`ExecuteNonQueryAsync` to return 0 without throwing (`fakeDbFactory.SetNonQueryResult(0)`) across
all 8 call sites.

**Third round (same day): a post-write failure was restoring PRE-write audit values.** Further
external review caught that the follow-up round's fix was itself too blunt: the generic
`catch { RestoreAuditFields(...); throw; }` restores unconditionally, but several of
`TableGateway.CreateAsync`'s `GeneratedKeyPlan` branches do a post-INSERT step (retrieving a
server-generated ID via a fallback query) *after* the INSERT has already committed. If that
fallback step throws — the row already exists with the new audit values; the INSERT itself
succeeded — restoring at that point makes the entity falsely claim a rollback that never happened,
which is arguably worse than the original bug (now the entity looks like a failed write when it
actually succeeded).

Fixed by tracking a `writeSucceeded` flag, set once each branch's actual persisting write is known
to have been accepted by the database (before any post-write fallback step that could itself
throw). The shared `catch` now only restores when `!writeSucceeded`. (A one-element `bool[]`
carries the flag into `ExecuteReaderInsertedIdAsync`, a private helper called via `await`, since
`ref`/`out` parameters aren't allowed on async methods.) The general principle, stated precisely:
```
before the write is known to have succeeded: failure → restore the prepared audit mutation
after the write is known to have succeeded:  failure → do NOT restore; the DB has the new values
```
Covered by a dedicated test forcing SQLite's `CompoundStatement` create plan to successfully
INSERT, then fail on the fallback `SELECT last_insert_rowid()` query specifically (via
`fakeDbConnection.SetCommandFailure`) — proving the entity keeps its new audit values rather than
being rolled back to defaults.

**Fourth round (same day): the flag itself was set a little too late in three branches.** Further
review of the third round's placements found the flag was being set *after* a step that could
still throw before the database had actually accepted anything else, narrowing but not closing the
gap: the Oracle `RETURNING`/`OutputInserted` branch set it after `GetParameterValue(...)` (reading
an already-populated OUT parameter, but still a call that could throw before the flag was true);
the `CompoundStatement` branch and `ExecuteReaderInsertedIdAsync` both set it *after* their entire
`await using (var reader = ...)` block, including navigating to and reading the trailing
`SELECT`/`LastInsertedId` result — but the INSERT itself (the compound statement's first result
set) has already run server-side the moment `ExecuteReaderAsync` returns without throwing, before
any of that navigation happens. Fixed by moving each assignment to immediately follow the call
that actually submits the write (right after `ExecuteNonQueryAsync`/`ExecuteScalarOrNullAsync` for
Oracle, and as the first line inside the `await using (var reader = ...)` block for the other two)
— the earliest point each branch can truthfully say the database has accepted the write. Also
reworded the surrounding comments from "committed" to "the database accepted the write" per this
round's feedback: this layer executes one command and observes whether it threw, which is a
narrower claim than "committed" implies for a provider participating in an external/ambient
transaction.

No new tests for this round: forcing `GetParameterValue`/`NextResultAsync`/
`GetLastInsertedIdFromCommand` specifically to throw *after* their preceding `Execute*Async` call
already succeeded isn't practically simulable with `fakeDb` today (there's no hook to fail reader
navigation independently of the initiating execute call). Verified by code inspection against the
same principle the third round's test already covers, plus the full regression suite (14 tests in
`AuditFieldRestoreOnFailureTests.cs`, full solution: 6191 tests) — documenting this gap explicitly
rather than claiming coverage that doesn't exist.

**Reframed, not fixed — Build-tier mutation is by design, not a defect.** Working through this
with the user reset the mental model entirely. There is no general invariant available of "after
Build (or even after Execute) the entity equals the database row" — even a fully successful write
can diverge immediately: triggers, computed columns, server-generated defaults, another
transaction's concurrent write. `[Version]` `rowversion`/`timestamp` columns make this structural,
not incidental (SQL Server generates the new value itself; nothing short of `OUTPUT`/`RETURNING`
or a reload can know it without another round trip). Given that, "Build must be side-effect free"
was the wrong contract to aim for. The right one distinguishes three separate concepts that were
being conflated:
```
1. Prepared entity state    "These are the values we're about to write."      — Build can set this
2. Write outcome             "Did the database accept the operation?"         — known from Execute
3. Database-current state    "Does this object exactly match the row now?"    — NOT generally knowable
```
`BuildCreate`/`BuildUpdateInternal` populating audit fields (and writable IDs, and initializing an
app-managed `[Version]` to 1) is concept #1 — legitimate inputs to the SQL being built, not a
false claim about #2 or #3. `var sc = gateway.BuildCreate(entity);` mutating `entity` before `sc`
is ever executed is therefore expected: those are the values the *prepared* INSERT would write,
not an assertion that it happened. Restoring on a **convenience method's** failure still makes
sense, because that API layer explicitly knows #2 — it attempted execution and knows the DB
rejected it. Build alone never reaches #2, so it has nothing to restore *from*. No code change
from this — documenting the contract precisely is the fix.

**Still open — partial batch failure.** `BatchCreateAsync`/`BatchUpdateAsync`/`BatchUpsertAsync` on
both gateway types mutate audit fields for *every* entity in one upfront loop, before any container
is built or executed; execution then happens container-by-container (or entity-by-entity for
`PrimaryKeyTableGateway`'s per-entity batch update) with no surrounding try/catch. A failure
partway through leaves earlier containers' entities correctly persisted-and-stamped, but every
entity in a not-yet-executed container already carrying mutated (wrong) audit fields. A correct fix
needs a snapshot scoped per-container (or per-entity), not one snapshot for the whole batch call —
restoring everything on any failure would incorrectly roll back audit fields on entities whose row
*did* get written. That's a different, bigger design than the single-entity fix and wasn't bundled
in here.

**Also explicitly out of scope, raised separately during this work — post-execution entity
freshness on *success*:** even a fully successful `UpdateAsync` never writes the new `[Version]`
value back into the caller's entity today. This splits into two cases with very different
fixability:
- **App-managed integer/counter version** (`SET version = version + 1`) — pengdows computed that
  increment itself and knows the exact new value with certainty. Free fix (no round trip): write
  it back to the entity on success. Not yet done.
- **DB-managed `rowversion`/`timestamp`** (`byte[]`, already correctly excluded from the SET
  clause — see `TableGateway.Sql.cs`'s "DB handles increment" comment) — the new value is
  generated server-side. There's no free fix; closing this needs either `OUTPUT`/`RETURNING`
  support to capture it inline, or the caller must explicitly reload. Staleness here is
  structural, not an oversight.

User's proposed design for a future pass: an enum/options parameter (e.g. `None` /
`RefreshComputedFields` / `ReloadFromDatabase`) letting callers choose the cost/freshness
trade-off explicitly per call, rather than the library silently picking one. Worth designing
properly (dialect `OUTPUT`/`RETURNING` capability varies) rather than folding into a future bug
fix — tracked here so the design isn't lost.

Overall coverage across the four rounds above: 14 tests in
`pengdows.crud.Tests/AuditFieldRestoreOnFailureTests.cs`, spanning both gateway types, all three
operations (Create/Update/Upsert), thrown failures, the version-conflict (0-rows-affected,
manually-raised-exception) path, non-throwing 0-rows results, and the post-write-success failure
case. The fourth round's precise flag-placement fix has no dedicated new test (see that round's
entry for why) — verified by code inspection plus the existing suite. Current status:
```
Thrown pre-write failure                        FIXED
Detected optimistic-concurrency failure         FIXED
0-row non-throwing failure                      FIXED
Post-write-success failure (must NOT restore)   FIXED, flag placed at the true write boundary
Build mutates prepared entity state             BY DESIGN — documented above, not a defect
Partial batch failure                           STILL OPEN — see above
Entity freshness after a successful write       SEPARATE OPTIONAL CAPABILITY — see above
```
