# Changelog

What is new in each release of `pengdows.crud`, newest first. Dates are the day the version was published to
NuGet. Entries for 2.0.0 to 2.0.5 were written after the fact from the release pull requests and the git history,
so they are summaries; 2.0.6 is described in full in [docs/release-notes-2.0.6.md](docs/release-notes-2.0.6.md),
which is the reference for behavior changes a 2.0.5 user can notice.

A release is not complete without an entry here: `DocumentationConsistencyTests` fails when the version in
`Directory.Build.props` has no heading, and the release workflow uses the version's section as the GitHub release
notes.

## [2.0.6] - unreleased

Binary compatible with 2.0.5 (package validation against 2.0.5 for `pengdows.crud`, `pengdows.crud.abstractions`
and `pengdows.crud.fakeDb`). Full detail: [docs/release-notes-2.0.6.md](docs/release-notes-2.0.6.md).

### Changed (behavior a 2.0.5 user can notice)
- SQL Server: `IsolationProfile.SafeNonBlockingReads` now also resolves, as ReadCommitted, when `READ_COMMITTED_SNAPSHOT`
  is on and snapshot isolation is off (it used to throw `TransactionModeNotSupportedException`). Snapshot isolation on still
  gives Snapshot; neither on still throws.
- Reads that returned a wrong value now fail loudly with `DataMappingException` naming the column: invalid or blank
  `[Json]`, blank text into a number, Guid, date or bool, a binary Guid that is not 16 bytes, an undefined enum
  value, a fractional number read into an integer.
- Gateway reads, `DataReaderMapper` reads and scalar reads now use the same coercion rules, including UTC
  `DateTime` values, numeric and flags enums, unsigned values and advanced provider types.
- Binary `Guid` columns now use the database's native byte order. Existing RFC 4122-order data written with
  `Guid.ToByteArray()` must be migrated or read as `byte[]`; columns declared as `DbType.Guid` are unchanged.
- SQLite: `query_only` is put back to OFF and `foreign_keys` to ON on every checkout, so a pooled connection a caller
  changed no longer carries the change to the next operation; `synchronous = NORMAL` when the database is in WAL
  (no opt-out yet); `PRAGMA optimize` once at startup, best effort.
- SQLite and DuckDB no longer prepare commands by default; explicit `CommandPrepareMode.Always` remains available.
- `ClampPoolsToServerConnectionLimit` and `ResourceConnectionHeadroom` can opt a context into provider-supported
  server connection-ceiling discovery and pool sizing. They are off by default for 2.x compatibility.
- An Aurora PostgreSQL cluster is detected as `AuroraPostgreSql` (reported as `PostgreSql` before).
- Single-row upserts are atomic against a concurrent insert of the same key (proven on live databases: 16 callers on
  one new key failed on PostgreSQL 5/16, SQL Server 1/16, Oracle 5/16, Db2 9/16 and Firebird 15/16 before). PostgreSQL
  uses `INSERT ... ON CONFLICT` for a single row, as batch upserts already did; SQL Server's `MERGE` takes
  `WITH (HOLDLOCK)`; Oracle, Db2 and Firebird retry the statement once on a unique violation (the row exists by then).
  The generated SQL for a single-row upsert on PostgreSQL 15+ and SQL Server changes shape.
- An upsert whose conflict key has a `NULL` component is refused with `InvalidOperationException` naming the column.
  It could never match, so it inserted again: a silent duplicate on SQLite, PostgreSQL and MySQL, a unique violation
  on SQL Server.
- A throwing connection dispose callback no longer leaks the governor permit (`PoolSaturatedException` for later callers).
- SingleStore error 1997 ("the outcome is unknown") is an `AmbiguousResultException`.

