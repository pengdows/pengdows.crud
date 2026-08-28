# Future Work

This document tracks features that have been designed or partially specified but are not yet
implemented. Items here are not roadmap commitments — they are recorded so the design thinking
is not lost and can be picked up when the need arises.

---

## Removed: `EphemeralSecureString` (2026-08-28)

`pengdows.crud/EphemeralSecureString.cs`/`IEphemeralSecureString` was a real, well-built mechanism intended to keep credentials (connection-string passwords) securely in memory: AES-encrypted on construction with a per-instance key/IV, decrypted only inside `Reveal()`/`WithRevealed()`/`WithRevealedAsync()`, auto-zeroed the cached plaintext ~750ms after first reveal, and zeroed key/IV/ciphertext on `Dispose()`. Confirmed with the maintainer this was meant to be the answer to keeping passwords out of plain memory, but it was never actually wired into `DatabaseContextConfiguration`, connection-string handling, or `DbProviderLoader` — zero production call sites, only its own unit test.

Kept deliberately for a while so the design work wasn't lost, per this document's own stated purpose — then removed by explicit maintainer decision once that purpose was served (this entry, plus the class's git history, is the record). Removal was a public-contract change: deleted `pengdows.crud/EphemeralSecureString.cs`, `pengdows.crud.abstractions/IEphemeralSecureString.cs`, and `pengdows.crud.Tests/EphemeralSecureStringTests.cs`, then regenerated the `interface-api-check` baseline (451 → 447 signatures, the interface plus its 3 methods). Full solution build clean; full unit suite (6515 tests) shows the same 4 pre-existing failures, none new.

---

## Unwired "weird type" attributes

`types/attributes/WeirdTypeAttributes.cs` declares 12 attributes (`DbEnumAttribute`,
`JsonContractAttribute`, `ConcurrencyTokenAttribute`, `RangeTypeAttribute`, `ComputedAttribute`,
`CaseInsensitiveAttribute`, `AsStringAttribute`, `MaxLengthForInlineAttribute`,
`CaseFoldOnReadAttribute`, `SpatialTypeAttribute`, `CurrencyAttribute`) whose only consumer in the
whole codebase is their own construction/property unit test
(`pengdows.crud.Tests/WeirdTypeAttributesTests.cs`). None of them are read by `TypeMapRegistry`,
`TypeCoercionHelper`, or any dialect — applying one to an entity property currently has zero
effect on SQL generation or type coercion. See `docs/advanced-types.md` for the full list and the
naming collisions with real, wired attributes (`DbEnumAttribute` vs. `EnumColumnAttribute`;
`JsonContractAttribute` vs. `[Json]`).

Needs a decision: wire each into the mapping pipeline (define what it should actually do —
e.g. `SpatialTypeAttribute.ExpectedSrid` enforcing SRID on write/read, `CurrencyAttribute`
validating/formatting a decimal column) or remove them. Leaving them in source as inert,
constructible attributes risks a caller applying one and silently getting no behavior.

---

## Pool-size precedence and mismatch logging — completed 2026-08-28

`MaxConcurrentReads`/`MaxConcurrentWrites` now have an explicit precedence contract over a conflicting provider `Max Pool Size`, and the mismatch is logged with both values and the winner. Enabled pools receive a minimum of `1`; `PreventDatabaseUnload` receives a minimum of `2` so its sentinel cannot consume the entire pool. Read-only writer pools remain at zero.

---

## Firebird Embedded Linux integration runtime — completed 2026-08-28

Firebird Embedded integration coverage now runs against the complete pinned Firebird
5.0.4 Linux distribution using Engine13. CI extracts the distribution and its TomMath/
TomCrypt dependencies into user-owned temporary storage, configures private Firebird
lock/temp directories, verifies that port 3050 has no listener, runs the real
`DatabaseContext` tests, and removes the exact runtime afterward. It does not invoke the
root-oriented installer, modify `/opt` or `/etc`, or depend on the obsolete `libfbembed.so`
deployment. The five embedded tests pass on both .NET 8 and .NET 10.

---

## `DatabaseMetrics.P95`/`.P99` silently return meaningless values unless `EnableApproxPercentiles` is set

`MetricsOptions.EnableApproxPercentiles` defaults to `false`; when unset, `MetricsCollector` never constructs its `PercentileRing` at all, so `DatabaseMetrics.P95`/`.P99` return their default value with nothing indicating why. See `docs/metrics.md` for the documented behavior as it stands today. Worth considering either flipping the default to `true` (the perf cost is a sliding window of `PercentileWindowSize` samples, not obviously expensive enough to justify opt-in-by-default) or having the property itself signal "not enabled" more clearly than a silently-zero value indistinguishable from "no commands executed yet" — a consumer building a dashboard off this value has no way to tell the two apart today.

---

## Two exception types for "read-only violation," not unified

A write attempt against a read-only context throws `NotSupportedException` (whole-context `ReadWriteMode.ReadOnly`) or a separate `InvalidOperationException("Transaction is read-only.")` (connection/transaction-scoped `IsReadOnlyConnection`), depending on which of two independent flags is set — see `docs/read-only-enforcement.md`. A caller wanting to catch "this was rejected for being read-only" generically has to catch both types with no shared marker beyond `Exception` itself. Worth considering a common base type or interface (`IReadOnlyViolation`?) both could implement, without changing which concrete type each site throws (to stay backward compatible) — purely additive.

---

## `ModeContentionException` sits outside the `DatabaseException` hierarchy — worth a deliberate look

Every other database/framework error in this library surfaces as a `DatabaseException` subclass (see `CLAUDE.md`'s Exception Hierarchy). `ModeContentionException` (a `SingleWriter`/`SingleConnection` mode-lock timeout) is the one exception: it extends `TimeoutException` directly. This may be entirely intentional — timeout semantics arguably matter more than database semantics for this one — but it wasn't found stated as a deliberate design decision anywhere, only as an implementation fact. Worth either documenting the "why" explicitly (so it reads as a decision, not an oversight) or reconsidering whether it should also implement a common marker so a `catch (DatabaseException)` block doesn't silently miss it.

---

## `AttributionStats` is half-wired — some counters recorded, never read; others never recorded at all

`DatabaseContext` holds an internal `AttributionStats` collector (`ReadRequests`/`WriteRequests`/governor-wait/governor-timeout/mode-wait counters). Only `RecordReadRequest`/`RecordWriteRequest` are ever actually called (`DatabaseContext.ConnectionLifecycle.cs`); the governor-wait, governor-timeout, and mode-wait recording methods are never invoked anywhere. And even the two counters that *are* recorded are never read back — `GetSnapshot()` has no caller anywhere in the codebase. So today this is pure overhead: two counters incremented on every request, for a snapshot nothing ever asks for. See `docs/metrics.md`. Either finish wiring it into `DatabaseMetrics`/`MetricsUpdated` (it looks like it was meant to enrich the existing metrics with governor-contention attribution) or remove it — right now it's neither providing value nor fully implementing its own apparent design intent.

---

## `DatabaseDetectionResult`'s evidence trail is never surfaced to callers

`DatabaseDetectionService` internally builds a `DatabaseDetectionResult(SupportedDatabase ResolvedProduct, IReadOnlyList<DetectionProbeAttempt> Attempts)`, where each `DetectionProbeAttempt(string ProbeName, bool Succeeded, string? FailureReason)` records one detection probe's outcome — genuinely useful evidence for diagnosing a misdetected database. Its own doc comment states the purpose explicitly: capturing evidence the bare-enum entry points otherwise discard. But every public-facing entry point only returns the bare `SupportedDatabase` enum — the evidence trail is built and then thrown away. When detection picks the wrong product (falls back to SQL-92, or misidentifies a flavor like Aurora/TiDB/Yugabyte), a user has no way to see *why* — which probes ran, which failed, what the failure reason was. Worth exposing via a diagnostic method or logging the attempts at a Debug level when the result doesn't match what was expected, rather than only ever returning the final enum value.

---

## `ConnectionStringNormalizationCache` grows unbounded, keyed on the raw connection string

`internal/ConnectionStringNormalizationCache.cs` is a static `ConcurrentDictionary<string, Dictionary<string,string>>` with no eviction, no bound, and no TTL, keyed on the literal connection string passed to `DatabaseContext`. The cached *value* correctly scrubs credentials before storage (`ShouldIgnoreKey` in `DatabaseContext.Initialization.cs`), but the *key* is the raw string, which for most providers embeds the password directly. Harmless for the common case (a fixed, small number of connection strings per process), but an application that constructs many distinct connection strings at runtime — per-tenant credentials, credential rotation, dynamically generated passwords — will grow this cache without bound for the process lifetime, retaining old/rotated-out credentials in memory indefinitely. Needs either an LRU bound (matching the pattern `BoundedCache<TKey,TValue>` already provides elsewhere) or a documented "don't do this" caveat if unbounded growth is judged an acceptable tradeoff for the common case. See `docs/architecture.md`'s "Connection-string handling in the normalization cache" section.

---

## `IDataReaderMapper`/`MapperOptions` and `TypeCoercionOptions.JsonPreference` are unreachable/dead externally

Same class of gap as `DbProviderLoader` below. `IMapperOptions`/`MapperOptions` (`Strict`, `ColumnsOnly`, `NamePolicy`, `EnumMode`) configure `IDataReaderMapper`, but its only implementation, `DataReaderMapper`, is `internal sealed` — no external consumer can ever obtain one. It's also not what `TableGateway`/`PrimaryKeyTableGateway` actually use for row hydration: the real gateway path goes through `GetOrBuildRecordsetPlan`/`MapReaderToObjectWithPlan`, a separate compiled-plan mechanism that never touches `DataReaderMapper` at all. If configurable strict/lenient mapping and column-name-policy control are meant to be a public feature, `IDataReaderMapper` needs a public factory/DI path — or should be removed/merged into the real hydration path if it's superseded by it.

Separately: `TypeCoercionOptions.JsonPreference` (`JsonPassThrough` enum: `PreferDocument`/`PreferText`) is fully dead — declared, defaulted, exercised by its own construction test, but never read anywhere in source, not even internally. `TypeCoercionOptions.TimePolicy` (`TimeMappingPolicy`) *is* read internally (gates `DateTime`→`DateTimeOffset` conversion in `TypeCoercionHelper`) but is unreachable externally since `BaseTableGateway`/`SqlContainer` only ever override `TypeCoercionOptions.Provider`, never `TimePolicy`, and `TypeCoercionHelper` itself is `internal static`.

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
  - Phase 3 (**explicitly deferred / ignore for the current release**, the risky part): expose it via a `DatabaseContext.CreateAsync` factory.
    Requires rewriting the ~400-line private constructor into a shared async core the sync
    constructor also routes through, making the full test suite the regression gate for that
    rewrite. `IConnectionStrategy.HandleDialectDetectionAsync` (an async twin of
    `HandleDialectDetection`) belongs here, not in Phase 2 — it would have no caller until this
    factory exists. Deliberately deferred on its own merits, independent of Phase 2 being done.

### P2

- **SQL Server UUIDv7 index-locality strategy — explicitly deferred / ignore for the current release.** RFC 9562 UUIDv7 places its Unix-millisecond
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
  `PengdowsMetricsObserver` exports matching OpenTelemetry gauges for all three values.
- ~~**Metric cardinality policy for dynamic multi-tenancy.**~~ — closed 2026-08-19 as a
  non-issue after source and listener-level verification. `PengdowsMetricsObserver` does **not**
  tag metrics with a tenant ID, connection string, or configured application name: its `db.name`
  value is `IDatabaseContext.Name`, which is the detected database product (for example `SQLite`
  or `PostgreSql`). The resulting value domain is bounded by `SupportedDatabase`, even when many
  tenant-named contexts are tracked. `Track_TenantNamedContextsOnSameProvider_UsesOneBoundedDatabaseNameTag`
  locks that contract down.
- **Stored-procedure multi-result handling** remains deliberately unsupported: `ITrackedReader`
  rejects `NextResult()` so callers cannot hold an unbounded connection lease across arbitrary,
  caller-driven result traversal. Supporting it would require a new, explicitly bounded API and
  lifecycle contract. ~~SQL Server OUT/INOUT parameters~~ were fixed 2026-08-19: the `EXEC`
  wrapper now emits the required `OUTPUT` marker for `Output` and `InputOutput` parameters.
  `StoredProc_OutputParameter_WorksOnSqlServer` proves the behavior against a real SQL Server;
  `ExecStyle_AppendsOutputForOutputAndInputOutputParameters` locks the generated SQL down.
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
  - Connection-mode semantics: `docs/connection/connection-modes.md` §4 had a concrete factual error, found
    while re-reading it against `TrackedConnection`'s actual behavior — it claimed session settings
    are "not reapplied when a connection is reused from pool," which is backwards for `Standard`/
    `SingleWriter` modes. `TrackedConnection._wasOpened` is a per-*wrapper*-instance flag, not a
    per-*physical*-connection one; ephemeral modes create a fresh wrapper per checkout, so the
    preamble genuinely reapplies every single time, even when the ADO.NET pool hands back an
    already-open physical connection (deliberately — see `docs/sql-server-session-settings.md`'s
    "Performance Trade-off" section for why trusting pooled connection state is a correctness
    risk, not just a consistency one). Only persistent modes (PreventDatabaseUnload's sentinel,
    SingleConnection) apply it exactly
    once. Fixed the doc to state the per-mode rule explicitly instead of one blanket (wrong) claim.
  - Generated/tested capability tables: already comprehensive — `docs/supported-databases.md` has
    a 114-line enum/version-floor/feature-threshold matrix across all 16 databases. Nothing to add.
  - `crud`-naming/positioning problem: still open — this is a product-positioning question
    (`docs/positioning/product-thesis.md` territory), not a documentation-accuracy bug; no doc edit resolves it.
- ~~**TiDB/MySql.Data prepare workaround** lacks a version number or upstream issue
  reference in its source comment~~ — fixed 2026-08-13: `TiDbDialect.cs` now names the
  tested `MySql.Data` version (9.3.0) and the exact mechanism (text-protocol backslash
  escaping corrupting string parameters). No public upstream issue could be found matching
  this exact bug despite a targeted search — it was apparently found empirically via this
  project's own TiDB integration testing, not from a tracked report. The comment now says
  so explicitly and flags that this should be re-verified against newer `MySql.Data`
  releases rather than assumed permanent.

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
