# Future Work

This document tracks features that have been designed or partially specified but are not yet
implemented. Items here are not roadmap commitments — they are recorded so the design thinking
is not lost and can be picked up when the need arises.

---

## Testbed skip audit (2026-09-25, branch 2.0.6)

**Rule:** a testbed skip is legitimate **only** when the database genuinely lacks the
capability being tested (e.g. no stored procedures, no savepoints). Anything else — a hardcoded
per-database list, a driver quirk the library could work around, a dialect flag that under-reports
what the engine can do, or "no test written for X" — is a gap to fix, not a skip.

Every skip must be gated on a real capability (an `ISqlDialect` / `IDataSourceInformation`
property or the isolation resolver), never on a `SupportedDatabase` switch in the testbed.

Baseline run (net8.0 and net10.0 identical): 19/19 databases, 378 checks passed, 0 failed,
**56 skipped**. After SKIP-001/002: **24 skipped**. After all items: **434 checks, 0 failed, 10 skipped** (net8.0 and net10.0), every one a capability skip. Skip output is now prefixed `[SKIP:<Product>]` so every skip is attributable. Work each item TDD-first (unit test red → fix → testbed re-run), then mark it
done here with the commit.

### Legitimate capability skips (keep)

| Skip | Databases | Why legitimate |
|---|---|---|
| `[StoredProc]` | SQLite, DuckDB, TiDB, Spanner | Engines have no stored procedures |
| `[Capabilities] Paging` | Sybase ASE | Engine has no row-skipping paging form (SKIP-012) |
| `[ParamBinding] Binary equality` | InterBase, SAP HANA, Informix | Engines cannot compare BLOB/BYTE values |
| `[RoundTrip] Trailing whitespace` | Sybase ASE, Informix | `PreservesTrailingWhitespace = false` (engine strips / provider trims; SKIP-006) |
| `[ExtendedTx] Savepoints` | Spanner, DuckDB | Engines have no savepoints (DuckDB verified live, SKIP-010) |

### Items to fix

