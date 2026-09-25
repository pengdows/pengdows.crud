# pengdows.crud — Implementation Evidence

This document is the companion to [`product-thesis.md`](./product-thesis.md). The thesis
states the architectural *why* and is meant to stay stable; this document tracks the
volatile *current status* — exact numeric limits, package versions, publish state,
instrument names, and internal wiring details — that changes independently of the
architecture and would otherwise make the thesis go stale every time an implementation
detail shifts. Treat everything here as a snapshot verified against source as of the date
below; re-verify against current code before quoting it externally.

Last verified: 2026-09-24, against branch `2.0.6` — a source-level re-check of every API
name, value, file path, and test citation below. This document deliberately records no
test-run totals or testbed pass/fail counts; run the suites yourself before quoting results.

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
| `pengdows.crud.opentelemetry` | OpenTelemetry metrics adapter | Genuinely built and tested (`PengdowsMetricsObserverTests.cs`): bridges `MetricsUpdated` into `System.Diagnostics.Metrics` without adding an OTel dependency to the core package, auto-discovers both DI-registered and per-tenant contexts via `ITenantContextRegistry` events, and emits per-pool (Reader/Writer) gauges — reinforcing principle 10's Emergent Capabilities metrics claim externally. Exposes **both** naming schemes side by side: the original `pengdows.db.client.*` instruments (unchanged, so no existing consumer breaks) and, additively, OTel semantic-convention names — `db.client.operation.duration` (a real Histogram, derived from the same `ActivitySource("pengdows.crud")` spans `SqlContainer` already emits for tracing, filtered to `Track()`ed contexts via a `pengdows.context_id` Activity tag so concurrent unrelated activity is never recorded) and the connection-pool counters `db.client.connection.count`/`.max`/`.pending_requests`/`.timeouts`. Still open: the semconv pool-side histograms (`create_time`/`wait_time`/`use_time`) would require new event hooks inside `PoolGovernor`'s concurrency-critical code, deliberately deferred rather than rushed; and the OTel bridge still exposes only aggregate command/connection/transaction counts, not the `DatabaseMetrics.Read`/`.Write` per-role split. **Not yet published to NuGet** as of this writing. `docs/opentelemetry-metrics.md` has the full design rationale and current instrument/tag detail |

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
- `SupportsNamedParameters` — `false` for the generic ANSI fallback dialect (`Sql92Dialect`)
  and for Informix, SAP HANA, Access, and FlatFile; `true` for every other dialect.
- `SupportsRepeatedNamedParameters` — defaults to the dialect's `SupportsNamedParameters`
  value; Oracle is the one dialect that supports named parameters but explicitly overrides
  this to `false` (no reusing a bind-variable name within one statement).
- `RequiresStoredProcParameterNameMatch` — `true` for PostgreSQL and Oracle.

## MySQL vs. MariaDB read-only session SQL (principle 4)

PostgreSQL: `SET default_transaction_read_only = on`. SQLite: `Mode=ReadOnly` in the
connection string. DuckDB: `access_mode=READ_ONLY`.

MySQL and MariaDB differ despite `MariaDbDialect` inheriting from `MySqlDialect`: MySQL
uses `SET SESSION transaction_read_only = 1`, MariaDB uses `SET SESSION tx_read_only = 1`.
Per `MariaDbDialect.cs`'s own version-history comment: MariaDB never adopted MySQL's
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
- **PGC027** — an error on any use of the API kept public only for 2.x binary compatibility
  (`DataSource`, the SQL-standard-level heuristics, internal bookkeeping types, the inert type
  attributes), and on assigning `DatabaseContext.ReadWriteMode`/`ProcWrappingStyle`, which are
  fixed at construction (the setters are no-ops; 3.0 makes them `init`).

## BenchmarkValidation mechanism (principle 10)

`BenchmarkValidation` asserts the target index actually exists and captures
`SET STATISTICS XML`/`SHOWPLAN` output to fail the benchmark run if the captured query plan
doesn't actually use that index — catching the case where a benchmark claims to measure an
indexed-lookup path but the query planner silently chose a different plan.

## `IDatabaseContext.DataSource` exposure (principle 5)

`IDatabaseContext.DataSource` (an obsolete compatibility-only `DbDataSource?`,
`pengdows.crud.abstractions/IDatabaseContext.cs`)
was introduced in the 2.0 rewrite (commit `d89b369`) and is still public on this branch;
`ITransactionContext` forwards its parent context's value. It returns the writer-side data
source the context was built with — a caller-supplied `DbDataSource` (e.g. `NpgsqlDataSource`)
or one the context created internally — or `null`. Any caller can use it to call
`DataSource.CreateConnection()` for a raw provider connection, outside governor accounting,
session settings, and disposal tracking. It is the one public raw-provider accessor on the
execution surface: there is no public `DbConnection` accessor (`GetConnection` is `internal`),
and `ISqlContainer`/`ITrackedReader` expose no connection. Application use is rejected by `PGC027`;
use the context execution APIs instead.

Tests that need to verify which `DbDataSource` a constructor actually wired up (including the
reader-side data source, which has no public accessor) read the private field via reflection
(`pengdows.crud.Tests/DatabaseContextTestExtensions.cs`, `GetInternalDataSource()`).

## Internal metrics wiring status

`DatabaseMetrics` now surfaces cumulative request and contention attribution. Request counts
come from `AttributionStats`, pool waits/timeouts come from the authoritative pool governors,
and mode waits/timeouts come from `ModeContentionStats`.

## Test coverage backing specific thesis claims

Principle 10 says every claim has a specific proof rather than a general assurance. The
table below is the volatile half of that promise — exact test file names, which will
rename/move/split over time — for the claims that got a source-level audit:

