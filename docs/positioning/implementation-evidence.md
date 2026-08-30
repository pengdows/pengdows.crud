# pengdows.crud — Implementation Evidence

This document is the companion to [`product-thesis.md`](./product-thesis.md). The thesis
states the architectural *why* and is meant to stay stable; this document tracks the
volatile *current status* — exact numeric limits, package versions, publish state,
instrument names, and internal wiring details — that changes independently of the
architecture and would otherwise make the thesis go stale every time an implementation
detail shifts. Treat everything here as a snapshot verified against source as of the date
below; re-verify against current code before quoting it externally.

Last verified: 2026-08-29, against branch `2.1` (HEAD at time of writing carries the full
CORE-*/TEST-*/DOC-* closures tracked in `docs/planning/future-work.md`). Sections below not
explicitly re-dated still reflect the 2026-08-13 audit and should be independently re-checked
before being quoted as current.

## Ecosystem package status

| Package | Purpose | Relationship to the core architecture |
|---|---|---|
| `pengdows.crud` | Core DAL: gateways, `ISqlContainer`, dialects | The architecture itself (principles 1–10) |
| `pengdows.crud.abstractions` | Public interfaces/enums | The coordinated boundary's contract surface |
| `pengdows.crud.fakeDb` | Fake ADO.NET provider | Falsifiability for principle 10 |
| `pengdows.crud.analyzers` | Roslyn rules PGC001/008/025/026 | Compile-time enforcement, principle 9 |
| `pengdows.poco.mint.cli` + Dockerized web UI | Schema-first POCO generation | Genuinely reuses `IDatabaseContext`/`ISqlDialect` for schema inspection (verified in `DatabaseInspector.cs`) — see principle 1 |
| `pengdows.hangfire` | SQL-first Hangfire job storage | A real downstream consumer: depends on `pengdows.crud` and `pengdows.crud.analyzers`, showing the architecture generalizes past CRUD to background-job storage |
| `pengdows.stormgate` | ADO.NET connection admission control (prevents "connection storm" thundering-herd opens) | Ships from the same repository/solution as `pengdows.crud` but is a standalone, separately-adopted package — not wired into `DatabaseContext`'s own connection governance (the `SingleWriter` turnstile governor is a distinct, internal mechanism) |
| `pengdows.threading` | `ConvergeWait` + adaptive throttling | A separate general-purpose concurrency library from the same author/namespace; no dependency relationship with `pengdows.crud` exists in source as of this writing |
| `pengdows.crud.opentelemetry` | OpenTelemetry metrics adapter | Genuinely built and tested (`PengdowsMetricsObserverTests.cs`): bridges `MetricsUpdated` into `System.Diagnostics.Metrics` without adding an OTel dependency to the core package, auto-discovers both DI-registered and per-tenant contexts via `ITenantContextRegistry` events, and emits per-pool (Reader/Writer) gauges — reinforcing principle 10's Emergent Capabilities metrics claim externally. Exposes **both** naming schemes side by side: the original `pengdows.db.client.*` instruments (unchanged, so no existing consumer breaks) and, additively, OTel semantic-convention names — `db.client.operation.duration` (a real Histogram, derived from the same `ActivitySource("pengdows.crud")` spans `SqlContainer` already emits for tracing, filtered to `Track()`ed contexts via a `pengdows.context_id` Activity tag so concurrent unrelated activity is never recorded) and the connection-pool counters `db.client.connection.count`/`.max`/`.pending_requests`/`.timeouts`. Still open: the semconv pool-side histograms (`create_time`/`wait_time`/`use_time`) would require new event hooks inside `PoolGovernor`'s concurrency-critical code, deliberately deferred rather than rushed; and the OTel bridge still exposes only aggregate command/connection/transaction counts, not the `DatabaseMetrics.Read`/`.Write` per-role split. **Not yet published to NuGet** as of this writing. `docs/opentelemetry-metrics.md` (renamed and rewritten 2026-08-13 from the stale `opentelemetry-metrics-plan.md`, which said "nothing here exists" long after the package shipped) has the full design rationale and current instrument/tag detail; `docs/planning/future-work.md` tracks what's still open |

