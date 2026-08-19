# Future Work

This document tracks features that have been designed or partially specified but are not yet
implemented. Items here are not roadmap commitments — they are recorded so the design thinking
is not lost and can be picked up when the need arises.

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

    `SqlDialectFactory.CreateDialectAsync` and `DataSourceInformation.CreateAsync` now both accept
    an optional `CancellationToken`; it is checked before opening/probing, passed to
    `ITrackedConnection.OpenAsync`, and propagated to `DetectProductAsync`. The focused
    `DataSourceInformationAsyncTests.CreateAsync_PreCanceledToken_DoesNotOpenOrProbeConnection`
    regression test proves a pre-cancelled request stops before any connection I/O. This is a
    direct-call capability only: ordinary `DatabaseContext` construction is still synchronous.
  - Phase 3 (not started, the risky part): expose it via a `DatabaseContext.CreateAsync` factory.
    Requires rewriting the ~400-line private constructor into a shared async core the sync
    constructor also routes through, making the full test suite the regression gate for that
    rewrite. `IConnectionStrategy.HandleDialectDetectionAsync` (an async twin of
    `HandleDialectDetection`) belongs here, not in Phase 2 — it would have no caller until this
    factory exists. Deliberately deferred on its own merits, independent of Phase 2 being done.

### P2

- **SQL Server UUIDv7 index-locality strategy.** RFC 9562 UUIDv7 places its Unix-millisecond
  timestamp in the most-significant 48 bits in network byte order, which makes canonical UUIDv7
  values naturally time ordered for providers that compare UUID bytes in that order. SQL Server's
  `uniqueidentifier` ordering is different: its comparison semantics do not compare the UUID bit
  pattern directly and give the final six bytes special significance. Therefore the existing,
  correct default — bind a `Guid` as `DbType.Guid` — preserves canonical UUIDv7 identity and
  portability but does not provide UUIDv7's expected clustered-index locality on SQL Server.

  Do **not** add an implicit `SqlServerDialect` byte conversion. A reversible byte permutation
  would preserve collision resistance, but the raw stored value would no longer be a canonical
  UUIDv7 and would be misleading to direct SQL consumers, replication/export tools, and tenants
  using another provider. It also cannot be selected unconditionally in generated tenant-shared
  code because a tenant may use PostgreSQL, Db2, SQLite, or another provider.

  `SqlServerUuid7OrderingTests.CanonicalUuid7Values_DoNotSortChronologicallyAsUniqueIdentifiers`
  now establishes the actual ordering behavior against a live SQL Server: two valid UUIDv7 values
  whose timestamps increase sort in the opposite order when stored as `uniqueidentifier`.

  Revisit only as an explicit, SQL-Server-only storage strategy, resolved from the tenant's
  `DatabaseContext`/dialect rather than remembered by application code. It must define migration
  rules and prohibit mixing canonical and transformed values in one column. If the feature is
  justified, add fakeDb ordering emulation and provider-specific integration tests.
- ~~**Reader latency doesn't distinguish database time from consumer time.**~~ — fixed 2026-08-19:
  `ExecuteReaderAsync` continues to complete its command metric when the provider returns the
  reader. `TrackedReader` now separately records reader-acquisition→first-row,
  first-row→dispose, and total reader-lease duration. The aggregate `DatabaseMetrics` and
  role-scoped `DatabaseRoleMetrics` expose the three EWMAs as `AvgReaderTimeToFirstRowMs`,
  `AvgReaderConsumptionMs`, and `AvgReaderLeaseMs`. Unit coverage proves synchronous and async
  row reads feed the lifecycle metrics without conflating consumer time with command execution.
- **Metric cardinality policy for dynamic multi-tenancy.** No deliberate policy yet for
  context/tenant-derived tags (e.g. `db.name`) that could become high-cardinality.
- **Stored-procedure multi-result/OUT parameter handling** is less complete than the best
  specialized competitors.