| Claim (product-thesis.md) | Proof |
|---|---|
| Non-lease execution paths self-clean on every outcome, including exception paths (principle 5) | `pengdows.crud.Tests/ExecuteReaderWriteConnectionLeakTests.cs` — asserts the connection is disposed when `ExecuteReaderAsync` fails before a `TrackedReader` is created |
| MySQL and MariaDB use different read-only session SQL (`transaction_read_only` vs `tx_read_only`) (principle 4) | `pengdows.crud.Tests/ReadOnlySessionSettingsTests.cs` and `pengdows.crud.Tests/dialects/MariaDbDialectTests.cs` — the latter explicitly asserts `Assert.DoesNotContain("transaction_read_only", settings)` for MariaDB |
| A transaction acquires its governed connection exactly once, not once per command inside it (principle 2) | `pengdows.crud.Tests/TransactionGovernorAcquisitionTests.cs` — asserts `PoolGovernor.TotalAcquired` moves by 1 for 5 commands inside one transaction, vs. by 5 for 5 sequential non-transactional commands (the contrast rules out a vacuous pass) |

This table itself needs re-verification if any of the referenced test files are renamed,
merged, or deleted — it's evidence of a point-in-time audit, not a standing contract that
the tests will always exist under these names.

### Additional claim-to-test mappings

| Claim (product-thesis.md) | Proof |
|---|---|
| Two tenant contexts sharing one singleton gateway each get their own dialect-correct SQL, including two versions of the same engine (principle 3) | `pengdows.crud.Tests/TableGatewayMultiTenantDialectCacheTests.cs` (fakeDb) and `pengdows.crud.IntegrationTests/Core/MultiTenantDialectVersionTests.cs` — `SharedGateway_TwoRealMySqlVersions_GeneratesCorrectSqlPerTenant` (two real MySQL containers) |
| `SingleWriter` mode's turnstile prevents writer starvation under sustained concurrent readers (principle 5) | `pengdows.crud.Tests/SingleWriterFairnessTortureTests.cs` — 16 continuous readers vs. 20 writers against a real file-backed SQLite context; also `PoolGovernorFairnessTests.WriterWithTurnstile_BlocksNewReaders` at the deterministic unit level |
| `PreventDatabaseUnload` sentinel repair (reconnecting a broken sentinel) does not leak the replacement connection when the context is disposed mid-repair, and a healthy sentinel is not needlessly reconnected (principle 5) | `pengdows.crud.Tests/PreventDatabaseUnloadSentinelReconnectTests.cs` — `GetConnection_ContextDisposedWhileSentinelRepairIsInFlight_DoesNotLeakTheReplacementConnection`, `GetConnection_SentinelHealthy_DoesNotReconnect` |
| The hazardous `GeneratedKeyPlan.SessionScopedFunction` path is unreachable by any shipped dialect — the generated-ID two-lease race can only occur in the narrower, real-provider-only inner-fallback case (principle 5) | `pengdows.crud.Tests/dialects/GeneratedKeyPlanReachabilityTests.cs` — `[Theory]` over every `SupportedDatabase` value via the real `SqlDialectFactory.CreateDialectForType` switch |
| Concurrent commit/rollback on one transaction has exactly one winner, and `Dispose` racing a held lock neither throws nor breaks the later release (principle 2) | `pengdows.crud.Tests/TransactionContextTests.cs` — `CommitAndRollback_RaceOnlyOneSucceeds`; `pengdows.crud.Tests/TransactionContextDisposeRaceTests.cs` |
| A context whose construction was still in flight when `ITenantContextRegistry` was disposed is still disposed once construction completes — never an orphaned, untracked instance (principle 3) | `pengdows.crud.Tests/TenantTests.cs` — `Dispose_RacingWithInFlightCreate_DisposesTheOrphanedContextOnceConstructionCompletes`; lease/invalidate exactly-once disposal in `pengdows.crud.Tests/TenantContextLeaseTests.cs` — `AcquireLease_ConcurrentReleaseAndInvalidateHammer_DisposesExactlyOnce` (the bare-reference `GetContext`-vs-`Invalidate` race remains open by design — see `docs/connection/multitenancy-architecture.md`) |

## Provider/version evidence

The maintained testbed (`testbed/`, run via `dotnet run -c Release -f net10.0 --project testbed`;
see `testbed/readme.md`) is the executable source of "verified database support" claims: it runs
one shared matrix of checks (CRUD, transactions, stored-procedure invocation, and the rest listed
in the readme) through the public pengdows.crud surface against real databases via
Testcontainers. The always-on set is SQLite, DuckDB, PostgreSQL, MySQL, MariaDB, SQL Server,
CockroachDB, Firebird, TiDB, and YugabyteDB; Oracle (`INCLUDE_ORACLE=true`, license acceptance)
and Snowflake (`INCLUDE_SNOWFLAKE=true`, cloud-only credentials) are opt-in. `AuroraMySql` and
`AuroraPostgreSql` are managed-AWS variants detected at runtime and covered by the MySQL/
PostgreSQL paths rather than separate targets (see CLAUDE.md's "Aurora variants" section).

This document does not record a per-engine pass/fail/skip table or tested version list — those
are properties of a specific run. Regenerate them from a fresh testbed run before quoting them.

**What this does not claim:** a database not covered by the testbed is not "unsupported" in the
sense of being rejected — an unrecognized product falls back to `Sql92Dialect` (generic ANSI
behavior, see `docs/capability-discovery.md` and `docs/connection/dynamic-provider-loading.md`'s
"Recognized dialect vs. wholly-unknown engine" section) rather than throwing. The distinction
drawn here is specifically **verified support** (testbed-covered, executable proof) vs.
**generic provider compatibility** (any other ADO.NET-loadable engine, unverified, ANSI-only
behavior).
