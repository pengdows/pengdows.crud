# Integration Test Suite

The testbed runs a fixed matrix of **17–19 checks** against every supported database to verify
that `pengdows.crud` behaves correctly across dialects. Tests run via Testcontainers (Docker)
with up to 2 databases in parallel.

## Running

```bash
# All always-on databases (10)
dotnet run -c Release --project testbed

# Include Oracle (requires license acceptance)
INCLUDE_ORACLE=true dotnet run -c Release --project testbed

# Include Snowflake (requires credentials)
INCLUDE_SNOWFLAKE=true dotnet run -c Release --project testbed

# Run only specific databases
dotnet run -c Release --project testbed -- --only PostgreSQL,MySQL

# Exclude specific databases
dotnet run -c Release --project testbed -- --exclude SQLite
```

## Always-on Databases

SQLite, DuckDB, PostgreSQL, MySQL, MariaDB, SQL Server, CockroachDB, Firebird, TiDB, YugabyteDB

## Opt-in Databases

| Database | Env var | Reason |
|----------|---------|--------|
| Oracle | `INCLUDE_ORACLE=true` | Image requires license acceptance |
| Snowflake | `INCLUDE_SNOWFLAKE=true` | Cloud-only, requires credentials |

---

## Test Scenarios

### 1. CreateTable
Drops and recreates a 10-column test table (`id`, `name`, `description`, `value`, `is_active`,
`created_at`, `created_by`, `updated_at`, `updated_by`) using dialect-specific DDL. Oracle uses
`NUMBER` + `SEQUENCE`/`TRIGGER`; SQL Server uses `IDENTITY`; MySQL uses `BIGINT AUTO_INCREMENT`,
etc.

### 2. InsertRows
Inserts one row using `BuildCreate()` + `ExecuteNonQueryAsync()` and asserts the row count goes
from 0 to 1.

### 3. RetrieveRows
Retrieves the inserted row by ID using `BuildRetrieve()` + `LoadListAsync()` and asserts all
fields match.

### 4. DeleteRows
Deletes the row using `BuildDelete()` and asserts the row count returns to 0.

### 5. Transactions
Two sub-checks:
- **Rollback**: insert inside a transaction, roll back, assert count unchanged.
- **Commit**: insert inside a transaction, commit, assert count incremented.

Uses `Context.BeginTransaction()`, `Commit()`, `Rollback()`.

### 6. Stored Procedure Return Value
Creates a database-specific stored procedure or function, calls it via `WrapForStoredProc()`,
and verifies the return value. Each database uses native syntax:
- SQL Server: `CREATE PROCEDURE … RETURN 5`
- PostgreSQL / CockroachDB / YugabyteDB: `CREATE FUNCTION … RETURNS INT`
- MySQL / MariaDB / TiDB: `CREATE PROCEDURE … SELECT 42`
- Oracle: `CREATE PROCEDURE … :result := 42`
- Firebird: `CREATE PROCEDURE … SUSPEND`
- Snowflake: `CREATE PROCEDURE … RETURNS VARCHAR LANGUAGE JAVASCRIPT`

*Skipped: SQLite, DuckDB (no stored procedure support)*

### 7. Scalar UDF
Creates and calls a scalar user-defined function inline in a `SELECT` statement. Currently only
implemented for Snowflake (the base class is a no-op, so all other databases skip this check
silently).

*Skipped: all databases except Snowflake*

### 8. Parameter Binding
Four sub-checks:
- **Marker format**: verifies correct prefix (`:` Oracle/Firebird, `@` SQL Server/MySQL/PostgreSQL,
  `$` DuckDB).
- **NULL semantics**: `col = NULL` returns 0 rows; `col IS NULL` on a NOT NULL column returns 0
  rows.
- **Duplicate named parameters**: SQL Server/MySQL/PostgreSQL allow the same name twice; Oracle
  requires distinct names (`p0`, `p1`) — the Oracle provider overrides this check.
- **Type matrix**: creates a `binding_matrix` table and round-trips `int`, `long`, `decimal`,
  `bool`, `string`, `DateTimeOffset` (skipped if unsupported), `Guid` (skipped if unsupported),
  `binary` through parameterized INSERT + SELECT.

### 9. Row Round-Trip
Inserts a row with Unicode text, `bool = false`, an enum value, a specific `int`, and a
`DateTime`, then retrieves it and asserts every field coerces back to the correct .NET value.

### 10. Row Round-Trip Fidelity
Deeper type fidelity check using a dedicated `fidelity_test` table with columns:

| Column | Value tested |
|--------|-------------|
| `unicode_text` | Multi-script Unicode string |
| `empty_text` | `""` (empty string) |
| `null_text` | `NULL` |
| `padded_text` | String with leading/trailing whitespace |
| `decimal_value` | Precise decimal with scale |
| `decimal_edge` | Very small / very large decimal |
| `is_active` | `bool` false |
| `dto_value` | `DateTimeOffset` with timezone offset *(skip if unsupported)* |
| `guid_value` | `Guid` *(skip if unsupported)* |
| `binary_value` | `byte[]` blob |

Each column is verified to survive a full insert→retrieve cycle within type-specific tolerances
(e.g. datetime tolerance defaults to 2 s, widens for SQLite text storage).

### 11. Extended Transactions — Rollback on Exception
Begins a transaction, inserts a row, then throws an exception inside the transaction scope.
Verifies the implicit rollback leaves the row count unchanged.