### Added
- **New in 2.0.6: nine databases**, the first databases added in 2.0 (2.0.0 to 2.0.5 supported the same fourteen), each
  with its own `SupportedDatabase` value and an integration suite green on net8.0 and net10.0
  ([docs/integration-status.md](docs/integration-status.md)): IBM Db2 for LUW (not z/OS or i), FlatFile
  ([pengdows.flatfile](https://github.com/pengdows/pengdows.flatfile)), SingleStore (detected as MySQL, MySQL dialect),
  Sybase (SAP) ASE, Google Cloud Spanner (PostgreSQL interface), IBM Informix, SAP HANA (opt-in suite), InterBase
  (opt-in suite) and Microsoft Access (Windows only). Always-on in the integration run: all but SAP HANA, InterBase and
  Access.
- **Four more supported products, verified with no code change:** Percona Server for MySQL (under MySQL), Citus,
  TimescaleDB and Fujitsu Enterprise Postgres (under PostgreSQL). They
  report themselves as MySQL or PostgreSQL, so detection uses the existing dialect, and each passed the same
  integration suite as the vanilla engine with the same number of checks. EDB Postgres Extended is not claimed (no
  public image to verify against).
- `AuditCreationPolicy` (init-only on the gateways) to choose how a gateway treats audit fields on create; see
  [docs/audit-fields.md](docs/audit-fields.md).
- `ColumnName(...)` on the gateways and as an `IDatabaseContext` extension, for the mapped column name in custom SQL.
- `ClampPoolsToServerConnectionLimit` and `ResourceConnectionHeadroom` on `DatabaseContextConfiguration` (opt-in).
- `services.AddDbProviderLoading(configuration)`.
- Aurora PostgreSQL and Aurora MySQL in the integration process, with their own AWS resources created and deleted by
  the run.
- Benchmarks that read cost and survival separately: SQLite file writer patterns (per-operation, pinned writer,
  writer queue, governed), storm and poison runs, DuckDB writer arms.
- Public API analyzers and package/API compatibility guards now protect the shipped 2.0.5 surface; new public members
  are declared explicitly in the unshipped API baselines.

### Fixed
- Writes and reads that failed on several databases now work, including Oracle spatial, PostgreSQL-family enums,
  Snowflake vectors, SAP HANA arrays/spatial values, Informix LOBs and intervals, Sybase high-precision temporal
  values, DuckDB negative intervals, TiDB BIT-safe upserts and SQLite extreme decimals.
- Upsert and batch-update audit fields now match create/update behavior; opaque row-version upserts no longer skip
  existing rows; version and audit state is restored correctly when a write fails.
- Connection-limit failures are translated consistently, including transaction begin and Oracle listener-capacity
  failures. Metrics now count affected rows, connection timings and pool waits correctly.
- SQLite is described by the library actually loaded, not a fixed table: the product version, the join types it
  supports, the parameter limit (including a custom `MAX_VARIABLE_NUMBER`) and JSON and window-function support are
  read from the live database, and the provider's own `GetSchema` answer is used when it has one.
- Every read path now converts values the same way (one type system), and fakeDb behavior is closer to the real
  providers it emulates.

## [2.0.5] - 2026-04-17

### Added
- `pengdows.crud.analyzers`, a new NuGet package of Roslyn analyzers for gateway code: `PGC008` (raw value injection
  into SQL predicates), `PGC025` (public gateway methods that call execution methods without forwarding an
  `IDatabaseContext`), a `DatabaseContext` singleton analyzer with a code fix, and a `WrapObjectName` composite
  identifier analyzer with a code fix.
- `pengdows.crud.opentelemetry`: 35 new instruments (about 42 in all): delta counters, observable gauges, pool
  counters, and the `PengdowsMetricsObserver.MeterName` constant for `AddMeter()`.
- Benchmarks: hydration hot path, SQLite write contention (the SingleWriter governor under a 100-way write storm),
  and an equal-footing SQL Server comparison against Dapper and EF Core.

### Fixed
- `PrimaryKeyTableGateway` resolves `context ?? _context` before every batch fast path; the null-context fast path
  used the wrong context.
- `BuildBaseRetrieve` with extra select expressions resolves the context before its early return.
- `GatewayMethodContextParameterAnalyzer` only exempts same-name delegation wrappers.

### Changed
- CI: coverage baselines moved from committed files to repository variables with an atomic ratchet; the
  OpenTelemetry package has an 83% coverage floor.

## [2.0.4] - 2026-03-31

### Fixed
- MariaDB read-only sessions use `SET SESSION tx_read_only` (MySQL keeps `transaction_read_only`).

### Changed
- SingleWriter documentation describes governor-serialized ephemeral writes with no pinned writer connection.

## [2.0.3] - 2026-03-27

### Added
- Typed exception translation for every database: Firebird, DuckDB and Oracle translators; `ReadOnlyViolationException`
  for SQLite and DuckDB writes against a read-only connection or database; more SQLite and SQL Server error codes.
- OpenTelemetry pool gauges, separate success and failure command latency histograms, and pool computed averages.
- `IDatabaseContext.GetSupportedIsolationLevels()`; `ContextCreated` and `ContextRemoved` events on
  `ITenantContextRegistry`.
- TiDB in the always-on integration suite.

### Fixed
- The role-delta metrics path emitted nothing because of a logic error.
- Oracle: control characters in the application name no longer cause `ORA-06550` on connection open.
- The `SafeNonBlockingReads` profile guard covers YugabyteDB as well as PostgreSQL.
- `PoolGovernor` timeout-to-tick conversion no longer depends on a fragile integer-division order.

## [2.0.2] - 2026-03-24

### Changed
- Type coercion options live in `pengdows.crud.abstractions`.
- The interface API baseline is enforced in CI.
- Isolation resolution corrected for snapshot support, and degradation tightened.

### Added
- Database metrics events; configurable clock modes for the UUIDv7 generator.

### Fixed
- The deploy workflow runs on the default branch and skips release pushes on protected branches.

## [2.0.1] - 2026-03-22

### Fixed
- The NuGet publish is idempotent (`--skip-duplicate`), so a retry after a partial publish completes.
- README badge and the publish git identity.

## [2.0.0] - 2026-02-28

The 2.0 line: SQL-first, strongly typed, testable data access for .NET 8.

### Breaking changes from 1.0
- `EntityHelper<TEntity, TRowID>` is renamed `TableGateway<TEntity, TRowID>`.
- Interface-first: every public API is exposed through `pengdows.crud.abstractions`, with an API baseline enforced by
  `tools/interface-api-check`.
- Hot-path execution methods return `ValueTask`.
- Integration tests moved to their own project.

### Added
- Session settings overhaul, metrics, and `PrimaryKeyTableGateway` for entities identified only by `[PrimaryKey]`.
- `pengdows.crud.fakeDb`, a complete ADO.NET provider for tests that need no database.

[2.0.6]: https://github.com/pengdows/pengdows.crud/compare/v2.0.5...2.0.6
[2.0.5]: https://github.com/pengdows/pengdows.crud/compare/v2.0.4...v2.0.5
[2.0.4]: https://github.com/pengdows/pengdows.crud/compare/v2.0.3...v2.0.4
[2.0.3]: https://github.com/pengdows/pengdows.crud/compare/v2.0.2...v2.0.3
[2.0.2]: https://github.com/pengdows/pengdows.crud/compare/v2.0.1...v2.0.2
[2.0.1]: https://github.com/pengdows/pengdows.crud/compare/v2.0.0...v2.0.1
[2.0.0]: https://github.com/pengdows/pengdows.crud/releases/tag/v2.0.0
