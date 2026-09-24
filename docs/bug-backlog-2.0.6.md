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
- [x] **Enum names for every string column type** — `ColumnInfo.MakeParameterValueFromField` only
  checked `DbType.String`, so `AnsiString`/fixed-length enum columns stored the number. *Release
  note:* rows written before the fix keep their numeric text; reads accept both. 3.0 has the same
  bug (see 3.0 follow-ups).
- [x] **`TotalConnectionsReused` marked `[Obsolete]`** — never incremented; always 0.

## Fix — no caller-visible break

- [x] **B01 — SQL numbers use the current culture.** *(fixed; also `Append(object)` and `AppendFormat(null, …)`)* `SqlQueryBuilder.cs:110-175`:
  `Append(int/long/double/decimal)` and `AppendFormat(string, …)` format with
  `CultureInfo.CurrentCulture`, so on `de-DE` `Append(1.5m)` writes `1,5` (invalid SQL); some ICU
  cultures emit U+2212 for negatives. The library's own paging goes through `Append(int)`.
  **Fix:** `CultureInfo.InvariantCulture`; update the `ISqlQueryBuilder` XML docs, which currently
  state "current culture". **3.0:** same bug.
- [x] **B02 — Aurora PostgreSQL and Spanner lose generated keys.** *(fixed; live Spanner check pending)* `dialects/SqlDialect.cs:2895-2911`
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
- [ ] **B09 — `JsonValue.AsElement` disposes the caller's document.**
  `types/valueobjects/JsonValue.cs:97-106`: `using var doc = AsDocument()` disposes the caller-owned
  `JsonDocument`; later `AsString()`/`ToObject()` throw `ObjectDisposedException`. **Fix:** return
  `_document.RootElement.Clone()`. **3.0:** same bug.
- [ ] **B10 — SQL Server spatial: SRID lost on read, GeoJSON sent to `STGeomFromText`.**
  `types/converters/SpatialConverter.cs:240-256` computes `STSrid` and never uses it (and
  `Convert.ToInt32(SqlInt32)` would throw; `STAsBinary()` returns `SqlBytes`). `:156-167` passes
  GeoJSON text to `STGeomFromText`; `SqlBytes`/`SqlChars` are looked up in the wrong namespace
  (`:136-137`, unverified). **Fix:** use the SRID; WKT/WKB only for SQL Server, clear error for
  GeoJSON-only values; fix the type lookups. **3.0:** same bugs.
- [ ] **B11 — Range literal `empty` doesn't parse.** `types/converters/PostgreSqlRangeConverter.cs:188`
  and `Range<T>.Parse`: PostgreSQL's canonical empty range throws. **Fix:** special-case `empty`.
  **3.0:** same bug. (Low severity.)
- [ ] **B12 — fakeDb `SetFailOnOpen(skipFirstOpen: true)` ignored.** `fakeDbConnection.cs:68, 428-433`:
  `_skipFirstFailOnOpen` is stored and never read. **Fix:** honor it in `Open`. **3.0:** same.
- [ ] **B13 — `_singleConnectionTransactionGate` never disposed.** `DatabaseContext.cs:131`; its sibling
  gates are disposed. **Fix:** dispose in both dispose paths; the transaction release path already
  tolerates `ObjectDisposedException`. **3.0:** same. (Hygiene.)

## Fix — changes observable behavior (decide first)

- [ ] **B20 — Batch update overwrites audit/version columns and never checks the version.**
  `TableGateway.Batch.cs:329-344` (`GetCachedUpdateableColumns`) includes `CreatedBy`, `CreatedOn` and
  the `[Version]` column, so a multi-row UPDATE writes creation audit from the in-memory entity and
  copies the client's version verbatim (no `+ 1`, no `WHERE version = …`) — silent lost updates. No
  rows-affected check on any batch path. **Proposed:** (1) exclude those three columns — no break;
  (2) route `[Version]` entities through the per-row update, which increments and checks — no break;
  (3) throw `ConcurrencyConflictException` when a versioned row isn't updated — **breaks callers:
  yes** (silent data loss becomes an exception, which is the documented `[Version]` contract).
  **3.0:** fixed (`5e6b244`, `21ccbca`, `91225cd`).
- [ ] **B21 — `CountWhereEqualsAsync` silently ignores `andWhereNotNull` when both flags are set.**
  `BaseTableGateway.Count.cs:82-85`; the audit wrote the precedence into the `ITableGateway` /
  `IPrimaryKeyTableGateway` docs. **Options:** throw `ArgumentException` like 3.0 (**breaks callers
  who pass both**, whose counts are already wrong) or apply both predicates (no break). Either way,
  restore the "at most one may be set" doc. **3.0:** throws (`7ca59ba`).
- [ ] **B22 — `Uuid7Options.FailFastOnBurst` has no effect.** `Uuid7Optimized.cs:67-74, 220-258`:
  `NewUuid7` always blocks on burst exhaustion. **Proposed:** honor it only when set explicitly —
  `DefaultsFor(PtpSynced)` currently turns it on, so honoring the default would make PtpSynced users
  start seeing exceptions (**breaks callers: yes** unless the default is changed). **3.0:** throws
  `InvalidOperationException` (`fb11369`).
