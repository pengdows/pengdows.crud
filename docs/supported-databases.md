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
| `Db2=16384` | IBM Db2 — dedicated `Db2Dialect`. **LUW specifically supported and tested; z/OS and IBM i should work but are untested** — see note below |
| `Spanner=262144` | Google Cloud Spanner through its PostgreSQL interface / PGAdapter |
| `Informix=524288` | IBM Informix Dynamic Server |
| `SapHana=1048576` | SAP HANA (opt-in integration coverage) |
| `InterBase=2097152` | Embarcadero InterBase (opt-in integration coverage) |
| `Access=4194304` | Microsoft Access / Jet/ACE (Windows-only, opt-in integration coverage) |

> **SQL-92 fallback:** If dialect detection cannot identify the connected product, pengdows.crud falls back to a conservative SQL-92 compatible dialect. SQL-92 is a fallback behavior, not a distinct supported database product, and has no `SupportedDatabase` enum value.

> **Aurora variants:** `AuroraMySql` and `AuroraPostgreSql` are managed AWS services with no Docker image. They are detected at runtime via `DatabaseDetectionService` and delegate to the MySQL/PostgreSQL dialect respectively. No separate integration suite is required.

> **Verified PostgreSQL/MySQL-compatible forks:** Percona Server for MySQL, Citus, TimescaleDB, and Fujitsu Enterprise Postgres are not distinct `SupportedDatabase` values — each still reports itself via the standard `DataSourceProductName`/version-string mechanism as "MySQL" or "PostgreSQL", so `DatabaseDetectionService` resolves them to `SupportedDatabase.MySql`/`PostgreSql` and they run through the exact same dialect and test suite as the vanilla engine. All four were verified live (full `PostgreSQLTestProvider`/MySQL test-provider suite, 100% pass, same check count as vanilla) and are always-on entries in the testbed (`testbed/ParallelTestOrchestrator.cs`) — not opt-in. Fujitsu Enterprise Postgres needs two non-default container settings to start at all: it listens on port **27500**, not 5432, and its entrypoint has no "skip if this is the default superuser" special-case, so configuring it via `PG_USER=postgres` crashes the container ("role postgres already exists") — use `PG_ADMIN_PASSWORD` instead to set the existing superuser's password. **EDB Postgres Extended** is very likely also just `PostgreSql` (same wire-protocol/version-string shape), but has no public Docker image to verify against — EDB moved it behind a private, subscription-gated registry — so it is *not* claimed as verified here.

> **SingleStore:** unlike the verified forks above, SingleStore genuinely needs its own `SupportedDatabase` value — it reports itself via schema/`SELECT VERSION()` as generic, indistinguishable MySQL (`5.7.32` with no marker), so `DatabaseDetectionService` runs a dedicated `SELECT @@memsql_version` probe (a SingleStore-only system variable, structurally identical to the existing `@@aurora_version` Aurora MySQL probe) to tell it apart. Once detected, it delegates to `MySqlDialect` the same way `AuroraMySql` does. Verified live against `ghcr.io/singlestore-labs/singlestoredb-dev`: core CRUD passes, and stored procedures need SingleStore's own syntax — `CREATE PROCEDURE proc() AS BEGIN ECHO SELECT ...; END` rather than MySQL's bare `BEGIN SELECT ...; END` (a real syntax error on SingleStore) — `CALL` invocation and quoted-identifier handling are otherwise identical to MySQL/MariaDB. SingleStore is not yet wired into the testbed orchestrator as an always-on container entry — that remains open work, distinct from the detection/dialect support described here.