| ID | Skip(s) | Databases | Problem | Planned fix | Status |
|---|---|---|---|---|---|
| SKIP-001 | `[ParamBinding] Guid binding`, `[RoundTrip] Guid` | MySQL (+Percona), MariaDB, TiDB, Db2, Sybase ASE, Spanner | Gated on a hardcoded `SupportsGuidBinding(product)` list; the library owns GUID storage, so not a capability. | Removed the list; every database now runs both checks with a real column type (`GetGuidType`). Sybase ASE: the testbed first used `CHAR(36)`, where AdoNetCore.AseClient writes a `DbType.Guid` as 16 raw bytes-as-characters; switching the dialect to `GuidStorageFormat.String` was then found (live, against 2.0.5 behavior) to **break `BINARY(16)`/`VARBINARY(16)` columns that 2.0.5 handled correctly**, so it was reverted: Sybase stays pass-through and the testbed uses `BINARY(16)`. | **Done** (live: all databases pass) |
| SKIP-002 | `[ParamBinding] DateTimeOffset binding`, `[RoundTrip] DateTimeOffset` | MySQL (+Percona), MariaDB, TiDB, Db2, Sybase ASE, Spanner, SQLite, Firebird | Gated on a hardcoded `SupportsDateTimeOffsetBinding(product)` list. | Removed the list; every database now runs both checks (`GetDateTimeOffsetType`, UTC-instant storage where the engine has no offset type). Exposed a real bug: `SybaseAseDialect` passed `DateTimeOffset` straight to AseClient, which rejects it → now coerced to a UTC `DateTime` like Db2/Firebird/InterBase (unit-tested). | **Done** (live: all databases pass) |
| SKIP-003 | `[StoredProc]` | Informix | Dialect reported `ProcWrappingStyle.None`. | New `ProcWrappingStyle.Informix` (`InformixProcWrappingStrategy`): `EXECUTE PROCEDURE name(args)`, parentheses always, reads and writes alike. Informix documents `EXECUTE PROCEDURE`/`EXECUTE FUNCTION` as the stand-alone statements and `CALL` as SPL-only; a first pass used the existing `Call` style because 15.0 happens to accept a top-level `CALL`, and was corrected to the documented form. Verified live (15.0.1.0.3): `EXECUTE PROCEDURE` runs procedures with and without `RETURNING` and `CREATE FUNCTION` routines; `EXECUTE FUNCTION` cannot run a no-return procedure; `SELECT * FROM name(args)` and a parenthesis-less `EXECUTE PROCEDURE name` are syntax errors. The testbed creates and calls an Informix SPL procedure (read and write, one positional arg). | **Done** (live) |
| SKIP-004 | `[Capabilities] Upsert` | Informix | `SupportsMerge => false`. | Verified live: `MERGE INTO t USING (SELECT CAST(? AS type) AS c, ... FROM sysmaster:sysdual) s ON ... ` works; the `USING (VALUES ...)` shape and untyped `? AS c` are syntax errors. Added `RenderMergeSource` + a live-verified `DbType`→cast map (`GetMergeSourceCastType`; unverified types throw), `UpsertIncomingColumn`. Informix MERGE has no conditional matched clause (`WHEN MATCHED AND`/`UPDATE ... WHERE` both fail), so upsert of a `[Version]` entity now throws `NotSupportedException` in both gateways (new internal `SupportsMergeMatchedCondition`). | **Done** (live) |
| SKIP-005 | `[Capabilities] Paging` | Informix | Dialect claimed no paging. | Verified live: `SELECT SKIP n FIRST m` (and `FIRST m`, ahead of `DISTINCT`) works; OFFSET/FETCH and `LIMIT m OFFSET n` fail. `AppendPaging` override inserts SKIP/FIRST after the leading SELECT. New `ISqlDialect.SupportsPaging` (default `SupportsOffsetFetch \|\| SupportsLimitOffset`, binary compatible) gates the testbed check. | **Done** (live) |
| SKIP-006 | `[ParamBinding] Type matrix`, `[RoundTrip] Fidelity` | Informix | Skipped wholesale, blamed on TEXT/BYTE binding. | Verified live: the testbed's binary column fell through to `BLOB` (smart LOB: rejects a plain binary parameter and needs an sbspace); a `BYTE` column binds `DbType.Binary` and round-trips byte-for-byte. Both checks now run. They exposed a real library bug: Informix.Net.Core has no `DbType.DateTimeOffset` mapping, so `InformixDialect` now coerces to a UTC `DateTime` (unit-tested). Remaining genuine limits, now narrow capability skips: `BYTE` values cannot be compared (`WHERE b = ?`: "Blobs are not allowed in this expression"), and trailing blanks don't round-trip (new `ISqlDialect.PreservesTrailingWhitespace`: false for Informix, whose .NET provider trims them on read with no option to stop, per IBM APAR IC63704; and for Sybase ASE, whose engine strips them on storage. Sybase used to be exempted silently by a product check). | **Done** (live) |
| SKIP-007 | `[Quoting] 'user' column` | Informix | Hardcoded product skip. | Verified live: unqualified `"user"` resolves to the USER special register, but alias-qualified `"q"."user"` resolves to the column. The check now uses the qualified form on every database; no skip. | **Done** (live) |
| SKIP-008 | ~~`[ExtendedTx] Savepoints`~~ | ~~Sybase ASE~~ | Misattributed in the first pass: `SybaseAseDialect` already has `SupportsSavepoints => true`, and Sybase runs the savepoint checks. The savepoint skips are DuckDB (SKIP-010), Spanner (legitimate) and Informix (SKIP-013). | — | **Void** |
| SKIP-009 | `[ParamBinding] Duplicate param` | Informix (not Sybase; misattributed in the first pass) | The check used `MakeParameterName()` twice, which is a bare `?` on positional providers, so it could only work where the driver resolves repeated names. | The library already supports a repeated logical parameter portably via the `{P}name` token (one marker + one bound copy per use on positional providers, deduplicated names on Oracle). The check now uses `{P}name` with a row reachable only through the second use, and runs on every database; Oracle's override (which used two different names and recorded no check) was removed. Pinned by a unit test; documented in `docs/parameter-naming-convention.md`, CLAUDE.md, llms-full.txt and all three skill trees. | **Done** (live: 19/19) |
| SKIP-010 | `[ExtendedTx] Savepoints` | DuckDB | The dialect comment gave a driver-reliability reason. | Verified live on DuckDB 1.3.2 and 1.5.5: `SAVEPOINT`/`ROLLBACK TO SAVEPOINT`/`RELEASE SAVEPOINT` are parser errors, so the engine lacks the capability. Comment corrected. | **Legitimate** |
| SKIP-011 | `[InvalidTxType]` | MySQL, MariaDB, SQL Server, Sybase ASE, Informix, Db2, Spanner | Gated on a hardcoded `_context.Product switch` of known-unsupported levels. | Replaced with `GetSupportedIsolationLevels()`: every unsupported standard level is now checked against the documented fail-up contract (weakest supported level at least as strong, else rejected). Skips only if the engine supports every level. | **Done** (live: 0 `[InvalidTxType]` skips; 406 checks, 16 skipped) |
| SKIP-012 | `[Capabilities] Paging` | Sybase ASE | Dialect reports no paging. | Verified live on ASE 16.0 SP02: `LIMIT/OFFSET`, `OFFSET ... FETCH`, `ROWS LIMIT` and `ROW_NUMBER() OVER` are all syntax errors; only `TOP n` exists, which cannot skip rows. | **Legitimate** |
| SKIP-013 | `[ExtendedTx] Savepoints` | Informix | Inherited `SupportsSavepoints => false`. | Verified live: base `SAVEPOINT`/`ROLLBACK TO SAVEPOINT`/`RELEASE SAVEPOINT` SQL works with quoted names. `SupportsSavepoints => true`. | **Done** (live) |
| SKIP-014 | (found while fixing SKIP-002) | Firebird 4+ | A `DateTimeOffset` could not be written to a `TIMESTAMP WITH TIME ZONE` column, and such columns read back as `FbZonedDateTime`, which the mapper could not convert. 3.0 only documents this as a driver constraint. | Verified live (Firebird 5.0.4, FirebirdClient 10.3.3): the driver rejects `DbType.DateTimeOffset`, but an `FbZonedDateTime(utcInstant, "UTC")` sent with `DbType.Object` is encoded against the server-described column type, so a zoned column stores the instant and a plain `TIMESTAMP` column stores the UTC wall time (byte-for-byte the previous behavior, under any session time zone). `FirebirdDialect` now sends that on Firebird 4+ (reflection via `FirebirdZonedDateTimeInterop`; falls back to the UTC-`DateTime` coercion on Firebird 3, before detection, or without the driver type). Re-created parameters stay zoned (the driver reports them as `Binary`; that broke cached-template cloning, found live). `TypeCoercionHelper` maps `FbZonedDateTime` reads to `DateTimeOffset`/UTC `DateTime`. The testbed covers both column types through a gateway. | **Done** (live) |
| SKIP-015 | (found while fixing SKIP-007) | Informix | Unqualified register-named columns in generated SQL (`user`, `today`, `current`, `sitename`, ...) resolved to the special register: `DELETE FROM "t" WHERE "user" = ?` deleted **every row** when the value equalled the session user name (verified live), and unaliased retrieves returned the register value. | New internal `QualifiesColumnReferences` (Informix only): the gateways table-qualify every column reference inside an expression (UPDATE/DELETE predicates, the version-increment right-hand side, unaliased SELECT lists and WHERE clauses, count helpers). SQL for every other database is unchanged. Pinned by `InformixSpecialRegisterColumnTests` (14 of 17 go red with the capability off) and a live testbed check on every database. | **Done** (live) |
| SKIP-016 | `[Spanner.GeneratedAlwaysIdentity]` | Spanner | Skipped because Spanner has no `GENERATED ALWAYS` (so no `OVERRIDING SYSTEM VALUE` path). | The caller-facing behavior (upsert with a client-supplied id into an identity column, then update by upsert) is now tested against Spanner's `GENERATED BY DEFAULT` identity instead. | **Done** (live) |
| HARN-001 | Integration harness | all | `DatabaseTestBase` turned an enabled provider that failed to start into skipped tests (all failed) or silently dropped it (some failed), so a broken SQL Server could still report a green run. `CommandTimeoutTests`/`SerializationConflictTests` did the same for their standalone containers. | Enabled-provider failures now fail the test (`EnsureProvidersInitialized`, unit-tested); only configuration exclusions skip. This exposed that SQL Server had been silently dropped from full runs. | **Done** |
| HARN-002 | Integration harness | SQL Server | SQL Server could not start within its wait under full-suite load, because six test classes that start their own containers (including `IntegrationMatrixTests`, the whole testbed matrix) had no `[Collection]` and ran in parallel with the fixture's startup. | They now share `StandaloneContainerCollection` (`DisableParallelization = true`), which runs alone after the parallel collections. SQL Server's startup wait was also raised to 180s in the testbed and in `CommandTimeoutTests`. | **Done** |
| HARN-003 | testbed | Sybase ASE | Intermittent "The model database is unavailable" at setup: ASE rebuilds tempdb from model on boot, and `CREATE DATABASE` could race it right after the SIGSEGV-workaround restart. | `CREATE DATABASE` is retried while model is busy (120s deadline). | **Done** |
| HARN-004 | CI coverage | all | 2.0.6's `deploy.yml` never runs `pengdows.crud.IntegrationTests`: the unit step filters it out and the integration step runs only the testbed. So integration tests (including the ported Firebird Embedded and live-provider tests) only run locally via `run-integration-tests.sh`. | Decide whether CI should run the integration project (about 11 minutes, Docker) and, if so, add the `scripts/install-firebird-embedded.sh` step before it (3.0 has that step). | Open (decide) |
| HARN-005 | Integration fixture coverage | Db2, Informix, SybaseASE, Spanner, SingleStore | `IntegrationTestFixture.BaseProviders` never starts these, so every `pengdows.crud.IntegrationTests` class excludes them by configuration, although `ParallelTestOrchestrator.CreateContainerAsync` already starts Db2/Informix/SybaseASE/Spanner always-on for the testbed. Only capability skips are legitimate. | Add them to `BaseProviders`, give each test class's setup DDL for them, fix what fails in the library. SingleStore first needs a 2.0.6 testbed container (3.0 has one). | Open |