- **Provider driver-version compatibility matrix.** Database-engine coverage is strong; testing
  across multiple meaningful driver releases (Npgsql, SqlClient, MySqlConnector/MySql.Data,
  Oracle providers, etc.) is not.
- **More mutation/fuzz/state-machine testing**, particularly around parameter rendering,
  connection lifecycle, transactions, cancellation, and mapping/coercion.
- ~~**SingleWriter fairness torture test.**~~ — fixed 2026-08-16:
  `SingleWriterFairnessTortureTests.cs` (`pengdows.crud.Tests`) proves writers don't starve under
  sustained concurrent readers against a real, file-backed SQLite `DatabaseContext` in
  `DbMode.SingleWriter`. 16 continuously-looping readers (each holding an `ITrackedReader` open
  briefly per iteration, a fresh admission attempt every loop) run for a 2-second contention
  window alongside 40 concurrent writers; asserts every write lands (no silent starvation), no
  writer's latency approaches the governor's acquire timeout, and readers keep making real
  progress throughout. This is deliberately an integration-level liveness net, not a re-proof of
  the gating mechanism itself — that's already covered deterministically at the bare
  `PoolGovernor` level (`PoolGovernorFairnessTests.WriterWithTurnstile_BlocksNewReaders` asserts a
  gated reader literally throws `OperationCanceledException` until the writer releases). An
  earlier attempt to make this test discriminate fairness on/off via wall-clock latency comparison
  was abandoned after empirical A/B testing showed the workload was too light (sub-millisecond
  local SQLite ops) to produce a measurable difference either way at realistic scale — a real
  finding, not a shortcut: precise timing-based fairness proof isn't a reliable lever here: the
  unit-level deterministic test already owns that job.