> **FlatFile:** [pengdows.flatfile](https://github.com/pengdows/pengdows.flatfile) is a file-backed ADO.NET provider over CSV/TSV/pipe/delimited/fixed-width/NDJSON files. The dialect is verified against the provider source (0.2.1-preview.1) and runs in the testbed. Parameters are named `:name` (positional `?` also works, but not mixed in one statement). `DbMode.Best`, `Standard` and `PreventDatabaseUnload` resolve to `SingleWriter`: the provider allows one non-readonly connection per database, and a second in-process writer waits `connectionTimeout` then throws `TimeoutException`; reads use `readonly=true` connections. Isolation: `ReadUncommitted`, `ReadCommitted` and `RepeatableRead` (a per-table snapshot on first read); `Serializable`/`Snapshot` requests and `StrictConsistency` throw; `SafeNonBlockingReads` runs as `RepeatableRead`, `FastWithRisks` as `ReadUncommitted`. Savepoints (create/rollback/release), `SET TRANSACTION READ ONLY`, `MERGE` upserts, schema-qualified tables and `OFFSET ... FETCH` paging are supported; there is no `LIMIT`, `RETURNING`, identity column or stored procedure, so `[Id(false)]` keys need a correlation-token column. Text columns cannot tell `NULL` from `''` unless the table declares `CREATE TABLE ... WITH (NULLTOKEN = '...')`. Server version comes from `FlatFileConnection.ServerVersion`. `pengdows.crud` takes no dependency on the `pengdows.flatfile` package.

> **Sybase (SAP ASE):** has its own dedicated `SybaseAseDialect` (not a fork delegating to another dialect) and a real testbed container (`nguoianphu/docker-sybase`), verified live against ASE 16. Notable genuine differences from every other T-SQL/SQL-92-family dialect here, all confirmed live rather than assumed from SQL Server parity: MERGE works but rejects a trailing statement-terminator semicolon (`RequiresMergeStatementTerminator => false`, same mechanism Oracle uses — this branch's Oracle MERGE-terminator fix was added alongside this Sybase support, since Sybase needed the same mechanism); `;` is rejected as a multi-statement batch separator entirely, not just as a trailing terminator (`SupportsSemicolonStatementSeparator => false`); no multi-row `INSERT ... VALUES`, no `VALUES`-derived-table-as-MERGE-source, and no `LIMIT`/`OFFSET` paging (uses `SELECT TOP N` like SQL Server instead); NOT NULL-by-default columns and a non-Unicode default charset. `AdoNetCore.AseClient`'s `AseException` does not derive from `DbException` and `GetSchema()` is unimplemented — both are handled generically via a duck-typed "Errors collection" fallback in `DbExceptionTranslationSupport` rather than Sybase-specific special-casing.

> **Sybase ASE Guids:** store Guids in `BINARY(16)`. `SybaseAseDialect` passes `DbType.Guid` straight to AdoNetCore.AseClient (unchanged since 2.0.5), which writes the 16-byte `Guid.ToByteArray()` form; that reads back as the same Guid and matches in equality lookups. Into a `CHAR(36)` column the driver writes those 16 bytes as characters, which do not read back as a Guid. Verified live on ASE 16.0 SP02.

> **Firebird `DateTimeOffset`:** FirebirdClient rejects `DbType.DateTimeOffset`, so on Firebird 4+ a `DateTimeOffset` is sent as an `FbZonedDateTime` holding the UTC instant. The driver encodes it against the column type the server reports: a `TIMESTAMP WITH TIME ZONE` column stores the instant, and a plain `TIMESTAMP` column stores the UTC wall time (the same value earlier releases stored). Zoned columns read back as `DateTimeOffset`/UTC `DateTime`. Firebird 3 has no zoned types and keeps the UTC-`DateTime` mapping. Verified live on Firebird 5.0.4 with FirebirdClient 10.3.3.

> **Informix special registers:** an *unqualified* quoted identifier named after a special register (`user`, `today`, `current`, `sitename`, `dbservername`, `current_user`) resolves to the register, not the column, wherever it appears in an expression. For example, `DELETE FROM "t" WHERE "user" = ?` compares the session user name. The gateways table-qualify every column reference on Informix, so generated SQL is safe. In your own SQL, always qualify such columns (`"t"."user"`, or an alias). Informix's .NET provider also trims trailing spaces from every `VARCHAR`/`LVARCHAR` value it reads, with no option to turn it off (IBM APAR IC63704); see `ISqlDialect.PreservesTrailingWhitespace`.

> **IBM Db2:** Db2 for Linux/Unix/Windows (LUW) is specifically supported and tested; Db2 for z/OS and Db2 for i should work but have not been tested. The dedicated `Db2Dialect` is verified live against Db2 LUW (`ibmcom/db2`) in the always-on testbed run (`testbed/Db2`). The Db2 family is three separate products sharing a common SQL subset (Db2 LUW, Db2 for z/OS, Db2 for i), not one engine on different operating systems, so behavior outside that common subset may differ on z/OS and IBM i. IBM's .NET driver may also require a Db2 Connect license to reach z/OS or IBM i. `DbMode.Best`: on a positively detected Db2 LUW server it selects `PreventDatabaseUnload` (LUW's default implicit activation deactivates an idle database; measured ~1.1 s per cold connection versus ~4 ms with a sentinel); any other or unrecognized Db2 server keeps `Standard`, and an explicit `Standard` is always honored. Generated-key retrieval wraps the ENTIRE insert statement — `SELECT "Id" FROM FINAL TABLE (INSERT INTO t (...) VALUES (...))` — rather than appending a trailing RETURNING/OUTPUT clause; this branch's dialect layer has no per-database "wrap the whole statement" hook (unlike newer branches), so `TableGateway.Core.cs` (`BuildCreateWithReturning`) special-cases `SupportedDatabase.Db2` directly, matching its existing SqlServer/Oracle special cases. Isolation levels (Db2's UR/CS/RS/RR map to ReadUncommitted/ReadCommitted/RepeatableRead/Serializable) are registered in `IsolationResolver`'s central per-database switch, not on the dialect itself — this branch's isolation system predates per-dialect isolation customization. Stored procedures use SQL-standard `CALL` syntax (same wrapping style as MySQL/MariaDB); a bare `SAVEPOINT name` is rejected (`SQL0104N`) — Db2 requires `ON ROLLBACK RETAIN CURSORS`. IBM's `DB2Exception` declares its own all-caps `SQLState` property (ambiguous with the inherited `DbException.SqlState`) and often doesn't populate SqlState via any property at all — both are handled by `DbExceptionTranslationSupport`'s ambiguity-safe reflection scan plus a message-embedded-SQLSTATE regex fallback, not Db2-specific special-casing.

