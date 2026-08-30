# Supported Databases

pengdows.crud supports 15 directly supported databases via the `SupportedDatabase` [Flags] enum, with tested ADO.NET providers:

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
| `Db2=16384` | IBM Db2 for Linux/Unix/Windows (Db2 LUW) |

> **SQL-92 fallback:** If dialect detection cannot identify the connected product, pengdows.crud falls back to a conservative SQL-92 compatible dialect. SQL-92 is a fallback behavior, not a distinct supported database product, and has no `SupportedDatabase` enum value.

> **Aurora variants:** `AuroraMySql` and `AuroraPostgreSql` are managed AWS services with no Docker image. They are detected at runtime via `DatabaseDetectionService` and delegate to the MySQL/PostgreSQL dialect respectively. No separate integration suite is required.

Providers must support `DbProviderFactory` and `GetSchema("DataSourceInformation")`.

## Verified Server Versions & Driver Matrix

Database support is a joint capability over **(database engine, client driver)**. The table reflects the versions verified by the automated testbed suite:

- **Verified Floor** — the lowest version validated in the automated testbed container sweep.
- **Recommended** — recommended version for production deployments.
- **Verified at** — exact container images/versions validated by the automated testbed suite.
- **Driver used** — the specific ADO.NET driver package and version tested.

| Database | Verified Floor | Recommended | Verified at | Driver used | Key reason / capabilities verified |
|----------|----------------|-------------|-------------|-------------|------------------------------------|
| SQL Server | 2017 | 2022 | 2017, 2019, 2022 | Microsoft.Data.SqlClient 6.0.2 | JSON support (`JSON_VALUE`), MERGE with OUTPUT, table triggers, and OFFSET/FETCH |
| PostgreSQL | 9.5 | 16 | 9.5, 15.0, 16.4 | Npgsql 9.0.3 | `INSERT ON CONFLICT` in 9.5; `GENERATED ALWAYS AS IDENTITY` in 10+; `MERGE` in 15 |
| Oracle | 18c | 23c | 18.4.0, 21.3.0, 23.8.0 | Oracle.ManagedDataAccess.Core 23.8.0 | Identity columns, JSON, sequence / PL/SQL blocks, and pool isolation |
| MySQL | 5.7 | 8.4 | 5.7, 8.0.36, 8.4.11 | MySqlConnector 2.4.0 / MySql.Data 9.3.0 | `transaction_read_only` session variable, CTEs/window functions (8.0+), EOF reader disposal |
| MariaDB | 10.2 | 11.4 | 10.2, 10.4, 10.11, 11.4.12 | MySqlConnector 2.4.0 (recommended) | CTEs, window functions, `tx_read_only` session variable, unsigned identity return |
| SQLite | 3.45 | 3.45 | 3.45.x | Microsoft.Data.Sqlite 9.0.5 | `INSERT ON CONFLICT`, `RETURNING`, JSON, and in-memory single-connection mode |
| Firebird | 3.0 | 5.0 | 3.0.9, 4.0.5, 5.0.2 | FirebirdSql.Data.FirebirdClient 10.3.3 | Window functions, CTEs, MERGE, and session isolation |
| DuckDB | 1.3.2 | 1.3.2 | 1.3.2 | DuckDB.NET.Data.Full 1.3.2 | `SET access_mode`, `RETURNING` auto-id, and single-writer concurrency |
| CockroachDB | v23.2 | latest | v23.2.14, v24.3.0, v25.1.0 | Npgsql 9.0.3 | Npgsql connection pool reset with `pg_advisory_unlock_all` validated |
| YugabyteDB | 2.25 | latest | 2.25.2.0, 2025.2.5.2 | Npgsql 9.0.3 | PostgreSQL-compatible distributed execution, savepoints, stored proc execution, `ON CONFLICT` upsert |
| TiDB | v7.5 | latest | v7.5.7, v8.5.7 | MySqlConnector 2.4.0 / MySql.Data 9.3.0 | Distributed MySQL compatibility, VALUES() upsert, and limit/offset paging |
| Snowflake | service | service | Cloud service | Snowflake.Data 5.6.0 | Cloud service — version managed by Snowflake |
| Db2 | 11.5.0 | 11.5.8.0 | 11.5.0.0a, 11.5.8.0 | Net.IBM.Data.Db2-lnx 8.0.0.500 | FINAL TABLE auto-id, typed MERGE parameter binding, and unique exception translation |