## 3.0 → 2.0.6 backport audit (2026-09-25, branch 2.0.6)

**Question audited:** does 2.0.6 contain every non-breaking change from `3.0`, and does it enforce
(rather than remove) everything 3.0 removed as "should never have been public"? **Answer: no, not
yet.** This section is the work list.

**Method:** `origin/3.0` (f6f56bf) vs `2.0.6` (8adcf3e), forked at 2.0.5 (c883579). 396 commits are
3.0-only; 196 of them touch shipped code (`pengdows.crud`, `.abstractions`, `.fakeDb`, analyzers).
Each of the 196 was classified against the actual 2.0.6 code (not commit messages): about 85 are
already present (cherry-picked or hand-ported), about 45 have no 2.0.6 effect (docs, dead code,
RetryContext internals, reverted work), about 20 are inherently 3.0-only (breaking), and the rest are
listed below. The public-API diff was also computed with ApiCompat in both directions.

**Rules for every item:**
- 2.0.6 must stay binary compatible with 2.0.5. Package validation enforces this on `dotnet pack`.
  A new interface member needs a default implementation; no public signature may change or disappear.
- TDD: re-verify the claim with a red test before fixing. The classifications came from triage and
  are evidence, not proof.
- Port the matching 3.0 integration/testbed test together with each fix.
- Before changing any stored/wire format, probe the previous release's behavior live for every
  natural column type (see the Sybase Guid lesson: a format change broke `BINARY(16)` users and was
  reverted).