- ~~**Broader transaction concurrency stress testing.**~~ — fixed 2026-08-16:
  `SingleConnectionConcurrencyTortureTests.cs` runs mixed concurrent reads, writes, and
  transactions (8 of each) against a real, file-backed SQLite `DatabaseContext` in
  `DbMode.SingleConnection` for a sustained window, asserting no lost/corrupted writes and no
  deadlock (bounded overall timeout).

  This test found two real, previously-unknown bugs on first runs, both now fixed:
  1. Concurrent `BeginTransaction()` calls could race directly on the provider's own transaction
     state (confirmed live: a raw `Microsoft.Data.Sqlite` "nested transactions" exception) — no
     lock was held around the `BeginTransaction()` call itself, for any provider.
  2. More seriously: an ordinary (non-transactional) **write** executing while another task's
     transaction was still open on the same shared connection could be silently absorbed into
     that transaction's uncommitted scope and rolled back with it — confirmed via a real
     268-vs-252 row-count mismatch under concurrent load. Reads are not at risk the same way (no
     side effect to lose), and — critically — an existing, intentional test
     (`TransactionStreamingTests.LoadStreamAsync_TransactionContext_PassedExplicitly_UsesCorrectConnection`)
     deliberately reads via the plain context while a transaction is open on the same connection,
     so any fix had to leave reads unblocked.

  **Fix:** `DbMode.SingleConnection` is already fully serialized by design (every operation, once
  it reaches the connection, is exclusive) — a transaction is now treated as a longer-held
  instance of that same serialization rather than a special case. A dedicated gate
  (`DatabaseContext.GetSingleConnectionTransactionGate()`, `RealAsyncLocker`-backed, reusing the
  existing `ModeLockTimeout` — default 30s — as its bound) is acquired by `BeginTransaction()`/
  `BeginTransactionAsync()` for the transaction's whole lifetime, and briefly by ordinary
  non-transactional **writes** only (`SqlContainer.ExecuteNonQueryAsync`) before executing. It is
  a separate semaphore from the connection's existing per-command lock (`TrackedConnection
  .GetLock()`), so a transaction holding it for its whole span never deadlocks against its own
  commands, which still acquire that other lock as normal per-command.

  **A genuine deadlock was found and fixed during this work, not just a design risk:**
  `ExecuteNonQueryAsync` and `ExecuteReaderAsyncInternal` originally acquired the new gate and the
  existing connection lock in *opposite orders* — a textbook circular-wait deadlock between a
  concurrent reader and writer, reproduced as a consistent (not intermittent) full-suite timeout.
  Resolved by removing the gate from the read path entirely (see reasoning above) rather than
  just reordering, since reads don't need it at all.

  Also found and fixed: calling the *synchronous* `BeginTransaction()` from inside an async
  `Task.Run` continuation under real contention risked starving the thread pool of the threads
  needed to run the continuations that would release the same gate — fixed by using
  `BeginTransactionAsync()` in the torture test's transaction workload, matching how async
  application code should call it under load in this mode.

  Covered by `SingleConnectionConcurrentTransactionGuardTests.cs` (block-then-succeed once the
  holder completes, timeout-after-`ModeLockTimeout` via `ModeContentionException`, and the
  specific ordinary-write-blocks-behind-active-transaction case) and the torture test itself.
  Full suite green across multiple repeated runs (6335 tests) after each fix, including the
  pre-existing `TransactionStreamingTests` suite that exercises the intentional
  read-while-transaction-is-open pattern this fix had to preserve.

### P3

- **One immutable capability snapshot.** Capability truth is still spread across
  dialect/detection/context structures rather than one typed, inspectable surface.
- **Benchmark process issues** (misleading `Fails=0` reporting under contention, correctness
  sidecar files not surviving BenchmarkDotNet artifact cleanup) — lives in `benchmarks/`,
  separate from the core library, not touched by this review.
- **Documentation lag** — partially resolved 2026-08-16:
  - Connection-mode semantics: `docs/CONNECTION-MODES.md` §4 had a concrete factual error, found
    while re-reading it against `TrackedConnection`'s actual behavior — it claimed session settings
    are "not reapplied when a connection is reused from pool," which is backwards for `Standard`/
    `SingleWriter` modes. `TrackedConnection._wasOpened` is a per-*wrapper*-instance flag, not a
    per-*physical*-connection one; ephemeral modes create a fresh wrapper per checkout, so the
    preamble genuinely reapplies every single time, even when the ADO.NET pool hands back an
    already-open physical connection (deliberately — see this file's SQL Server session-settings
    entry for why trusting pooled connection state is a correctness risk, not just a consistency
    one). Only persistent modes (KeepAlive's pinned connection, SingleConnection) apply it exactly
    once. Fixed the doc to state the per-mode rule explicitly instead of one blanket (wrong) claim.
  - Generated/tested capability tables: already comprehensive — `docs/supported-databases.md` has
    a 114-line enum/version-floor/feature-threshold matrix across all 16 databases. Nothing to add.
  - `crud`-naming/positioning problem: still open — this is a product-positioning question
    (`docs/PRODUCT_THESIS.md` territory), not a documentation-accuracy bug; no doc edit resolves it.
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
  unchanged from the first fix) rather than fingerprint-keyed — no live bug exists today, since
  distinct dialect instances never collide regardless of `GuidStorageMode` while they stay
  identity-keyed. Converting them to fingerprint-keying is not planned/decided.

  **Precondition (a) satisfied 2026-08-17, as groundwork only, ahead of any decision to
  convert:** `FirebirdDialect.CacheFingerprint` now folds in `GuidStorageMode`
  (`$"{base.CacheFingerprint}|{GuidStorageMode}"`), so two Firebird tenants on the identical
  server version but different `GuidStorageMode` no longer collapse onto one fingerprint if/when
  something does start keying on it. Covered by `FirebirdCacheFingerprintTests.cs`
  (`pengdows.crud.Tests/dialects/`): differing `GuidStorageMode` values produce different
  fingerprints; matching values still share one. Full suite green (6337 tests) after the change.

  Precondition (b) — the `DbProviderFactory`-identity gap — remains open and undecided: either
  accept it as a documented assumption ("tenants sharing a fingerprint are assumed to use the same
  driver package for that engine," realistic in practice — nobody mixes two different ADO.NET
  providers for one engine across tenants in the same app) or add a way to source a stable factory
  identity onto `ISqlDialect`. Still needed before actually converting `_containersByDialect`/
  `_insertBinders`/`_upsertBinders`/`_updateBinders` to fingerprint-keying — which itself remains
  undecided/not scheduled.

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
- ~~**App-managed integer/counter version**~~ — fixed 2026-08-16: `BaseTableGateway.Version.cs`'s
  new `WriteBackIncrementedVersion(entity)` computes `current + 1` and writes it back after a
  successful `UpdateAsync` (`rowsAffected > 0`), for both `TableGateway<TEntity,TRowID>` and
  `PrimaryKeyTableGateway<TEntity>`. No round trip needed: the entity's version property is never
  mutated while building the UPDATE (the WHERE clause reads it as-is for the optimistic-concurrency
  check), so a successful write's WHERE-match guarantees the new value is deterministically
  "current + 1". Skipped for `byte[]` rowversion/timestamp columns (see below — no free fix exists
  for those). Covered by `TableGatewayVersionWriteBackTests.cs` (success write-back for both
  gateway types, and a conflict case proving a *failed* write does not fabricate a new value) plus
  a new byte[]-exclusion regression test in `TableGatewayByteArrayVersionTests.cs`. Full suite:
  6328 tests, one pre-existing flaky test unrelated to this change
  (`SqlContainerActivityContextIdTests`, confirmed by rerunning it alone).
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

---

## Fixed: hardcoded per-database checks removed from the gateway layer (2026-08-14)

**What was wrong:** while working the `writeSucceeded` precision fix above, the user raised a
standing architectural principle this codebase is supposed to follow — database independence.
Any database-specific behavior belongs behind an `ISqlDialect` capability the gateway asks about
generically; the generic gateway classes (`TableGateway`, `PrimaryKeyTableGateway`,
`BaseTableGateway`) should never name a specific `SupportedDatabase` value themselves. An audit
found 5 places (9 call sites, all confined to `TableGateway.Core.cs`, `TableGateway.Upsert.cs`,
`PrimaryKeyTableGateway.Upsert.cs` — zero elsewhere) where the gateway violated this:

1. `TableGateway.Core.cs` — `dialect.DatabaseType == SupportedDatabase.Oracle` (two overloads)
   picked `ExecuteNonQueryAsync`+`GetParameterValue` vs `ExecuteScalarOrNullAsync` for
   generated-key retrieval after a `Returning`/`OutputInserted` INSERT.
2. `TableGateway.Core.cs`'s `BuildCreateWithReturning` — `== SupportedDatabase.SqlServer` picked
   OUTPUT-before-VALUES clause placement, sitting right next to an *already-existing, already-
   correct, completely unused* capability (`ISqlDialect.InsertReturningClauseBeforeValues`,
   `SqlServerDialect.cs:265`) that did the exact same job — the generic mechanism had already
   been built and just never wired in. The same block also had `== SupportedDatabase.Oracle` for
   the OUT-parameter/clause-rewriting sub-case.
3. `TableGateway.Upsert.cs` / `PrimaryKeyTableGateway.Upsert.cs` — `dialect.SupportsMerge &&
   ctx.DataSourceInfo.Product != SupportedDatabase.Firebird` decided whether a 0-rows-affected
   UPSERT could be trusted as a version conflict.
4. Same two files — `ctx.DataSourceInfo.Product == SupportedDatabase.Firebird` routed to
   `BuildFirebirdMergeUpsert`/`BuildPkFirebirdMergeUpsert` instead of standard MERGE, both gated
   by the same `SupportsMerge` flag.
5. `PrimaryKeyTableGateway.Upsert.cs` — `ctx.DataSourceInfo.Product != SupportedDatabase.Firebird`
   guarded whether a pure-`[PrimaryKey]`-only entity (no updateable columns) could upsert at all.

Root cause of #3/#4: `SupportsMerge` is overloaded to mean both "in the merge-syntax family" and
"emits literal `MERGE ... WHEN MATCHED`" — Firebird is `true` for the first sense (it's
version-gated SQL:2003-level support) but needs `false` for the second everywhere it's consumed,
so every consumer had bolted on its own `!= Firebird` patch instead of asking a single capability.

**Fix:** three new `ISqlDialect` capabilities, following the exact pattern the codebase already
uses successfully for `GeneratedKeyPlan` and `InsertReturningClauseBeforeValues` — declared as
C# 8 default-interface-method properties (so only dialects that differ from the default need to
override), with a matching `public virtual` declaration on the `SqlDialect` base class (required
for subclasses to `override` a default-interface member):

- `bool RequiresOutputParameterForReturning` (default `false`; Oracle `true`) — replaces the
  three Oracle checks (#1 both overloads, #2's Oracle half).
- `bool EmitsAnsiMergeSyntax` (default **`true`**; Firebird `false`) — replaces #3/#4's four
  `!= Firebird`/`== Firebird` checks. Defaulting to `true` (not `false`) was a deliberate,
  verified choice: `SupportsMerge` is currently `true` for SQL Server, Oracle, Snowflake, DuckDB
  1.4+, and PostgreSQL 15+ — defaulting the new property to `false` and only overriding the first
  three would have silently broken conflict detection for DuckDB/PostgreSQL, which the *old*
  `!= Firebird` check happened to get right by accident. Verified each `SupportsMerge` override
  directly (`grep`'d every dialect) before picking the default, specifically to avoid that trap.
- `bool SupportsPureKeyUpsert` (default `false`; Firebird `true`) — replaces #5, opposite polarity
  from the merge property since Firebird is the sole *positive* exception here, not the sole
  negative one.

`#2`'s SqlServer half was fixed by simply wiring in the pre-existing
`InsertReturningClauseBeforeValues` — no new API needed for that part.

