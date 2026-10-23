# Supported Databases

pengdows.crud supports 23 directly supported database products via the `SupportedDatabase` [Flags] enum. Some are managed-service or wire-compatible variants that reuse an existing dialect; the table lists every enum value:

| Enum Value | Product |
|---|---|
| `PostgreSql=1` | PostgreSQL (including TimescaleDB, Citus, and Fujitsu Enterprise Postgres — see note below) |
| `SqlServer=2` | SQL Server / Express / LocalDB |
| `Oracle=4` | Oracle |
| `Firebird=8` | Firebird |
| `CockroachDb=16` | CockroachDB |
| `MariaDb=32` | MariaDB |
| `MySql=64` | MySQL (including Percona Server for MySQL — see note below) |
| `Sqlite=128` | SQLite |
| `DuckDB=256` | DuckDB |
| `YugabyteDb=512` | YugabyteDB |
| `TiDb=1024` | TiDB |
| `Snowflake=2048` | Snowflake (opt-in; cloud-only, requires credentials) |
| `AuroraMySql=4096` | Aurora MySQL (AWS managed; detected at runtime, delegates to MySQL dialect) |
| `AuroraPostgreSql=8192` | Aurora PostgreSQL (AWS managed; detected at runtime, delegates to PostgreSQL dialect) |
| `SingleStore=65536` | SingleStore (formerly MemSQL); detected at runtime, delegates to MySQL dialect — see note below |
| `FlatFile=32768` | [pengdows.flatfile](https://github.com/pengdows/pengdows.flatfile) — embedded ADO.NET provider over CSV/TSV/delimited/fixed-width/NDJSON files |
| `SybaseASE=131072` | Sybase (SAP) Adaptive Server Enterprise — dedicated `SybaseAseDialect`, T-SQL family — see note below |
| `Db2=16384` | IBM Db2 for Linux/Unix/Windows (LUW) — dedicated `Db2Dialect`. **Only LUW is supported; Db2 for z/OS and Db2 for i are not** — see note below |
| `Spanner=262144` | Google Cloud Spanner through its PostgreSQL interface / PGAdapter |
| `Informix=524288` | IBM Informix Dynamic Server |
| `SapHana=1048576` | SAP HANA (opt-in integration coverage) |
| `InterBase=2097152` | Embarcadero InterBase (opt-in integration coverage) |
| `Access=4194304` | Microsoft Access / Jet/ACE (Windows-only; the integration suite runs it automatically on Windows and not elsewhere, `testbed/Program.cs`) |

> **New in 2.0.6:** `Db2`, `FlatFile`, `SingleStore`, `SybaseASE`, `Spanner`, `Informix`, `SapHana`, `InterBase` and `Access` are the first databases added in 2.0; 2.0.0 to 2.0.5 had the other fourteen. Their integration status is in [integration-status.md](integration-status.md) and the release history in [CHANGELOG.md](../CHANGELOG.md).

> **SQL-92 fallback:** If dialect detection cannot identify the connected product, pengdows.crud falls back to a conservative SQL-92 compatible dialect. SQL-92 is a fallback behavior, not a distinct supported database product, and has no `SupportedDatabase` enum value.

> **Aurora variants:** `AuroraMySql` and `AuroraPostgreSql` are managed AWS services with no Docker image. They are detection labels: `DatabaseDetectionService` reports them as the context's `Product`, and everything else behaves exactly as MySQL/PostgreSQL — the same dialect class, session settings, isolation levels, type mappings (JSON, arrays, ranges, network types) and exception translation. Code that branches on the engine should treat both values alike (e.g. `Product is SupportedDatabase.PostgreSql or SupportedDatabase.AuroraPostgreSql`). Until 2.0.6 some of that did not hold: isolation levels, advanced-type mappings and read coercion were keyed on `MySql`/`PostgreSql` only (VAR-001). No separate integration suite is required; 3.0 folds the two values into `MySql`/`PostgreSql` (VAR-002).

> **Verified PostgreSQL/MySQL-compatible forks:** Percona Server for MySQL, Citus, TimescaleDB, and Fujitsu Enterprise Postgres are not distinct `SupportedDatabase` values — each still reports itself via the standard `DataSourceProductName`/version-string mechanism as "MySQL" or "PostgreSQL", so `DatabaseDetectionService` resolves them to `SupportedDatabase.MySql`/`PostgreSql` and they run through the exact same dialect and test suite as the vanilla engine. All four were verified live (full `PostgreSQLTestProvider`/MySQL test-provider suite, 100% pass, same check count as vanilla) and are always-on entries in the testbed (`testbed/ParallelTestOrchestrator.cs`) — not opt-in. Fujitsu Enterprise Postgres needs two non-default container settings to start at all: it listens on port **27500**, not 5432, and its entrypoint has no "skip if this is the default superuser" special-case, so configuring it via `PG_USER=postgres` crashes the container ("role postgres already exists") — use `PG_ADMIN_PASSWORD` instead to set the existing superuser's password. **EDB Postgres Extended** is very likely also just `PostgreSql` (same wire-protocol/version-string shape), but has no public Docker image to verify against — EDB moved it behind a private, subscription-gated registry — so it is *not* claimed as verified here.

> **SingleStore:** unlike the verified forks above, SingleStore genuinely needs its own `SupportedDatabase` value — it reports itself via schema/`SELECT VERSION()` as generic, indistinguishable MySQL (`5.7.32` with no marker), so `DatabaseDetectionService` runs a dedicated `SHOW VARIABLES LIKE 'memsql_version'` probe (a SingleStore-only system variable, structurally identical to the `SHOW VARIABLES LIKE 'aurora_version'` Aurora MySQL probe; `@@memsql_version` raised "Unknown system variable" on the others) to tell it apart. Once detected, it delegates to `MySqlDialect` the same way `AuroraMySql` does. Verified live against `ghcr.io/singlestore-labs/singlestoredb-dev`: core CRUD passes, and stored procedures need SingleStore's own syntax — `CREATE PROCEDURE proc() AS BEGIN ECHO SELECT ...; END` rather than MySQL's bare `BEGIN SELECT ...; END` (a real syntax error on SingleStore) — `CALL` invocation and quoted-identifier handling are otherwise identical to MySQL/MariaDB. SingleStore is an always-on entry in the testbed (`testbed/SingleStore`), and its integration suite is green ([integration-status.md](integration-status.md)).
>
> **SingleStore concurrency (live, 2026-10-17, 9.1.1):** two concurrent multi-row `INSERT ... ON DUPLICATE KEY UPDATE` statements on overlapping keys deadlock; the engine kills both at its lock-wait timeout (about 30 s), the kill lands in the commit phase, and the provider raises error 1997 ("the outcome is unknown ... Please check if it succeeded"). One caller works (264 ms); two fail. pengdows.crud reports it as `AmbiguousResultException` (the write may have been applied). Single-row upserts under 16-way contention on one key pass. Keep overlapping batch upserts to one caller at a time on SingleStore.

> **FlatFile:** [pengdows.flatfile](https://github.com/pengdows/pengdows.flatfile) is a file-backed ADO.NET provider over CSV/TSV/pipe/delimited/fixed-width/NDJSON files. The dialect is verified against the provider source (0.2.1-preview.1) and runs in the testbed (which references 0.2.1-preview.2). Parameters are named `:name` (positional `?` also works, but not mixed in one statement). `DbMode.Best`, `Standard` and `PreventDatabaseUnload` resolve to `SingleWriter`: the provider allows one non-readonly connection per database, and a second in-process writer waits `connectionTimeout` then throws `TimeoutException`; reads use `readonly=true` connections. Isolation: `ReadUncommitted`, `ReadCommitted` and `RepeatableRead` (a per-table snapshot on first read); `Serializable`/`Snapshot` requests and `StrictConsistency` throw; `SafeNonBlockingReads` runs as `RepeatableRead`, `FastWithRisks` as `ReadUncommitted`. Savepoints (create/rollback/release), `SET TRANSACTION READ ONLY`, `MERGE` upserts, schema-qualified tables and `OFFSET ... FETCH` paging are supported; there is no `LIMIT`, `RETURNING`, identity column or stored procedure, so `[Id(false)]` keys need a correlation-token column. Text columns cannot tell `NULL` from `''` unless the table declares `CREATE TABLE ... WITH (NULLTOKEN = '...')`. Server version comes from `FlatFileConnection.ServerVersion`. `pengdows.crud` takes no dependency on the `pengdows.flatfile` package.

> **Sybase (SAP ASE):** has its own dedicated `SybaseAseDialect` (not a fork delegating to another dialect) and a real testbed container (`nguoianphu/docker-sybase`), verified live against ASE 16. Notable genuine differences from every other T-SQL/SQL-92-family dialect here, all confirmed live rather than assumed from SQL Server parity: MERGE works but rejects a trailing statement-terminator semicolon (`RequiresMergeStatementTerminator => false`, same mechanism Oracle uses); `;` is rejected as a multi-statement batch separator entirely, not just as a trailing terminator (`SupportsSemicolonStatementSeparator => false`); no multi-row `INSERT ... VALUES`, no `VALUES`-derived-table-as-MERGE-source, and no `LIMIT`/`OFFSET` paging (uses `SELECT TOP N` like SQL Server instead); NOT NULL-by-default columns and a non-Unicode default charset. `AdoNetCore.AseClient`'s `AseException` does not derive from `DbException` and `GetSchema()` is unimplemented — both are handled generically via a duck-typed "Errors collection" fallback in `DbExceptionTranslationSupport` rather than Sybase-specific special-casing.

> **Sybase ASE Guids:** store Guids in `BINARY(16)`. `SybaseAseDialect` passes `DbType.Guid` straight to AdoNetCore.AseClient (unchanged since 2.0.5), which writes the 16-byte `Guid.ToByteArray()` form; that reads back as the same Guid and matches in equality lookups. Into a `CHAR(36)` column the driver writes those 16 bytes as characters, which do not read back as a Guid. Verified live on ASE 16.0 SP02.

> **Firebird `DateTimeOffset`:** FirebirdClient rejects `DbType.DateTimeOffset`, so on Firebird 4+ a `DateTimeOffset` is sent as an `FbZonedDateTime` holding the UTC instant. The driver encodes it against the column type the server reports: a `TIMESTAMP WITH TIME ZONE` column stores the instant, and a plain `TIMESTAMP` column stores the UTC wall time (the same value earlier releases stored). Zoned columns read back as `DateTimeOffset`/UTC `DateTime`. Firebird 3 has no zoned types and keeps the UTC-`DateTime` mapping. Verified live on Firebird 5.0.4 with FirebirdClient 10.3.3.

> **Informix special registers:** an *unqualified* quoted identifier named after a special register (`user`, `today`, `current`, `sitename`, `dbservername`, `current_user`) resolves to the register, not the column, wherever it appears in an expression. For example, `DELETE FROM "t" WHERE "user" = ?` compares the session user name. The gateways table-qualify every column reference on Informix, so generated SQL is safe. In your own SQL, always qualify such columns (`"t"."user"`, or an alias). Informix's .NET provider also trims trailing spaces from every `VARCHAR`/`LVARCHAR` value it reads, with no option to turn it off (IBM APAR IC63704); see `ISqlDialect.PreservesTrailingWhitespace`.

> **IBM Db2:** only Db2 for Linux/Unix/Windows (LUW) is supported. The Db2 family is three separate IBM products sharing a common SQL subset (Db2 LUW, Db2 for z/OS, Db2 for i), not one engine on different operating systems, and their real features differ — transactions among them (e.g. IBM i commitment control depends on journaling) — so Db2 for z/OS and Db2 for i are **not supported**. A Db2 server not recognized as LUW (via `SYSPROC.ENV_GET_INST_INFO()`, which exists only on LUW) logs a warning rather than refusing, because a restricted LUW user may be unable to run the recognition query. They are planned as separate `SupportedDatabase` values with their own dialects once they can be tested (tracker VAR-003). The dedicated `Db2Dialect` is verified live against Db2 LUW (`ibmcom/db2`) in the always-on testbed run (`testbed/Db2`). **Pool-size caveat (IBM driver, confirmed live):** IBM.Data.Db2 fixes a pool's size the first time a connection string opens it and rejects a later string that asks a different size for the same server and database. `PreventDatabaseUnload` needs at least two pooled connections, so `DbMode.Best` stays `Standard` when you configure a maximum pool size below 2; requesting `PreventDatabaseUnload` explicitly together with `Max Pool Size=1` fails in the driver with "Invalid argument" — use a maximum of at least 2 for that mode. `DbMode.Best`: on a positively detected Db2 LUW server it selects `PreventDatabaseUnload` (LUW's default implicit activation deactivates an idle database; measured ~1.1 s per cold connection versus ~4 ms with a sentinel); any other or unrecognized Db2 server keeps `Standard`, and an explicit `Standard` is always honored. Generated-key retrieval wraps the ENTIRE insert statement — `SELECT "Id" FROM FINAL TABLE (INSERT INTO t (...) VALUES (...))` — rather than appending a trailing RETURNING/OUTPUT clause; the dialect owns that shape. Isolation levels (Db2's UR/CS/RS/RR map to ReadUncommitted/ReadCommitted/RepeatableRead/Serializable) and the portable profile mapping are declared on `Db2Dialect` (`GetSupportedIsolationLevels`, `GetIsolationProfileMapping`). Stored procedures use SQL-standard `CALL` syntax (same wrapping style as MySQL/MariaDB); a bare `SAVEPOINT name` is rejected (`SQL0104N`) — Db2 requires `ON ROLLBACK RETAIN CURSORS`. IBM's `DB2Exception` declares its own all-caps `SQLState` property (ambiguous with the inherited `DbException.SqlState`) and often doesn't populate SqlState via any property at all — both are handled by `DbExceptionTranslationSupport`'s ambiguity-safe reflection scan plus a message-embedded-SQLSTATE regex fallback, not Db2-specific special-casing.

Providers must support `DbProviderFactory` and `GetSchema("DataSourceInformation")`.

## Adding a New Database: Type Contract

Adding a database is not complete when basic CRUD SQL executes. The dialect must also describe
how the provider represents its values and make those representations work through the central
coercion system. Application code should not need provider-specific converters for a type the
database support claims to cover.

The responsibility is divided deliberately:

| Layer | Responsibility |
|---|---|
| `SqlDialect` | Vendor SQL, capabilities, parameter binding, provider-specific command/connection configuration, and database type facts |
| `TypeCoercionHelper` / `CoercionRegistry` | Shared conversion rules, provider-specific coercion dispatch, null handling, precision/range checks, JSON, sequences, and CLR target conversion |
| `TableGateway` / `DataReaderMapper` | Apply the resolved coercion plan when hydrating objects and bind converted values when writing |
| Database documentation and test matrix | State the supported database/CLR mappings, limitations, version gates, and real-provider evidence |

For every supported database, verify at minimum:

- Primitive numerics, including signed/unsigned and wide integer values.
- Decimal precision, scale, rounding, overflow, `NaN`, and infinity behavior where applicable.
- `DateOnly`, `TimeOnly`, `DateTime`, `DateTimeOffset`, timezone, precision, and UTC semantics (why UTC: [utc-and-time.md](utc-and-time.md)).
- `Guid` text and binary representations, including byte order.
- Boolean, character, string, binary, stream, and large-object values.
- Null and `DBNull` behavior for reads, writes, nullable properties, and non-nullable properties.
- Enums, including numeric values, string literals, `[Flags]`, nullable enums, and invalid values.
- JSON strings, native JSON/provider JSON values, `JsonDocument`, `JsonElement`, `JsonNode`, `JsonValue`, and arbitrary complex CLR types marked `[Json]`.
- Arrays, lists, vectors, ranges, intervals, spatial values, network values, and other provider-specific types exposed by the provider.
- Generated keys and any provider-specific value returned by `RETURNING`, `OUTPUT`, identity functions, or compound statements.
- Unsupported or lossy conversions: these must fail as `DataMappingException` or another documented mapping error, never silently become a default value.

The dialect must provide any required provider hooks for parameter type configuration or value
normalization. The central coercion layer owns the conversion machinery; the dialect owns the
provider truth. A new dialect must not duplicate general conversion logic or require application
code to branch on the database product.

Each supported type needs both directions tested where meaningful:

```text
provider value -> CLR value -> provider parameter
```

The evidence should include fakeDb tests for generated SQL, parameter shape, and failure paths,
plus real-provider integration tests for round trips, precision, nulls, provider-native value
shapes, and transaction behavior. A dialect that passes basic CRUD but has not completed this
type contract is partially supported, not fully supported.

## Minimum Server Versions

Two thresholds matter for each database:

- **Floor** — the oldest version where basic CRUD (connect, parameterized SELECT/INSERT/UPDATE/DELETE) works without errors. Below this, the library will crash or silently misbehave.
- **Recommended minimum** — the oldest version where all commonly-needed features (upsert, savepoints, session-level read-only enforcement, auto-generated IDs) are fully operational.

| Database | Floor | Recommended Min | Key reason for recommended floor |
|----------|-------|-----------------|-----------------------------------|
| SQL Server | 2008 (v10) | 2016 (v13) | JSON support (`JSON_VALUE`) requires v13; MERGE available from v10 |
| PostgreSQL | 9.5 | 15 | `INSERT ON CONFLICT` (upsert) added in 9.5; `MERGE` added in 15 |
| Oracle | 12c | 19c | Identity columns and JSON both require 12c; SQL:2016 compliance at 19c |
| MySQL | 5.7.20 | 8.0 | `transaction_read_only` session variable requires 5.7.20; CTEs/window fns at 8.0 |
| MariaDB | 10.2 | 10.4 | CTEs and window functions at 10.2; `tx_read_only` session variable requires 10.1 |
| SQLite | 3.24 | 3.35 | `INSERT ON CONFLICT` (upsert) requires 3.24; `RETURNING` clause requires 3.35 |
| Firebird | 2.5 | 3.0 | MERGE and CTEs at 2.0; window functions require 3.0; declared minimum is 2.5 |
| DuckDB | 0.8.0 | 1.0.0 | `SET access_mode` since 0.3.0; stable API and SQL:2016 at 1.0; MERGE at 1.4 |
| CockroachDB | ~22.x | latest | PostgreSQL 13-compatible wire protocol; version not user-controlled in the same way |
| YugabyteDB | 2.x | latest | PostgreSQL 11+ compatible; MERGE intentionally disabled (throws `0A000`) |
| Snowflake | service | service | Cloud service — version managed by Snowflake; no minimum to configure |
| TiDB | not recorded | not recorded | Runs on `MySqlDialect`; the testbed's per-version checks cover v7.5.7 and v8.5.7 (`TestbedImageMatrix`), and the all-types integration matrix runs on the newest, v8.5.7. `READ ONLY` transactions are a no-op unless `tidb_enable_noop_functions` is set |
| Aurora MySQL / Aurora PostgreSQL | service | service | AWS-managed; same floor and recommendation as the MySQL / PostgreSQL row for the engine version the cluster runs |
| SingleStore | not recorded | not recorded | Detected by `SHOW VARIABLES LIKE 'memsql_version'`, runs on `MySqlDialect`; suite runs `ghcr.io/singlestore-labs/singlestoredb-dev` |
| Db2 (LUW) | not recorded | not recorded | The dialect has no version gate; the suite covers 11.5.0.0a and 11.5.8.0 (`ibmcom/db2`) |
| Informix | not recorded | not recorded | No version gate in `InformixDialect`; the suite runs `icr.io/informix/informix-developer-database:latest` |
| Sybase ASE | not recorded | not recorded | No version gate in `SybaseAseDialect`; verified live against ASE 16 (SP02) |
| Spanner | not recorded | not recorded | PostgreSQL interface through PGAdapter; the suite runs Spanner Omni `2026.r2.1-beta` with the PGAdapter image |
| SAP HANA, InterBase, Access | not recorded | not recorded | No version gates in their dialects; opt-in (HANA, InterBase) or Windows-only (Access) integration coverage, no server version recorded in the repo |
| FlatFile | n/a | n/a | Embedded provider; dialect verified against `pengdows.flatfile` 0.2.1-preview.1 source, testbed runs 0.2.1-preview.2 |

> **MySQL / MariaDB read-only note:** The `SET SESSION transaction_read_only = 1` syntax requires
> MySQL 5.7.20+. MariaDB uses `SET SESSION tx_read_only = 1` which is available in 10.1+.
> Earlier versions only support `SET SESSION TRANSACTION READ ONLY`
> which applies to the next transaction only, not the session persistently.

### Feature Version Thresholds

What version of each database first enables each major feature:

| Feature | SQL Server | PostgreSQL | MySQL | MariaDB | Oracle | SQLite | DuckDB | Firebird |
|---------|-----------|-----------|-------|---------|--------|--------|--------|---------|
| **MERGE / Upsert** | 2008 (v10) | 15 | — (uses `ON DUPLICATE KEY`) | — (uses `ON DUPLICATE KEY`) | 9i (always on) | — (uses `ON CONFLICT`) | 1.4 | 2.0 |
| **INSERT ON CONFLICT** | — | 9.5 (always on) | — | — | — | 3.24 (always on) | 1.0 (always on) | — |
| **ON DUPLICATE KEY UPDATE** | — | — | always on | always on | — | — | — | — |
| **INSERT RETURNING** | always on | always on | — | 10.5 | always on | 3.35 | always on | always on (2.1+) |
| **JSON types** | 2016 (v13) | 9.x | 5.7.8 | — (no native JSON) | 12c | 3.45 | always on | — |
| **CTEs** | always on | always on | 8.0 | 10.2 | always on | 3.8.3 | always on | 2.0 |
| **Window functions** | always on | always on | 8.0 | 10.2 | always on | 3.25 | always on | 3.0 |
| **Savepoints** | always on | always on | always on | always on | always on | always on | — | always on |
| **DROP TABLE IF EXISTS** | always on | always on | always on | always on | — (PL/SQL only) | always on | always on | always on |
| **Identity / autoincrement** | always on | always on | always on | always on | 12c | always on | always on | always on |

`—` means the feature is either not supported or uses a different mechanism on that database.

This table covers only the databases whose dialects gate a capability on the server version. The others are not columns because their dialects do not gate on version: CockroachDB, YugabyteDB and Spanner inherit the PostgreSQL dialect's flags (`SupportsMerge` is `false` on all three: excluded for CockroachDB in `PostgreSqlDialect`, overridden in `YugabyteDbDialect` and `SpannerDialect`), TiDB, SingleStore and the Aurora variants follow their MySQL/PostgreSQL parent, and Snowflake, Db2, Informix, SAP HANA, InterBase, Sybase ASE, Access and FlatFile declare fixed flags. Read the flags for the connected server at runtime through `context.Dialect` (see [capability-discovery.md](capability-discovery.md)) rather than from this document.

### Latent Version Mismatches

The following features have no version gate in the dialect code but require a minimum server version to function. Connecting to an older server will produce SQL errors at runtime rather than a capability flag returning `false`:

- **PostgreSQL `SupportsInsertOnConflict = true` is ungated** — requires PostgreSQL 9.5+. A server running 9.0–9.4 will receive `INSERT ... ON CONFLICT` SQL it cannot parse.
- **SQLite `SupportsInsertOnConflict = true` is ungated** — requires SQLite 3.24+. A SQLite file opened on 3.23 will fail on upsert SQL.
- **Oracle `SupportsIdentityColumns = true` is ungated** — identity columns (`GENERATED AS IDENTITY`) require Oracle 12c. Pre-12c servers will fail when inserting entities with `[Id(false)]`.
- **SQL Server MERGE at `IsVersionAtLeast(10)` is broader than the declared "2012+" header** — SQL Server 2008 (v10) will pass the version check and receive MERGE SQL. The "2012+" comment in the source is a conservative recommendation, not enforced by the gate.

## Minimum Driver Versions

`DatabaseContext` refuses these drivers, or these versions of them, before it opens a connection, throwing `NotSupportedException`
(`internal/ProviderVersionRequirements.cs`):

| Driver | Minimum | Why |
|--------|---------|-----|
| MySqlConnector | 2.6.1 | 2.3.0 through 2.6.0 fail a prepared `INSERT ... RETURNING` executed a second time on MariaDB, whatever the parameter values ("Expected result set to have 0 columns, but it contains 1 columns"), and close the connection. pengdows.crud prepares statements on MySqlConnector, so any repeated insert hits it. 2.2.7 and older don't have the bug but are below the minimum too. Confirmed live on MariaDB 11.4.13, 2026-10-22. Every 2.x release reports assembly version 2.0.0.0, so the file version is compared. |
| pengdows.flatfile | 0.2.1-preview.6 | Earlier releases refuse DateOnly/TimeOnly parameters and cannot compare a DateTime or TimeSpan with a DATE or TIME column, so a DATE or TIME column cannot be filtered; preview.2 to preview.5 also wrote a TIME outside a day and then failed every read of the table (pengdows/pengdows.flatfile#1). Every 0.2.1 preview reports file version 0.2.1.0, so the informational version is compared, prerelease-aware. |
| MySql.Data | refused | Fails the ADO.NET contract pengdows.crud relies on: `StateChange` reports `Open → Open` when a connection opens, reader access before `Read` doesn't throw, parameter defaults are wrong, async commands can hang past their timeout, and it substitutes backslash-escaped literals into SQL text (BP-115) and threw reading a `BINARY(16)` Guid (COR-014). Fixes are submitted upstream (mysql/mysql-connector-net #95–#112) but not released. Use MySqlConnector. |

## Default Pool Sizes (Provider vs Practical)

| SupportedDatabase | Default Max Pool Size (provider) | Practical / Recommended Max Pool Size | Key Practical Limits & Advice |
|-------------------|----------------------------------|---------------------------------------|-------------------------------|
| SqlServer (Microsoft.Data.SqlClient) | 100 | 50-200 (often 100-150 safe) | Per app instance rarely >200; total server connections limited by memory (approx 10-20 KB per conn + query plans). Rule of thumb: 2-4x CPU cores per app instance, or 100-300 total cluster-wide. Large pools (>500) often cause context switching thrash on DB server. |
| PostgreSql (Npgsql) | 100 (since ~3.1) | 20-100 per app instance (often 30-80 optimal) | Strong consensus: 2-4x CPU cores on the DB server. Each conn ~1-3 MB RAM on Postgres side. >100-150 often overloads small/medium instances. Use PgBouncer if >50-100 needed per app; set app pool to 20-50 and let PgBouncer multiplex. |
| MySql / MariaDb (MySqlConnector / MySql.Data) | 100 | 50-200 (often 100-150) | Similar to SqlServer: 100 is safe default. Threads are lighter than Postgres but still ~1-2 MB per conn. Practical ceiling often 200-500 before thread contention or memory pressure. ProxySQL or MySQL Router recommended beyond ~200. |
| Oracle (Oracle.ManagedDataAccess) | 100 | 50-200 | Sessions are heavier (few MB each). Practical max often 100-300 before session/memory limits kick in. Enterprise tuning often caps at 100-150 per instance. |
| Sqlite (Microsoft.Data.Sqlite) | Effectively unlimited (pooling enabled by default since v6, no hard max) | 1-20 (or unlimited for in-memory) | Single-writer lock means >1-4 concurrent writers kills perf. Practical: keep pool small (5-20) or disable pooling for high concurrency. In-memory/shared can handle more, but still file-lock limited on disk. |
| DuckDb (.NET DuckDB) | Effectively unlimited (no hard pool limit in most impls) | 1-8 (or up to threads count) | Embedded: connection creation is cheap. Practical: single connection often best; multiple only if parallelizing queries. Limit to CPU cores or threads setting. No real pool exhaustion; bottleneck is CPU/RAM for queries, not connections. |
| CockroachDb / YugabyteDb / Spanner (Npgsql) | 100 (Npgsql) | Follow the PostgreSql row | Run on `PostgreSqlDialect`: same `Minimum Pool Size` / `Maximum Pool Size` keywords, dialect default 100. Spanner reaches PGAdapter over Npgsql. |
| AuroraPostgreSql | 100 (Npgsql) | Follow the PostgreSql row | Same dialect as PostgreSql (detection label only). |
| TiDb / SingleStore / AuroraMySql | 100 (MySqlConnector / MySql.Data) | Follow the MySql row | Run on `MySqlDialect`: `MinimumPoolSize` / `MaximumPoolSize` with MySqlConnector, `Min Pool Size` / `Max Pool Size` with MySql.Data; dialect default 100. |
| Firebird (FirebirdClient) | Not verified in this repo | Not verified | Keywords `MinPoolSize` / `MaxPoolSize` (`FirebirdDialect`); dialect default 100. |
| Snowflake (Snowflake.Data) | 10 (Snowflake.Data 4.x, per the dialect comment) | Not verified | Keywords `minPoolSize` / `maxPoolSize`; `SnowflakeDialect.DefaultMaxPoolSize` is 10, kept aligned with the provider unless the caller configures a limit. |
| Db2 (IBM.Data.Db2) | Not verified in this repo | Not verified | Keywords `Min Pool Size` / `Max Pool Size`; dialect default 100. The driver fixes a pool's size the first time a connection string opens it (see the Db2 note above). |
| Informix (Informix.Net.Core) | 100 (builder default, per the dialect comment) | Not verified | `InformixDialect` registers no pool-size keyword, so a `MaxPoolSize` you write into the connection string is not read by pengdows.crud; it governs on the dialect default 100. |
| Sybase ASE (AdoNetCore.AseClient), SAP HANA, InterBase | Not verified in this repo | Not verified | No pool-size keyword registered in `SybaseAseDialect`, `HanaDialect` or `InterBaseDialect`, so pengdows.crud governs on the dialect default 100 and does not read an explicit maximum from the connection string. |
| Access (Jet/ACE over OLE DB), FlatFile | Not applicable | Not applicable | `SupportsExternalPooling` is `false`: no ADO.NET pool keywords are injected (Access rejects an unrecognised `Pooling` keyword). Both run in `SingleWriter`. |

The "provider default" column is what the ADO.NET provider does on its own; pengdows.crud's own limit (the governor) is the explicit `MaxPoolSize` in the connection string where the dialect registers a keyword for it, otherwise `DefaultMaxPoolSize` on the dialect (100 unless a row above says otherwise; `int.MaxValue` for SQLite and DuckDB).

---

## Read-Only Enforcement Matrix

pengdows.crud enforces read-only intent at multiple levels where supported by the database engine and provider.

| Database | Connection String | Session / transaction SQL | Dual Enforcement | Enforcement Strategy |
| :--- | :---: | :---: | :---: | :--- |
| **PostgreSQL** | Yes | Yes | **Yes** | `Options='-c default_transaction_read_only=on'` + `SET default_transaction_read_only = on` |
| **CockroachDB**, **YugabyteDB** | Yes | Yes | **Yes** | Inherit `PostgreSqlDialect` unchanged (YugabyteDB does not bake startup options, so the `SET` runs on every checkout) |
| **Spanner** | Yes | Yes | **Yes** | Inherits `PostgreSqlDialect`, but the database does not refuse a write in a read-intent transaction (`EnforcesReadOnlyTransactions` is `false`, live); only the library guard protects |
| **SQLite** | Yes | No | No | `Mode=ReadOnly` (file opened read-only; not applied to `:memory:` databases) |
| **DuckDB** | Yes | No | No | `access_mode=READ_ONLY` (file-open attribute; cannot be changed on an open connection) |
| **SQL Server** | Yes | No | No | `ApplicationIntent=ReadOnly`: an Availability Group routing hint, not server-side enforcement (`SqlServerDialect.GetReadOnlyConnectionParameter`); hard enforcement needs read-only credentials or permissions |
| **MySQL** | No | Yes | No | `SET SESSION transaction_read_only = 1` (5.7.20+) |
| **MariaDB** | No | Yes | No | `SET SESSION tx_read_only = 1` (10.1+) |
| **TiDB** | No | Yes | No | MySQL's `SET SESSION transaction_read_only = 1`, but TiDB treats `READ ONLY` as a no-op unless `tidb_enable_noop_functions` is set |
| **SingleStore** | No | Yes | No | MySQL's `SET SESSION transaction_read_only = 1`; SingleStore accepts the statement and still allows the write (live) |
| **Aurora MySQL**, **Aurora PostgreSQL** | as parent | as parent | as parent | Same dialect as MySQL / PostgreSQL |
| **Snowflake** | No | No | No | None — Snowflake has no session read-only mode; use read-only roles/credentials |
| **Oracle** | No | Yes | No | `SET TRANSACTION READ ONLY` (issued at read-only transaction start; no persistent session mode) |
| **Firebird** | No | Yes | No | `SET TRANSACTION READ ONLY` |
| **InterBase** | No | No | No | None wired: the driver supports read-only transactions through `IBTransactionOptions` at creation time, which the dialect hook (SQL after the transaction opens) cannot use |
| **Db2** | No | No | No | None: `Db2Dialect` defines no read-only connection parameter or SQL; only the library guard applies |
| **Sybase ASE** | No | No | No | None: `SybaseAseDialect` defines no read-only connection parameter or SQL; only the library guard applies |
| **Informix** | No | Yes | No | `SET TRANSACTION READ ONLY` at read-only transaction start |
| **SAP HANA** | No | Yes | No | `SET TRANSACTION READ ONLY` at read-only transaction start; the session is reset to `SET TRANSACTION READ WRITE` because HANA's setting is sticky across commits |
| **FlatFile** | Yes | Yes | No | `readonly=true` connection parameter + `SET TRANSACTION READ ONLY` |
| **Access** | Yes | No | No | `Mode=Read` (ACE read-only open; a write fails with "must use an updateable query") |

> **Dual Enforcement:** For PostgreSQL, the intent is baked into the connection string (forcing the driver level) AND re-asserted via SQL on every lease, providing maximum security against "dirty" connections in a shared pool. SQLite and DuckDB rely on the connection-string parameter alone: it is applied when the database file is opened, which is stronger than a session flag any caller could reset.

> **Oracle read-only transactions are snapshots.** `SET TRANSACTION READ ONLY` reads the whole transaction as of its start, and Oracle refuses that read for a table whose DDL is newer than the snapshot, which can include a table created a few seconds earlier (ORA-01466 "table definition has changed"). pengdows.crud raises it as a retryable `SerializationConflictException`; end the transaction and start a new one, as for ORA-08177.

### DuckDB driver compatibility

Verified 2026-10-18 against the full DuckDB integration suite on net8.0 and net10.0 (152 of 152 each): **DuckDB.NET 1.5.6 and
1.5.6.1**. pengdows.crud works around a few DuckDB.NET behaviors, and the workarounds stay correct on both versions. DuckDB.NET 1.5.5
and older are covered by the connection-close gate (`CommandBehavior.CloseConnection` is not used, because those releases close the
database before releasing the reader).

| DuckDB.NET behavior | 1.5.6 | 1.5.6.1 | What pengdows.crud does |
|---|---|---|---|
| Reading a negative `INTERVAL` as a `TimeSpan` | throws | works | Reads the interval from its months, days and microseconds; correct on both. |
| Reading an interval with months | `+1 month` throws, `-1 month` silently reads as zero | both throw | Months are read from the parts, never through `TimeSpan`. |
| Binding a negative `TimeSpan` | `OverflowException` | works | An interval is sent as DuckDB's own interval text with a string `DbType`; correct on both. |
| Binding a `TimeSpan` for a `TIME` column inside a `MERGE` source | works (sent as text) | **fails**: sent as an `INTERVAL`, which DuckDB cannot cast to `TIME` | A `TimeSpan` within one day written to a `TIME` column is sent as a `TimeOnly`, which binds as a `TIME` on every release (found while validating 1.5.6.1). |
| Binding a `BitArray` | fails (sent as its `ToString()`) | fails | A `BIT` value is sent as DuckDB's bit string. |

Interval handling above was fixed in DuckDB.NET by changes we contributed (Giorgi/DuckDB.NET#359 and #360); the workarounds remain
for older releases and for the cases still open. Code that bypasses the gateways, for example a hand-written `SELECT` read through
the driver, gets the driver's own behavior.

