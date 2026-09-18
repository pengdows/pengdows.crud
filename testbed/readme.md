# Integration Test Suite

**This project's own scope narrowed after a consolidation with `pengdows.crud.IntegrationTests`
(see `CLAUDE.md`'s "Adding a New Database" checklist, item 4) — most of what this document used
to describe as "the testbed's 17–19 checks" no longer runs here.** `testbed` now only does
container-provisioning-adjacent work per database: spin up a real Testcontainers instance
(or, for SQLite/DuckDB, a local file/memory connection), create the shared test table
(`CreateTable`), run a scalar-UDF smoke check and the `DbMode`/`PreventDatabaseUnload`
idle-unload probe, then dispose the container — plus a `RunAdditionalTestsAsync()` hook for the
handful of checks that genuinely need the live container itself (not just a connection string),
overridden today by SQLite, DuckDB, PostgreSQL, SQL Server, MariaDB, Spanner, and Db2's own
`TestProvider` subclasses.

Everything else — CRUD round-trips, parameter binding, transactions/isolation, stored procedures,
upsert/error-mapping/identifier-quoting capability probes, pool isolation, kill-connection
rollback behavior, `DbMode` lock-contention scenarios — now lives in `pengdows.crud.IntegrationTests`
as ordinary xUnit tests (see that project's `Core/`, `ErrorHandling/`, and `DatabaseSpecific/`
folders), using `DatabaseTestBase`/`IntegrationTestFixture` rather than this project's
container-orchestration machinery. `run-integration-tests.sh` runs both halves — this program and
that xUnit suite — as "the two halves of running the integration tests." Tests here still run via
Testcontainers (Docker) with up to 2 databases in parallel.

## Running

```bash
# All always-on databases (16 SQL engines + FlatFile)
dotnet run -c Release --project testbed

# Include Snowflake (requires credentials)
INCLUDE_SNOWFLAKE=true dotnet run -c Release --project testbed

# Run only specific databases
dotnet run -c Release --project testbed -- --only PostgreSQL

# Run selected configured versions
dotnet run -c Release --project testbed -- --only PostgreSQL --versions 16.4,15.0

# Override with any exact image references, including older tags
TESTBED_POSTGRESQL_IMAGES=postgres:9.5,postgres:16.4 dotnet run -c Release --project testbed -- --only PostgreSQL

# Exclude specific databases
dotnet run -c Release --project testbed -- --exclude SQLite

Per-engine overrides use `TESTBED_<ENGINE>_IMAGES` with two comma-separated image references. The `--versions` filter accepts configured labels or exact image references. Results are named `Engine [version]`.
```

## Always-on Databases

Oracle and several others were promoted from opt-in to unconditional as their integration
matured — this list reflects `ParallelTestOrchestrator.GetTestConfigurations()` as it actually
stands today, not the original smaller set:

SQLite, DuckDB, FlatFile (not an RDBMS — a flat-file provider, included here since it registers
the same way), PostgreSQL, Google Spanner, MySQL, MariaDB, SQL Server, CockroachDB, Firebird,
TiDB, YugabyteDB, Oracle, IBM Db2 LUW, SingleStore, Sybase ASE, Informix

## Opt-in Databases

| Database | Env var | Reason |
|----------|---------|--------|
| Snowflake | `INCLUDE_SNOWFLAKE=true` | Cloud-only, requires credentials |
| SAP HANA | `INCLUDE_SAPHANA=true` | Real Docker image, but a working container needs 16-32GB RAM — far beyond a standard CI runner and every database above |
| InterBase | `INCLUDE_INTERBASE=true` | A personal, non-shareable, node-locked Developer Edition license (registration state lives in a persistent volume, not the image) plus a native `libgds.so` required on the host running the testbed process — see `InterBaseTestContainer.cs` |
| Access | `INCLUDE_ACCESS=true` | No Docker image exists at all — Access isn't a server process. Windows-only (the ACE OLE DB provider and the ADOX COM interop used to create the `.accdb` file both require it) — see `AccessTestContainer.cs` |

> **SAP HANA**: `HanaTestContainer`/`HanaTestProvider` spin up `saplabs/hanaexpress`, single pinned
> image (no version matrix — see `HanaTestContainer.cs`). Confirmed live: full CRUD lifecycle and
> stored-procedure execution pass; container spinup takes ~2-3 minutes even with the image already
> pulled locally (`StartupWeightSeconds = 300`, the highest of any database here). Run in isolation:
> `INCLUDE_SAPHANA=true dotnet run --project testbed -- --only "SAP HANA"`.
>
> **Access**: `AccessTestContainer`/`AccessTestProvider` create a fresh `.accdb` file per run via
> ADOX COM interop (no Testcontainers/Docker at all — modeled on `SqliteTestContainer.cs`, not
> `InterBaseTestContainer.cs`'s externally-managed-container pattern). Confirmed live: full
> `TableGateway` CRUD round-trip, a real duplicate-key insert correctly throwing
> `UniqueConstraintViolationException`, `UpsertAsync` correctly throwing `NotSupportedException`
> (Access has no server-side upsert mechanism at all), and that `OleDbFactory` alone never implies
> Access. Run in isolation: `INCLUDE_ACCESS=true dotnet run --project testbed -- --only "Access"`.

---

## What Still Runs Here

### CreateTable
Drops and recreates a 10-column test table (`id`, `name`, `description`, `value`, `is_active`,
`created_at`, `created_by`, `updated_at`, `updated_by`) using dialect-specific DDL. Oracle uses
`NUMBER` + `SEQUENCE`/`TRIGGER`; SQL Server uses `IDENTITY`; MySQL uses `BIGINT AUTO_INCREMENT`,
etc. Runs for every database, every time.

### Scalar UDF
Creates and calls a scalar user-defined function inline in a `SELECT` statement
(`TestScalarUdf()`, a virtual no-op by default). Currently only implemented for Snowflake; every
other database skips this check silently.

### DbMode / PreventDatabaseUnload idle-unload probe
Measures cold-vs-warm connection latency after an idle period to validate the
`PreventDatabaseUnload` sentinel's actual effect (see `CLAUDE.md`'s connection-modes section for
the empirical background — this is the check that produced those numbers, not just documentation
asserting them).

### `RunAdditionalTestsAsync()` container-level hooks
A small number of databases override this for a check that genuinely needs the live container
itself, not just a pooled connection string — currently SQLite, DuckDB, PostgreSQL, SQL Server,
MariaDB, Spanner, and Db2. See each database's own `*TestProvider.cs` for what it actually checks;
these are container-provisioning-adjacent probes, not general CRUD/transaction coverage.

## Where the Rest Moved

The checks this document used to describe in detail — insert/retrieve/delete, transactions
(commit/rollback/read-your-writes/savepoints), stored procedures, parameter binding, row
round-trip and type-fidelity checks, concurrency, command/container reuse, upsert, paging, error
mapping, and identifier quoting — are not gone, they moved to `pengdows.crud.IntegrationTests` as
ordinary `[SkippableFact]` xUnit tests during the consolidation `CLAUDE.md` documents. Rough
mapping, by folder (exact per-check file names weren't individually re-verified for this pass —
grep the folder for the scenario name if you need the precise file):

| Old testbed scenario | Now lives in |
|---|---|
| Insert/Retrieve/Delete, row round-trip, type fidelity | `Core/BasicCrudTests.cs`, `Core/RoundTripTests.cs`, `Core/TypeHydrationTests.cs` |
| Transactions (commit/rollback/read-your-writes) | `Core/TransactionResilienceTests.cs` |
| Savepoints | `Core/TransactionResilienceTests.cs` (capability-gated per dialect's `SupportsSavepoints`) |
| Stored procedures | `Core/StoredProcedureTests.cs` |
| Parameter binding | `Core/ParameterBindingTests.cs` |
| Command/container reuse | `Core/SqlContainerReuseTests.cs` |
| Upsert / merge conflicts | `Core/MergeConflictTests.cs`, `Core/VersionedUpsertConflictTests.cs` |
| Identifier quoting | `Core/QuotingTortureTests.cs`, `Core/EmbeddedQuoteIdentifierIntegrationTests.cs` |
| Error mapping (constraint violations, deadlocks, timeouts, transient errors) | `ErrorHandling/ConstraintViolationTests.cs` and siblings |
| Pool isolation, kill-connection rollback, `DbMode` lock contention | `DatabaseSpecific/`, `Core/MultiTenantDialectVersionTests.cs`, `Core/RetryContextTests.cs` |

If you're adding integration coverage for a new behavior, it almost certainly belongs in one of
those `pengdows.crud.IntegrationTests` folders, not here — see `CLAUDE.md`'s "Adding a New
Database" checklist item 4 for the exact division of responsibility.

---

## Skip Matrix

**These checks now execute via `pengdows.crud.IntegrationTests` (see "Where the Rest Moved"
above), not this project — the matrix is kept here because it hasn't been re-homed into that
project's own docs yet, not because these checks still run in `testbed`.**

| Check | SQLite | DuckDB | MySQL | MariaDB | TiDB | Firebird | PostgreSQL | SQL Server | CockroachDB | YugabyteDB | Oracle | Snowflake |
|-------|--------|--------|-------|---------|------|----------|------------|------------|-------------|------------|--------|-----------|
| Stored Proc | ⏭ | ⏭ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Scalar UDF | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ✅ |
| DateTimeOffset binding | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ✅ | ✅ | ✅ | ✅ | ✅ | ⏭ |
| Guid binding | ⏭ | ✅ | ⏭ | ⏭ | ⏭ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⏭ |
| Savepoints | ⏭ | ✅ | ⏭ | ⏭ | ⏭ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⏭ |
| Unique violation check | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⏭ |

⏭ = skipped (dialect limitation)  ✅ = runs

**This table predates Spanner, Db2, SingleStore, Sybase ASE, Informix, SAP HANA, and InterBase
being added to the testbed and has not been re-verified against them** — treat it as accurate
for the twelve databases listed and unknown (not "all ✅") for the rest until someone checks each
one's actual skip behavior and adds a column.

---

## Architecture

```
testbed/
├── Program.cs                        Entry point; parses args and env vars
├── ParallelTestOrchestrator.cs       Starts containers, runs TestProvider per DB, collects results
├── TestProvider.cs                   Base class: CreateTable, scalar-UDF, idle-unload probe (~466 lines)
├── TestContainer.cs                  Shared container startup / wait-for-ready logic
├── Sqlite/                           SQLite — no container (file/memory based)
├── DuckDb/
├── Db2/
├── PostgreSQL/
├── Spanner/
├── MySQL/
├── mariaDb/
├── SqlServer/
├── Cockroach/
├── Firebird/
├── TiDB/
├── Yugabyte/
├── Oracle/                           Always-on (promoted from opt-in as integration matured)
├── SingleStore/
├── Sybase/
├── Informix/
├── Snowflake/                        Opt-in (INCLUDE_SNOWFLAKE=true)
├── SapHana/                          Opt-in (INCLUDE_SAPHANA=true)
├── InterBase/                        Opt-in (INCLUDE_INTERBASE=true)
└── Access/                           Opt-in (INCLUDE_ACCESS=true)
```

`TestProvider.cs`'s own detailed responsibilities (CreateTable, scalar-UDF check, idle-unload
probe, the `RunAdditionalTestsAsync` hook) are described above under "What Still Runs Here" — the
17-19 check CRUD/transaction/etc. battery this file used to run directly now lives in
`pengdows.crud.IntegrationTests` instead.

Each database subdirectory contains:
- `*TestContainer.cs` — starts the Testcontainers image and returns a connection string
- `*TestProvider.cs` — inherits `TestProvider`, overrides DDL and any dialect-specific checks

Provider overrides are minimal by design — any deviation from baseline behavior (e.g. Oracle
using `NUMBER` instead of `INT`, Oracle requiring distinct parameter names) is captured in the
override rather than branching inside the base class.