Providers must support `DbProviderFactory` and `GetSchema("DataSourceInformation")`.

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
| **INSERT RETURNING** | always on | always on | — | — | always on | 3.35 | always on | always on (2.1+) |
| **JSON types** | 2016 (v13) | 9.x | 5.7.8 | — (no native JSON) | 12c | 3.45 | always on | — |
| **CTEs** | always on | always on | 8.0 | 10.2 | always on | 3.8.3 | always on | 2.0 |
| **Window functions** | always on | always on | 8.0 | 10.2 | always on | 3.25 | always on | 3.0 |
| **Savepoints** | always on | always on | always on | always on | always on | always on | — | always on |
| **DROP TABLE IF EXISTS** | always on | always on | always on | always on | — (PL/SQL only) | always on | always on | always on |
| **Identity / autoincrement** | always on | always on | always on | always on | 12c | always on | always on | always on |

`—` means the feature is either not supported or uses a different mechanism on that database.

### Latent Version Mismatches

The following features have no version gate in the dialect code but require a minimum server version to function. Connecting to an older server will produce SQL errors at runtime rather than a capability flag returning `false`:

- **PostgreSQL `SupportsInsertOnConflict = true` is ungated** — requires PostgreSQL 9.5+. A server running 9.0–9.4 will receive `INSERT ... ON CONFLICT` SQL it cannot parse.
- **SQLite `SupportsInsertOnConflict = true` is ungated** — requires SQLite 3.24+. A SQLite file opened on 3.23 will fail on upsert SQL.
- **Oracle `SupportsIdentityColumns = true` is ungated** — identity columns (`GENERATED AS IDENTITY`) require Oracle 12c. Pre-12c servers will fail when inserting entities with `[Id(false)]`.
- **SQL Server MERGE at `IsVersionAtLeast(10)` is broader than the declared "2012+" header** — SQL Server 2008 (v10) will pass the version check and receive MERGE SQL. The "2012+" comment in the source is a conservative recommendation, not enforced by the gate.

