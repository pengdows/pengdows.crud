# Supported Databases

pengdows.crud supports 14 directly supported databases via the `SupportedDatabase` [Flags] enum, with tested ADO.NET providers:

| Enum Value | Product |
|---|---|
| `PostgreSql=1` | PostgreSQL (including TimescaleDB) |
| `SqlServer=2` | SQL Server / Express / LocalDB |
| `Oracle=4` | Oracle |
| `Firebird=8` | Firebird |
| `CockroachDb=16` | CockroachDB |
| `MariaDb=32` | MariaDB |
| `MySql=64` | MySQL |
| `Sqlite=128` | SQLite |
| `DuckDB=256` | DuckDB |
| `YugabyteDb=512` | YugabyteDB |
| `TiDb=1024` | TiDB |
| `Snowflake=2048` | Snowflake (opt-in; cloud-only, requires credentials) |
| `AuroraMySql=4096` | Aurora MySQL (AWS managed; detected at runtime, delegates to MySQL dialect) |
| `AuroraPostgreSql=8192` | Aurora PostgreSQL (AWS managed; detected at runtime, delegates to PostgreSQL dialect) |

> **SQL-92 fallback:** If dialect detection cannot identify the connected product, pengdows.crud falls back to a conservative SQL-92 compatible dialect. SQL-92 is a fallback behavior, not a distinct supported database product, and has no `SupportedDatabase` enum value.

> **Aurora variants:** `AuroraMySql` and `AuroraPostgreSql` are managed AWS services with no Docker image. They are detected at runtime via `DatabaseDetectionService` and delegate to the MySQL/PostgreSQL dialect respectively. No separate integration suite is required.

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
| **Savepoints** | always on | always on | always on | always on | always on | — | — | always on |
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
| **SQLite** | Yes | Yes | **Yes** | `Mode=ReadOnly` + `PRAGMA query_only = ON` |
| **DuckDB** | Yes | Yes | **Yes** | `access_mode=READ_ONLY` + `SET access_mode = 'read_only'` |
| **SQL Server** | Yes | No | No | `ApplicationIntent=ReadOnly` (Driver-managed) |
| **MySQL** | No | Yes | No | `SET SESSION transaction_read_only = 1` (5.7.20+) |
| **MariaDB** | No | Yes | No | `SET SESSION tx_read_only = 1` (10.1+) |
| **Snowflake** | No | Yes | No | `ALTER SESSION SET TRANSACTION_READ_ONLY = TRUE` |
| **Oracle** | No | Yes | No | `SET TRANSACTION READ ONLY` |
| **Firebird** | No | Yes | No | `SET TRANSACTION READ ONLY` |

> **Dual Enforcement:** For PostgreSQL, SQLite, and DuckDB, the intent is baked into the connection string (forcing the driver level) AND re-asserted via SQL on every lease, providing maximum security against "dirty" connections in a shared pool.