> **Driver Constraints & Handshake Caveats:**
> - **CockroachDB & Npgsql Pool Reset:** Npgsql 8.x and 9.x issue `SELECT pg_advisory_unlock_all()` on pooled connection reset. CockroachDB supported advisory lock functions are validated from v23.2+.
> - **Embedded Engines (SQLite & DuckDB):** SQLite and DuckDB execute in-process, bundled with their ADO.NET provider (`Microsoft.Data.Sqlite` bundles SQLite 3.45+; `DuckDB.NET.Data.Full` bundles DuckDB 1.3.2).
> - **MariaDB & Driver Support:** MariaDB versions report a `5.5.5-10.x.x-MariaDB` handshake prefix (MDEV-28910). Both `MySql.Data 9.3.0` and `MySqlConnector 2.4.0` connect cleanly to MariaDB 10.x/11.x in automated testbed validation.
> - **MySQL / MariaDB Prepared Statements:** `COM_STMT_PREPARE` in MySQL 5.7 and MariaDB ≤ 10.5 rejects DDL / stored procedure statements with error 1295. `MySqlDialect` automatically detects error 1295 and falls back to text execution protocol seamlessly.
> - **Firebird & DateTimeOffset Driver Constraint:** `FirebirdSql.Data.FirebirdClient 10.3.3` throws "Incorrect time zone value" in its internal `DbValue.GetTimeZoneId()` when binding raw CLR `DateTimeOffset` values. This is an ADO.NET client driver constraint; use UTC `DateTime` values when targeting Firebird.
> - **PostgreSQL & Npgsql Support Policy:** PostgreSQL 9.5, 15.0, and 16.4 are all validated. `CREATE PROCEDURE` is verified on PostgreSQL 11+, while PostgreSQL 9.5 executes `CREATE FUNCTION`.
> - **MySQL / MariaDB read-only syntax:** `SET SESSION transaction_read_only = 1` is verified on MySQL 5.7+. MariaDB uses `SET SESSION tx_read_only = 1` (verified 10.2+).

### Feature Support Matrix on Verified Versions

All features below are verified on the tested engine versions:

| Feature | SQL Server (2017+) | PostgreSQL (9.5+) | MySQL (5.7+) | MariaDB (10.2+) | Oracle (18c+) | SQLite (3.45+) | DuckDB (1.3.2) | Firebird (3.0+) |
|---------|-------------------|-------------------|--------------|-----------------|---------------|----------------|----------------|-----------------|
| **Upsert** | MERGE | INSERT ON CONFLICT (9.5+) / MERGE (15+) | ON DUPLICATE KEY | ON DUPLICATE KEY | MERGE | INSERT ON CONFLICT | INSERT ON CONFLICT | MERGE |
| **Generated ID Return** | OUTPUT clause | RETURNING / IDENTITY | AUTO_INCREMENT | AUTO_INCREMENT | RETURNING / Sequence | RETURNING | RETURNING | RETURNING |
| **JSON types** | `JSON_VALUE` | JSON / JSONB | JSON (5.7.8+) | TEXT / JSON alias | Native JSON | JSON1 | Native JSON | — |
| **CTEs** | Supported | Supported | Supported (8.0+) | Supported | Supported | Supported | Supported | Supported |
| **Window functions** | Supported | Supported | Supported (8.0+) | Supported | Supported | Supported | Supported | Supported |
| **Savepoints** | Supported | Supported | Supported | Supported | Supported | — | — | Supported |
| **DROP TABLE IF EXISTS** | Supported | Supported | Supported | Supported | PL/SQL block | Supported | Supported | Supported |
| **Identity / autoincrement** | IDENTITY | SERIAL / IDENTITY (10+) | AUTO_INCREMENT | AUTO_INCREMENT | IDENTITY | AUTOINCREMENT | Sequence / Default | Generator / Sequence |

`—` means the feature is not supported by the engine or uses a distinct mechanism.

### Version-Specific Capabilities within Verified Range

The dialect system dynamically adapts to capabilities across verified ranges:

- **PostgreSQL `CREATE PROCEDURE` vs `CREATE FUNCTION`** — PostgreSQL 11+ uses `CREATE PROCEDURE` / `CALL proc()`; PostgreSQL 9.5–10 uses `CREATE FUNCTION` / `SELECT func()`.
- **PostgreSQL `GENERATED ALWAYS AS IDENTITY`** — PostgreSQL 10+ uses standard SQL identity; PostgreSQL 9.5 uses `SERIAL` sequences.
- **MySQL Window Functions & CTEs** — MySQL 8.0+ enables window functions and CTEs; MySQL 5.7 uses standard joins and grouping.
- **MariaDB Unsigned Identity** — MariaDB 10.2+ cleanly returns full `uint` and `ulong` auto-increment ranges.