**Deliberately not done:** the audit's own suggestion was a full `GetUpsertSyntaxStyle()` enum
(mirroring `GeneratedKeyPlan`) to replace the top-level upsert dispatch entirely. Declined: the
top-level dispatch (`SupportsMerge`/`SupportsInsertOnConflict`/`SupportsOnDuplicateKey`) was
already clean and database-agnostic — the actual problem was narrower (distinguishing Firebird's
merge-*like* syntax from true ANSI MERGE *within* the already-correct `SupportsMerge` bucket).
Replacing working, already-generic dispatch with a parallel enum to fix a problem one boolean
already solves would have been a larger diff for no correctness gain.

Covered by 26 new tests in `pengdows.crud.Tests/dialects/DialectCapabilityTests.cs` (one dialect
per `SupportedDatabase` value per property, both the lone exception and the "everyone else"
case) — a deliberate deviation from strict TDD ordering (properties were written before tests,
given they're simple additive facts, not complex logic) was caught and corrected: verified each
test is actually meaningful by temporarily breaking `FirebirdDialect.SupportsPureKeyUpsert` and
confirming the corresponding test failed, before restoring it. Zero remaining
`SupportedDatabase.X` comparisons in `TableGateway.*.cs`/`PrimaryKeyTableGateway.*.cs`/
`BaseTableGateway.*.cs` (confirmed by grep). Full regression suite (6233 tests) passes unchanged,
confirming the refactor preserved existing Oracle/SqlServer/Firebird behavior rather than just
compiling.

**Integration validation — done.** The capability refactor above, plus the multitenancy
dialect-cache fix and all four audit-field-restoration rounds, have now been validated against
real database instances via `pengdows.crud.IntegrationTests` (Testcontainers) and the full
`testbed/` suite, not just `fakeDb`. Running against real engines surfaced two genuine bugs that
`fakeDb` is structurally incapable of catching, since it never parses or executes real SQL and
never returns a real server version banner:

1. **`SqlDialect.ParseVersion` picked up the C compiler's version instead of the server's.**
   The base implementation (`SqlDialect.cs`) matched the *last* dotted-number sequence in a
   version string. Real PostgreSQL's `SELECT version()` banner ends with the gcc version it was
   compiled with (e.g. `"PostgreSQL 18.1 ..., compiled by gcc (Debian 14.2.0-19) 14.2.0, 64-bit"`),
   so on virtually every gcc-built PostgreSQL server — i.e. every Linux/Docker image — this
   silently returned `14.2.0` instead of `18.1`, disabling every `IsVersionAtLeast()`-gated
   capability (`SupportsMerge`, `SupportsJsonTypes`, `SupportsSqlJsonConstructors`,
   `SupportsJsonTable`, `SupportsMergeReturning`) regardless of the real server version. Fixed
   with a `PostgreSqlDialect.ParseVersion` override matching `PostgreSQL\s+(\d+(?:\.\d+)*)`
   specifically, ignoring anything after. Covered by
   `pengdows.crud.Tests/PostgreSqlVersionParsingTests.cs` using real captured banners from two
   different PostgreSQL builds.
2. **MERGE version-increment fragment produced an ambiguous column reference on real Postgres.**
   The `"version" = "version" + 1` fragment (`TableGateway.Sql.cs`,
   `PrimaryKeyTableGateway.Core.cs`) reused the same alias-prefix variable on both sides, which is
   empty for dialects where `MergeUpdateRequiresTargetAlias == false`. Both the MERGE target and
   source expose a `version` column, so the unqualified RHS is genuinely ambiguous — PostgreSQL's
   real MERGE parser rejects it (`42702: column reference "version" is ambiguous`); `fakeDb` never
   parses the SQL so never caught it. Fixed by hardcoding the RHS to the target alias (`t.`)
   regardless of `MergeUpdateRequiresTargetAlias`. Covered by new regression tests in
   `BuildUpsertSqlGenerationTests.cs` and `PrimaryKeyTableGatewayTests.cs` asserting the qualified
   form, plus real end-to-end coverage against live PostgreSQL 18 and Firebird in
   `pengdows.crud.IntegrationTests/Core/VersionedUpsertConflictTests.cs`.

Both fixes independently verified (not just trusted from the validation pass): read and confirmed
the root cause in the actual source for each, rebuilt the full solution clean, ran the full unit
suite (6240/6240), ran the new/modified integration tests directly against real Docker/
Testcontainers (11/11 passed: Firebird pure-key upsert, real-unique-constraint audit-restoration,
real-PostgreSQL-18/Firebird MERGE conflict-detection), ran
`MultiTenantDialectVersionTests.cs` (two real MySQL 8.0.19/8.0.33 containers sharing one
`TableGateway`, proving version-specific SQL is generated correctly for each) directly, and ran
the full `testbed/` suite directly: 11/11 databases, 207/207 checks, 0 failures, 23 pre-existing
skips.

**Benchmark validation — done.** Two new fakeDb-only BenchmarkDotNet benchmarks were added since
none of the existing suite exercised `CreateAsync` execution or `BuildUpsert`'s MERGE-capability
branches:

- `benchmarks/CrudBenchmarks/Internal/CreateAsyncAuditOverheadBenchmarks.cs` — `CreateAsync` with
  vs. without audit columns, isolating the CPU cost of the `writeSucceeded`/audit snapshot-restore
  bookkeeping.
- `benchmarks/CrudBenchmarks/Internal/UpsertCapabilityBenchmarks.cs` — `BuildUpsert` on SQL Server
  (`EmitsAnsiMergeSyntax == true` branch) vs. Firebird (`== false` branch).

Compared a read-only worktree at the pre-session commit, HEAD with the capability refactor
reverted, and HEAD with the refactor applied (`InvocationCount=8192, IterationCount=20,
WarmupCount=5`, all fakeDb/in-memory, no real I/O). Results:

- **Audit snapshot/restore overhead:** ~8-13% cost difference between audited and non-audited
  entities, but that gap is present even in the pre-session baseline (it's the pre-existing cost
  of `SetAuditFields` reflection, not the new snapshot/restore bookkeeping) and does not increase
  monotonically across pre-fix → refactor-reverted → current; all three land within each other's
  StdDev. No measurable regression.
- **Dialect capability property reads:** SQL Server ANSI-MERGE and Firebird MATCHING-MERGE builds
  differ by ~1% between baseline and current, fully inside noise — consistent with a virtual
  property read costing the same as the enum comparison it replaced.
- **Fingerprint caching (multitenancy fix):** not independently isolated in a per-call benchmark —
  it's already committed on `2.0.6` HEAD (not part of this session's uncommitted diff), and
  architecturally it's a one-time-per-dialect-fingerprint cost (`Lazy`-cached template building via
  `ConcurrentDictionary<string, T>`), so a per-call `CreateAsync`/`BuildUpsert` benchmark wouldn't
  show it regardless. Allocated bytes were flat across all three benchmarked states, consistent
  with no added steady-state cost. If a dedicated cache-hit-rate/warm-up benchmark is wanted later,
  it isn't built yet.

