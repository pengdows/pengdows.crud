# Supported Databases

pengdows.crud supports the following databases with tested ADO.NET providers:

- SQL Server / Express / LocalDB
- PostgreSQL / TimescaleDB / CockroachDB
- Oracle
- MySQL / MariaDB
- SQLite
- Firebird
- DuckDB

Providers must support `DbProviderFactory` and `GetSchema("DataSourceInformation")`.

## Minimum Server Versions

| Database | Minimum Version | Notes |
|----------|----------------|-------|
| SQL Server | 2014 (12.x) | Required for `JSON_VALUE` if used; RCSI recommended |
| PostgreSQL | 10 | Required for logical replication, `GENERATED ALWAYS AS IDENTITY` |
| Oracle | 12c (12.1) | Required for identity columns, JSON support requires 12.2+ |
| MySQL | 5.7.20 | `transaction_read_only` session variable required for read-only enforcement |
| MariaDB | 10.4 | `transaction_read_only` session variable required for read-only enforcement |
| SQLite | 3.35 | Required for `RETURNING` clause support |
| Firebird | 2.5 | Minimum for MERGE, UTF-8 session settings; 3.0+ for window functions |
| DuckDB | 0.8.0 | `SET access_mode` supported since 0.3.0; CI testbed pins to 1.x |

> **MySQL / MariaDB read-only note:** The `SET SESSION transaction_read_only = 1` syntax requires
> MySQL 5.7.20+ or MariaDB 10.4+. Earlier versions only support `SET SESSION TRANSACTION READ ONLY`
> which applies to the next transaction only, not the session persistently.

## Default Pool Sizes (Provider vs Practical)

| SupportedDatabase | Default Max Pool Size (provider) | Practical / Recommended Max Pool Size | Key Practical Limits & Advice |
|-------------------|----------------------------------|---------------------------------------|-------------------------------|
| SqlServer (Microsoft.Data.SqlClient) | 100 | 50-200 (often 100-150 safe) | Per app instance rarely >200; total server connections limited by memory (approx 10-20 KB per conn + query plans). Rule of thumb: 2-4x CPU cores per app instance, or 100-300 total cluster-wide. Large pools (>500) often cause context switching thrash on DB server. |
| PostgreSql (Npgsql) | 100 (since ~3.1) | 20-100 per app instance (often 30-80 optimal) | Strong consensus: 2-4x CPU cores on the DB server. Each conn ~1-3 MB RAM on Postgres side. >100-150 often overloads small/medium instances. Use PgBouncer if >50-100 needed per app; set app pool to 20-50 and let PgBouncer multiplex. |
| MySql / MariaDb (MySqlConnector / MySql.Data) | 100 | 50-200 (often 100-150) | Similar to SqlServer: 100 is safe default. Threads are lighter than Postgres but still ~1-2 MB per conn. Practical ceiling often 200-500 before thread contention or memory pressure. ProxySQL or MySQL Router recommended beyond ~200. |
| Oracle (Oracle.ManagedDataAccess) | 100 | 50-200 | Sessions are heavier (few MB each). Practical max often 100-300 before session/memory limits kick in. Enterprise tuning often caps at 100-150 per instance. |
| Sqlite (Microsoft.Data.Sqlite) | Effectively unlimited (pooling enabled by default since v6, no hard max) | 1-20 (or unlimited for in-memory) | Single-writer lock means >1-4 concurrent writers kills perf. Practical: keep pool small (5-20) or disable pooling for high concurrency. In-memory/shared can handle more, but still file-lock limited on disk. |
| DuckDb (.NET DuckDB) | Effectively unlimited (no hard pool limit in most impls) | 1-8 (or up to threads count) | Embedded: connection creation is cheap. Practical: single connection often best; multiple only if parallelizing queries. Limit to CPU cores or threads setting. No real pool exhaustion; bottleneck is CPU/RAM for queries, not connections. |
| Sql92 fallback / unknown | 100 | 50-100 | Conservative defaults for generic relational DBs. |

---

## Read-Only Enforcement Matrix

pengdows.crud enforces read-only intent at multiple levels where supported by the database engine and provider.

| Database | Connection String | Session SQL | Dual Enforcement | Enforcement Strategy |
| :--- | :---: | :---: | :---: | :--- |
| **PostgreSQL** | Yes | Yes | **Yes** | `Options='-c default_transaction_read_only=on'` + `SET ...` |
| **SQLite** | Yes | Yes | **Yes** | `Mode=ReadOnly` + `PRAGMA query_only = ON` |
| **DuckDB** | Yes | Yes | **Yes** | `access_mode=READ_ONLY` + `SET access_mode = 'read_only'` |
| **SQL Server** | Yes | No | No | `ApplicationIntent=ReadOnly` (Driver-managed) |
| **MySQL / MariaDB** | No | Yes | No | `SET SESSION transaction_read_only = 1` (MySQL 5.7.20+ / MariaDB 10.4+) |
| **Snowflake** | No | Yes | No | `ALTER SESSION SET TRANSACTION_READ_ONLY = TRUE` |
| **Oracle** | No | Yes | No | `SET TRANSACTION READ ONLY` |
| **Firebird** | No | Yes | No | `SET TRANSACTION READ ONLY` |

> **Dual Enforcement:** For PostgreSQL, SQLite, and DuckDB, the intent is baked into the connection string (forcing the driver level) AND re-asserted via SQL on every lease, providing maximum security against "dirty" connections in a shared pool.

