# 2.0.6 bug backlog

Bugs found on the `2.0.6` branch by checking code comments against what the code actually does
(September 2026). Each discrepancy was resolved by deciding which side is right — using 3.0's
behavior, the docs, test intent, git history, and whether the current behavior is harmful — rather
than by rewriting the comment to match the code.

**How to work an item:** TDD — write the failing test first, confirm it's red, fix, run the full
suite on net8.0 and net10.0. Restore or correct the comment so it states the intended behavior.
Tick the box and add the commit hash. `2.0.6` is a non-breaking patch line: anything marked
**Breaks callers: yes** needs an explicit decision and a release note before it lands.

Columns used below: **Where** is the 2.0.6 location; **3.0** says whether 3.0 already fixed it
(with the commit to port) or has the same bug.

## Intended behavior (decided)

These are the rules the code is being brought in line with.

- **Enum storage:** a numeric column stores the enum's underlying number; a string column (`String`,
  `AnsiString`, `StringFixedLength`, `AnsiStringFixedLength`) stores the enum's name. Storing a
  number in a string column is a bug.
- **Isolation fails up, never down:** an explicit `IsolationLevel` resolves to the requested level or
  the weakest stronger supported one, else throws; an `IsolationProfile` that can't be met throws
  `TransactionModeNotSupportedException`.
- **`TotalConnectionsReused`** is not tracked (provider pool reuse is invisible to pengdows.crud); it
  is `[Obsolete]` on 2.0.x and removed in 3.0.

## Done

- [x] **Isolation fail-up** — explicit levels resolve up; profiles throw instead of degrading. (`b88cea6`)
  The testbed `[InvalidTxType]` check still expected every unsupported level to be rejected; it
  now checks that the level fails up (PostgreSQL, Firebird, SQLite, YugabyteDB, Oracle,
  CockroachDB, DuckDB) and is rejected only where nothing stronger exists (TiDB, Snowflake).
  `TransactionTests.Transaction_IsolationProfile_SafeNonBlockingReads_Works` likewise still expected
  SQL Server to run the profile; it now checks `snapshot_isolation_state` and expects
  `TransactionModeNotSupportedException` when snapshot isolation is off.
- [x] **Enum names for every string column type** — `ColumnInfo.MakeParameterValueFromField` only
  checked `DbType.String`, so `AnsiString`/fixed-length enum columns stored the number. *Release
  note:* rows written before the fix keep their numeric text; reads accept both. 3.0 has the same
  bug (see 3.0 follow-ups).
- [x] **`TotalConnectionsReused` marked `[Obsolete]`** — never incremented; always 0.

- [x] **fakeDb brought level with 3.0** — `UPDATE … SET "col" = …` with quoted names now persists
  (it reported 1 row but never wrote the value); `SELECT col AS alias` / `col alias` return the
  alias; opt-in `FakeDataStore.StrictMode` throws `NotSupportedException` for SQL it doesn't
  recognize; emulated Access reports ServerVersion `04.00.0000`. Everything else in fakeDb that
  differs from 3.0 is 2.0.6's own fix (B12) or a comment correction.

- [x] **Exception classification and translators brought level with 3.0** — classification moved
  onto the dialects (per-dialect `Is*Violation` and protected `TryClassifyProviderException`
  overrides); every translator takes the `ISqlDialect` and delegates to them, so the thrown
  `DatabaseException` type and `AnalyzeException` can no longer disagree. New
  `DbErrorCategory.AmbiguousResult` / `AmbiguousResultException` (CockroachDB SQLSTATE 40003, not
  transient); new `FlatFileExceptionTranslator`/`SnowflakeExceptionTranslator`; `LooksLikeTimeout`
  walks inner exceptions; provider-exception detection in `SqlContainer` is by type
  (`AseException`), not by property shape. Kept 2.0.6 behavior 3.0 lacks: Informix
  -908/-27001/-27002 → `ConnectionException`. *Release notes (widenings taken from 3.0):* MySQL
  SQLSTATE 40001 without error 1213 now throws `SerializationConflictException`; PostgreSQL
  SQLSTATE 55P03 (lock_not_available) now throws `CommandTimeoutException`; DuckDB "Conflict on …"
  (insert/delete, not just update) now throws `SerializationConflictException`; CockroachDB 40003
  throws `AmbiguousResultException`; an application exception that merely exposes a
  `SqlState`/`Number` property is no longer wrapped as a `DatabaseException`.