- RetryContext (4a0d8ec and follow-ups) is **out of scope** for 2.0.x (maintainer decision).

### Enforcement (things 3.0 removed or locked down)

| ID | Item | 3.0 commit | Plan | Status |
|---|---|---|---|---|
| BP-E01 | `pengdows.crud.tenant.ITenantConfiguration`: empty public marker, nothing implements or consumes it | b96ea22 (made internal) | Added to PGC027 `BlockedTypes` + `[Obsolete(false)]` (analyzer test `TenantConfigurationMarker_IsReported`) | **Done** |
| BP-E02 | `AuditCreationPolicy` setter on `ITableGateway`/`IPrimaryKeyTableGateway`/`BaseTableGateway`: mutable shared state on a singleton gateway | 6f01a9a (`init`) | Now `init` on all three (the interfaces keep a default body). New in 2.0.6, so no 2.0.5 impact (package validation clean). Pinned via the `IsExternalInit` modreq test ported from 3.0 | **Done** |
| BP-E03 | `IDatabaseContextConfiguration.SessionInitializationFailureMode` is non-nullable on 2.0.6, nullable on 3.0 | ba8268f (CORE-039) | Now `SessionInitializationFailureMode?`, default `null`, matching 3.0's type. On 2.0.x `null` still means BestEffort for **every** context (2.0.5 behavior); 3.0's FailClosed-for-read-only default stays 3.0-only (behavior change). Tests pin both | **Done** |
| — | `TransactionModeNotSupportedException` | f045677 (removed) | **Keep.** Still thrown on 2.0.6 (`IsolationResolver.cs:76,84`), so blocking it would be wrong | Decided |
| — | DataSource, SqlStandardLevel family, EphemeralSecureString, ConnectionLocalState, ILockerAsync, TypeCoercionOptions, EnumStorage, inert attributes, ReadWriteMode/ProcWrappingStyle setters | various | Already `[Obsolete]` + PGC027 on 2.0.6 | Done |

### Tier 1: bug fixes, no public-API change (low risk)