### 12. Extended Transactions — Read-Your-Writes
Inside a transaction, inserts a row and immediately reads count — verifies the new row is
visible within the same transaction before commit. Rolls back; verifies count outside is
unchanged.

### 13. Extended Transactions — Savepoints
Creates a savepoint after inserting a first row, inserts a second row, rolls back to the
savepoint, then commits. Verifies only the first row persists.

*Skipped: MySQL, MariaDB, TiDB, Snowflake (dialect `SupportsSavepoints = false`)*

### 14. Concurrency
Spawns 5 parallel `Task`s; each independently inserts a row with a random description,
retrieves it by ID, and deletes it. Asserts all 5 tasks complete without error, exercising the
connection pool and thread-safety of `DatabaseContext`.

### 15. Command Reuse
Builds one parameterized `ISqlContainer` targeting a specific row. Executes it (1 row
returned). Swaps the parameter value via `SetParameterValue()` to target a different row.
Executes again (1 row returned). Verifies the container can be reused without rebuilding SQL.

### 16. Upsert
Two sub-checks via `BuildUpsert()`:
- **Insert path**: upserts a row that does not yet exist — row is created.
- **Update path**: mutates a field on the entity, upserts again — existing row is updated and
  retrieved value matches the mutation.

Uses dialect-native upsert:
- SQL Server / Oracle: `MERGE`
- PostgreSQL / CockroachDB / YugabyteDB: `INSERT … ON CONFLICT DO UPDATE`
- MySQL / MariaDB / TiDB: `INSERT … ON DUPLICATE KEY UPDATE`
- Firebird: `MERGE`
- SQLite: `INSERT OR REPLACE`

*Skipped: databases that support none of the above (currently none)*

### 17. Paging
Inserts 10 rows with known IDs, then paginates using dialect-specific syntax:

| Dialect | Syntax |
|---------|--------|
| PostgreSQL, MySQL, MariaDB, TiDB, SQLite, DuckDB, CockroachDB, YugabyteDB | `LIMIT n OFFSET m` |
| SQL Server | `OFFSET m ROWS FETCH NEXT n ROWS ONLY` |
| Oracle | `OFFSET m ROWS FETCH NEXT n ROWS ONLY` |
| Firebird | `ROWS m TO n` |

Retrieves page 1 (first 5 IDs) and page 2 (next 5 IDs). Asserts each page has exactly 5 rows
and there is no overlap between pages.

### 18. Error Mapping
Three sub-checks:
- **Unique violation**: inserts a duplicate row expecting a `DbException` with an informative
  message containing the constraint name. *(Skipped: Snowflake — does not enforce PK constraints)*
- **Connection health after exception**: asserts a follow-up `COUNT(*)` query succeeds after the
  exception, proving the connection is not permanently broken.
- **Syntax error**: executes an intentionally malformed SQL statement and asserts a `DbException`
  is raised with a non-empty message.

### 19. Identifier Quoting
Creates a table whose column names are reserved words or contain special characters:

| Column name | Challenge |
|-------------|-----------|
| `order` | Reserved keyword |
| `user` | Reserved keyword |
| `default` | Reserved keyword |
| `display name` | Space in name |
| `CamelCase` | Mixed case |

Inserts a row and retrieves each column by name using `WrapObjectName()` (which emits
dialect-specific quoting: `[…]` SQL Server, `"…"` PostgreSQL/Oracle, `` `…` `` MySQL). Verifies
values survive the quoting round-trip correctly.

---

## Skip Matrix

| Check | SQLite | DuckDB | MySQL | MariaDB | TiDB | Firebird | PostgreSQL | SQL Server | CockroachDB | YugabyteDB | Oracle | Snowflake |
|-------|--------|--------|-------|---------|------|----------|------------|------------|-------------|------------|--------|-----------|
| Stored Proc | ⏭ | ⏭ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Scalar UDF | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ✅ |
| DateTimeOffset binding | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ⏭ | ✅ | ✅ | ✅ | ✅ | ✅ | ⏭ |
| Guid binding | ⏭ | ✅ | ⏭ | ⏭ | ⏭ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⏭ |
| Savepoints | ⏭ | ✅ | ⏭ | ⏭ | ⏭ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⏭ |
| Unique violation check | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⏭ |

⏭ = skipped (dialect limitation)  ✅ = runs

---

## Architecture

```
testbed/
├── Program.cs                        Entry point; parses args and env vars
├── ParallelTestOrchestrator.cs       Starts containers, runs TestProvider per DB, collects results
├── TestProvider.cs                   Base class with all 17+ test scenario methods (~2000 lines)
├── TestContainer.cs                  Shared container startup / wait-for-ready logic
├── Sqlite/                           SQLite — no container (file/memory based)
├── DuckDb/
├── PostgreSql/
├── MySql/
├── MariaDb/
├── SqlServer/
├── CockroachDb/
├── Firebird/
├── TiDb/
├── YugabyteDb/
├── Oracle/                           Opt-in (INCLUDE_ORACLE=true)
└── Snowflake/                        Opt-in (INCLUDE_SNOWFLAKE=true)
```

Each database subdirectory contains:
- `*TestContainer.cs` — starts the Testcontainers image and returns a connection string
- `*TestProvider.cs` — inherits `TestProvider`, overrides DDL and any dialect-specific checks

Provider overrides are minimal by design — any deviation from baseline behavior (e.g. Oracle
using `NUMBER` instead of `INT`, Oracle requiring distinct parameter names) is captured in the
override rather than branching inside the base class.