## Fix — no caller-visible break

- [x] **B01 — SQL numbers use the current culture.** *(fixed; also `Append(object)` and `AppendFormat(null, …)`)* `SqlQueryBuilder.cs:110-175`:
  `Append(int/long/double/decimal)` and `AppendFormat(string, …)` format with
  `CultureInfo.CurrentCulture`, so on `de-DE` `Append(1.5m)` writes `1,5` (invalid SQL); some ICU
  cultures emit U+2212 for negatives. The library's own paging goes through `Append(int)`.
  **Fix:** `CultureInfo.InvariantCulture`; update the `ISqlQueryBuilder` XML docs, which currently
  state "current culture". **3.0:** same bug.
- [x] **B02 — Aurora PostgreSQL and Spanner lose generated keys.** *(fixed; no live coverage yet — Spanner isn't in `InsertReturningTests` or the integration suite's default databases, and the testbed's table uses a client-supplied `[Id]`, so nothing exercises Spanner's generated-key path. Aurora PostgreSQL needs AWS.)* `dialects/SqlDialect.cs:2895-2911`
  `RenderInsertReturningClause` has no arm for `AuroraPostgreSql`/`Spanner`, but both inherit
  `SupportsInsertReturning = true`, so the INSERT goes out without `RETURNING` and the gateway falls
  back to `SELECT lastval()` on a different pooled connection (error, or a stale id). **Fix:** port
  3.0's `PostgreSqlDialect.RenderInsertReturningClause` override. **3.0:** fixed (`d85450d`).
  Verify Spanner `RETURNING` against the live testbed.
- [x] **B03 — SingleConnection gate leaked when a sync read-only begin fails.** *(fixed)*
  `TransactionContext.cs:186-196`: if `TryEnterReadOnlyTransaction` throws, the catch rolls back and
  closes the connection but never releases `_singleConnectionTransactionGate` (or completes metrics /
  disposes locks). In `DbMode.SingleConnection` every later transaction or write then waits until
  `ModeLockTimeout`, or forever when it is null. The async path is correct. **Fix:** catch body →
  `Dispose(); throw;`. **3.0:** fixed (`dd4b470`).
- [x] **B04 — `PostgreSqlInterval.FromTimeSpan` counts days twice.** *(fixed)*
  `types/valueobjects/PostgreSqlInterval.cs:72-77` uses the full `Ticks` *and* `TotalDays`, so a
  3-day interval round-trips as 6 days. Hot read path (Npgsql returns `TimeSpan`,
  `AdvancedCoercions.cs:98`). `ValueObjectCoverageTests.cs:26` asserts the wrong value. **Fix:**
  `value.Ticks % TimeSpan.TicksPerDay`. **3.0:** fixed (`81eef2b`).
- [x] **B05 — Interval parse drops years.** *(fixed; also weeks)* `types/converters/PostgreSqlIntervalConverter.cs:224-246`:
  the ISO-8601 parser ignores `Y` (and `W`), so `P1Y2M` → 2 months. `AdvancedTypeConverterTests.cs:294`
  locks in the bug. **Fix:** `Y` adds 12 months, `W` adds 7 days, `M` accumulates. **3.0:** same bug.