| ID | Bug on 2.0.6 | 3.0 commit | Status |
|---|---|---|---|
| BP-101 | **Security:** `DbProviderLoader` symlink escape: assemblies can be loaded from outside the provider directory (`DbProviderLoader.cs:206-222`) | 4399d3a | **Done** (e253343): real-symlink red test |
| BP-102 | Reader plan cache keyed by a bare 64-bit hash, so a collision reuses the wrong mapper (`BaseTableGateway.Core.cs:111`, `DataReaderMapper.cs:142`) | 4399d3a, 350e437 (RecordsetShape key) | **Done** (16f4ebc): internal `RecordsetShape` key; red via a real in-process hash collision |
| BP-103 | `TrackedConnection` dispose retries with an unbounded `WaitAsync()`, so Dispose can hang forever (`TrackedConnection.cs:559`) | bbb2ef8 | **Done** (bb24803) |
| BP-104 | `PoolGovernor.TryAcquire/TryAcquireAsync` leak turnstile interest on a pre-cancelled token (no try/catch, `PoolGovernor.cs:410-470`) | 5e6b244 | **Done** (c2142c6) |
| BP-105 | `TenantContextRegistry`: `Invalidate` racing `Dispose` can double-dispose and double-fire `ContextRemoved` (no `TryClaimDisposal`) | bbb2ef8 | **Done** (93df999): `ContextRemoved` fired twice before the fix |
| BP-106 | `TransactionException` drops the inner exception's `IsTransient` at commit/rollback (`TransactionContext.cs:858/909`), so a commit deadlock looks non-transient | 923a02b (IsTransient part only; `Phase` would need a new ctor overload) | **Done** (2454128) |
| BP-107 | `CommitAsync`/`RollbackAsync` call the sync `_transaction.Commit()/Rollback()`, ignoring the token and provider async (`TransactionContext.cs:624-647`) | 317d303 (TransactionContext part) | **Done** (e725e0c) |
| BP-108 | `ParseVersion` mis-parses 5-part versions: Oracle "23.26.2.0.0 … 26ai" becomes major 26, breaking version gates (`SqlDialect.cs:2381-2393`) | 5d80b5c | **Done** (4d2db38): on 2.0.6 the parse returned null, not 26; truncates to 4 parts |
| BP-109 | SingleConnection + `FailClosed`: a session-settings failure during construction leaks the PersistentConnection (`Initialization.cs:409`) | 91225cd | **Done** (870c59f) |
| BP-110 | `TrackedReader.Close()` doesn't release the lease; reader dispose stops at the first failure; `CreateSqlContainer` after Dispose isn't guarded; data sources are disposed even when a governor fails to drain | c5083b0 | **Done** (ee0f710): 4 sub-bugs, each red first. 2.0.6 has no unique-connection-string registry, so (d) covers data sources only |
| BP-111 | PostgreSQL HStore round-trip broken on Npgsql 9 (writes a string with `Hstore` type; reads only strings) (`ProviderParameterFactory.cs:142`) | 11c3c6c | **Done** (live PG red→green) |
| BP-112 | PostgreSQL MacAddr8: always sent as `MacAddr`, so 8-byte EUI-64 values fail | 1187d9d + rest of cb8a514 | **Done** (live PG): every PG MacAddress write failed on 2.0.5/2.0.6, so no format to preserve |
| BP-113 | Firebird DDL resets only the writer pool; reader connections can see stale metadata (`SqlContainer.cs:1168`) | 7e87fad | **Done**: unit red; the live DDL failure did not reproduce on 2.0.6 |
| BP-114 | SingleStore: SAVEPOINT rollback is a silent no-op (data meant to be discarded survives); FK/UNIQUE/CHECK DDL rejected; PREPARE mis-parses (missing `MySqlDialect` SingleStore overrides) | f92350b | **Done** (no SingleStore container on 2.0.6) |
| BP-115 | TiDB `NO_BACKSLASH_ESCAPES` in session settings corrupts strings/JSON with MySql.Data | 81eef2b | **Done** (live): also MySQL/AuroraMySql/MariaDB/SingleStore on MySql.Data (3.0 has the MySQL half of this bug, hidden by its MySqlConnector test container). 2.0.5 had identical flags, so this is a fix, not a format change |
| BP-116 | YugabyteDB/CockroachDB inherit PostgreSQL's `ANY(@array)` set-valued parameters (`SupportsSetValuedParameters` should be false); PG set-valued parameters should use a typed Npgsql array | 81eef2b | **Done** (live): single-id RetrieveAsync failed on PG/CRDB/YB. `SupportsSetValuedParameters=false` for CRDB/YB not ported: multi-id `= ANY` passed live |
| BP-117 | Batch upsert never emits `OVERRIDING SYSTEM VALUE`; YugabyteDB never gets it (product switch, `Upsert.cs:232`); PG<10 gate missing | c58cb96, a7ae9ef | **Done** (live PG/YB/CRDB) |
| BP-118 | CockroachDB: `MergeStartupOptions` overwrites a caller's explicit `lock_timeout` (`PostgreSqlDialect.cs:533`) | c58cb96 | **Done** (live, writer); reader half fixed separately (d35c929) |
| BP-119 | MySQL error 1295 ("not supported in prepared statement protocol") doesn't trigger the disable-prepare fallback; CockroachDB inherits PG proc wrapping (should be None); TiDB VALUES() upsert override missing (defensive) | 3eb997c | **Done** (1295 fallback unit-only; TiDB VALUES() live). CRDB proc wrapping=None **not** ported: CREATE PROCEDURE/CALL work live on CRDB v25.1 |
| BP-120 | Provider factory can't be found by its ADO.NET invariant name (`DbProviderLoader.cs:63` registers only the section key) | 335c5d0 | **Done** (d683dac) |
| BP-121 | FlatFile uses the resolver's default isolation mapping (no ReadUncommitted; SafeNonBlockingReads→ReadCommitted instead of RepeatableRead) | 15feeb2 | **Done**: verified against ~/prj/pengdows/pengdows.flatfile (0.2.1-preview.1). Levels RU/RC/RR; SafeNonBlockingReads→RR, StrictConsistency throws (no Serializable), FastWithRisks→RU. Live FlatFile testbed 22 checks (1 capability skip: stored procs) on both TFMs |
| BP-122 | Metrics: percentiles sort up to 2048 doubles on every operation when a `MetricsUpdated` subscriber (the OTel observer) is attached (no memoization) | d0bc060 | **Done** (1a9efc1): recompute every 32nd call |
| BP-123 | Explicit `ReadOnlyConnectionString` equal to `ConnectionString` disables SingleWriter turnstile sharing | d5b24e3 | **Done** (e1c41fa) |
| BP-124 | UNSURE, needs a red test first: type-coercion dispatch still gated on `AdvancedTypes.IsMappedType` (`SqlDialect.cs:1387`); `NeedsCommonConversions` opt-in for DuckDB/Firebird/Oracle/Snowflake | ed71269, 81eef2b | **Done** (df02754, c3e01b9): targeted fixes (Stream/TextReader write binding, Oracle interval format/read, template-clone DbType fallback, DuckDB reader-owned BLOB streams on all 4 read paths). 3.0's full coercion-registry rewrite and the `NeedsCommonConversions` opt-ins were **not** ported: a probe showed no mis-conversion on 2.0.6. Live: Oracle interval + DuckDB portable round trips green |
| BP-125 | UNSURE: FlatFile named `:` parameters and capability flags; depends on which pengdows.flatfile version 2.0.6 targets | 9f0299e | **Done**: named `:` parameters, SingleWriter mode policy, savepoints, read-only transactions, OFFSET/FETCH paging, MERGE, namespaces and more, all verified against the provider source plus live probes. FlatFile added to the testbed and integration fixture via a conditional ProjectReference to the sibling repo |

### Found during the Tier 1 backport: bugs 3.0 still has