`pengdows.poco.mint` maintains its own separate test suite (`core.tests`, `api.tests`,
`IntegrationTests`, with distinct CLI/web coverage baselines) verifying schema-generation
correctness — a real testing discipline, but a separate repository's, not part of
`pengdows.crud`'s own suite described in thesis principle 10.

Download counts and latest versions (including `pengdows.poco.mint.cli`'s current release)
change continuously and are intentionally not baked into this document as fixed values —
pull current numbers from nuget.org before using them in external-facing material (sales
collateral, onboarding decks).

## Per-dialect capability flags (principle 6)

`MaxOutputParameters` per-dialect cap, as currently implemented: SQL Server/Oracle 1024,
MySQL/Snowflake 65535, Firebird 1499, PostgreSQL 100.

Other stored-procedure capability flags, as currently implemented:
- `SupportsNamedParameters` — `false` only for the generic ANSI/ODBC fallback dialect.
- `SupportsRepeatedNamedParameters` — `false` only for Oracle (no reusing a bind-variable
  name within one statement).
- `RequiresStoredProcParameterNameMatch` — `true` for PostgreSQL and Oracle.

## MySQL vs. MariaDB read-only session SQL (principle 4)

PostgreSQL: `SET default_transaction_read_only = on`. SQLite: `Mode=ReadOnly` in the
connection string. DuckDB: `access_mode=READ_ONLY`.

MySQL and MariaDB differ despite `MariaDbDialect` inheriting from `MySqlDialect`: MySQL
uses `SET SESSION transaction_read_only = 1`, MariaDB uses `SET SESSION tx_read_only = 1`.
Per `MySqlDialect.cs`'s own version-history comment: MariaDB never adopted MySQL's
`transaction_read_only` alias, and MySQL 8.0.3 removed the older `tx_read_only` name — so
the two forks need distinct SQL, not a shared implementation, even though one dialect class
inherits from the other.

## Complete analyzer rule list (principle 9)

The `pengdows.crud.analyzers` Roslyn package currently defines four rules:

- **PGC001** — DI registrations of `DatabaseContext`/`TableGateway`/`PrimaryKeyTableGateway`
  as `AddScoped`/`AddTransient` are errors; these types must be singletons.
- **PGC008** — raw/interpolated value injection into SQL `WHERE`/`JOIN ON`/`HAVING`/`AND`/`OR`
  is an error; values must be parameterized (`IS NULL`/`IS NOT NULL` are exempt).
- **PGC025** — gateway execution/build methods must resolve and use the execution context
  parameter (see thesis principle 3).
- **PGC026** — warns on the split `WrapObjectName("alias") + "." + WrapObjectName("column")`
  pattern in favor of the single-call `WrapObjectName("alias.column")` form.

## BenchmarkValidation mechanism (principle 10)

`BenchmarkValidation` asserts the target index actually exists and captures
`SET STATISTICS XML`/`SHOWPLAN` output to fail the benchmark run if the captured query plan
doesn't actually use that index — catching the case where a benchmark claims to measure an
indexed-lookup path but the query planner silently chose a different plan.

## DataSource removal history (principle 5)

`IDatabaseContext.DataSource` (a public `DbDataSource?`) was introduced in the 2.0 rewrite
(commit `d89b369`, 2026-02-28) and stayed public until 2026-08-13, when this document's own
audit caught it: any caller could reach past `ISqlContainer`/gateways entirely and call
`DataSource.CreateConnection()` for a raw provider connection, outside governor accounting,
session settings, and disposal tracking — the opposite of the 2.0 rewrite's own goal of
removing public connection leaks.

The fix went through two commits the same day:

1. `e76bce0` first made `DataSource` `internal`, routed through
   `IInternalConnectionProvider` (the same pattern `GetConnection` already used).
2. `ad54140` removed it as a named accessor entirely — checking usage showed nothing
   internal ever read the property; every real connection-creation path
   (`DatabaseContext.ResolveDataSource`, `FactoryCreateConnection`) already read the
   private `_dataSource`/`_readerDataSource` fields directly. The property only existed to
   be read back by callers and tests, so keeping even an internal-only version would have
   been a standing temptation for future code to grab the raw `DbDataSource` instead of
   going through governance.

Tests that need to verify which `DbDataSource` a constructor wired up now read the private
field via reflection (`DatabaseContextTestExtensions.GetInternalDataSource()`) instead of a
production accessor. A regression test, `pengdows.crud.Tests/IDatabaseContextPublicSurfaceTests.cs`,
walks `IDatabaseContext`'s and `ITransactionContext`'s full `GetInterfaces()` closure (plain
`Type.GetProperty` doesn't search base interfaces) so the property can't silently reappear
on either public interface.

**Confirmed present on branch `2.1` as of 2026-08-29** — `IDatabaseContext` (`pengdows.crud.abstractions/IDatabaseContext.cs`)
exposes no bare `DataSource` property; only `DataSourceInfo` (`IDataSourceInformation`, a
metadata-only object, not a raw `DbDataSource`) is public. All five test files this section
originally named (`IDatabaseContextPublicSurfaceTests.cs`, `TransactionGovernorAcquisitionTests.cs`,
`ExecuteReaderWriteConnectionLeakTests.cs`, `ReadOnlySessionSettingsTests.cs`,
`dialects/MariaDbDialectTests.cs`) still exist under these exact names. The original framing
(fix "pending on branch `2.0.6`, not yet in `main`") described a different, patch-line branch's
merge status and does not describe `2.1` — this correction removes that stale, branch-specific
claim rather than leaving a reader to wonder whether the fix applies here. This doc now tracks
`2.1`'s current status going forward; if you need `2.0.x`'s status, check that branch's own copy
of this file rather than inferring it from here.

## Full release-gate suite status (2026-08-29, branch `2.1`)

Per `docs/planning/future-work.md`'s TEST-006 closure:

- **Unit suite** (`pengdows.crud.Tests`): 7441 tests, 0 failed, 0 skipped, both `net8.0` and `net10.0`.
- **Testbed matrix**: 30/30 database targets, 878 checks passed, 0 failed, 53 documented skips — see
  the Provider/version evidence table below for the per-engine breakdown.
- **`pengdows.crud.IntegrationTests`**: 192 passed, 5 skipped (Firebird Embedded Linux-distribution
  tests, environment-gated per their own documented setup requirements), 0 failed, both target
  frameworks.

## Internal metrics wiring status

`DatabaseMetrics` now surfaces cumulative request and contention attribution. Request counts
come from `AttributionStats`, pool waits/timeouts come from the authoritative pool governors,
and mode waits/timeouts come from `ModeContentionStats`.

## Test coverage backing specific thesis claims

Principle 10 says every claim has a specific proof rather than a general assurance. The
table below is the volatile half of that promise — exact test file names, which will
rename/move/split over time — for the claims that got a source-level audit on 2026-08-13:

| Claim (product-thesis.md) | Proof |
|---|---|
| Non-lease execution paths self-clean on every outcome, including exception paths (principle 5) | `pengdows.crud.Tests/ExecuteReaderWriteConnectionLeakTests.cs` — asserts the connection is disposed when `ExecuteReaderAsync` fails before a `TrackedReader` is created |
| MySQL and MariaDB use different read-only session SQL (`transaction_read_only` vs `tx_read_only`) (principle 4) | `pengdows.crud.Tests/ReadOnlySessionSettingsTests.cs` and `pengdows.crud.Tests/dialects/MariaDbDialectTests.cs` — the latter explicitly asserts `Assert.DoesNotContain("transaction_read_only", settings)` for MariaDB |
| A transaction acquires its governed connection exactly once, not once per command inside it (principle 2) | `pengdows.crud.Tests/TransactionGovernorAcquisitionTests.cs` (added 2026-08-13) — asserts `PoolGovernor.TotalAcquired` moves by 1 for 5 commands inside one transaction, vs. by 5 for 5 sequential non-transactional commands (the contrast rules out a vacuous pass) |
| `IDatabaseContext`/`ITransactionContext` do not expose a `DataSource` property, even transitively through inherited interfaces (principle 5) | `pengdows.crud.Tests/IDatabaseContextPublicSurfaceTests.cs` (added 2026-08-13, same day the property was found public, then removed as a named accessor entirely — not merely made internal) — walks each interface's full `GetInterfaces()` closure since `Type.GetProperty` doesn't search base interfaces on its own |

This table itself needs re-verification if any of the referenced test files are renamed,
merged, or deleted — it's evidence of a point-in-time audit, not a standing contract that
the tests will always exist under these names.

### Additions from the 2.1 CORE-*/TEST-* closure pass (2026-08-29)

| Claim (product-thesis.md) | Proof |
|---|---|
| Two tenants' independent `PoolGovernor`s isolate failure — saturating one tenant's admission never affects another sharing the same singleton gateway (principle 3) | `pengdows.crud.Tests/TwoTenantFailureContainmentTests.cs` — `SaturatedWriterGovernor_OnOneTenant_DoesNotAffectAnotherTenant_OnSharedSingletonGateway` |
| `SingleWriter` mode's turnstile prevents writer starvation under sustained concurrent readers (principle 5) | `pengdows.crud.Tests/SingleWriterFairnessTortureTests.cs` — 16 continuous readers vs. 40 writers against a real file-backed SQLite context; also `PoolGovernorFairnessTests.WriterWithTurnstile_BlocksNewReaders` at the deterministic unit level |
| `PreventDatabaseUnload` sentinel repair (reconnecting a broken sentinel) is permit-neutral — no leak, no double-acquire (principle 5) | `pengdows.crud.Tests/PreventDatabaseUnloadTests.cs` — `BrokenSentinel_Repair_IsPermitNeutral` |
| The hazardous `GeneratedKeyPlan.SessionScopedFunction` path is unreachable by any of the 16 shipped dialects — the generated-ID two-lease race can only occur in the narrower, real-provider-only inner-fallback case (principle 5) | `pengdows.crud.Tests/dialects/GeneratedKeyPlanReachabilityTests.cs` — `[Theory]` over every `SupportedDatabase` value via the real `SqlDialectFactory.CreateDialectForType` switch |
| Transaction commit/rollback/dispose races have exactly one winner; an open reader fails fast rather than deadlocking or corrupting the connection (principle 2) | `pengdows.crud.Tests/TransactionCompletionReaderGuardTests.cs`, `TransactionContextDisposeRaceTests.cs`, `TransactionContextTests.CommitAndRollback_RaceOnlyOneSucceeds` — see `docs/transactions.md`'s Concurrency Contract section for the full race matrix |
| A context handed back by `ITenantContextRegistry.GetContext` is never an orphaned, untracked instance when a concurrent `Invalidate` races its construction (principle 3) | `pengdows.crud.Tests/TenantTests.cs` — `Invalidate_RacingWithInFlightCreate_DoesNotLeakOrphanedContext`, `Dispose_RacingWithInFlightCreate_ThrowsInsteadOfLeakingOrphanedContext` (the narrower lookup-vs-invalidate race remains open — see CORE-010 and `docs/connection/multitenancy-architecture.md`) |

## Provider/version evidence (DOC-005)

The maintained testbed (`testbed/`, run via `dotnet run -c Release --project testbed`) is the
source of the "12 engines, 30 targets" claim used elsewhere in this project's positioning
material. This is **verified database support** in the three-tier sense
`docs/planning/future-work.md`'s "What 'supported database' means" section defines: each target
below passed the shared unit/integration/testbed contracts through the public pengdows.crud
surface, using a currently-maintained .NET provider, on 2026-08-29 (repo-root `testbed-results.json`,
tracked in git — regenerate before quoting a later date).

| Engine | Versions tested | Targets | Checks (pass/fail/skip) | .NET provider package |
|---|---|---|---|---|
| SQL Server | 2017, 2019, 2022-CU25 | 3 | 90 / 0 / 6 | `Microsoft.Data.SqlClient` 6.0.2 |
| PostgreSQL | 9.5, 15.0, 16.4 | 3 | 89 / 0 / 5 | `Npgsql` 9.0.3 |
| MySQL | 5.7, 8.0.36, 8.4.11 | 3 | 87 / 0 / 3 | `MySqlConnector` 2.4.0 / `MySql.Data` 9.3.0 |
| MariaDB | 10.2, 10.4, 10.11.11, 11.4.12 | 4 | 120 / 0 / 8 | `MySqlConnector` 2.4.0 / `MySql.Data` 9.3.0 |
| Oracle | 18c, 21c, 23.26.2 | 3 | 90 / 0 / 3 | `Oracle.ManagedDataAccess.Core` 23.8.0 |
| SQLite | (file-based, single version) | 1 | 28 / 0 / 2 | `Microsoft.Data.Sqlite` 9.0.5 |
| DuckDB | (single version) | 1 | 29 / 0 / 3 | `DuckDB.NET.Data.Full` 1.3.2 |
| Firebird | 3.0.9, 4.0.5, 5.0.2 | 3 | 81 / 0 / 9 | `FirebirdSql.Data.FirebirdClient` 10.3.3 |
| CockroachDB | v23.2.14, v24.3.0, v25.1.0 | 3 | 84 / 0 / 6 | `Npgsql` 9.0.3 (PostgreSQL-compatible) |
| YugabyteDB | 2.25.2.0-b359, 2025.2.5.2-b5 | 2 | 58 / 0 / 2 | `Npgsql` 9.0.3 (PostgreSQL-compatible) |
| TiDB | v7.5.7, v8.5.7 | 2 | 56 / 0 / 4 | `MySqlConnector` 2.4.0 (MySQL-compatible) |
| Db2 | 11.5.0.0a, 11.5.8.0 | 2 | 66 / 0 / 2 | `Net.IBM.Data.Db2-lnx` 8.0.0.500 |
| **Total** | | **30** | **878 / 0 / 53** | |

Zero failures across every target; the 53 skips are documented, engine-specific capability
gaps (e.g. PostgreSQL 9.5 predates `GENERATED ALWAYS AS IDENTITY`, requiring PG10+; a
MySql.Data-specific reader-disposal quirk) — not silent omissions. `AuroraMySql` and
`AuroraPostgreSql` are managed-AWS variants detected at runtime and covered by the MySQL/
PostgreSQL suites rather than a separate matrix row (see CLAUDE.md's "Aurora variants" section).
`Snowflake` requires the opt-in `INCLUDE_SNOWFLAKE=true` environment variable (cloud-only, no
Docker image) and is correctly excluded from this always-on 30-target run.

**What this table does not claim:** a database not in this list is not "unsupported" in the
sense of being rejected — an unrecognized product falls back to `Sql92Dialect` (generic ANSI
behavior, see `docs/capability-discovery.md` and `docs/connection/dynamic-provider-loading.md`'s
"Recognized dialect vs. wholly-unknown engine" section) rather than throwing. The distinction
this table draws is specifically **verified support** (this list, executable proof) vs.
**generic provider compatibility** (any other ADO.NET-loadable engine, unverified, ANSI-only
behavior) — see `docs/planning/future-work.md`'s "What 'supported database' means" for the full
three-tier definition this table applies.