- [x] **B06 — PostgreSQL interval writes are rejected.** *(fixed: writes send `NpgsqlInterval`; entity hydration — `CompiledMapperFactory` and `DataReaderMapper` — reads interval columns as `NpgsqlInterval` so months and the days/time split round-trip; ISO fallback formatter fixed; live-verified on PostgreSQL/CockroachDB/YugabyteDB, net8.0 and net10.0. Also found: `docs/advanced-types.md` declared value-object columns as `DbType.String`, which the validator rejects — corrected to `DbType.Object`.)*
  `PostgreSqlIntervalConverter.cs:89-197` sends an ISO string with `NpgsqlDbType.Interval` (Npgsql
  rejects that combination); the formatter also drops whole days held in the time part and
  sub-millisecond precision; YugabyteDB is missing from the provider check. **Fix:** send an
  `NpgsqlInterval` (months/days/microseconds, preserving months), add YugabyteDB. **3.0:** partly
  fixed (`81eef2b` sends `ToTimeSpan()`, which drops months — do better here).
- [x] **B07 — Tenant configuration clone drops settings.** *(fixed; reflection test covers every configuration property)*
  `tenant/TenantConnectionResolver.cs:149-169` omits `MaxQueuedWrites`, `MaxQueuedReads` and
  `SessionInitializationFailureMode`; every registered tenant is cloned, so they all silently get the
  defaults (e.g. `FailClosed` becomes `BestEffort`). **Fix:** copy all three; port 3.0's
  reflection-based "every property preserved" test. *Release note:* tenants that set these now get
  them. **3.0:** fixed (`c5083b0`, CORE-001).
- [x] **B08 — Connection-string cache retains credentials, unbounded.** *(fixed, including the key-parameter issue 3.0 still has)*
  `internal/ConnectionStringNormalizationCache.cs`: static dictionary keyed by the raw connection
  string (passwords kept for the process lifetime), no size limit. Also its key ignores the
  read-only key/value, application name and suffix that shape the cached value. **Fix:** port 3.0's
  SHA-256-keyed 256-entry `BoundedCache`, and fold those parameters into the key. **3.0:** fixed
  (`c5083b0`, CORE-012) except the key-parameter issue.
- [x] **B09 — `JsonValue.AsElement` disposes the caller's document.** *(fixed)*
  `types/valueobjects/JsonValue.cs:97-106`: `using var doc = AsDocument()` disposes the caller-owned
  `JsonDocument`; later `AsString()`/`ToObject()` throw `ObjectDisposedException`. **Fix:** return
  `_document.RootElement.Clone()`. **3.0:** same bug.
- [x] **B10 — SQL Server spatial: SRID lost on read, GeoJSON sent to `STGeomFromText`.** *(fixed and unit-tested against the real Microsoft.SqlServer.Types 160 API — writes never worked before: the converter looked up `SqlBytes`/`SqlChars` in the wrong namespace and passed a `SqlInt32` SRID. Live SQL Server round-trip still pending: needs the package in the integration project and `UdtTypeName` on the parameter.)*
  `types/converters/SpatialConverter.cs:240-256` computes `STSrid` and never uses it (and
  `Convert.ToInt32(SqlInt32)` would throw; `STAsBinary()` returns `SqlBytes`). `:156-167` passes
  GeoJSON text to `STGeomFromText`; `SqlBytes`/`SqlChars` are looked up in the wrong namespace
  (`:136-137`, unverified). **Fix:** use the SRID; WKT/WKB only for SQL Server, clear error for
  GeoJSON-only values; fix the type lookups. **3.0:** same bugs.
- [x] **B11 — Range literal `empty` doesn't parse.** *(fixed)* `types/converters/PostgreSqlRangeConverter.cs:188`
  and `Range<T>.Parse`: PostgreSQL's canonical empty range throws. **Fix:** special-case `empty`.
  **3.0:** same bug. (Low severity.)
- [x] **B12 — fakeDb `SetFailOnOpen(skipFirstOpen: true)` ignored.** *(fixed, sync and async)* `fakeDbConnection.cs:68, 428-433`:
  `_skipFirstFailOnOpen` is stored and never read. **Fix:** honor it in `Open`. **3.0:** same.