### Sweep Methodology & Outcome Classification

Empirical testing across container images serves as a **falsifier** of documented floors, not a replacement for vendor feature specifications:

- **`IMAGE_UNAVAILABLE`**: Container image/tag does not exist or was pruned from registry (e.g. `ibmcom/db2` vs `icr.io/db2_community/db2`).
- **`CONNECT_FAILED`**: Driver, protocol, TLS, or authentication incompatibility (e.g., Connector/NET MariaDB `5.5.5-` prefix rejection, or missing `TrustServerCertificate=True`).
- **`CHECKS_FAILED`**: Genuine engine/dialect behavior failure on a connected instance (falsifies documented capability floor).

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
| **Db2** | No | No | No | Not implemented — no session/connection-level read-only enforcement configured yet |

> **Dual Enforcement:** For PostgreSQL, SQLite, and DuckDB, the intent is baked into the connection string (forcing the driver level) AND re-asserted via SQL on every lease, providing maximum security against "dirty" connections in a shared pool.

---

## Behavioral Gotchas by Database

Capability-flag differences and dialect-specific behavior that aren't obvious from the matrices
above — verified directly against each `dialects/*.cs` implementation. This is not exhaustive;
it lists the quirks most likely to surprise a caller who assumes uniform SQL-standard behavior.

**CockroachDB**
- Only `Serializable` isolation is ever offered — no `ReadCommitted` fallback exists at all
  (`CockroachDbDialect.GetSupportedIsolationLevels`).
- Version banner never contains a bare "PostgreSQL" token even though CockroachDB is
  wire-compatible, so the `PostgreSqlDialect.ParseVersion` fix (below) never activates for it —
  `CockroachDbDialect` needs, and has, its own `ParseVersion` override; without it every
  `IsVersionAtLeast()`-gated capability would silently stay off.
- No stored procedure support (`ProcWrappingStyle.None`).

**Db2**
- Three session-level "special registers" — `CURRENT ISOLATION`, `CURRENT TEMPORAL SYSTEM_TIME`,
  `CURRENT TEMPORAL BUSINESS_TIME` — are not transaction-scoped; their `SET` statements survive a
  rollback. `GetBaseSessionSettings()` resets all three on every checkout specifically because a
  prior pooled-connection borrower could otherwise leave a non-default isolation override or an
  as-of-time temporal-table view in effect for the next caller.
- Generated-key retrieval wraps the entire INSERT: `SELECT ... FROM FINAL TABLE (INSERT ...)`,
  not a trailing `RETURNING`/`OUTPUT` clause like every other RETURNING-capable dialect.
- `IBM.Data.Db2`'s driver throws immediately on `DbType.Guid` before any conversion runs; GUIDs
  must be remapped to `DbType.String` at parameter-creation time.
- A bare `SAVEPOINT name` fails with SQL0104N — needs the `ON ROLLBACK RETAIN CURSORS` suffix.

**DuckDB**
- `ReadOnlyConnectionsCanBlockConcurrentWriters => true` and
  `RequiresSerializedConnectionOpen => true` — both real connection-handling constraints of
  DuckDB's ADO.NET provider, not choices pengdows.crud made.
- `RejectsExplicitIsolationLevelOnBeginTransaction => true` — passing an explicit
  `IsolationLevel` to `BeginTransaction` throws; only the implicit/default path works.
- No unique or check constraint enforcement, no savepoints.
- MERGE only on 1.4+, and its `MergeUpdateRequiresTargetAlias => false` — the opposite of most
  MERGE-capable dialects, which require the target alias on the SET side.

**Firebird**
- `FirebirdSql.Data.FirebirdClient` starts an implicit transaction per command and auto-commits
  it on success, but has no corresponding auto-rollback on failure — a command that throws
  mid-execution leaves that transaction open, and Firebird's server-side lock on the affected
  row/table is not released by merely closing/disposing the connection; it can persist for the
  life of the pooled connection. Confirmed live via a deliberate unique-constraint-violation
  repro. `pengdows.crud` compensates automatically: `SqlContainer` issues an explicit bare
  `ROLLBACK` after any failed write on Firebird (`SqlDialect.RequiresExplicitRollbackAfterFailedWrite`,
  internal) before the connection returns to the pool — covering both `ExecuteNonQueryAsync` and
  the reader/scalar path used by autoincrement `[Id(false)]` creates (Firebird's
  `GeneratedKeyPlan.Returning`). Never fires inside an explicit `ITransactionContext`, whose own
  commit/rollback lifecycle owns that connection instead. No application code needs to know about
  this.