- [ ] **B23 — Command on a transaction while its own reader is open hangs.**
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
- [ ] **D05 — `RowVersion` as `[Version]`: the two validators disagree.** `TypeMapRegistry.cs:457`
  (`ValidateVersionColumn`) accepts `RowVersion`, but `:605` (`ValidateVersionType`, which also runs)
  rejects it, so registration throws. The UPDATE paths would also treat it as an integer (`SET v = v
  + 1`, invalid on SQL Server). Options: support it (port 3.0's `IsOpaqueVersionColumn` handling from
  `21ccbca`, as an internal helper — additive) or reject it cleanly (remove the dead acceptance and
  fix the `VersionAttribute` comment, which currently says `RowVersion` is accepted).

## Comment-only corrections

- [ ] **C01 — `CommandPrepareMode`:** restore the caveat the audit dropped — a connection whose
  `Prepare()` fails stops preparing, for both `Auto` and `Always` (`SqlContainer.cs:1857-1899`).
- [ ] **C02 — `DecimalHelpers.cs:25`:** the new "not currently called by library code" is false
  (`SqlDialect.cs:1441`, `AdvancedTypeRegistry.cs:384, 417`); revert to the old remark.
- [ ] **C03 — `VersionAttribute`:** says `RowVersion` is accepted; resolve together with D05.

## Testbed and tooling

- [ ] **T01 — Db2 not in the testbed.** `testbed/ParallelTestOrchestrator.cs` has no Db2 entry in
  `CreateContainerAsync` or `GetTestConfigurations`, against its own POLICY comment; delete the stale
  duplicate `testbed/Db2TestContainer.cs`. **3.0:** wired.
- [ ] **T02 — `Db2NativeLibraryBootstrap.Register()` never called.** `testbed/Program.cs` must call it
  before `DbProviderFactoryFinder.FindAllFactories()`. **3.0:** calls it.
- [ ] **T03 — Testbed registers `IAuditValueResolver` as scoped.** `testbed/Program.cs:30`; CLAUDE.md
  requires singleton, and it's resolved from the root provider. **3.0:** same bug.
- [ ] **T04 — `verify-novendor --allow`:** the usage text (`Program.cs:56`) shows `--allow "x;y"`, which
  the parser silently ignores (it only reads `--allow=`). Accept both forms. **3.0:** same.

## To investigate

- **Intermittent timing failures, only when net8.0 and net10.0 run in parallel:**
  `SingleConnectionConcurrencyTortureTests.MixedReadWriteTransactionLoad_SerializesCorrectly_RealSqliteSingleConnection`
  and `PoolGovernorSyncAcquireTests.Acquire_SlotBusyThenReleasedWithinTimeout_SucceedsViaTimedSemaphoreWait`
  each failed once in a combined run and passed alone and in later full runs. Capture the failure
  message next time; likely timing margins under double load.
- **`PostgreSqlIntervalCoercion.TryWrite` writes `value.ToTimeSpan()`**, dropping months, for any
  provider without an `AdvancedTypeRegistry` interval mapping (the PostgreSQL family has one, so it
  isn't affected).
- **`TypeCoercionHelper.ConvertWithCache` returns a default value** when it can't convert a struct,
  instead of failing — this is how `DataReaderMapper` produced all-zero intervals during B06. Silent
  defaults hide conversion bugs.
- **Oracle `RETURNING … INTO`:** the gateway's SQL says `:1` but the output parameter is named `o0`;
  it only works through positional binding. Same on 3.0. Check against live Oracle.
- **SQLite `[Version]` upserts get no concurrency check:** `SqliteDialect` doesn't set
  `SupportsOnConflictWhere`.
- **`TypeCoercionHelper.ReadBytes`** doesn't check how many bytes `GetBytes` returned.
- **`PrimaryKeyTableGateway.BuildUpdateAsync(entity, loadOriginal, …)`** ignores `loadOriginal`
  (TODO at `PrimaryKeyTableGateway.Update.cs:31-38`) although the interface exposes it.
- **Batch update keys on `[PrimaryKey]`, single-row update on `[Id]`.** 3.0 changed this (`bbb2ef8`);
  changing it on 2.0.6 would break callers — leave unless a bug report forces it.
- **Dead code** (candidates for removal on 3.0, not 2.0.6): `ProviderParameterFactory`'s
  `NpgsqlDbType` numbers are wrong and its Oracle Guid branch is broken, but nothing on 2.0.6 calls
  it (it is **live and still wrong on 3.0** — fix there urgently); `TableGateway.BuildUpdateByKey`
  and helpers, `SqlContainer.Reset()`, `DataReaderMapper.CoerceValue`/`TryHandleEnumFailure`,
  the Snowflake branch in `PostgresExceptionTranslator`, the Oracle `PrefetchSequence` branch in
  `SqlDialect.GetGeneratedKeyPlan`, and a duplicated enum-converter block in `TypeMapRegistry`.

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
- `TotalConnectionsReused`: remove. `ConnectionPoolEfficiency` is computed from it (reused ÷
  created), so it is always 0 on both branches too — decide whether it goes with it.
- Stale comments that 2.0.6 has corrected but 3.0 still carries: the Oracle, PostgreSQL,
  CockroachDB and Snowflake dialect headers, Spanner's "EVERY constraint violation", SQL Server's
  "forced ON", and several abstractions docs (`IMapperOptions`, `DbMode`, `ModeLockTimeout`, the
  `BuildUpdateAsync` reload note).