## Default Pool Sizes (Provider vs Practical)

| SupportedDatabase | Default Max Pool Size (provider) | Practical / Recommended Max Pool Size | Key Practical Limits & Advice |
|-------------------|----------------------------------|---------------------------------------|-------------------------------|
| SqlServer (Microsoft.Data.SqlClient) | 100 | 50-200 (often 100-150 safe) | Per app instance rarely >200; total server connections limited by memory (approx 10-20 KB per conn + query plans). Rule of thumb: 2-4x CPU cores per app instance, or 100-300 total cluster-wide. Large pools (>500) often cause context switching thrash on DB server. |
| PostgreSql (Npgsql) | 100 (since ~3.1) | 20-100 per app instance (often 30-80 optimal) | Strong consensus: 2-4x CPU cores on the DB server. Each conn ~1-3 MB RAM on Postgres side. >100-150 often overloads small/medium instances. Use PgBouncer if >50-100 needed per app; set app pool to 20-50 and let PgBouncer multiplex. |
| MySql / MariaDb (MySqlConnector / MySql.Data) | 100 | 50-200 (often 100-150) | Similar to SqlServer: 100 is safe default. Threads are lighter than Postgres but still ~1-2 MB per conn. Practical ceiling often 200-500 before thread contention or memory pressure. ProxySQL or MySQL Router recommended beyond ~200. |
| Oracle (Oracle.ManagedDataAccess) | 100 | 50-200 | Sessions are heavier (few MB each). Practical max often 100-300 before session/memory limits kick in. Enterprise tuning often caps at 100-150 per instance. |
| Sqlite (Microsoft.Data.Sqlite) | Effectively unlimited (pooling enabled by default since v6, no hard max) | 1-20 (or unlimited for in-memory) | Single-writer lock means >1-4 concurrent writers kills perf. Practical: keep pool small (5-20) or disable pooling for high concurrency. In-memory/shared can handle more, but still file-lock limited on disk. |
| DuckDb (.NET DuckDB) | Effectively unlimited (no hard pool limit in most impls) | 1-8 (or up to threads count) | Embedded: connection creation is cheap. Practical: single connection often best; multiple only if parallelizing queries. Limit to CPU cores or threads setting. No real pool exhaustion; bottleneck is CPU/RAM for queries, not connections. |

---

## Read-Only Enforcement Matrix

pengdows.crud enforces read-only intent at multiple levels where supported by the database engine and provider.

| Database | Connection String | Session SQL | Dual Enforcement | Enforcement Strategy |
| :--- | :---: | :---: | :---: | :--- |
| **PostgreSQL** | Yes | Yes | **Yes** | `Options='-c default_transaction_read_only=on'` + `SET ...` |
| **SQLite** | Yes | No | No | `Mode=ReadOnly` (file opened read-only; not applied to `:memory:` databases) |
| **DuckDB** | Yes | No | No | `access_mode=READ_ONLY` (file-open attribute; cannot be changed on an open connection) |
| **SQL Server** | Yes | No | No | `ApplicationIntent=ReadOnly` (Driver-managed) |
| **MySQL** | No | Yes | No | `SET SESSION transaction_read_only = 1` (5.7.20+) |
| **MariaDB** | No | Yes | No | `SET SESSION tx_read_only = 1` (10.1+) |
| **Snowflake** | No | No | No | None — Snowflake has no session read-only mode; use read-only roles/credentials |
| **Oracle** | No | Yes | No | `SET TRANSACTION READ ONLY` (issued at read-only transaction start; no persistent session mode) |
| **Firebird** | No | Yes | No | `SET TRANSACTION READ ONLY` |

> **Dual Enforcement:** For PostgreSQL, the intent is baked into the connection string (forcing the driver level) AND re-asserted via SQL on every lease, providing maximum security against "dirty" connections in a shared pool. SQLite and DuckDB rely on the connection-string parameter alone: it is applied when the database file is opened, which is stronger than a session flag any caller could reset.