- **3.0 async dispose ignores a governor drain timeout.** `DatabaseContext.DisposeManagedAsync` ends with
  `await base.DisposeManagedAsync()`, which re-runs the sync `DisposeManaged()`. On that second pass the
  governors are null and report "drained", so data sources and unique-connection-string claims are
  released anyway. CORE-026 only holds for sync `Dispose()`. 2.0.6 fixes it with a sticky
  `_sharedResourceDisposalDeferred` flag (BP-110 (d), test `DisposeAsync_GovernorDrainTimesOut_DoesNotDisposeOwnedDataSources`).
- **MySQL + MySql.Data stores `"` as `\"`.** The session `sql_mode` includes `NO_BACKSLASH_ESCAPES`, but
  MySql.Data (no server-side prepare) backslash-escapes text-protocol parameters. 3.0 only removes the flag
  for TiDB, and its MySQL test container switched to MySqlConnector, which hides the bug. Tracked with BP-115 on 2.0.6.
- **PostgreSQL-family read connections drop the caller's `Options`.** The read-only connection string
  appended a second `Options=` key, replacing e.g. `-c lock_timeout=120s` on every read connection.
  Red live on PG/CRDB/YB; fixed on 2.0.6 (d35c929) by merging into the existing Options and forcing
  `default_transaction_read_only=on`.
- **FlatFile.** 3.0's `FlatFileDialect` declares Serializable (provider rejects it since d333c99) and maps
  FastWithRisks→RC (RU is supported); stores Guid as String although the provider has native Guid/UUID; says
  no user-defined types (CREATE TYPE works); lacks savepoints, read-only transactions, `SupportsLimitOffset=false`,
  FETCH FIRST natural-key lookup, namespaces, JSON_TABLE/SQL-JSON constructors. Its testbed hard-references the
  sibling flatfile repo, which CI doesn't check out.
- **Firebird Embedded Best-mode test** asserts Best → PreventDatabaseUnload while both branches' FirebirdDialect
  resolve Best → Standard by policy; 3.0's `SentinelPreventsUnload` probe measures the wrong thing (see BP-206).
- **DuckDB BLOB → `Stream` reads zeros.** 3.0 fixes the compiled mapper and coercions but not `DataReaderMapper`'s
  own setter path; 2.0.6 covers all four (c3e01b9).

### Found during Tier 2 and the new mode rules (2026-09-25)

