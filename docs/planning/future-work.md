# Future Work

This document tracks features that have been designed or partially specified but are not yet
implemented. Items here are not roadmap commitments — they are recorded so the design thinking
is not lost and can be picked up when the need arises.

---

## Removed: `EphemeralSecureString` (2026-08-28)

`pengdows.crud/EphemeralSecureString.cs`/`IEphemeralSecureString` was a real, well-built mechanism intended to keep credentials (connection-string passwords) securely in memory: AES-encrypted on construction with a per-instance key/IV, decrypted only inside `Reveal()`/`WithRevealed()`/`WithRevealedAsync()`, auto-zeroed the cached plaintext ~750ms after first reveal, and zeroed key/IV/ciphertext on `Dispose()`. Confirmed with the maintainer this was meant to be the answer to keeping passwords out of plain memory, but it was never actually wired into `DatabaseContextConfiguration`, connection-string handling, or `DbProviderLoader` — zero production call sites, only its own unit test.

Kept deliberately for a while so the design work wasn't lost, per this document's own stated purpose — then removed by explicit maintainer decision once that purpose was served (this entry, plus the class's git history, is the record). Removal was a public-contract change: deleted `pengdows.crud/EphemeralSecureString.cs`, `pengdows.crud.abstractions/IEphemeralSecureString.cs`, and `pengdows.crud.Tests/EphemeralSecureStringTests.cs`, then regenerated the `interface-api-check` baseline (451 → 447 signatures, the interface plus its 3 methods). Full solution build clean; full unit suite (6515 tests) shows the same 4 pre-existing failures, none new.

---

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

## Percentile availability flags — completed 2026-08-28

`DatabaseMetrics` and `DatabaseRoleMetrics` now expose explicit availability flags for
command and transaction percentiles. P95/P99 remain opt-in for performance compatibility,
but consumers can distinguish disabled or empty data from a real zero-valued measurement.

---

## Read-only violation marker — completed 2026-08-28

The existing concrete exception behavior is preserved through derived exceptions, while
`ReadOnlyContextException`, `ReadOnlyAccessException`, and provider-translated
`ReadOnlyViolationException` implement the public `IReadOnlyViolation` marker. Callers can now
handle all library-generated read-only failures without inspecting messages.

---

## `ModeContentionException` sits outside the `DatabaseException` hierarchy — worth a deliberate look

Every other database/framework error in this library surfaces as a `DatabaseException` subclass (see `CLAUDE.md`'s Exception Hierarchy). `ModeContentionException` (a `SingleWriter`/`SingleConnection` mode-lock timeout) is the one exception: it extends `TimeoutException` directly. This may be entirely intentional — timeout semantics arguably matter more than database semantics for this one — but it wasn't found stated as a deliberate design decision anywhere, only as an implementation fact. Worth either documenting the "why" explicitly (so it reads as a decision, not an oversight) or reconsidering whether it should also implement a common marker so a `catch (DatabaseException)` block doesn't silently miss it.

---

## `AttributionStats` wiring — completed 2026-08-28

`DatabaseMetrics` now exposes cumulative request, pool-wait, pool-timeout, mode-wait, and
mode-timeout attribution. Pool and mode counts use their authoritative collectors rather than
duplicating those counters in `AttributionStats`.

---

## `DatabaseDetectionResult`'s evidence trail is never surfaced to callers

`DatabaseDetectionService` internally builds a `DatabaseDetectionResult(SupportedDatabase ResolvedProduct, IReadOnlyList<DetectionProbeAttempt> Attempts)`, where each `DetectionProbeAttempt(string ProbeName, bool Succeeded, string? FailureReason)` records one detection probe's outcome — genuinely useful evidence for diagnosing a misdetected database. Its own doc comment states the purpose explicitly: capturing evidence the bare-enum entry points otherwise discard. But every public-facing entry point only returns the bare `SupportedDatabase` enum — the evidence trail is built and then thrown away. When detection picks the wrong product (falls back to SQL-92, or misidentifies a flavor like Aurora/TiDB/Yugabyte), a user has no way to see *why* — which probes ran, which failed, what the failure reason was.

**Attempted and reverted 2026-08-29:** a public `DatabaseDetection` wrapper class was added exposing this evidence directly (`DatabaseDetection.DetectFromConnectionWithDetail`/`Async`), with the result types made `public` in the `pengdows.crud` namespace. Code review caught two problems: (1) it put public API surface directly in `pengdows.crud` instead of `pengdows.crud.abstractions`, bypassing `interface-api-check`'s baseline validation entirely since that tool only checks the abstractions assembly; (2) `DetectionProbeAttempt.FailureReason` stored raw provider `ex.Message` text, which can contain server names, database names, or SQL fragments — unsafe to hand to arbitrary external callers without sanitization. Reverted back to fully `internal`. If this is revisited, it needs to live in `pengdows.crud.abstractions` as a proper public contract, and `FailureReason` needs to become a coarse category rather than a raw provider message.

---

## `ConnectionStringNormalizationCache` grows unbounded, keyed on the raw connection string — fixed 2026-08-29 (CORE-012)

`internal/ConnectionStringNormalizationCache.cs` was a static `ConcurrentDictionary<string, Dictionary<string,string>>` with no eviction, no bound, and no TTL, keyed on the literal connection string passed to `DatabaseContext`. The cached *value* correctly scrubbed credentials before storage (`ShouldIgnoreKey` in `DatabaseContext.Initialization.cs`), but the *key* was the raw string, which for most providers embeds the password directly.

**Fix:** the cache now keys on a SHA-256 digest of the raw connection string and is bounded via `BoundedCache<TKey,TValue>` (256 entries, LRU eviction) — see the 2.1 tracker's CORE-012 row (section 1 below) for the exact tests added.

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

---

## pengdows.crud 2.1 — Code, Proof, and Documentation Tracker

Last consolidated: 2026-08-29
Core scope: pengdows.crud and pengdows.crud.abstractions
Reference branch: 2.1

### Purpose

This is the durable source of truth for shortcomings, missing proof, documentation debt, and product claims discovered during review of pengdows.crud 2.1.

Only pengdows.crud and pengdows.crud.abstractions are the library. Other repositories and projects are tests, evidence, integrations, or adoption paths that lead developers to the core library. They may prove the architecture, but they are not part of the core product surface.

### Status legend

Open — confirmed work remains.

Verify — evidence suggests an issue, but the intended contract must be confirmed.

Planned — accepted and sequenced.

In progress — actively being changed.

Verified — implemented and supported by inspected evidence.

Done — implementation, proof, and documentation are complete.

Blocked — cannot proceed until the stated dependency is resolved.

### 1. Core code defects and release risks

| ID | Priority | Status | Area | Finding | Completion criteria |
|---|---|---|---|---|---|
| CORE-001 | P0 | **Done (2026-08-29)** | Tenant configuration | `TenantConnectionResolver.CloneConfiguration()` omits `ReaderPlanCacheSize`, `SessionInitializationFailureMode`, `MaxQueuedWrites`, `MaxQueuedReads`, and `EnforceUniqueConnectionString`. Tenant configurations therefore silently revert those values when cloned. | **Resolved:** `CloneConfiguration` now assigns all 5 previously-omitted properties. Added a reflection-based completeness test (`TenantConnectionResolverTests.Register_And_GetConfiguration_PreservesEveryConfigurationProperty`, iterates every `IDatabaseContextConfiguration` property so a future interface addition without a matching clone assignment fails immediately) plus 4 targeted tests for `ReaderPlanCacheSize`, `MaxQueuedReads`/`MaxQueuedWrites`, `SessionInitializationFailureMode.FailClosed`, and `EnforceUniqueConnectionString`. Confirmed genuinely red before the fix (`ReaderPlanCacheSize` reflection assertion failed as expected). Full suite green (7346 tests). |
| CORE-002 | P0 | **Done (2026-08-29)** | Pool configuration | `DatabaseContext.Initialization` injects `Min Pool Size=1` for every enabled pool and 2 for PreventDatabaseUnload, while existing tests assert no default minimum for Standard, PreventDatabaseUnload/KeepAlive, and SingleWriter. This is an implementation/test contract collision and likely a suite failure. | **Resolved:** caller-supplied minimums are now preserved as-is for Standard/SingleWriter/KeepAlive (no implicit injection); only `PreventDatabaseUnload`/`KeepAlive` forces `Min Pool Size=2`. Also found and fixed an independent bug in the same area: the public `ConnectionString` property was snapshotted *before* pool-governor mutation ran, so it silently diverged from the real connection string used to open connections — this is what let the original contradictory tests pass for the wrong reason. See `DatabaseContext.Initialization.cs` (`InitializePoolGovernors`, `RefreshRedactedConnectionStrings` call), `ConnectionPoolingConfiguration.EnsureMinimumPoolSize`, and `DatabaseContextConstructorTests`/`MinPoolSizeBehaviorTests`/`PreventDatabaseUnloadTests`. Full suite green (7341 tests). |
| CORE-003 | P1 | **Done (2026-08-29)** | Metrics | Initial PDU sentinel creation uses `AcquireSlot`, which increments read/write request attribution. A new context can report application requests before application work occurs. | **Resolved, and found broader than scoped:** not just sentinel creation — the initial dialect-detection connection and `TestConnect` validation connection also went through the attributing path. Added `AcquireInfrastructureSlot` (identical to `AcquireSlot` but does not record into `_attributionStats`) and routed every infrastructure connection (dialect detection, TestConnect, sentinel creation/repair) through it. Confirmed via live instrumentation that `Metrics.ReadRequests`/`WriteRequests` were 2/2 after construction with zero application queries before the fix, 0/0 after. See `DatabaseContext.ConnectionLifecycle.cs` and `PreventDatabaseUnloadTests.Construction_DoesNotAttributeSentinelSlotAcquisitionAsApplicationRequests`. Note: `Metrics` returns an all-zero snapshot whenever `EnableMetrics` is off — tests must enable it explicitly or they pass vacuously. |
| CORE-004 | P1 | **Done (2026-08-29)** | API architecture | Public detection types (`DatabaseDetection`, `DatabaseDetectionResult`, `DetectionProbeAttempt`) live in `pengdows.crud`, apparently bypassing the project's interface-first abstraction boundary and API baseline. | **Resolved by reverting, not relocating** (deliberate decision, not the default recommendation): removed the public `DatabaseDetection` wrapper entirely; `DatabaseDetectionResult`/`DetectionProbeAttempt` are `internal` again in `pengdows.crud.@internal`. `interface-api-check` baseline unaffected either way (448 signatures) since it only ever validated `pengdows.crud.abstractions.dll` — confirms the gap this item described. See the "`DatabaseDetectionResult`'s evidence trail" entry above in this file for full history. If revisited, DOC-025/CAP-016 below still describe the value of exposing this properly through abstractions. |
| CORE-005 | P1 | **Done (2026-08-29)** | Security/diagnostics | Detection diagnostics expose raw provider exception messages through public `FailureReason`; these can include server, database, SQL, or connection details. | **Resolved as a consequence of CORE-004's revert**, not by sanitizing: `FailureReason` still carries raw `ex.Message`, but the containing types are `internal` again, so this is no longer a public-boundary exposure. If detection diagnostics are ever re-exposed publicly, this sanitization work still needs to happen — see the note left in this file's "`DatabaseDetectionResult`'s evidence trail" entry. |
| CORE-006 | P1 | **Done (2026-08-29)** | Runtime provider loading | `DbProviderLoader` registers keyed DI using the configuration-section provider key, while `TenantContextRegistry` resolves using tenant `ProviderName`; that name also denotes the invariant registered in `DbProviderFactories`. One name may be carrying two different identities. | **Confirmed and resolved via the "document it precisely" option** (the simpler of the two the completion criteria offered) rather than introducing distinct `ProviderKey`/`ProviderInvariantName` concepts. Confirmed the identity split is real and already relied upon: an existing test (`FrameworkInfrastructureEdgeCaseTests.DbProviderLoader_RegistersProviderUsingAssemblyResolvedFactory`) explicitly resolves a factory registered under config section key `"loader"` while its `ProviderName` field is the unrelated `"Coverage94.LoaderProvider"` — proving `providerKey != ProviderName` is an intentional, already-supported pattern, which rules out "require equality" as a fix (it would break that existing behavior). The actual, confirmed requirement: a tenant's `ProviderName` must equal the `DatabaseProviders` configuration section *key*, not the ADO.NET invariant name its own name suggests. Documented this precisely in three places: `IDatabaseContextConfiguration.ProviderName`'s XML doc (`<remarks>` explaining the DI-lookup-path requirement), a code comment at `DbProviderLoader`'s keyed-registration call site (the other side of the same contract), and — the actionable fix — `TenantContextRegistry.CreateDatabaseContext`'s exception message, which previously just said `"No factory registered for 'X'."` and now explains the section-key requirement directly, with an example. Test: `FrameworkInfrastructureEdgeCaseTests.TenantContextRegistry_WhenTenantProviderNameIsInvariantNameNotSectionKey_ThrowsActionableError` reproduces the exact confusing scenario (tenant `ProviderName` set to the invariant name) and asserts the new message names the section-key requirement; confirmed genuinely red first against the old bare message. Updated one pre-existing test (`DialectAndTenantEdgeCaseTests.TenantContextRegistry_GetContext_UnregisteredProvider_ThrowsInvalidOperationException`) whose assertion depended on the exact old wording. Full suite green (7388 tests; one `SingleWriterFairnessTortureTests` flake during the run was confirmed unrelated — passed cleanly in isolation and on the immediate full-suite rerun, matching its documented timing-sensitive nature). |
| CORE-007 | P3 | **Done (2026-08-29)** | Disposal contract | Source remarks appeared to say contexts obtained before registry disposal remain usable until independently disposed, while registry disposal appears to dispose created contexts. | **Resolved: the code's ownership semantics were already correct and intentional — the doc was wrong, not the implementation.** `DisposeManaged`/`DisposeManagedAsync` dispose every already-created tenant context on registry shutdown, exactly matching the "registry owns and disposes what it creates" contract `Invalidate`/`InvalidateAll` already use for a single tenant (and matching the existing `Dispose_RacingWithInFlightCreate_ThrowsInsteadOfLeakingOrphanedContext` test's own assumption, which predates this fix and already treats registry-disposed contexts as expected to end up disposed). No test anywhere in the suite relied on the documented "survives registry disposal" claim — searched for it and found none — so the chosen fix was to correct the class's own XML `<remarks>` to state the real contract (registry owns every context it creates; a context handed back by `GetContext` is not safe to use after the registry itself is disposed) rather than change working, already-tested disposal behavior to match a stale doc. Added `TenantTests.Dispose_DisposesAllContextsItCreated_RegistryOwnsCreatedContexts` — a plain (non-racing) case that gets one context, disposes the registry, and asserts the context is now disposed — to make the real contract explicit and regression-proof going forward, since the only prior coverage of this behavior was buried inside race tests. Full suite green (7389 tests, both net8.0/net10.0). |
| CORE-008 | P3 | **Done (2026-08-29)** | Naming/comments | Internal `KeepAliveConnectionStrategy` naming and stale MinPool comments/tests obscure the public `PreventDatabaseUnload` terminology and current behavior. | **Resolved:** renamed to `PreventDatabaseUnloadConnectionStrategy` (fully `internal`, zero public API impact) across the implementation file, its test-extension class, 6 test files, `ConnectionStrategyFactory`, `IConnectionStrategy`, `docs/architecture.md`, and `llms-full.txt`. Stale MinPool comments/tests were also corrected as part of CORE-002. |
| CORE-009 | P0 | **Done (2026-08-29)** | Tenant identity | `TenantConnectionResolver` uses `StringComparer.OrdinalIgnoreCase`, while `TenantContextRegistry` uses the default case-sensitive comparer. `foo` and `FOO` can resolve the same tenant configuration into two independently governed contexts, consume two cardinality entries, and evade the one-context-per-tenant invariant. | **Resolved:** `TenantContextRegistry._contexts` now constructed with `StringComparer.OrdinalIgnoreCase`, matching the resolver. `Invalidate`/`GetContext` automatically inherit the case-insensitive comparison since `ConcurrentDictionary` bakes the comparer in — no other call-site changes needed. Confirmed genuinely red first: `GetContext_WithDifferentCasing_ReturnsSameContextAndRaisesContextCreatedOnce` and `GetContext_WithMixedCaseConcurrentCallers_ReturnsOneContextAndOneCreationEvent` (`TenantTests.cs`) both failed before the fix (4 distinct contexts/creation events for 4 casings of one tenant), pass after. Full suite green (7348 tests). |
| CORE-010 | P0 | **Partially Done (2026-08-29)** | Tenant lifecycle concurrency | `Invalidate` can remove a `Lazy<IDatabaseContext>` before it is evaluated. A racing `GetContext` can then evaluate that removed `Lazy`, return an untracked context, and leak it past registry disposal. A similar race can return a context while invalidation/disposal is disposing it. | **Resolved: the orphan-leak sub-case (invalidate-during-create, dispose-during-create).** `GetContext` now loops: after resolving a newly-created `Lazy`, it re-verifies the dictionary still holds that exact `Lazy` instance for the tenant key; if a racing `Invalidate`/registry `Dispose()` evicted it first (the pre-existing bug — `Invalidate`'s dispose-if-created check is a no-op against a not-yet-evaluated `Lazy`), the orphaned context is disposed and the call retries rather than being handed back untracked and undisposed. `ThrowIfDisposed()` moved inside the loop so a retry forced by registry disposal fails closed with `ObjectDisposedException` instead of looping forever. Deterministic tests (`TenantTests.cs`: `Invalidate_RacingWithInFlightCreate_DoesNotLeakOrphanedContext`, `Dispose_RacingWithInFlightCreate_ThrowsInsteadOfLeakingOrphanedContext`) use a `BlockingContextFactory` test double to reproduce both races without timing flakiness; confirmed genuinely red first. **Not resolved: "lookup-during-invalidate"** — a caller that hits the fast `TryGetValue` path and gets a reference to an already-created context has no protection against a concurrent `Invalidate`/`Dispose()` disposing that same context immediately after return. Closing that fully requires either reference-counted/leased context handles (an API shape change) or resolving CORE-007's disposal-ownership contract first — deliberately left open rather than attempting a larger redesign in this pass. Full suite green (7350 tests). |
| CORE-011 | P1 | **Partially Done (2026-08-29)** | Tenant lifecycle events | If a `ContextCreated` subscriber throws, context creation faults after the context exists; the faulted `Lazy` is evicted but the created context is not disposed. Event behavior also differs across invalidation and shutdown, and events do not carry the tenant ID. | **Resolved: subscriber-failure isolation and disposal.** `CreateDatabaseContext` now invokes each `ContextCreated` subscriber individually (via `GetInvocationList()`) inside its own try/catch instead of a single multicast `Invoke()` call, so one faulting subscriber no longer prevents later, well-behaved subscribers from being notified. If any subscriber faulted, the just-created (and now definitely unpublished/uncached) context is disposed and the first subscriber's exception is rethrown via `ExceptionDispatchInfo` — the `Lazy` wrapping this method still gets evicted by the caller's existing fault-handling in `ResolveLazy`, so this closes the leak without changing that contract. Tests: `TenantTests.GetContext_WhenContextCreatedSubscriberThrows_DisposesTheCreatedContext` and `GetContext_WhenOneContextCreatedSubscriberThrows_StillNotifiesOtherSubscribers`; confirmed genuinely red first. **Not resolved (deliberately deferred, not attempted this session):** adding the tenant ID to the event signature — `ContextCreated`/`ContextRemoved` are documented public API (`Action<IDatabaseContext>`, see CLAUDE.md), and changing that signature to include a tenant ID is a breaking API change requiring an explicit decision, not a bug fix; needs its own sign-off before attempting. Full suite green (7382 tests). |
| CORE-012 | P0 | **Done (2026-08-29)** | Credential lifetime/memory | `ConnectionStringNormalizationCache` is a static, unbounded dictionary keyed by the complete raw connection string. Although normalized values omit secret fields, keys retain usernames/passwords/tokens for process lifetime. Tenant-specific and rotating connection strings also cause unbounded growth. | **Resolved:** the cache now keys on a SHA-256 digest of the raw connection string (never the raw string itself) and is bounded via the existing `BoundedCache<TKey,TValue>` LRU pattern (256 entries — distinct connection strings per process are typically small in number; this bounds worst-case pathological growth). Added `BoundedCache<TKey,TValue>.Count`. Tests: `ConnectionStringNormalizationCacheTests.TryAdd_DoesNotRetainRawConnectionStringOrCredentialsAsCacheKey` (reflects into the backing store and asserts no key equals or contains the raw connection string/password substring) and `Cache_IsBounded_EvictsOldEntriesBeyondCapacity` (inserts capacity+50 distinct entries, asserts `Count <= capacity` and the earliest entry was evicted); confirmed genuinely red first (`NullReferenceException`/`Assert.NotNull` failures against the old flat-`ConcurrentDictionary` shape). Digest collisions between two connection strings differing only in credential value are harmless by design — the cached value never depended on which credential was used, since credential keys were already excluded from it. Full suite green (7353 tests). |
| CORE-013 | P1 | **Partially Done (2026-08-29)** | Reader mapping correctness | `BaseTableGateway<TEntity>` does isolate compiled reader plans by entity type, so a plan cannot cross from one entity type into another. Within the same entity, however, plans are keyed by a 32-bit `HashCode` result cast to `long`, with no schema equality verification. `DataReaderMapper` likewise keys plans by a derived hash rather than the immutable schema. Two projections/shapes for the same entity can collide and silently hydrate incorrectly or invoke the wrong typed getter. | **Resolved for `BaseTableGateway<TEntity>`** (the real, externally-reachable hot path every gateway operation uses — `DataReaderMapper` is a separate, already-confirmed-dead-externally path, see below): added a private `RecordsetShape` struct (field names + types with structural `Equals`/`GetHashCode`, `OrdinalIgnoreCase` names) and changed `_readerPlans` from `BoundedCache<long, HybridRecordsetPlan>` to `BoundedCache<RecordsetShape, HybridRecordsetPlan>` — this lets `ConcurrentDictionary`'s own correct collision handling (hash to find the bucket, `Equals` to confirm the entry) do what it already guarantees, rather than trusting a bare hash value as unique. A transient lookup shape wraps the method's already-rented, pooled arrays (never stored); a cache miss persists a fresh, exactly-sized copy before inserting. Test: `TableGatewayRecordsetPlanTests.MapReaderToObject_TwoDistinctShapesWithCollidingHash_CacheTwoSeparatePlans` performs a bounded, in-process brute-force search (reproducing the exact hashing algorithm) for two genuinely different 3-column shapes that collide under *this run's* `HashCode` seed — deterministic in the sense the review asked for (a systematic search for a real, reproducible-within-the-run collision, not hoping two arbitrary hard-coded values happen to collide, which wouldn't survive `HashCode`'s per-process seed randomization anyway) — then asserts the plan cache holds 2 entries, not 1. Confirmed genuinely red first via a temporary `git stash` of just the fix files (1 cached entry instead of 2), not a fresh red-then-implement cycle, since the investigation needed to precede the test design. **Deliberately not fixed: `DataReaderMapper`'s parallel `PlanCacheKey(Type, schemaHash: long, ...)`.** Its `BuildSchemaHash` is a genuine 64-bit rolling hash (not a 32-bit `HashCode.ToHashCode()` widened to `long`), making a practical collision astronomically less likely than the `BaseTableGateway` case fixed here — combined with `DataReaderMapper` being confirmed dead code externally (see this file's own "`IDataReaderMapper`/`MapperOptions`... are unreachable/dead externally" entry above: `internal sealed`, no public factory/DI path, and not what `TableGateway`/`PrimaryKeyTableGateway` actually use for hydration), the risk/effort tradeoff did not justify the same restructuring in this pass. Full suite green (7383 tests). |
| CORE-014 | P1 | **Done (2026-08-29)** | Runtime provider loading | Assembly-based factory resolution accepts only a public static `Instance` property. ADO.NET factories commonly expose `Instance` as a static field; valid providers can therefore load successfully as assemblies but fail factory discovery. | **Resolved:** `DbProviderLoader.LoadProviderFactory` now checks for a public static `Instance` **property** first (preserving prior precedence for a type that happens to expose both), then falls back to a public static `Instance` **field** — matching real-world ADO.NET convention split: `System.Data.SqlClient.SqlClientFactory.Instance` and MySql.Data's `MySqlClientFactory.Instance` are both fields, while Npgsql's `NpgsqlFactory.Instance` is a property. The "neither exists" error message now says "property or field" instead of only "property". Test: `FrameworkInfrastructureEdgeCaseTests.DbProviderLoader_RegistersProviderUsingFieldBasedInstanceConvention` uses a new field-only `FieldInstanceLoaderFactory` test double (public static readonly field, deliberately no property) loaded through the real `AssemblyName`/`FactoryType` config path; confirmed genuinely red first (old code threw "does not have a static Instance property"). No construction-fallback beyond property/field was added — the completion criteria's "only any explicitly accepted construction fallback" is satisfied by explicitly not adding one (e.g. a public parameterless constructor), since neither of the two conventions actually used by shipping ADO.NET providers needs it and adding an unrequested fallback would be speculative. Full suite green (7390 tests, both net8.0/net10.0; one `ReusableAsyncLockerTests.LockAsync_Contended_WaitsUntilReleased` timing flake during the batched net8.0 run was confirmed unrelated — passed cleanly both in isolation and on an immediate full-suite rerun). |
| CORE-015 | P1 | **Done (2026-08-29)** | Runtime provider loading security | `ResolveAssemblyPath` performs lexical containment only. A symlink located under the application base can point outside it and still pass the check, contradicting the documented containment guarantee. | **Resolved via the "containment is a security boundary" option:** after the existing lexical (`..`-traversal) check passes and the candidate path exists, `ResolveAssemblyPath` now also resolves the file's real target with `File.ResolveLinkTarget(candidatePath, returnFinalTarget: true)` and re-validates that resolved target against the same base-directory prefix check, throwing the same "must stay within" exception if it escapes. `returnFinalTarget: true` follows a chain of symlinks-to-symlinks to the ultimate target, not just one hop. Test: `SecurityRegressionTests.LoadAndRegisterProviders_RejectsSymlinkUnderBaseDirectoryPointingOutside` creates a real file in a temp directory outside the app base directory, a real symlink inside the base directory pointing at it, and configures `AssemblyPath` to name the symlink; confirmed genuinely red first (old code proceeded past containment and got as far as `Assembly.LoadFrom` failing on the placeholder file's bad image, not the expected "must stay within" rejection). CI runs `ubuntu-latest` only (checked `.github/workflows/*.yml`), so the test creates a real `File.CreateSymbolicLink` unconditionally rather than adding a Windows-specific skip path — this repo's policy is no skipped tests. **Deliberately out of scope:** symlinked intermediate *directory* components (not just the leaf file) — planting a symlinked directory inside the app's own base directory requires an attacker who already has arbitrary write access to the deployed app, which is a strictly larger compromise than the "one configured relative path names a planted symlink" threat this containment check exists for; walking every path segment for its own symlink status was judged disproportionate complexity for that materially smaller residual risk. Full suite green (7391 tests, both net8.0/net10.0). |
| CORE-016 | P1 | **Verified, deliberately deferred (2026-08-29)** | Generated IDs | `ReaderInsertedId` and compound-statement paths fall back to `PopulateGeneratedIdAsync` when provider-specific extraction/multiple-result navigation fails. That fallback obtains a new connection and executes a session-scoped last-ID query, reintroducing the exact two-lease race those plans were designed to prevent. | **Confirmed real, not fixed this pass — both viable fixes carry more risk than warranted without a real provider to verify against.** Traced every call site in `TableGateway.Core.cs`: `PopulateGeneratedIdAsync` does call `ctx.CreateSqlContainer(lastIdQuery)` fresh, with no guarantee of reusing the INSERT's physical connection, exactly as described. However, every one of its call sites is explicitly commented "Fallback: ... for test/fake scenarios" — the `CompoundStatement` plan's own comment states the *primary* path ("append the ID query to the INSERT, execute as one batch on one connection") *is* the two-lease fix already; the fallback exists only because `fakeDbDataReader.NextResult()` always returns `false`, so fakeDb-based unit tests need a secondary path to get a value at all. This means: (1) **"fail closed"** (the completion criteria's simpler suggested option) would throw on every real-provider construction test in the current suite that relies on this fallback for `fakeDb` — a wide, real regression, not a no-op; (2) **"keep insert and fallback retrieval on the same physical connection"** requires restructuring connection lifetime so `PopulateGeneratedIdAsync`'s query executes through the *same* already-open `ITrackedConnection`/reader scope rather than asking the context for a fresh one — a real architectural change I'm not confident implementing correctly without a live MySQL/Oracle/MySqlConnector provider to verify the fix actually preserves session-scoped ID correctness end-to-end (fakeDb can't exercise the hazard at all, since it has no real session state to get wrong). Given the practical risk is that a *real* provider would need to fail its primary extraction mechanism (OK-packet property absent, or `NextResult()` misbehaving) for this to trigger in production — genuinely rare, but not impossible — this is real, verified technical debt worth fixing with the a real-provider testbed available, not guessed at with fakeDb alone. **Not audited:** whether any active dialect still reaches the base `SessionScopedFunction`/generic last-insert-id path at all (deferred alongside the fix). |
| CORE-017 | P2 | **Done (2026-08-29)** | Tenant cardinality configuration | `TenantContextRegistry` supports `maxTenantCount` only as a concrete constructor argument. `MultiTenantOptions` and `AddMultiTenancy` do not expose/bind it, so the standard configuration path cannot set the advertised production safety cap. | **Resolved via the "add a configuration-level cap" option.** Added `MultiTenantOptions.MaxTenantCount` (`int?`, binds from the `MultiTenant` config section like every other option on that POCO). `AddMultiTenancy` no longer registers `TenantContextRegistry` via plain `services.AddSingleton<ITenantContextRegistry, TenantContextRegistry>()` — that relies on implementation-type constructor DI, which has no way to source a value for an unregistered `int?` parameter and always supplied the constructor's own `null` default regardless of configuration. It now registers via an explicit factory delegate that closes over the already-bound `options.MaxTenantCount` and passes it through explicitly. Test: `MultitenantIntegrationTests.AddMultiTenancy_HonorsConfiguredMaxTenantCount` configures `MultiTenant:MaxTenantCount=1` with two tenants through the real `services.AddMultiTenancy(configuration)` entry point, gets the first tenant successfully, and asserts the second throws `InvalidOperationException` naming the cap; confirmed genuinely red first (old code let both tenants through — the config value never reached the registry). Full suite green (7392 tests, both net8.0/net10.0). Also documented as part of the CORE-006 doc pass: see `docs/connection/dynamic-provider-loading.md` and the `connections.md` skill files (kept in sync via `tools/check-skill-drift.sh`) for the config-driven provider-loading feature this same area depends on, which had no prior documentation anywhere in the repo. |
| CORE-018 | P1 | **Done (2026-08-29)** | Context-derived generation | `BaseTableGateway.CheckParameterLimit` validates against the gateway constructor's `_context.MaxParameterLimit`, not the operation context represented by the supplied `ISqlContainer`. A singleton gateway can accept an oversized query or reject a valid one based on another tenant/provider. | **Resolved by reusing an existing internal pattern, not new API surface.** `CheckParameterLimit` now checks `sc is ISqlDialectProvider dialectProvider ? dialectProvider.Dialect.MaxParameterLimit : _context.MaxParameterLimit` — `SqlContainer` already implements the internal `ISqlDialectProvider` to expose its own dialect (`BuildWhereInternal` already used this exact cast for the same purpose one call site away), so this needed no new public or internal API. Tests: `SharedGatewayParameterLimitTests.cs` — one gateway constructed against SQL Server (`MaxParameterLimit=2100`) called with a MySQL (`65535`) operation context and 3000 ids must succeed (previously threw, using SQL Server's limit); the reverse (MySQL-constructed gateway, SQL Server operation context, 3000 ids) must still correctly throw. Confirmed genuinely red first (first case wrongly threw, second wrongly didn't). **Audit of remaining `_context` reads:** grepped every `_context.<member>` access across `BaseTableGateway*.cs`/`TableGateway*.cs`/`PrimaryKeyTableGateway*.cs` — `CheckParameterLimit` was the only instance of this bug pattern in the gateway classes. The same grep also surfaces many `_context.X` reads inside `SqlContainer`/`TransactionContext`, but those are not instances of the bug: each `SqlContainer`/`TransactionContext` is a per-operation, non-singleton instance whose own `_context` field *is* already the correct operation context by construction — the bug is specific to the gateway types being long-lived singletons that receive a *separate* per-call context parameter. Full suite green (7385 tests). |
| CORE-019 | P2 | **Done (2026-08-29)** | Gateway configuration scope | `ReaderPlanCacheSize` is captured from the gateway constructor context. Supplying another tenant context to an operation does not apply that tenant's configured cache size. Table metadata is also captured from the constructor context, which may be intentional if mappings are application-global. | **Resolved via the "define scope, document the invariant" option — confirmed both are gateway-global by design, not a gap.** `_tableInfo` comes from `TypeMapRegistry.GetTableInfo<TEntity>()`, itself derived purely from `TEntity`'s own `[Table]`/`[Column]`/`[Id]`/`[PrimaryKey]` attributes — reflection-derived, static per .NET type, and identical regardless of which context's `TypeMapRegistry` computed it; there is no per-tenant variation to lose. `_readerPlans` is keyed by `RecordsetShape` (column names+types of a query's actual result set) — a property of the entity's result shape, not of which tenant's connection produced the reader; `MapReaderToObject` makes this concrete since it takes only a reader, with no `IDatabaseContext` parameter at all, so it has no way to be tenant-aware even in principle. Sharing one cache per gateway across every tenant using the same entity is therefore correct and avoids pointless per-tenant recompilation, not a missed per-tenant setting. Documented this explicitly: `IDatabaseContextConfiguration.ReaderPlanCacheSize`'s XML doc now has a `<remarks>` block spelling out the "read once at construction, gateway-lifetime capacity knob, not tenant-context-specific" contract and what to do instead (construct separate gateways) if a caller genuinely needs different tenants to get different cache sizes; a matching code comment sits at the `_readerPlans`/`_tableInfo` construction site in `BaseTableGateway.Core.cs`. Test: `ReaderPlanCacheSizeTests.TableGateway_ReaderPlanCacheSize_StaysFixedFromConstructorContext_RegardlessOfLaterReaderSource` constructs a gateway against one context (`ReaderPlanCacheSize=5`), maps a row via a reader with no ties to a second context whose `ReaderPlanCacheSize=999`, and asserts the gateway's cache capacity is still 5 (while confirming the cache was actually used, not a vacuous check) — passed immediately (no red-green cycle: this pins down already-correct, intentional behavior, the same "verify then document" shape as CORE-007, not a bug fix). Full suite green (7393 tests, both net8.0/net10.0). |
| CORE-020 | P0 | **Done (2026-08-29)** | Reader ownership | `TrackedReader.Close()` delegates only to the underlying reader's `Close()`. It does not dispose the wrapper, command, connection, governor slot, connection/context/transaction locks, or active-reader registration. A consumer using the standard `IDataReader.Close()` contract can exhaust the governed pool. | **Resolved:** `Close()` now simply calls `Dispose()` (inherited idempotency from `SafeAsyncDisposableBase` — safe to call `Close()` multiple times or followed by `Dispose()`), giving it the exact same complete release: reader disposal, command disposal, connection disposal (releasing the governor slot) when owned, all three lock layers, and the lifetime-listener notification. Tests: `TrackedReaderTests.Close_PerformsFullOwnershipRelease_LikeDispose` (command/connection/locker all released) and `Close_IsIdempotent_AndDoesNotDoubleDisposeWhenFollowedByDispose`; confirmed genuinely red first (command was not disposed). Full suite green (7355 tests), all 55 TrackedReader-related tests pass with no regressions. |
| CORE-021 | P0 | **Done (2026-08-29)** | Reader failure cleanup | `TrackedReader.DisposeManaged` and `DisposeManagedAsync` perform cleanup sequentially without an internal continue-on-failure structure. An unexpected reader-dispose or command-cleanup exception exits before closing the connection and releasing permits/locks. Existing tests assert propagation but do not assert that remaining resources are released. | **Resolved:** both methods now wrap each ownership phase (reader, command, connection, all three lock layers, lifetime-listener notification) in its own try/catch, preserve the first exception via `first ??= ex`, attempt every remaining phase regardless, and rethrow the first exception (via `ExceptionDispatchInfo.Capture(first).Throw()`) only after everything has been attempted — matching `SafeAsyncDisposableBase`'s own stated principle. Tests in `TrackedReaderTest.cs` force a throw at each phase (`Dispose_ReaderDisposeThrows_...`, `Dispose_CommandDisposeThrows_...`, `Dispose_ConnectionDisposeThrows_...`, `Dispose_ConnectionLockerDisposeThrows_...`, plus an async variant) and assert every other resource still released; confirmed genuinely red first (5 failures matching the exact skip-on-throw bug). **Testing pitfall worth remembering:** the first draft of the throwing test doubles (`ThrowingOnDisposeCommand`/`ThrowingOnDisposeReader`) threw unconditionally in `Dispose(bool disposing)`, including when `disposing == false` — i.e. also from the GC finalizer thread on an unrelated later test run, which crashed the entire test host process (an unhandled exception on the finalizer thread terminates a .NET process). Fixed by only throwing when `disposing == true`. Full suite green (7360 tests). |
| CORE-022 | P0 | **Verified — false positive (2026-08-29)** | Metrics integrity | `TrackedConnection.HandleMetricsStateChange` calls `_metricsCollector.ConnectionOpened()` twice for one `ConnectionState.Open` event. Each call also propagates to the parent collector. Current/opened/peak counts are doubled while close is recorded once, leaving telemetry permanently above zero. | **Does not reproduce.** `HandleMetricsStateChange` has exactly one call site for `ConnectionOpened()`/`ConnectionClosed()` each; git history (`git log -S "ConnectionOpened()"`) shows no duplicate call was ever introduced or removed. `MetricsCollector.ConnectionOpened()`'s `_parent?.ConnectionOpened()` call increments a DIFFERENT collector instance (role-scoped vs. aggregate) exactly once each — correct roll-up, not same-counter duplication; the original finding appears to have misread that pattern. Empirically verified live: constructing a `DatabaseContext` (SQL Server, Standard mode, `EnableMetrics=true`) shows `ConnectionsOpened=1/ConnectionsClosed=1` after construction's own dialect-detection connection, then exactly `2/2` (not `3/3`) after one more `ExecuteNonQueryAsync` — a clean delta of 1 per operation. Locked in as a permanent regression test: `DatabaseMetricsTests.Metrics_ConnectionOpenClose_CountsExactlyOncePerOperation`. No production code changed. Full suite green (7361 tests). |
| CORE-023 | P0 | **Done (2026-08-29)** | Transaction concurrency | Transaction commands/readers hold `_userLock`, but commit, rollback, and disposal coordinate only through `_completionLock`. Completion can therefore dispose the transaction and connection while a command or streaming reader is active. Savepoint operations also bypass `_userLock`. This conflicts with the class's thread-safety claim and explicit connection ownership. | **Resolved via the existing reader-aware lock mechanism, not a new state machine.** `ReusableAsyncLocker` (wrapping `_userLock`) already had a `MarkHeldByActiveReader()`/`ThrowIfBlockedBehindActiveReader()` fail-fast mechanism, wired up when `ExecuteReaderAsync` opens a reader on a transaction — but `CompleteTransaction`/`CompleteTransactionAsync` (used by both explicit `Commit()`/`Rollback()` and `Dispose()`'s auto-rollback path) and `SavepointAsync`/`RollbackToSavepointAsync` never consulted it. Fix: `CompleteTransaction`/`CompleteTransactionAsync` now acquire `_reusableLocker` *before* flipping `_completedState` (so a rejected attempt — reader still open — leaves the transaction fully retryable, not permanently stuck) and hold it through the entire teardown; `SavepointAsync`/`RollbackToSavepointAsync` now acquire it around their command execution like any ordinary command. Also fixed a second, related bug found while implementing this: `DisposeManaged`/`DisposeManagedAsync` disposed `_userLock` *unconditionally*, which would throw `ObjectDisposedException` from a still-open reader's own later cleanup `Release()` call — now only disposed via a new `DisposeUserLockUnlessHeld()` (skips disposal, matching the existing `shouldDisposeLock`/`_completionLock` precedent right next to it, if the lock isn't immediately acquirable). Broadened `ThrowIfBlockedBehindActiveReader`'s message to cover completion, not just "another command." Tests: `TransactionCompletionReaderGuardTests.cs` (7 tests: `Commit`/`CommitAsync`/`Rollback`/`RollbackAsync`/`Dispose` while a reader is open, plus `SavepointAsync`/`RollbackToSavepointAsync`), all reusing the exact `ExecuteReaderAsync`-opens-a-reader fixture from the pre-existing `TransactionReaderLockLifetimeTests.cs`; confirmed genuinely red first (all 7 failed — no exception thrown / connection disposed while reader open). Full suite green (7368 tests), all 422 transaction-related tests pass with no regressions. **Residual, deliberately out of scope:** an extremely narrow TOCTOU remains between a command's `GetLock()` call (checks `IsCompleted`) and its subsequent `LockAsync()` (actual acquire) — closing it fully would need a bigger redesign (single atomic gate for "begin operation" vs. "begin completion"); the fix here covers every realistic, materialized interleaving the completion criteria named (command/commit, reader/commit, command/rollback, reader/dispose, savepoint/command). |
| CORE-024 | P1 | **Done (2026-08-29)** | Telemetry data exposure | `SqlContainer` records full `db.statement`, exception messages, and complete exception strings/stack traces into activities. Explicit SQL can contain literals and provider failures can contain infrastructure or connection details. | **Resolved with a behavior-only fix, no new config surface** (user's explicit choice among three options — truncate/sanitize by default vs. an opt-in config knob vs. document-only). `db.statement` is now truncated to 4000 chars (`MaxTelemetryStatementLength`); `exception.message` is truncated to 1000 chars (`MaxTelemetryMessageLength`); the raw `exception.stacktrace` tag (a full `Exception.ToString()`, the worst offender for leaking file paths/connection internals) is no longer recorded at all — stack traces belong in logs, not trace tags. Factored the 6 duplicated `if (activity != null) { activity.AddEvent(...) }` blocks (3 in `ExecuteNonQueryAsync`, 3 in `ExecuteReaderAsync`) into one shared `AddSanitizedExceptionEvent(activity, ex)` helper. Also fixed the separately-flagged stale `ActivitySource` version: was hardcoded `"2.0.1"` while the package itself is now `3.0.0` (`Directory.Build.props`) — now reads `typeof(SqlContainer).Assembly.GetName().Version` so it can never go stale the same way again. Tests: `SqlContainerTelemetryRedactionTests.cs` — a 20,000-char query proves `db.statement` gets truncated; a forced command failure with a 5,000-char exception message proves `exception.message` is truncated and `exception.stacktrace` is absent from the emitted `ActivityEvent`. Confirmed genuinely red first. Verified `pengdows.crud.opentelemetry` (the separate OTel adapter package) doesn't reference either removed/changed tag. Full suite green (7387 tests). |
| CORE-025 | P0 | **Done (2026-08-29)** | Context terminal state | `DatabaseContext` does not override `ValidateCanCreateContainer`, and its connection/transaction entry points do not consistently call `ThrowIfDisposed`. After disposal, `AcquireSlot` sees nulled governors and returns an ungoverned default slot, so a container created either before or after disposal can create a fresh physical connection and execute outside the context's admission controls. | **Resolved:** `DatabaseContext` now overrides `ValidateCanCreateContainer()` to call `ThrowIfDisposed()` (rejects `CreateSqlContainer` immediately post-disposal); `GetStandardConnectionWithExecutionType` — the entry point every connection-acquisition path ultimately reaches — now calls `ThrowIfDisposed()` first. `AcquireSlot`/`AcquireInfrastructureSlot`'s null-governor fallback now throws `ObjectDisposedException` instead of silently returning an ungoverned default slot — but **only when `IsDisposed`**: a null governor is also the legitimate state during the narrow bootstrap window before `InitializePoolGovernors()` has run (`_effectivePoolGovernorEnabled` defaults to `true` specifically so the very first, pre-governor connection during construction still reaches this code) — conflating the two states initially caused a real regression (24 test failures constructing contexts) that surfaced immediately on the first full-suite run after implementing, fixed by adding the `IsDisposed` distinction via a new `ThrowIfGovernorMissingAfterDisposal()` helper. Tests: `DatabaseContextGovernorDisposalTests.cs` (`CreateSqlContainer_AfterDispose_ThrowsObjectDisposedException`, `GetConnection_AfterDispose_ThrowsObjectDisposedException`, `GetConnectionAsync_AfterDisposeAsync_ThrowsObjectDisposedException`); confirmed genuinely red first via a temporary revert-and-rerun (not a fresh red-then-green cycle, since the fix was implemented ahead of tests due to the complexity of the investigation — see note in CORE-027's entry on why). Full suite green (7378 tests). |
| CORE-026 | P0 | **Done (2026-08-29)** | Context disposal/lease ownership | Disposal waits for governors to drain only for `PoolAcquireTimeout`. On timeout it logs and continues to dispose owned data sources and release unique-connection claims even though commands/readers may still own permits and physical connections. The context can therefore return while old work remains alive and another context can claim the same connection identity. | **Resolved via "defer dependent cleanup," the third option in the completion criteria's shutdown-policy list** (not "safely cancel/abort" — no cancellation mechanism exists for in-flight ADO.NET commands, and forcing one was judged out of scope). `DisposePoolGovernors()`/`Async` now call `governor.Close()` on both governors *before* draining either (closing admission atomically first — see CORE-027), and return `false` if any governor times out draining instead of just logging. `DisposeManaged()`/`DisposeManagedAsync()` now only call `DisposeOwnedDataSources()` and `UniqueConnectionStringRegistry.ReleaseAll`/`UnregisterAllForWarning` when every governor drained cleanly; on a timeout they log a warning and skip that cleanup entirely — leaking the data source/claim rather than risking tearing down a resource an outstanding lease still depends on. Added `FakeDbDataSource.WasDisposed` (fakeDb) as a reusable observable-disposal test hook. Test: `EnforceUniqueConnectionStringTests.SecondContext_SameConnectionString_AfterFirstDisposalTimesOutDraining_StillThrows` — holds a connection open past a 50ms `PoolAcquireTimeout` so drain times out, then proves the uniqueness claim was NOT released (a second context on the same connection string is still rejected); confirmed genuinely red first ("No exception was thrown"). Full suite green (7379 tests). |
| CORE-027 | P0 | **Done (2026-08-29)** | Governor shutdown race | `PoolGovernor` has no closed/draining admission state. `WaitForDrainAsync` only observes `_inUse == 0`; a caller that captured the governor reference can acquire after the drain signal or race semaphore disposal. Nulling the context fields does not close the governor itself. | **Resolved.** `PoolGovernor` now extends `SafeAsyncDisposableBase` (per user suggestion mid-session, replacing an initial manual `_disposedOnce` `Interlocked` guard that duplicated what the base class already provides — matches the convention every other disposable type in this codebase already follows) instead of implementing bare `IDisposable`. Added `Close()`/`IsClosed` (independent of `IsDisposed` — `Close()` can run ahead of actual teardown to stop admission before draining) and a `ThrowIfClosed()` check at the top of all four acquire entry points (`Acquire`, `TryAcquire`, `AcquireAsync`, `TryAcquireAsync`), each throwing `ObjectDisposedException` immediately rather than either succeeding past intended shutdown or failing deep inside torn-down semaphore machinery. `DisposeManaged()` (overriding the base's template method instead of a raw `Dispose()`) calls `Close()` first, so `WaitForDrainAsync` now has a real guarantee once combined with a `Close()` call before draining begins: `_inUse` can only decrease, never increase. Tests: `PoolGovernorTests.cs` (7 new tests — closed-then-acquire for all 4 entry points, close-prevents-new-admission-so-drain-completes, dispose-is-idempotent, dispose-closes-admission). **Residual, deliberately out of scope** (same class of TOCTOU as CORE-023's residual note): a caller that already read a governor reference and is about to call an acquire method has no protection between that read and the call itself — closing this fully would need every acquire path to hold a coordinating lock across the check, a larger change not attempted here. Full suite green (7378 tests). |
| CORE-028 | P0 | **Done (2026-08-29)** | Constructor failure ownership | `InitializeReadOnlyConnectionResources` can create internally owned writer and reader `DbDataSource` instances before later validation, governor setup, session initialization, and unique-claim steps. The outer constructor catch cleans persistent connections/governors and warning registrations but never disposes those data sources. Since no context is returned, these provider resources can leak permanently on construction failure. | **Resolved by reusing the existing `DisposeOwnedDataSources()` method** (already correctly distinguishes caller-supplied from internally-created data sources via `_dataSourceProvided`, and already de-duplicates when reader/writer share one instance) rather than building a new ownership-scope abstraction — added a call to it in both the inner catch (around `ClaimUniqueConnectionStrings`) and the outer constructor catch. Added `fakeDbFactory.CreatedDataSources`/`FakeDbDataSource.WasDisposed` as reusable observable-creation/disposal test hooks (matching the existing `CreatedConnections` pattern). Test: `DatabaseContextConstructionFailureCleanupTests.Construction_FailsClaimingUniqueConnectionString_DisposesInternallyCreatedDataSource` — constructs a context successfully (claiming a connection string), then a second construction on the same string with `EnforceUniqueConnectionString=true` fails inside `ClaimUniqueConnectionStrings` (exercising both the inner and outer catch in sequence, since the inner catch's `throw;` re-enters the outer one), and asserts the internally-created `DbDataSource` was disposed; confirmed genuinely red first ("must be disposed" failure, with the test's own self-check confirming a data source really was created). Not separately tested: failure injection at every other construction phase (governor setup, session init) — the fix is a single reusable call at both existing catch sites, so it applies uniformly regardless of which phase throws; deeper phase-by-phase injection matrices were judged lower-value than the broader disposal ordering already covered by CORE-025/026/027. Full suite green (7380 tests). |
| CORE-029 | P0 | **Done (2026-08-29)** | Exception identity under admission control | Discovered while writing TEST-001 (two-tenant failure containment). `PoolSaturatedException` and `ModeContentionException` both extend `TimeoutException` directly and are documented (`ModeContentionException`, explicitly, in this file's own Exception Hierarchy section) as NOT part of the `DatabaseException` hierarchy — "a `catch (DatabaseException)` will not catch it." But `SqlContainer`'s `catch (Exception ex) when (ex is not DatabaseException && IsTimeout(ex))` clause treats any `TimeoutException`-derived exception as a raw provider timeout and translates it into `CommandTimeoutException` (a `DatabaseException` subclass) via the dialect's exception translator — silently destroying that documented type-identity contract whenever either exception originates from inside an actual command execution (`ExecuteNonQueryAsync`/`ExecuteReaderAsync`/`ExecuteScalarAsync` all share this code path), as opposed to, e.g., `BeginTransaction`, which never reaches it and was already correctly covered by an existing test. | **Resolved:** `SqlContainer.IsTimeout(Exception)` now returns `false` immediately for `PoolSaturatedException`/`ModeContentionException`, so both fall through to the existing (correct) `catch (Exception ex) when (ex is not DatabaseException)` clause, which already rethrows a non-`DbException` unchanged via `throw;` — a one-line, single-point-of-truth fix since both call sites (`ExecuteNonQueryAsync`, `ExecuteReaderAsyncInternal`) share this one helper. Tests: `InfrastructureTimeoutExceptionIdentityTests.PoolSaturatedException_PropagatesUnwrapped_FromCommandExecution_NotTranslatedToCommandTimeoutException` (saturates a `MaxConcurrentWrites=1` writer governor, asserts `ExecuteNonQueryAsync` throws `PoolSaturatedException` itself, not `CommandTimeoutException`) and `..._ModeContentionException_PropagatesUnwrapped_FromOrdinaryOpDuringActiveTransaction_..." (holds a `SingleConnection` transaction open, asserts an ordinary concurrent `ExecuteScalarOrNullAsync` throws `ModeContentionException` itself); both confirmed genuinely red first (both threw `CommandTimeoutException` with the real exception demoted to `InnerException`). Also fixed `TwoTenantFailureContainmentTests` (TEST-001, written first and what surfaced this bug) to assert `PoolSaturatedException` propagates as originally designed, replacing its interim `TotalSlotTimeouts` assertion (which turned out to depend on which of `PoolGovernor`'s two internal rejection paths — queue-depth overflow vs. genuine wait-timeout — fired, not the actual point of the test) with `InUse`/`TotalAcquired` assertions that hold regardless of which rejection path is hit. Full suite green (7396 tests, both net8.0/net10.0) — no other test anywhere depended on the old wrapping behavior. |

### 2. Missing proof and regression coverage

| ID | Priority | Status | Proof needed | Completion criteria |
|---|---|---|---|---|
| TEST-001 | P0 | **Done (2026-08-29)** | Two-tenant failure containment | Use two distinct contexts and one singleton gateway. Saturate or fail tenant A and prove tenant B continues, with independent governors, queues, and metrics. A deterministic fake provider can prove ownership; add a real-provider case where valuable. `TwoTenantFailureContainmentTests.SaturatedWriterGovernor_OnOneTenant_DoesNotAffectAnotherTenant_OnSharedSingletonGateway` constructs one `TableGateway` against tenant A, saturates tenant A's `MaxConcurrentWrites=1` governor by holding its one write connection open, then proves `CreateAsync` against tenant A fails while the exact same gateway instance's `CreateAsync` against tenant B succeeds — with each tenant's `PoolStatisticsSnapshot` independently correct (tenant A: `InUse=1`/`TotalAcquired=1`, the held connection only; tenant B: `TotalSlotTimeouts=0`, its own clean acquire). Writing this test surfaced a real, separate bug now fixed as CORE-029 (see that row) — `PoolSaturatedException` was silently getting rewrapped into `CommandTimeoutException` by `SqlContainer`, so the fake-provider proof needed against fakeDb only; a real-provider case was judged unnecessary since the containment mechanism (independent `PoolGovernor` per `DatabaseContext`) has no real-provider-specific behavior to exercise beyond what CORE-029's fix already covers. |
| TEST-002 | P1 | Open | Live provider-changing tenant migration | Resolve a tenant through provider A, execute through a shared gateway, re-register it for provider B, invalidate it, and execute through the same gateway using the new dialect/pool. Verify context lifecycle telemetry and disposal. |
| TEST-003 | P0 | **Done (2026-08-29)** | Configuration contract completeness | **Resolved as part of CORE-001** — reflection-based test added over the full `IDatabaseContextConfiguration` contract plus 4 targeted behavioral checks for the previously-omitted values. See CORE-001's row for exact test names. |
| TEST-004 | P1 | Verified (partial, 2026-08-29) | PDU invariants | Prove separate read/write sentinels target the correct pools, repair preserves permit accounting, provider-pool lifetime is unsurprising, and sentinel work does not appear as application demand. `PreventDatabaseUnloadTests.cs` already covers dual-sentinel pool targeting, Min Pool Size enforcement, and (new, CORE-003) sentinel-slot attribution neutrality. Repair-preserves-permit-accounting and provider-pool-lifetime aspects still need explicit coverage. |
| TEST-005 | P1 | Open | Detection API and threat behavior | Add public API-baseline coverage and verify sanitized diagnostics against exception messages containing secrets or infrastructure identifiers. Superseded in scope by CORE-004's revert: since detection diagnostics are internal again, this is no longer a public-API-baseline concern — re-open only if the API is re-exposed publicly. |
| TEST-006 | P0 | Blocked locally | Full release suite | Run all unit, integration, and testbed coverage in an environment with the .NET SDK and required database fixtures. The current review environment had no dotnet executable, so source inspection could not be confirmed by execution. Note: a later session (2026-08-29) had a working `dotnet` SDK and Docker, and ran the full `pengdows.crud.Tests` suite (7341 tests, net8.0 + net10.0, 0 failures, 0 skipped) after CORE-002/003/004/005/008 fixes. The Docker-based `testbed`/`IntegrationTests` matrix was not run in that session (judged unnecessary for pure connection-string/pool-governor/metrics changes) — still open for changes that touch dialect SQL generation. |
| TEST-007 | P2 | Verified | Runtime unknown-provider loading | Tests cover assembly path/name/legacy registration, keyed DI, `DbProviderFactories`, edge cases, and path traversal. Preserve this proof and link it from the docs rather than treating the capability as hypothetical. |
| TEST-008 | P0 | **Done (2026-08-29)** | Tenant registry interleavings | Deterministically pause context creation and exercise case aliases, invalidation, registry disposal, creation-event failure, and concurrent re-creation. Assert exact create/dispose/event counts and that no orphan or disposed context is returned. Case aliases (`GetContext_With(Different|MixedCase)...`), invalidate/dispose racing an in-flight *first* creation (`Invalidate_RacingWithInFlightCreate...`, `Dispose_RacingWithInFlightCreate...`), and concurrent faulting callers (`GetContext_WhenManyConcurrentFaultingCallers...`) were already covered by CORE-009/010/012's tests. **New:** `GetContext_ConcurrentCallersAfterInvalidation_CreateExactlyOneReplacementContext` fills the one remaining gap — a deterministic (via a call-numbered blocking factory double, not timing-hoped) race of 8 concurrent callers recreating a tenant immediately after `Invalidate`, asserting **exact** counts throughout: exactly 2 total factory construction calls across the whole sequence (1 initial + 1 recreation, never 1 + 8), all 8 racing callers converge on the identical new context instance, that instance is not the disposed original, exactly 2 `ContextCreated` events fire total (not 1, not 9), and exactly 1 `ContextRemoved` event. Confirmed passing (already-correct `Lazy<T>` `ExecutionAndPublication` behavior — no bug found here, unlike TEST-001) across 3 repeated runs to rule out flakiness in the deterministic pause mechanism itself. Full suite green (7397 tests, both net8.0/net10.0). |
| TEST-009 | P1 | Open | Reader-plan collision safety | Force equal hash values for distinct schemas and verify both gateway and general mapper select structurally correct plans. Include adversarial aliases, reordered columns, and different field types. |
| TEST-010 | P1 | Open | Generated-ID connection affinity | Make the provider return different physical sessions on consecutive leases. Prove every session-scoped generated-ID strategy remains on the insert connection or fails without mutating the entity ID. |
| TEST-011 | P1 | **Partially Done (2026-08-29)** | Provider-loader compatibility/threat cases | Test a real/static-field factory, symlink escape, duplicate registration, partial multi-provider failure, and the exact provider-key/invariant-name contract. Three of five now covered, added alongside CORE-006/014/015: `DbProviderLoader_RegistersProviderUsingFieldBasedInstanceConvention` (static-field `Instance`), `LoadAndRegisterProviders_RejectsSymlinkUnderBaseDirectoryPointingOutside` (symlink escape), `TenantContextRegistry_WhenTenantProviderNameIsInvariantNameNotSectionKey_ThrowsActionableError` (provider-key/invariant-name contract). **Still missing:** duplicate registration behavior (two `DatabaseProviders` entries resolving to the same section key or the same underlying factory type) and partial multi-provider failure (one of several configured providers throws during `LoadAndRegisterProviders` — do the others still register, or does the whole call abort?). |
| TEST-012 | P0 | Open | Reader ownership under all exits | Cover EOF, Close, sync/async dispose, cancellation, reader-dispose failure, command-dispose failure, connection-dispose failure, and locker failure. Assert permits, active-reader registrations, connections, and every lock return to baseline. |
| TEST-013 | P0 | Open | Transaction lifecycle interleavings | Pause commands/readers/savepoints at deterministic points and race commit, rollback, dispose, cancellation, and timeout. Assert one terminal outcome, no command on a completed transaction, and exact connection/permit ownership. |
| TEST-014 | P0 | Open | Exact metrics lifecycle | Assert exact — not merely nonnegative — global and reader/writer-role counts for open, close, broken, failed-open, sentinel, and disposal paths. |
| TEST-015 | P0 | Open | Context terminal-state enforcement | Create containers before and after context disposal and attempt commands, readers, scalar operations, and transactions through sync/async APIs. Assert `ObjectDisposedException` before provider connection creation, admission/attribution changes, or unique-claim release side effects. |
| TEST-016 | P0 | Open | Disposal with active and racing leases | Hold commands/readers and queued acquisitions while disposing the context. Race acquisition at the exact drain boundary. Assert admission closes, no post-close lease is granted, data sources and uniqueness claims remain owned until the last lease ends, and every resource releases exactly once. |
| TEST-017 | P0 | Open | Constructor rollback matrix | With disposable fake native data sources, inject failure after writer-source creation, reader-source creation, read-only validation, governor creation, session initialization, and uniqueness claim. Assert internally owned resources unwind in reverse order, injected resources survive, and the original exception is preserved. |

### 3. Documentation backlog

| ID | Priority | Status | Document | Required content |
|---|---|---|---|---|
| DOC-001 | P0 | Open | Authoritative multitenancy architecture | Explain that the tenant selects a complete execution environment; database-per-tenant is the deployment/provisioning invariant; context/admission isolation is library-enforced; tenant resolution must be trusted; providers and server versions may differ; rotation uses re-registration/invalidation; address tenant cardinality. |
| DOC-002 | P0 | **Partially Done (2026-08-29)** | Runtime provider loading | Make this first-class. Show configuration, assembly path/name/legacy modes, provider key versus invariant name, base-directory security restriction, tenant usage, supported/detectable engine versus wholly unknown engine behavior, and process-lifetime limitations. `docs/connection/dynamic-provider-loading.md` (written alongside CORE-006/014/015/017) now covers configuration shape, `AddDbProviderLoading`/`DbProviderLoader` usage, assembly path/name + legacy `DbProviderFactories.GetFactory` fallback, the provider-key-vs-`ProviderName` identity gotcha, symlink-safe base-directory containment, and `MaxTenantCount`; linked from `docs/README.md`, `llms.txt`/`llms-full.txt`, and the `connections.md` skill. **Still missing:** supported/detectable-engine versus wholly-unknown-engine runtime behavior (a `DatabaseDetectionService` topic, not really `DbProviderLoader`'s), and process-lifetime limitations (loaded assemblies are never unloaded from the default `AssemblyLoadContext` — not documented anywhere). |
| DOC-003 | P0 | Open | Operational pool governance | Distinguish provider pooling from pengdows admission control. Cover read/write limits, queue caps, timeouts, SingleWriter fairness, PDU sentinels, duplicate-connection detection, and relevant metrics. |
| DOC-004 | P0 | Open | Library guarantees and ownership | State exactly what the context, transaction, reader lease, gateway, sentinel, and registry own. Document permits, disposal, failure behavior, and the absence of a public raw `DbDataSource` escape hatch. |
| DOC-005 | P1 | Open | Provider/version evidence | Publish the current evidence: 12 engines and 30 engine/version targets, sourced from the maintained testbed results. Distinguish tested support from generic ANSI fallback. |
| DOC-006 | P1 | Open | Observability guide | Map operational questions to signals: slow command versus pool wait versus mode wait, reader lease duration, percentiles, context IDs, tenant lifecycle events, and OpenTelemetry discovery. |
| DOC-007 | P1 | Open | Claim-to-test evidence index | Map every important claim to the exact unit/integration/testbed proof. Refresh the partial implementation-evidence document and remove stale branch statements. |
| DOC-008 | P1 | Open | Tenant lifecycle recipe | Trusted tenant ID → registry `GetContext` → pass context to a shared gateway → re-register/invalidate on rotation → observe creation/removal. Include safe failure and retry behavior. |
| DOC-009 | P1 | Open | Heterogeneous SaaS examples | Show tenants on different providers and server versions behind shared gateways, including dialect-capability cache isolation and a live migration example. Present multi-server and multi-database-server operation as additional capability, not the foundation of isolation. |
| DOC-010 | P2 | Open | Schema-management boundary | Clearly separate current schema inspection/adoption tooling from the future generalized schema executor: inspect → target → diff → provider adjustment → ordering → reviewable DDL → execute → verify. Emphasize DBA authorization and owned-object boundaries. |
| DOC-011 | P1 | Open | Product/repository boundary | State that only pengdows.crud and pengdows.crud.abstractions contain the core library. Tests, examples, integrations, generators, and Hangfire are proof or adoption paths. |
| DOC-012 | P1 | Open | Limitations and precise claims | Document runtime-loaded assembly lifetime, dependency/version collision risk, restart expectations, generic-engine limits, physical-database contention, and exact isolation boundaries. Avoid absolute claims the library cannot enforce. |
| DOC-013 | P2 | Planned | Hangfire README cleanup | Correct the stale SQLite-only integration statement. Replace "pool exhaustion is a non-issue" and "cannot compete regardless of load" with the bounded-admission guarantee and the distinction between app-side permits/provider pools and shared physical database resources. Add DB2 after the PC 2.1 release as planned. |
| DOC-014 | P0 | Open | Tenant identity and rotation contract | Define tenant-ID case/normalization rules and the concurrency semantics of re-registration, invalidation, in-flight requests, disposal, and generation changes. State whether rotation is immediate, eventual, or drain-based. |
| DOC-015 | P1 | Open | Provider code trust model | State that runtime provider loading executes code inside the application process. Define who may control provider configuration/files, whether path containment is a security guarantee, assembly/dependency lifetime, restart behavior, and supported factory conventions. |
| DOC-016 | P1 | Open | Cache security and cardinality | Inventory process-wide and gateway caches, their keys, bounds, eviction, tenant/version cardinality, secret handling, and collision guarantees. This matters directly to long-lived multi-tenant processes. |
| DOC-017 | P0 | Open | Transaction concurrency contract | State whether concurrent use is supported, serialized, or rejected. Cover active readers, commit/rollback/dispose races, savepoints, cancellation, mode locks, and ownership of the pinned connection. Documentation must match enforced behavior. |
| DOC-018 | P1 | Open | Context-derived generation contract | Explain that gateways use the operation context for dialect, SQL templates, parameters, and execution. Separately identify gateway-global entity metadata/cache policy. Document that `ISqlContainer.Clone(otherContext)` changes execution/parameter behavior but cannot translate arbitrary caller-authored SQL between dialects. |
| DOC-019 | P1 | Open | Reader lease contract | Document EOF auto-disposal, explicit Close/Dispose equivalence, streaming cancellation, command/connection/permit ownership, and what happens when cleanup itself fails. |
| DOC-020 | P0 | Open | Context shutdown contract | Define admission closure, active/queued work behavior, drain/cancellation policy, shutdown timeout distinct from acquisition timeout, data-source lifetime, uniqueness-claim lifetime, sync versus async disposal, and the exception returned by every operation attempted after disposal. |
| DOC-021 | P0 | Open | Multitenancy quick start and API reference | The implementation exposes `AddMultiTenancy`, `MultiTenantOptions`, `TenantConfiguration`, `ITenantConnectionResolver`, and `ITenantContextRegistry`, but `AddMultiTenancy` does not occur anywhere in the public docs. Provide an `appsettings.json` example, DI registration, request-time resolution, singleton-gateway usage, registration/update/invalidation flow, lifecycle events, application-name composition, cardinality guidance, and provider-key requirements. Link it from the root README and docs index. |
| DOC-022 | P0 | Open | Runtime provider-loading quick start and API reference | The implementation exposes `AddDbProviderLoading`, `DatabaseProviders` binding, assembly-path, assembly-name, and legacy `DbProviderFactories` fallback, plus keyed DI registration. `AddDbProviderLoading` does not occur anywhere in the public docs. Provide exact configuration examples for every loading mode, startup order with multitenancy, key/invariant semantics, diagnostics, trust/lifetime constraints, and supported factory conventions. Link it from the root README and docs index. |
| DOC-023 | P1 | Open | Portable database-error analysis | `ISqlDialect.ClassifyException` and `AnalyzeException` expose provider-neutral categories, constraint kind, transient/retryable signals, provider code, and SQLSTATE across the dialect family. Current docs mention individual translated exceptions but do not teach this as an application control-flow/observability feature. Document the taxonomy, provider coverage, retry-policy boundaries, examples, and relationship to thrown pengdows exceptions and metrics. |
| DOC-024 | P1 | Open | Automatic audit-field lifecycle | Gateways automatically populate `[CreatedBy]`, `[CreatedOn]`, `[LastUpdatedBy]`, and `[LastUpdatedOn]` through `IAuditValueResolver`; batch operations resolve once; timestamps require UTC; user IDs are coerced to target types; `AuditCreationPolicy` controls preservation versus authority; failed writes restore entity audit values. The only discoverable material is an isolated resolver example and positioning prose. Add a complete feature guide and mapping table. |
| DOC-025 | P1 | Open | Database detection evidence API | `DatabaseDetection.DetectFromConnectionWithDetail` and its genuine async twin return probe evidence, confidence/resolution detail, and failure information. Superseded by CORE-004's revert: this API is `internal` again as of 2026-08-29, so this item is now "design a proper public version" rather than "document the existing one." If revisited, must be paired with moving the contract into `pengdows.crud.abstractions` and sanitizing `FailureReason` (see CORE-004/CORE-005 resolution notes). |
| DOC-026 | P1 | Open | Stored-procedure portability | Dialect detection selects `ProcWrappingStyle` and the library contains wrapping strategies for CALL, EXEC, EXECUTE PROCEDURE, Oracle, and PostgreSQL forms. Current docs describe the strategy internally and list scattered support facts, but provide no consumer workflow or cross-provider example. Document construction/execution, input/output/return parameters, wrapping behavior, limits, and the provider matrix. |
| DOC-027 | P0 | Open | Shared gateway/container template model | `ISqlContainer.Clone()` copies SQL and parameter structure for low-cost rebinding; `Clone(IDatabaseContext)` rebinds the container and parameters to a transaction or tenant context/dialect. The implementation and tests explicitly treat this as essential to cached templates and multitenancy, but no public document mentions the overload. Explain template construction, immutable-versus-mutable state, parameter rebinding, transaction use, tenant use, dialect boundaries, cache reuse, disposal, and safe singleton patterns. |
| DOC-028 | P1 | Open | SQL-container composition guide | Public fluent helpers quote identifiers and format parameters through the active dialect (`AppendName`, `AppendParam`, fragment helpers), while execution overloads select read/write intent, cancellation, scalar semantics, tracked readers, and `CommandBehavior.SingleRow`. Most appear only in the unindexed `api-supplements.md`; `WrapForStoredProc` and context cloning are absent entirely. Create one discoverable end-to-end guide and link `api-supplements.md` from the docs index or merge it into maintained reference material. |
| DOC-029 | P1 | Open | Streaming query guide | `LoadStreamAsync` and `RetrieveStreamAsync` provide repeatable async streams, cancellation, early-break cleanup, transaction-context execution, and bounded materialization behavior. They are named in the README but only discussed incidentally in internal architecture/performance notes. Document reader ownership, query-per-enumeration semantics, early termination, transaction restrictions, and when to choose list versus stream. |
| DOC-030 | P2 | Open | Gateway count helpers | Every gateway exposes `CountAllAsync`, equality/LIKE count, null count, and compound equality-plus-null/not-null count helpers with identifier quoting and parameterization. All four APIs have zero references in public documentation. Add them to the gateway reference with exact limitations: string-valued predicates, supported compound shape, context override, and no general expression system. |
| DOC-031 | P0 | Open | Complete entity-mapping reference | The overview merely names most attributes. It does not teach `[Column]` inclusion/ignore rules, `DbType`, ordinal ordering, writable/generated `[Id]`, composite `[PrimaryKey]` order, JSON serializer options, enum literals/failure policy, insert/update exclusion, audit attributes, correlation tokens, or version behavior as one coherent mapping contract. Create an attribute matrix with read/write behavior, valid combinations, defaults, examples, and gateway compatibility. |
| DOC-032 | P0 | Open | Runtime database-capability discovery | `IDataSourceInformation` exposes detected product/version, parsed version, quoting, parameter marker/regex/length, named/repeated parameter behavior, parameter/output limits, preparation defaults, procedure style, upsert/merge/truncate/drop capabilities, fallback status, and compatibility warnings. `ISqlDialect` exposes a substantially broader capability surface including batching, namespaces, savepoints, pooling, read-only behavior, modern SQL features, JSON/array/regex/window/CTE/graph support, generated-key strategy, pagination, and exception analysis. Current docs discuss pieces internally but do not teach applications or higher-level Pengdows libraries to query capabilities instead of branching on database names. Provide a public capability reference, stability contract, examples, and provider/version matrix. |

### 4. Verified capabilities that are currently under-explained

These are not speculative roadmap items. They exist in the inspected code or maintained evidence and should be taught explicitly.

| ID | Capability | Evidence/meaning to preserve in docs |
|---|---|---|
| CAP-001 | Runtime provider discovery | Arbitrary ADO.NET provider assemblies can be loaded at runtime without a compile-time provider reference, then registered through keyed DI and `DbProviderFactories`. A provider for a recognized database can receive the database dialect; a wholly unknown engine receives only generic behavior until dialect support is supplied. |
| CAP-002 | Context-per-tenant architecture | Isolation is not a row-filter convention. Each tenant receives an independently governed execution context; database-per-tenant is the deployment model. |
| CAP-003 | Independent admission state | Reader/writer governors, queues, metrics, mode locks, and sentinels are context-owned rather than global. |
| CAP-004 | Tenant registry lifecycle | Lazy singleton context per tenant, concurrent cardinality limit, fault eviction/retry, targeted and global invalidation, and creation/removal events are implemented and tested. |
| CAP-005 | Singleton-gateway safety | Analyzer rules and context propagation protect shared gateways. Binder caches are isolated by dialect instance and SQL templates by capability fingerprint. |
| CAP-006 | Real heterogeneous-version behavior | A maintained integration test uses one shared gateway against MySQL 8.0.19 and 8.0.33, emits the correct version-specific UPSERT, and executes it. |
| CAP-007 | Compile-time guardrails | Analyzers include singleton registration checks, predicate-value parameterization, gateway context propagation, and composite identifier wrapping. |
| CAP-008 | OpenTelemetry lifecycle | Context creation/removal is observed automatically, singleton contexts can be discovered, context IDs correlate traces, and metrics distinguish roles. |
| CAP-009 | Duplicate connection protection | `EnforceUniqueConnectionString` supports warning and hard-reject modes with atomic claims, credential-sensitive hashing without plaintext retention, cleanup, and rollback proof. CORE-001 currently prevents reliable tenant-path propagation and must be fixed. |
| CAP-010 | Broad database evidence | The maintained testbed records 12 engines and 30 engine/version targets: CockroachDB, DB2, DuckDB, Firebird, MariaDB, MySQL, Oracle, PostgreSQL, SQL Server, SQLite, TiDB, and YugabyteDB. |
| CAP-011 | PDU sentinel model | PreventDatabaseUnload uses held connections as passive sentinels while integrating with context permit accounting. Its operational contract needs CORE-002/CORE-003 (both done as of 2026-08-29) plus explicit proof for the remaining aspects of TEST-004. |
| CAP-012 | Configuration-native multitenancy | `AddMultiTenancy` binds a complete tenant list, composes per-tenant application names, registers a case-insensitive configuration resolver, a lazy context registry, a replaceable context factory, and keyed-provider resolution. Configurations can also be registered at runtime and contexts invalidated for rotation. This is shipped code but currently undiscoverable as a usable feature. |
| CAP-013 | Configuration-native provider plug-ins | `AddDbProviderLoading` loads ADO.NET factories by local assembly path, assembly name, or existing `DbProviderFactories` registration, then registers each as a keyed DI singleton and in the legacy registry. This is the mechanism that permits providers unknown at compile time and is currently absent from the public documentation path. |
| CAP-014 | Provider-neutral failure intelligence | Dialects expose both coarse classification and detailed `DbExceptionInfo`: category, constraint kind, transient/retryable flags, provider error code, and SQLSTATE. Shipped translators cover the major supported database families with a fallback path. |
| CAP-015 | Transactional audit-field automation | Entity attributes plus `IAuditValueResolver` drive create/update audit fields, including batch resolution, UTC validation, type coercion, explicit-value policy, and rollback of in-memory mutations when persistence fails. |
| CAP-016 | Evidence-bearing database detection | Public sync and async detection APIs expose not just a product enum but the probes that led to resolution. That is valuable for provider onboarding, diagnostics, and heterogeneous fleets, yet is not presented as a product feature. As of 2026-08-29 (CORE-004) this capability is internal-only again; re-exposing it publicly needs the abstractions placement and sanitization work described there. |
| CAP-017 | Context-rebindable SQL templates | `ISqlContainer.Clone(IDatabaseContext)` preserves a built query and parameter structure while recreating provider parameters for the target dialect/context. Tests cover different dialects, transactions, output parameters, cached command text, and different tenant contexts. This is a core mechanism behind shared gateways, not a minor cloning convenience. |
| CAP-018 | Dialect-safe explicit SQL composition | `SqlContainerExtensions` supplies a small fluent vocabulary for quoted identifiers, provider-formatted placeholders, common SQL fragments, execution intent, cancellation, and single-row hints without introducing an expression translator. |
| CAP-019 | Lease-safe async entity streaming | Both gateway families can execute arbitrary prebuilt containers as async entity streams, and row-ID gateways can stream ID collections directly. The API explicitly defines re-enumeration, cancellation, and early-break behavior. |
| CAP-020 | Parameterized gateway counts | Both gateway families inherit focused count operations that quote caller-supplied identifiers and parameterize values, covering common operational queries without a general query language. |
| CAP-021 | Attribute-driven persistence policy | The mapping surface does more than property-to-column naming: it controls insertion, updating, generated identifiers, natural/composite keys, audit fields, optimistic versioning, correlation-token key recovery, JSON, enum literals, and deterministic column ordering. |
| CAP-022 | Runtime capability negotiation | The connected context exposes normalized metadata and dialect capabilities rather than forcing consumers to hard-code provider names. This includes syntax, limits, feature support, pooling/read-only constraints, procedure behavior, generated-key strategy, namespaces, isolation, pagination, and compatibility warnings. It is a foundation for pengdows.hangfire and future schema tooling, but the public docs present it only as scattered facts and internal architecture. |

### 5. Downstream evidence and adoption paths (not core library)

**pengdows.hangfire**

Treat as downstream transactional proof and an adoption path to pengdows.crud.

Current inspected coverage used live fixtures across 11 engines and shared suites for connections, queues, transactions, locks, expiration, watchdog behavior, and counter aggregation.

Planned after the PC 2.1 release: cleanup and DB2 support, bringing the downstream matrix to 12 engines.

Its claims must remain precise: pengdows bounds and attributes admitted demand. Separate contexts isolate application admission state and provider-pool usage, but cannot eliminate contention for shared physical database resources.

**pengdows.poco.mint and related tooling**

Treat schema-first inspection/generation as an adoption path, not core CRUD functionality.

Current schema inspection is distinct from the proposed generalized, DBA-governed schema executor.

Do not imply that inspecting metadata already provides universal diff planning, dependency ordering, executable DDL, or post-execution verification.

### 6. Product-positioning guardrails

**Goals**

Be the DAL that DBAs insist on.

Be the obvious meaningful choice for relational, database-per-tenant .NET SaaS.

**Defensible central claim**

pengdows.crud combines database-per-tenant deployment with context-per-tenant execution governance, independent admission and telemetry, shared singleton gateways, heterogeneous providers, heterogeneous server versions, runtime provider loading, and real transactional proof through downstream integrations.

**Suggested category statement**

Database-governed, SQL-first data access for relational SaaS.

**Claims to avoid**

"The only meaningful choice for SaaS" without narrowing the category to relational, database-per-tenant .NET systems.

"Pool exhaustion is impossible" or "workloads cannot compete regardless of load."

Implying the library alone provisions one database per tenant or authenticates tenant identity.

Implying arbitrary unknown database engines automatically receive a complete dialect merely because their ADO.NET provider can be loaded.

Treating tests, Hangfire, or generators as code shipped by the two core library projects.

### 7. Recommended release sequence

1. Stabilize the contract: CORE-001 and CORE-002 (done), followed by the full test suite.
2. Correct attribution and security: CORE-003 (done) through CORE-006.
3. Prove isolation end to end: TEST-001 through TEST-005.
4. Publish the architecture: DOC-001 through DOC-008 and DOC-011/012.
5. Publish evidence: provider/version matrix and claim-to-test index.
6. Strengthen downstream proof: Hangfire cleanup and DB2 after PC 2.1.
7. Expand the DBA story: design the generalized schema executor as a separate, reviewable, DBA-authorized capability.

### 8. Definition of ready for the 2.1 claim

The release is ready to make the strong SaaS/DBA positioning claim when:

- every P0 core issue is closed;
- the full unit, integration, and testbed suite passes;
- two-tenant failure containment is directly proven;
- tenant configuration copying cannot silently drift again;
- runtime provider loading and its limits are first-class documentation;
- multitenancy guarantees clearly distinguish library enforcement from deployment responsibility;
- every headline claim links to executable evidence;
- observability and resource-ownership contracts are documented precisely enough for production operators and DBAs to evaluate them.

### 9. Review notes

This ledger records source-inspection findings, not executed-test results, because the review environment lacked the .NET SDK (except where a row explicitly states a later session ran and verified them, e.g. CORE-002/003/004/005/008 and TEST-006's update, both 2026-08-29).

Re-check file/line references when fixes land; track conclusions and contracts here rather than brittle line numbers.

When an item closes, preserve a short resolution note and link its tests/docs instead of deleting the row.