- [x] **B13 — `_singleConnectionTransactionGate` never disposed.** *(closed, no change — not a leak.
  `SemaphoreSlim` only holds an unmanaged handle once `AvailableWaitHandle` is used, which nothing
  here does, so `Dispose()` frees nothing. Disposing it would add a failure mode instead:
  `RealAsyncLocker.ReleaseIfHeld()` releases the semaphore unguarded, so a transaction completing
  after its context is disposed would throw `ObjectDisposedException` from `Commit`/`Dispose`.)*

## Fix — changes observable behavior (decide first)

- [x] **B20 — Batch update overwrites audit/version columns and never checks the version.** *(fixed without 3.0's `ISqlDialect.BuildBatchUpdateSql` signature change: the batch SET excludes `[Version]`/`CreatedBy`/`CreatedOn`; `[Version]` entities are updated row by row via `UpdateAsync(entity, loadOriginal: false)` and `BuildBatchUpdate` returns per-row containers for them. `PrimaryKeyTableGateway` already did both. Tests: `TableGatewayBatchUpdateVersionAuditTests`. *Release note:* a batch update containing a stale `[Version]` row now throws `ConcurrencyConflictException` instead of silently overwriting; rows before it in the batch are already updated unless the caller uses a transaction.)*
  `TableGateway.Batch.cs:329-344` (`GetCachedUpdateableColumns`) includes `CreatedBy`, `CreatedOn` and
  the `[Version]` column, so a multi-row UPDATE writes creation audit from the in-memory entity and
  copies the client's version verbatim (no `+ 1`, no `WHERE version = …`) — silent lost updates. No
  rows-affected check on any batch path. **Proposed:** (1) exclude those three columns — no break;
  (2) route `[Version]` entities through the per-row update, which increments and checks — no break;
  (3) throw `ConcurrencyConflictException` when a versioned row isn't updated — **breaks callers:
  yes** (silent data loss becomes an exception, which is the documented `[Version]` contract).
  **3.0:** fixed (`5e6b244`, `21ccbca`, `91225cd`).
- [x] **B21 — `CountWhereEqualsAsync` silently ignores `andWhereNotNull` when both flags are set.** *(ported 3.0's throw, `7ca59ba`. **Release note:** passing both now throws `ArgumentException`.)*
  `BaseTableGateway.Count.cs:82-85`; the audit wrote the precedence into the `ITableGateway` /
  `IPrimaryKeyTableGateway` docs. **Options:** throw `ArgumentException` like 3.0 (**breaks callers
  who pass both**, whose counts are already wrong) or apply both predicates (no break). Either way,
  restore the "at most one may be set" doc. **3.0:** throws (`7ca59ba`).
- [x] **B22 — `Uuid7Options.FailFastOnBurst` has no effect.** *(ported 3.0 as-is, `fb11369`. **Release note:** with `FailFastOnBurst` set — including `Configure(new Uuid7Options(Uuid7ClockMode.PtpSynced))`, whose mode defaults set it — `NewUuid7` throws `InvalidOperationException` after 4096 IDs/ms on one thread instead of blocking.)* `Uuid7Optimized.cs:67-74, 220-258`:
  `NewUuid7` always blocks on burst exhaustion. **Proposed:** honor it only when set explicitly —
  `DefaultsFor(PtpSynced)` currently turns it on, so honoring the default would make PtpSynced users
  start seeing exceptions (**breaks callers: yes** unless the default is changed). **3.0:** throws
  `InvalidOperationException` (`fb11369`).
- [x] **B23 — Command on a transaction while its own reader is open hangs.** *(ported from 3.0 `d5b24e3`/`c5083b0`: `ReusableAsyncLocker.MarkHeldByActiveReader` fails any contended lock fast; commit/rollback (sync and async) and all three savepoint calls go through the same lock, before `_completedState` flips so a failed attempt is retryable; the provider transaction is now disposed in the completion path (from `5e6b244`), and `_userLock` is only disposed when not held; `TrackedReader` releases the context lock even if releasing the connection lock throws. Tests: `ReusableAsyncLockerTests`, `TransactionReaderLockLifetimeTests`, `TransactionCompletionReaderGuardTests`, `TrackedReaderBranchTests`. Known, as in 3.0: `Dispose()` of a transaction while its reader is open skips the rollback and leaves the transaction to the GC. *Release note:* a command, commit, rollback or savepoint on a transaction while a reader opened on it is still open now throws `InvalidOperationException` instead of waiting; code that shared one transaction across concurrent tasks must serialize its own calls.)*
  `SqlContainer.cs:1523-1575` + `ReusableAsyncLocker`: the reader holds the transaction's lock and any
  further command/commit/rollback on that transaction waits with no timeout. **Proposed:** port 3.0's
  fail-fast (`InvalidOperationException` "…while a reader opened on it is still active…").
  **Breaks callers: yes** for code that relied on the wait (e.g. sharing one transaction across
  concurrent tasks). **3.0:** fixed (`c5083b0`, CORE-023).

## Decisions needed

- [ ] **D01 — Interval text that isn't ISO-8601 parses as zero, successfully.**
  `PostgreSqlIntervalConverter.Parse`: PostgreSQL's default `intervalstyle=postgres` text
  (`1 year 2 mons 3 days 04:05:06`) and any invalid string become a zero interval and report success;
  `IntervalConverter_ShouldTreatInvalidStringAsZero` asserts this. Options: implement the postgres
  style (the old comment's promise; additive) or reject unrecognized input (breaks that test).
- [ ] **D02 — PostGIS writes never send the SRID.** `SpatialConverter.cs:170-188` sends plain WKB (or
  WKT/GeoJSON strings with `DbType.Binary`), so `geometry(Point,4326)` columns reject SRID 0 and
  untyped columns store 0. Options: emit EWKB / `SRID=n;WKT` (the old comment's promise) or document
  "SRID not transmitted; use `ST_SetSRID`".
- [ ] **D03 — `[CorrelationToken]` on a `Guid` property throws at create.**
  `TableGateway.Core.cs:233, 431` always sets `Guid.NewGuid().ToString("N")`; registration doesn't
  check the property type. Options: support `Guid`/`Guid?` (the old comment's promise; additive) or
  reject non-string properties at registration.
- [ ] **D04 — Updating a deleted `[Version]` row throws `InvalidOperationException`.**
  `TableGateway.Update.cs:37-48` reloads the original and throws "Original record not found for
  update." Options: keep it, or report `ConcurrencyConflictException` (a deleted row is a
  concurrency conflict from the caller's point of view).
- [x] **D05 — `RowVersion` as `[Version]`: the two validators disagree.** *(ported from 3.0 `21ccbca`: `ValidateVersionType` accepts `RowVersion`; internal `IsOpaqueVersionColumn()` treats `byte[]` and `RowVersion` alike (no `+ 1`, WHERE-only); `SqlDialect.CreateDbParameter` binds a `RowVersion` as its bytes. Tests: `TableGatewayByteArrayVersionTests`; live `SqlServerRowVersionTests` (real `rowversion`, stale update → `ConcurrencyConflictException`).)* `TypeMapRegistry.cs:457`
  (`ValidateVersionColumn`) accepts `RowVersion`, but `:605` (`ValidateVersionType`, which also runs)
  rejects it, so registration throws. The UPDATE paths would also treat it as an integer (`SET v = v
  + 1`, invalid on SQL Server). Options: support it (port 3.0's `IsOpaqueVersionColumn` handling from
  `21ccbca`, as an internal helper — additive) or reject it cleanly (remove the dead acceptance and
  fix the `VersionAttribute` comment, which currently says `RowVersion` is accepted).

## Comment-only corrections

- [x] **C01 — `CommandPrepareMode`:** restore the caveat the audit dropped — a connection whose
  `Prepare()` fails stops preparing, for both `Auto` and `Always` (`SqlContainer.cs:1857-1899`).
- [x] **C02 — `DecimalHelpers.cs:25`:** the new "not currently called by library code" is false
  (`SqlDialect.cs:1441`, `AdvancedTypeRegistry.cs:384, 417`); revert to the old remark.
- [x] **C03 — `VersionAttribute`:** *(comment now states byte[]/RowVersion are compared, not incremented; docs updated.)* says `RowVersion` is accepted; resolve together with D05.

## Testbed and tooling

- [x] **T01 — Db2 not in the testbed.** *(fixed; stale duplicate container deleted. First live testbed run found the shared stored-proc check had no Db2 case — added, using 3.0's live-verified SQL PL. Db2: 23 passed, 0 failed, 5 skipped; also runs inside `IntegrationMatrixTests`.)* `testbed/ParallelTestOrchestrator.cs` has no Db2 entry in
  `CreateContainerAsync` or `GetTestConfigurations`, against its own POLICY comment; delete the stale
  duplicate `testbed/Db2TestContainer.cs`. **3.0:** wired.
- [x] **T02 — `Db2NativeLibraryBootstrap.Register()` never called.** *(fixed)* `testbed/Program.cs` must call it
  before `DbProviderFactoryFinder.FindAllFactories()`. **3.0:** calls it.
- [x] **T03 — Testbed registers `IAuditValueResolver` as scoped.** *(fixed there and in two integration-test hosts with the same registration)* `testbed/Program.cs:30`; CLAUDE.md
  requires singleton, and it's resolved from the root provider. **3.0:** same bug.
- [x] **T04 — `verify-novendor --allow`:** *(fixed; both forms accepted)* the usage text (`Program.cs:56`) shows `--allow "x;y"`, which
  the parser silently ignores (it only reads `--allow=`). Accept both forms. **3.0:** same.

## Investigated (tests written first; outcome per item)

- [x] **Versioned upsert — broken on 6 providers, not just SQLite.** A live stale-version upsert test
  (`MergeConflictTests.VersionedEntity_StaleUpsert_DetectsConflict`) found: PostgreSQL MERGE and
  CockroachDB/YugabyteDB ON CONFLICT rejected every versioned upsert of an existing row
  (`"version" = "version" + 1` is ambiguous with the MERGE source / `EXCLUDED`); Oracle rejected it
  (`ORA-02000`: no `WHEN MATCHED AND` in Oracle MERGE); SQLite and DuckDB silently let a stale
  upsert win. *(fixed, both gateways, single-row and batch: the MERGE increment reads `t.`;
  ON CONFLICT gets its own SET fragment (3.0's split, replacing the `s.`→`EXCLUDED.` string
  replace) with the increment qualified by the table; Oracle puts the check in
  `UPDATE ... WHERE` via internal `IInternalSqlDialect.MergeMatchedConditionAsUpdateWhere`;
  SQLite/DuckDB set `SupportsOnConflictWhere`. Unit: `UpsertVersionSqlTests`; live: all providers
  green, MySQL-family/Firebird documented as unable to detect.)* *Release note:* a stale-version
  upsert on SQLite/DuckDB now throws `ConcurrencyConflictException` instead of overwriting.
  **3.0:** the ON CONFLICT ambiguity (CockroachDB/YugabyteDB), Oracle `WHEN MATCHED AND` and
  SQLite/DuckDB detection are all still broken there.
- [x] **Oracle `RETURNING … INTO :1`** — worked only because ODP.NET binds by position and the OUT
  parameter came last; the dialect's own comment said it needs a named parameter. *(fixed: `INTO :o0`,
  shared constant `OracleDialect.ReturningParameterName`; live Oracle identity tests pass.)* **3.0:** same.
- [x] **`TypeCoercionHelper.ReadBytes`** — confirmed: a provider returning partial chunks produced a
  truncated, zero-padded array; `ReadGuidFromBytes` wrongly threw "does not contain 16 bytes".
  *(fixed: both read until complete; fakeDb gained `fakeDbDataReader.MaxBytesPerGetBytesCall` to
  simulate streaming providers.)* **3.0:** same.
- [x] **`Range<T>` writes to PostgreSQL were broken entirely**, not just `Empty`: the registry used
  `NpgsqlDbType` names that don't exist (`Int4Range`/`TsRange`), so ranges went out as text, which
  PostgreSQL rejects for a range column (the unit tests' mock enum used the same wrong names).
  *(fixed: ported 3.0's `IntegerRange`/`BigIntRange`/`TimestampRange`, `NpgsqlRange<T>` write,
  `Range<long>` mapping, YugabyteDB; plus `Range<T>.Empty` is now its own value written as
  `NpgsqlRange<T>.Empty`/`empty`, read back from `NpgsqlRange.IsEmpty`. New `IsEmptyRange`;
  `IsEmpty` unchanged (still true for unbounded). Live: `PostgreSqlRangeRoundTripTests` on
  PostgreSQL + YugabyteDB.)* *Release note:* `Range<T>.Empty` no longer equals `default`/`(,)`.
  **3.0:** still writes `Empty` as unbounded ("all values").
- [x] **Other intermittent failures under the combined net8.0+net10.0 run** — captured on 3.0:
  `PoolGovernorAsyncAcquireGapTests…ReaderWaitsOutBusyTurnstile…` failed with the test's own 10s
  `WaitAsync` timing out *before* the reader's equal 10s acquire deadline surfaced, i.e. thread-pool
  continuations were delayed past 10s — starvation from two test hosts on 8 cores, not a lost
  wakeup. That test and `ReusableAsyncLockerTests.LockAsync_Contended_WaitsUntilReleased` (5s)
  now use 60s safety-net timeouts. The suite doesn't raise `ThreadPool` minimum threads; doing so
  would cut these flakes but could also mask the next sync-over-async bug, so it is left as is.
- [x] **Flaky `PoolGovernorTurnstileTests…QueueExceedsMaxQueueDepth`** — test race: the occupier's 2s
  timeout could expire before a starved polling loop saw it. *(fixed in the test: dedicated thread,
  30s timeout, released at the end.)* 16 loaded combined runs: the other timing tests didn't fail.
- [x] **`ConvertWithCache` "returns a default"** — not a bug: it throws `InvalidCastException`. The
  zeros came from `DataReaderMapper`'s non-Strict mode (logs, leaves the default — documented).
  Regression test added.
- [x] **`PostgreSqlIntervalCoercion.TryWrite` drops months** — not reachable: only
  `ProviderParameterFactory` (dead on 2.0.6) calls it; a provider without an interval mapping gets
  the value object itself. Pinned by a test; moved to dead code.
- [x] **`PrimaryKeyTableGateway` ignores `loadOriginal`** — by design (CLAUDE.md: "exists for API
  symmetry but is always ignored"); the interface docs promised a reload. *(docs and TODO fixed,
  pinned by a test.)* Implementing it would change behavior for callers passing `true` — 3.0 decision.

- [x] **Cancellation during BeginTransaction was wrapped in `TransactionException`** — breaks the
  "`OperationCanceledException` is never wrapped" rule; found as an intermittent 3.0 failure
  (`RetryContextTransactionalExecutionTests…CancellationDuringBackoff…`), timing-dependent. *(fixed on
  both begin paths: gate and connection are still released, then the cancellation is rethrown as-is.
  Test: `TransactionBeginCancellationTests`.)* **3.0:** same bug, fixed there too.

## Decisions needed (found while investigating)

- [x] **D06 — `DbMode.SingleConnection`: a plain read during another task's open transaction fails on
  real SQLite.** *(decided: reject the read. `DatabaseContext.ThrowIfSingleConnectionTransactionOpen` — set while a transaction holds the single-connection gate — makes such a read throw `InvalidOperationException`; otherwise an ordinary reader holds the gate for its lifetime, so a transaction can't begin underneath it. Only reads are rejected: a write issued through the reader path (e.g. the compound `INSERT; SELECT` create) waits for the transaction like any other write (found porting to 3.0; `SingleConnectionConcurrentTransactionGuardTests.OrdinaryWrite_ViaExecuteReaderAsync…`). Live SQLite showed the problem is also silent: a command created while a transaction is open is auto-enlisted in it. Tests: `SingleConnectionReadDuringTransactionTests` (incl. real SQLite); `TransactionStreamingTests`/metrics tests read through the transaction (as on 3.0); torture test accepts the rejection. *Release note:* in SingleConnection mode, reading through the context while a transaction is open now throws — read through the transaction.)* Reads deliberately skip the single-connection transaction gate (so code can read
  through the plain context while its own transaction is open), but Microsoft.Data.Sqlite rejects a
  command without its `Transaction` set while one is pending: "Execute requires the command to have a
  transaction object…". This is the intermittent `SingleConnectionConcurrencyTortureTests` failure
  (reproduced, message captured); fakeDb doesn't enforce the rule. 3.0 (`81eef2b`) makes reads hold the
  gate for the reader's lifetime and changed the streaming tests to read through the transaction —
  but then a plain read from the flow that owns the open transaction waits on the gate (forever when
  `ModeLockTimeout` is null). Alternative: attach the open transaction to such reads.

## Still open

- **Batch update keys on `[PrimaryKey]`, single-row update on `[Id]`.** 3.0 changed this (`bbb2ef8`);
  changing it on 2.0.6 would break callers — leave unless a bug report forces it.
- **`Range<T>.IsEmpty` is true for an unbounded range** — kept for compatibility; 3.0 should make it
  mean "empty" only.
- **Dead code** (candidates for removal on 3.0, not 2.0.6): `ProviderParameterFactory`'s
  `NpgsqlDbType` numbers are wrong and its Oracle Guid branch is broken, but nothing on 2.0.6 calls
  it (it is **live and still wrong on 3.0** — fix there urgently); `PostgreSqlIntervalCoercion.TryWrite`
  (drops months); `TableGateway.BuildUpdateByKey` and helpers, `SqlContainer.Reset()`,
  `DataReaderMapper.CoerceValue`/`TryHandleEnumFailure`, the Snowflake branch in
  `PostgresExceptionTranslator`, the Oracle `PrefetchSequence` branch in
  `SqlDialect.GetGeneratedKeyPlan`, and a duplicated enum-converter block in `TypeMapRegistry`.
- **`docs/FUTURE_WORK.md`** describes 3.0 work (e.g. `VersionedUpsertConflictTests.cs`) as if it were on
  this branch — a planning doc, left as is.

## 3.0 follow-ups found during this work

- Same bugs still on 3.0: B01, B05, B09, B10, B11, B12, B13, T03, T04; enum names for
  `AnsiString`/fixed-length columns; isolation fail-up (profiles degrade with a warning, and an
  unsupported explicit level throws instead of resolving up); `ProviderParameterFactory` numbers
  (live on 3.0).
- Intervals: 3.0 writes `ToTimeSpan()` (drops months) and can't read an interval with months
  (Npgsql throws reading it as `TimeSpan`); port B06's `NpgsqlInterval` write and
  `IntervalFieldReader` read. `docs/advanced-types.md` on 3.0 also shows `DbType.String` for
  value-object columns (use `DbType.Object`).
- `ConnectionStringNormalizationCache` on 3.0 hashes only the connection string, not the
  read-only key/value, application name and suffix that shape the cached map — port B08's key.
- 3.0's `Uuid7OptimizedTests.NewUuid7*_ThrowsWhenCounterExhausted_*` are racy: a millisecond tick between
  setup and the call resets the counter so nothing throws. 2.0.6 pins `LastMs` a minute ahead; also put the
  `Uuid7Optimized` static-state test classes in one serial collection (`Uuid7StaticStateSerial`).
- `TotalConnectionsReused`: remove. `ConnectionPoolEfficiency` is computed from it (reused ÷
  created), so it is always 0 on both branches too — decide whether it goes with it.
- Stale comments that 2.0.6 has corrected but 3.0 still carries: the Oracle, PostgreSQL,
  CockroachDB and Snowflake dialect headers, Spanner's "EVERY constraint violation", SQL Server's
  "forced ON", and several abstractions docs (`IMapperOptions`, `DbMode`, `ModeLockTimeout`, the
  `BuildUpdateAsync` reload note).