Verdict: no statistically meaningful performance regression from any of this session's changes.

---

## RetryContext Subsystem (Governor-Aware Resilient Execution)

### Architectural Problem
Existing third-party retry libraries (such as Polly or manual retry loops) are unaware of low-level connection pool topology, connection hold times, or admission control. Wrapping raw ADO.NET or TableGateway calls in an external retry policy leads to two critical operational failure modes:
1. **Connection Holding during Backoff / Sleep**: If a transaction or connection is held while the thread sleeps between retries, connection pools saturate, starving other concurrent requests.
2. **Thundering Herds & Connection Storms**: When multiple concurrent requests experience transient database errors (e.g. deadlocks, lock timeouts), external retries wake up simultaneously and storm the connection pool and database engine, causing cascaded collapse.

### Design Principles of `RetryContext`

`RetryContext` is a first-class execution coordinator designed specifically to integrate with `DatabaseContext`, `PoolGovernor`, and `IAuditValueResolver`.

#### 1. Dual Retry Modes
- **Mode 1: Transactional (`ExecuteTransactionalAsync`)**:
  - Treats the entire operation delegate as an atomic, all-or-nothing unit of work.
  - On a transient error (`DatabaseException.IsTransient == true`), the current transaction is rolled back and its connection lease is immediately disposed.
  - In-memory entity modifications (such as audit stamps) are reverted using `RestoreAuditSnapshot` to ensure entity state matches the pre-execution baseline.
  - The thread releases its `PoolGovernor` slot before waiting with decorrelated exponential jitter.
  - On wake-up, it acquires a fresh slot from `PoolGovernor.AcquireAsync(ct)` and begins a new transaction lease from Step 1.
- **Mode 2: Sequential (`ExecuteSequentialAsync`)**:
  - Processes a stream or queue of independent items in strict sequence.
  - If a transient failure occurs on item $K$, only item $K$ is retried with backoff.
  - Items $1 \dots K-1$ remain committed and are not re-executed; once item $K$ succeeds, execution advances to item $K+1$.

#### 2. PoolGovernor Slot Coordination
- During backoff sleep, **zero connection slots are held**.
- Re-admission after backoff passes through the fairness turnstile of `PoolGovernor`, eliminating connection storms and preventing starvation of non-retrying traffic.

#### 3. Transient Exception Classification
- Automatically filters exceptions via `DatabaseException.IsTransient`:
  - `DeadlockException` (`TransientWriteConflictException`) $\to$ Retryable
  - `SerializationConflictException` $\to$ Retryable
  - `CommandTimeoutException` $\to$ Retryable
  - `UniqueConstraintViolationException` $\to$ Non-transient, fails fast without retry
  - `ForeignKeyViolationException` $\to$ Non-transient, fails fast without retry