- Separately, Firebird's DDL commit (`CREATE`/`DROP`/`ALTER`/`TRUNCATE`) requires that no OTHER
  connection — even one holding only cleanly-committed transactions — still be idle in the
  ADO.NET connection pool referencing the table's current metadata generation. After enough prior
  round trips reusing pooled connections, a later DDL statement can fail with "object TABLE ...
  is in use" even though nothing is actually still running or uncommitted. `pengdows.crud`
  compensates automatically here too: before executing DDL, `SqlDialect.ResetConnectionPoolForDdl`
  (internal, Firebird-only) clears the ADO.NET pool for that exact connection string via
  reflection into `FbConnection.ClearPool(string)` — no application code needs to know about this
  either.

**Snowflake**
- Parses constraint DDL but enforces none of it at runtime: `EnforcesConstraints`,
  `EnforcesForeignKeyConstraints`, `SupportsUniqueConstraints`, and `SupportsCheckConstraints`
  are all `false`. A unique/FK/check constraint in your DDL is documentation, not enforcement.
- No `INSERT ... RETURNING`, no savepoints; upsert always routes through MERGE.
- `MaxRowsPerBatch => 16384` — a real per-batch cap, not just a default tuning knob.

**TiDB**
- Accepts `SERIALIZABLE` isolation syntax without error but silently enforces only
  `RepeatableRead` — `GetSupportedIsolationLevels`/`GetIsolationProfileMapping` deliberately
  don't offer `Serializable` at all, specifically so callers can't rely on a guarantee TiDB never
  provides. `IsolationProfile.StrictConsistency` maps to `RepeatableRead`, its best available
  level.
- No FK or check-constraint enforcement (compatibility-mode defaults).
- The `MySql.Data` driver (not MySqlConnector) cannot prepare statements against TiDB at all —
  `PrepareStatements` is disabled unless the connector in use is MySqlConnector. Originally
  suspected to be a text-protocol backslash-escaping bug that corrupts string parameters;
  re-verified 2026-08-30 against a live TiDB container across `MySql.Data` 9.3.0, 9.4.0, and
  9.7.0 (see `testbed.DriverVersionMatrix/`) and found to be worse and more fundamental than
  that: `MySqlCommand.Prepare()` itself throws an unhandled `KeyNotFoundException` (a
  character-set-index lookup failure) before any parameter value is ever sent, identically across
  all three driver versions spanning nearly two years of releases. The workaround remains
  necessary; this is not a since-fixed-upstream issue.

**YugabyteDB**
- Reports its version as "PostgreSQL 15.x-YB-...", which would normally trigger
  `SupportsMerge => true` via version-gating (PostgreSQL 15+ supports MERGE) — but YSQL doesn't
  actually implement the SQL:2016 MERGE statement and throws `0A000` if you try. `SupportsMerge`
  is hardcoded `false` regardless of detected version; upsert always routes through
  `INSERT ... ON CONFLICT` instead.
- Prepared statements are fully disabled (`PrepareStatements => false`), not just left
  auto-managed — YSQL doesn't reliably preserve a prepared statement across a pooled connection's
  checkout/checkin cycle, and the observed failure mode is a cryptic "Connection is not open"
  error with no obvious link back to prepare/pooling.

**Firebird, MySQL/MariaDB, Oracle** (already covered conceptually by the v1 wiki pages —
these are the v2-only additions since then)
- Firebird: `GuidStorageMode` is a public, `init`-only property on `FirebirdDialect` (default
  `Binary`, settable to `String` for new schemas) that changes GUID wire format per dialect
  instance — and is folded into `CacheFingerprint` specifically so two Firebird tenants on the
  same server version but different `GuidStorageMode` don't collide in shared gateway caches.
  Firebird is also the sole dialect with `EmitsAnsiMergeSyntax => false` (merge-*like* syntax,
  not ANSI `MERGE ... WHEN MATCHED`) combined with `SupportsPureKeyUpsert => true` (upsert on an
  entity with only `[PrimaryKey]` columns and no updateable column, which every other dialect
  rejects).
- MySQL/MariaDB: which ADO.NET driver is in use changes the generated-key retrieval *strategy*,
  not just the connection string — MySqlConnector doesn't support `AllowMultipleStatements`, so
  it uses `ReaderInsertedId` where `MySql.Data` uses `CompoundStatement` for the identical
  INSERT-then-fetch-ID operation.
- Oracle: `RequiresOutputParameterForReturning => true` is Oracle's sole positive case for that
  capability among all 16 dialects — every other RETURNING-capable database gets its generated
  key via a result set, not an OUT parameter.