| ID | Finding | Status |
|---|---|---|
| NEW-001 | Firebird `Best` → PreventDatabaseUnload; explicit `Standard` honored wherever `Best` picks PreventDatabaseUnload (LocalDB, Firebird); DuckDB honors explicit `Standard` (maintainer rules found testing 3.0; LocalDB/DuckDB from 3.0 b356af1) | **Done** (3156d2c). 3.0 still maps Firebird Best → Standard |
| NEW-002 | Firebird DDL fails ("object TABLE ... is in use") under PreventDatabaseUnload: sentinel attachments, even freshly reopened ones, block DDL | **Done** (c5d1c3d): sentinels closed before the pool reset, kept closed during the DDL, reopened after. 3.0 has the same exposure |
| NEW-003 | Data sources created before BP-204's `MinPoolSize=2` was applied, so working connections never got the minimum | **Done** (c5d1c3d) |
| NEW-004 | Spanner inherited `SupportsOverridingSystemValue` from PostgreSQL after BP-117 (live: "Statements with OVERRIDING clauses are not supported"); fakeDb never answered the Spanner detection probe | **Done** (5f5cf51) |
| NEW-005 | Npgsql type cache stale on the reader data source after CREATE EXTENSION/TYPE/DOMAIN | **Done** (7ef48b7): reload types on every owned data source |
| NEW-006 | Db2 "Value cannot be null." (ArgumentNullException from IBM's `DB2ConnPool.ReplaceConnStrPwd`) in the full testbed run, twice (net10 then net8) | **Resolved (harness)**: caused by the testbed's idle-unload probe calling `DB2Connection.ReleaseObjectPool()` right before the concurrency test. A standalone repro (no server) shows ReleaseObjectPool followed by concurrent ConnectionString assignment segfaults the IBM driver. The library never calls it; the Db2 probe no longer runs (reported "not measured") |
| FF-DEF | pengdows.flatfile provider defects found (repo not modified): read-only/writer-contention violations are InvalidOperationException/TimeoutException, not DbException; VARCHAR = Guid parameter throws raw ArgumentException (BoundPredicateEvaluator.cs:1166); README's isolation text is stale | Open (flatfile repo) |
| HARN-006 | A test that targets a specific provider (e.g. `TransactionRollbackOnKilledConnectionTests`, Firebird Embedded) fails instead of skipping when `INTEGRATION_ONLY` excludes that provider | Open |

### Tier 2: fixes that change visible behavior (decide per item)

| ID | Change | 3.0 commit | Status |
|---|---|---|---|
| BP-201 | PK gateway `UpdateAsync`/`BatchUpdateAsync`/`BatchUpsertAsync` don't throw `ConcurrencyConflictException` on a `[Version]` mismatch (`PrimaryKeyTableGateway.Update.cs:56/78`) | 5e6b244 | **Done** (live 11/11 providers) |
| BP-202 | `BatchUpsertAsync` silently swallows a stale `[Version]` (except the ON DUPLICATE KEY path, which can't detect it) | 21ccbca, 89db70d | **Done**: MySQL-family ON DUPLICATE KEY and Firebird UPDATE OR INSERT can't detect a stale version (documented). The live run also found every MySQL 8.0.19+ versioned upsert failing with an ambiguous `version` column; fixed |
| BP-203 | After `UpdateAsync`/`BatchUpdateAsync` the entity keeps the old `[Version]`, so reusing the instance always conflicts (`WriteBackIncrementedVersion`) | 04719e7, 21ccbca | **Done**: fakeDb taught `col ± n` SET arithmetic so persisted versions advance |
| BP-204 | `MaxConcurrentWrites=0` means read-only only in SingleWriter; PreventDatabaseUnload pool capacity ≥2; config-vs-connection-string mismatch warning | d506a74 | **Done**: `MaxConcurrentWrites=0` → ReadOnly in every mode; PreventDatabaseUnload pools ≥2 (mode-guarded); config vs connection-string mismatch warning |
| BP-205 | DuckDB read-only safety check runs after the explicit `ReadOnlyConnectionString` check (`DatabaseContext.cs:261`); native batch UPDATE keys on `[PrimaryKey]`, not `[Id]` (`TableGateway.Batch.cs:264`) | 5e6b244 | **Done**: DuckDB read-only check order; native batch UPDATE keys on `[Id]` |
| BP-206 | PreventDatabaseUnload sentinel never repaired when Broken/Closed; no per-pool (reader) sentinel; sentinel replacement and permit accounting | 5e6b244, 61201f6, cd74c28 | **Done**: Broken/Closed sentinel replaced via compare-and-swap; one sentinel per pool; read-only sentinel takes a reader permit. The testbed `SentinelPreventsUnload` probe was also rewritten: 3.0's version compared a cold sample with a warm pooled one and failed live on SQL Server/Firebird. The Firebird Embedded 5th method was ported with PreventDatabaseUnload requested explicitly, because Best resolves to Standard by policy (3.0's version contradicts its own dialect) |
| BP-207 | `TenantContextRegistry.DisposeManagedAsync` fire-and-forgets in-flight construction instead of awaiting it (3.0 hit a `Lazy<Task>` deadlock here) | 7743c19 | **Done**: DisposeAsync awaits a per-entry construction signal (no pool thread parked, unlike 3.0's Task.Run) |
| BP-208 | Read-only violations throw inconsistent types (`InvalidOperationException` on the reader write path vs `NotSupportedException` elsewhere) | 7db5f4c (with BP-302) | **Done** (9eec794): reader-path write on a read-only context now throws `NotSupportedException` like every other write path (3.0 semantics; read-only *transaction* writes keep `InvalidOperationException`, as in 3.0) |
| BP-209 | PostgreSQL/YugabyteDB `SafeNonBlockingReads` throws `TransactionModeNotSupportedException`; 3.0 maps it to RepeatableRead (behavior part of 1cbd073 only; the signature change is 3.0-only) | 1cbd073 | **Done** (7cedda9): live `SHOW transaction_isolation` = repeatable read on PG/YB |

### Tier 3: additive public API (minor-bump question)

| ID | Feature | 3.0 commit | Status |
|---|---|---|---|
| BP-301 | Opt-in `EnforceUniqueConnectionString` + warning on duplicate connection strings + hashed pool key + dispose-on-reject (one package, all or nothing) | 8c2d22b, 5aa33b7, 91225cd, 21ccbca | Open (decide) |
| BP-302 | `ReadOnlyContextException` / `ReadOnlyAccessException` + `IReadOnlyViolation` (subclasses of the types thrown today) | 28cff9c | Open (decide) |
| BP-303 | Metrics: percentile-availability and contention-attribution `init` properties on `DatabaseMetrics`/`DatabaseRoleMetrics`, `PoolStatisticsSnapshot.TotalWaits` (additive `init` props only; the positional-record changes are 3.0-only) | c3e61b1, 1a11ce3 | Open (decide) |
| BP-304 | `AddDbProviderLoading` DI entry point (the loader ctor is internal, so the feature is unreachable today) | ee13db6 | Open (decide) |
| BP-305 | Public `DataReaderMapper` | be7756b | Open (decide) |
| BP-306 | `MultiTenantOptions.MaxTenantCount` wired through DI | 4399d3a | Open (decide) |
| BP-307 | Opt-in multitenancy call-site analyzer. **Needs a new diagnostic ID** (PGC027 is CompatibilityLeakAnalyzer on 2.0.6) | f34d7bf | Open (decide) |
| BP-308 | `ISqlDialect.JoinParenthesization` (with a default implementation; nothing consumes it yet) | 70d3b99 | Open (decide) |
| BP-309 | Oracle array-bound `BatchCreate` (ArrayBindCount instead of INSERT ALL) | b30c970 | Open (decide) |
| BP-310 | Oracle batch UPDATE via MERGE | e639f9a | Open (decide) |
| BP-311 | Async `DatabaseContext.CreateAsync` + `ITenantContextRegistry.GetContextAsync`/`AcquireLeaseAsync` (+ its review fixes: re-entrancy guard, OperationCanceledException wrapping, logger race). About 1000 lines of init rewrite; high risk | eca20ec, 8693e5f | Open (decide) |

### Integration tests to port with the fixes

3.0 has 81 integration test methods that 2.0.6 lacks, in 19 3.0-only files; 2.0.6 has 14 of its own.
- **Port with the matching BP item:** `VersionedUpsertConflictTests` (BP-201..203), `TransactionTests` (5 new), `StoredProcedureTests` (4), `MergeConflictTests`, `AuditFieldTests`, `DiagnosticsTests`, `NpgsqlEnumNameLivenessTests`, `PostgreSqlAdvancedTypeRoundTripTests` (BP-111/112), `OraclePoolIsolationAndBooleanDbTypeTests`, `OracleIntervalRoundTripTests`.
- **Port as coverage for existing 2.0.6 behavior (verify each applies):** `FirebirdEmbeddedConnectionTests`, `SqliteModeIsolationTests`, `TransactionRollbackOnKilledConnectionTests`, `SqlServerOddTypeRoundTripTests`, `PortableAdvancedTypeRoundTripTests`.
  - 2026-09-25: ported `FirebirdEmbeddedConnectionTests` (4 of 5 methods; `BestModeUsesPreventDatabaseUnloadAndRealWorkAttachments` waits on BP-206), with `scripts/install-firebird-embedded.sh` provisioned by `run-integration-tests.sh`; a missing engine now fails instead of skipping.
- **Only with their feature:** `RetryContextTests` (out of scope), `CapabilityProbeTests`, `FirebirdPureKeyUpsertTests`.
- **3.0 harness self-tests (need 3.0 testbed infrastructure first):** `DatabaseTypeCatalogTests`, `ProviderMatrixCompletenessTests`, `ParallelTestOrchestrator*Tests`, `ProcessReexecHelperTests`, `IntegrationTestConfigurationTests` additions.
- **Testbed:** 3.0 has `DbMode.IdleUnloadProbe` / `DbMode.SentinelPreventsUnload` (pair with BP-206), SingleStore and FlatFile containers, and named per-provider checks (`Db2.FinalTableInsert/Retrieve/TypedMergeParams`, `DuckDb.ReturningClause`, `SqlServer.PagingWithoutOrderBy/TriggerIdentityReturn`, `PostgreSql.GeneratedAlwaysIdentity`). 2.0.6 prints some of these to the console without recording a check.

### Forward-port to 3.0 (2.0.6-only work)

These 2.0.6 changes need to go into `3.0` too (or be consciously dropped). `3.0-backports` already
carries earlier 2.0.6 → 3.0 ports.
- Testbed skip audit fixes (SKIP-001..016): Sybase `DateTimeOffset` coercion (Guid stays pass-through),
  Informix savepoints/SKIP-FIRST paging/typed-CAST MERGE/`EXECUTE PROCEDURE` (`ProcWrappingStyle.Informix`)/`DateTimeOffset`
  coercion/`QualifiesColumnReferences`, Firebird 4+ `FbZonedDateTime` writes + reads, `ISqlDialect.SupportsPaging`,
  `PreservesTrailingWhitespace`, the `{P}name` repeated-parameter documentation.
- Harness: HARN-001..003 (fail instead of skip on provider startup failure, standalone-container
  collection, SQL Server wait, Sybase model-database retry).
- 2.0.6's 26-method testbed check suite (parameter binding, round trips, isolation fail-up, paging,
  upsert, register-named columns, Firebird time zones, ...): 3.0's `TestProvider` no longer has it; confirm
  where 3.0 covers each check, or port the missing ones.

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
- **Stored-procedure multi-result/OUT parameter handling** is less complete than the best
  specialized competitors.

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
- **SingleWriter fairness torture test.** The turnstile activation bug is fixed and covered by a
  unit test (`SingleWriterTurnstileActivationTests.cs`), but there's no long-running stress test
  proving writers don't starve under continuous concurrent readers against a real SQLite file.
- **Broader transaction concurrency stress testing.** The specific reader-lock-lifetime gap is
  now covered (`TransactionReaderLockLifetimeTests.cs`), but general multi-threaded torture
  testing of the no-op/real/reusable locker architecture doesn't exist yet.

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
  threshold). These four are therefore safe to fingerprint-key, and the fingerprint itself exists
  on this branch — `IInternalSqlDialect.CacheFingerprint` (default impl on `SqlDialect`:
  `"{DatabaseType}|{ParsedVersion}"`) — but on 2.0.6 they are **still identity-keyed**
  (`ConditionalWeakTable<ISqlDialect, ...>`), so same-version tenants do not yet share entries and
  cache cardinality still follows the number of live dialect instances. Switching them to
  `ConcurrentDictionary<string, ...>` keyed by the fingerprint is not done on this branch. The
  different-version correctness tests in `TableGatewayMultiTenantDialectCacheTests` cover the
  current identity-keyed behavior.
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

  These four caches are also identity-keyed (`ConditionalWeakTable<ISqlDialect, ...>`,
  unchanged from the first fix) and must stay that way rather than be fingerprint-keyed — no live bug exists today, since
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

**Implemented — partial batch failure.** Both gateway types now weakly associate each generated
batch container with its entity slice and restore audit fields only for failed or unexecuted
containers. This covers chunked multi-row SQL and one-entity fallback containers for create,
update, and upsert. Deterministic success-then-failure regressions prove that persisted entities
retain their audit stamps while unpersisted entities are restored.

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
