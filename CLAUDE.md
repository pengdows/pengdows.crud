# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Test Commands

```bash
# Build entire solution
dotnet build pengdows.crud.sln

# Run unit tests
dotnet test pengdows.crud.Tests/pengdows.crud.Tests.csproj

# Run integration tests (requires Docker)
dotnet test pengdows.crud.IntegrationTests/pengdows.crud.IntegrationTests.csproj

# Run testbed orchestrator (all databases)
cd testbed && dotnet run

# Run benchmarks
cd benchmarks/CrudBenchmarks && dotnet run -c Release

# Run tests in container (no local SDK needed)
./tools/run-tests-in-container.sh
```

## Project Overview

pengdows.crud is a SQL-first, strongly-typed, testable data access layer for .NET 8. It's designed for developers who want full control over SQL without ORM magic. The project consists of multiple components:

- `pengdows.crud` - Core library with EntityHelper, DatabaseContext, and SQL dialects
- `pengdows.crud.abstractions` - Interfaces and enums
- `pengdows.crud.fakeDb` - a complete .net DbProvider for mocking low level calls
- `pengdows.crud.Tests` - Comprehensive unit test suite
- `pengdows.crud.IntegrationTests` - Database-specific integration tests
- `testbed` - Multi-database provider testing orchestrator (requires Docker)
- `docs/` - Connection management walkthroughs and parameter naming guidance
- `benchmarks/CrudBenchmarks/` - BenchmarkDotNet suite for performance validation
- `tools/` - Build and validation utilities:
  - `verify-novendor/` - Ensures vendor directories aren't committed
  - `interface-api-check/` - Validates interface API changes
  - `run-tests-in-container.sh` - Runs tests in Docker without local .NET SDK

## Core Architecture

The library follows a layered architecture with these key components:

### Main Entry Points
- **DatabaseContext**: Primary connection management class that wraps ADO.NET DbProviderFactory
- **EntityHelper<TEntity, TRowID>**: Generic CRUD operations for entities with strongly-typed row IDs
- **SqlContainer**: SQL query builder with parameterization support

### Key Patterns
- Entities use attributes for table/column mapping (`TableAttribute`, `ColumnAttribute`, `IdAttribute`)
- Audit fields supported via `CreatedBy/On`, `LastUpdatedBy/On` attributes
- SQL dialect abstraction supports multiple databases (SQL Server, PostgreSQL, Oracle, MySQL, SQLite, etc.)
- Connection strategies: Standard, KeepAlive, SingleWriter, SingleConnection
- Multi-tenancy support via tenant resolution

### Supported Database Dialects

| Database     | `SupportedDatabase` Enum | Tested Version | Notes |
|--------------|--------------------------|----------------|-------|
| SQL Server   | `SqlServer`              | 2019+          | Full MERGE support, session settings |
| PostgreSQL   | `PostgreSql`             | 12+            | `ON CONFLICT`, windowed upserts |
| Oracle       | `Oracle`                 | 19c+           | MERGE + sequence support |
| MySQL        | `MySql`                  | 8.0+           | `ON DUPLICATE KEY UPDATE` |
| MariaDB      | `MariaDb`                | 10.5+          | MySQL-compatible dialect |
| SQLite       | `Sqlite`                 | 3.35+          | File-backed and `:memory:` |
| Firebird     | `Firebird`               | 4.0+           | Embedded mode, returning clauses |
| CockroachDB  | `CockroachDb`            | 22+            | PostgreSQL-compatible |
| DB2          | `Db2`                    | 11.5+          | Partitioned SQL, MERGE |
| Snowflake    | `Snowflake`              | Current        | Cloud native, ANSI vibes |
| Informix     | `Informix`               | 14+            | Legacy support, stored procs |
| SAP HANA     | `SapHana`                | 2.0+           | UPSERT-style semantics |

### Default Pool Sizes (Provider vs Practical)

| SupportedDatabase | Default Max Pool Size (provider) | Practical / Recommended Max Pool Size | Key Practical Limits & Advice |
|-------------------|----------------------------------|---------------------------------------|-------------------------------|
| SqlServer (Microsoft.Data.SqlClient) | 100 | 50-200 (often 100-150 safe) | Per app instance rarely >200; total server connections limited by memory (approx 10-20 KB per conn + query plans). Rule of thumb: 2-4x CPU cores per app instance, or 100-300 total cluster-wide. Large pools (>500) often cause context switching thrash on DB server. |
| PostgreSql (Npgsql) | 100 (since ~3.1) | 20-100 per app instance (often 30-80 optimal) | Strong consensus: 2-4x CPU cores on the DB server. Each conn ~1-3 MB RAM on Postgres side. >100-150 often overloads small/medium instances. Use PgBouncer if >50-100 needed per app; set app pool to 20-50 and let PgBouncer multiplex. |
| MySql / MariaDb (MySqlConnector / MySql.Data) | 100 | 50-200 (often 100-150) | Similar to SqlServer: 100 is safe default. Threads are lighter than Postgres but still ~1-2 MB per conn. Practical ceiling often 200-500 before thread contention or memory pressure. ProxySQL or MySQL Router recommended beyond ~200. |
| Oracle (Oracle.ManagedDataAccess) | 100 | 50-200 | Sessions are heavier (few MB each). Practical max often 100-300 before session/memory limits kick in. Enterprise tuning often caps at 100-150 per instance. |
| Sqlite (Microsoft.Data.Sqlite) | Effectively unlimited (pooling enabled by default since v6, no hard max) | 1-20 (or unlimited for in-memory) | Single-writer lock means >1-4 concurrent writers kills perf. Practical: keep pool small (5-20) or disable pooling for high concurrency. In-memory/shared can handle more, but still file-lock limited on disk. |
| DuckDb (.NET DuckDB) | Effectively unlimited (no hard pool limit in most impls) | 1-8 (or up to threads count) | Embedded: connection creation is cheap. Practical: single connection often best; multiple only if parallelizing queries. Limit to CPU cores or threads setting. No real pool exhaustion; bottleneck is CPU/RAM for queries, not connections. |
| Sql92 fallback / unknown | 100 | 50-100 | Conservative defaults for generic relational DBs. |

### Connection Modes (DbMode) - Quick Reference

| Mode | Connections | Use Case | Thread-Safety |
|------|-------------|----------|---------------|
| Standard | Per-operation | Default, most flexible | Yes |
| KeepAlive | Pooled/shared | High-throughput reads with persistent sentinel connection | Yes (with locking) |
| SingleWriter | 1 write + N read | Embedded providers, serialized writes | Yes |
| SingleConnection | 1 shared | Ordered work, single-threaded orchestration | No (requires outer synchronization) |

### Performance: StringBuilderLite (v1.1+)

`StringBuilderLite` is a lightweight string builder optimized for SQL construction:
- 60-70% fewer allocations than `StringBuilder` for common workloads
- Stack-allocated initial buffer with heap fallback for SQL batches <4 KB
- Tailored for query/command builders such as `SqlContainer.Query`
- Exposed in `pengdows.crud/StringBuilderLite.cs`

The SQL container uses `StringBuilderLite` internally to minimize GC pressure during CRUD loops while still supporting interpolation-safe SQL fragments.

### CRITICAL: Pseudo Key (Row ID) vs Primary Key (Business Key)

**THIS IS A FUNDAMENTAL DESIGN PRINCIPLE. DO NOT CONFUSE THESE CONCEPTS.**

pengdows.crud distinguishes between two types of keys:

#### Pseudo Key / Row ID (`[Id]` attribute)
- **What it is**: A surrogate identifier for the row itself, typically auto-increment or GUID
- **Always single column** - never composite
- **Purpose**: Easy lookup, foreign key references, EntityHelper operations
- **Required by EntityHelper** for `CreateAsync`, `UpdateAsync`, `DeleteAsync(TRowID)`, etc.
- **Attribute**: `[Id]` or `[Id(false)]` for DB-generated (autoincrement)

#### Primary Key / Business Key (`[PrimaryKey]` attribute)
- **What it is**: The natural/business key - the reason the row exists in business terms
- **Can be composite** (multiple columns)
- **Purpose**: Business uniqueness constraint
- **Database level**: Creates unique index, no nulls, often determines physical row ordering (clustered)
- **Attribute**: `[PrimaryKey(order)]` where order defines column sequence

#### Example: Order Line Items
```csharp
[Table("order_items")]
public class OrderItem
{
    [Id(false)]  // Pseudo key - DB-generated, used by EntityHelper
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [PrimaryKey(1)]  // Business key part 1
    [Column("order_id", DbType.Int32)]
    public int OrderId { get; set; }

    [PrimaryKey(2)]  // Business key part 2
    [Column("product_id", DbType.Int32)]
    public int ProductId { get; set; }

    [Column("quantity", DbType.Int32)]
    public int Quantity { get; set; }
}
```

**Database DDL:**
```sql
CREATE TABLE order_items (
    id INTEGER PRIMARY KEY,              -- Pseudo key (clustered)
    order_id INTEGER NOT NULL,
    product_id INTEGER NOT NULL,
    quantity INTEGER NOT NULL,
    UNIQUE (order_id, product_id)        -- Business key constraint
);
```

**Key Rules:**
1. `[Id]` and `[PrimaryKey]` are MUTUALLY EXCLUSIVE - never both on the same column
2. EntityHelper REQUIRES an `[Id]` column for CRUD operations
3. `[PrimaryKey]` defines business uniqueness, enforced via UNIQUE constraint
4. Both can coexist on DIFFERENT columns - pseudo key for operations, business key for domain integrity
5. `RetrieveOneAsync(TEntity)` uses `[PrimaryKey]` columns for lookup
6. `DeleteAsync(TRowID)` uses the `[Id]` column

### Version Column (Optimistic Concurrency)

The `[Version]` attribute enables optimistic concurrency control:

| Operation | Behavior |
|-----------|----------|
| **Create** | If version is null/0, automatically set to 1 |
| **Update** | Increments version by 1 in SET clause; adds `WHERE version = @currentVersion` |

**Conflict detection:** If `UpdateAsync` returns 0 rows affected, another process modified the row (version mismatch).

### Upsert Behavior

`UpsertAsync` / `BuildUpsert` determines insert vs update based on conflict key:

1. **Primary choice:** `[PrimaryKey]` columns (if any defined)
2. **Fallback:** `[Id]` column ONLY if writable (`[Id(true)]` or `[Id]`)
3. **Error:** Throws if no `[PrimaryKey]` AND `[Id]` is not writable (`[Id(false)]`)

**SQL generated depends on database:**
- SQL Server/Oracle: `MERGE`
- PostgreSQL: `INSERT ... ON CONFLICT`
- MySQL/MariaDB: `INSERT ... ON DUPLICATE KEY UPDATE`

### Id Attribute: Writable vs Non-Writable

| Attribute | Meaning | INSERT behavior |
|-----------|---------|-----------------|
| `[Id]` or `[Id(true)]` | Client provides value | Id column included in INSERT |
| `[Id(false)]` | DB generates value (autoincrement/identity) | Id column omitted from INSERT |

**SQL Server note:** Attempting to insert a value into an IDENTITY column throws an error unless `SET IDENTITY_INSERT ON`.

### Multi-Tenancy

pengdows.crud uses **context-per-tenant** (not query filtering):

- Each tenant gets a separate `DatabaseContext` (different connection string/database)
- Request resolves which context to use
- All operations use that context - no additional filtering required
- **No "WHERE tenant_id = X" injection** - tenants are physically separated

### ExecutionType (Read vs Write)

`ExecutionType` declares intent so the context can provide the appropriate connection:

| Type | Intent | Connection behavior |
|------|--------|---------------------|
| `ExecutionType.Read` | Read-only operation | May get ephemeral or shared connection |
| `ExecutionType.Write` | Modifying operation | Gets write-capable connection |

In `SingleWriter` mode, this determines whether you get the pinned write connection or an ephemeral read connection.

### TypeMapRegistry.Register<T>()

**Explicit registration is NOT required.** `GetTableInfo<T>()` uses `GetOrAdd` - auto-builds on first access.

```csharp
// These are equivalent:
typeMap.Register<MyEntity>();           // Explicit pre-registration
typeMap.GetTableInfo<MyEntity>();       // Auto-registers on first call
new EntityHelper<MyEntity, long>(ctx);  // Also triggers auto-registration
```

**When to register explicitly:** pre-registering at startup can help catch mapping errors early and slightly reduce on-demand reflection for high-volume hot paths, but it is optional in most scenarios.

### Parallel Operations with `pengdows.threading`

When you need bulk inserts, updates, or random reads with throttled concurrency, combine `EntityHelper` with `pengdows.threading` helpers:

```csharp
using var converge = new ConvergeWait(maxConcurrency: 10);
var helper = new EntityHelper<MyEntity, long>(context);

foreach (var entity in entities)
{
    converge.Queue(async () =>
    {
        var container = helper.BuildCreate(entity);
        await container.ExecuteNonQueryAsync();
    });
}

await converge.WaitAsync();
if (converge.Exceptions.Any())
{
    // Handle failures
}
```

`ConvergeWait` lets you bound concurrency, retry transient errors, and aggregate exceptions while staying within the single `DatabaseContext`.

### Enum Storage

Enum storage format is determined by `DbType` in the `[Column]` attribute:

| DbType | Storage |
|--------|---------|
| `DbType.String` | Stored as enum name (string) |
| Numeric (`Int32`, etc.) | Stored as underlying numeric value |

**Throws** if DbType is neither string nor numeric.

```csharp
[Column("status", DbType.String)]    // Stored as "Active", "Inactive", etc.
public StatusEnum Status { get; set; }

[Column("priority", DbType.Int32)]   // Stored as 0, 1, 2, etc.
public PriorityEnum Priority { get; set; }
```

### RetrieveOneAsync(TEntity) Requirements

`RetrieveOneAsync(TEntity objectToRetrieve)` uses `[PrimaryKey]` columns to find the row.

**If no `[PrimaryKey]` columns defined:** Throws `"No primary keys found for type {TypeName}"`

This method is for lookup by business key. Use `RetrieveOneAsync(TRowID id)` for lookup by pseudo key.

### CRITICAL: Audit Field Behavior

**BOTH CreatedBy/On AND LastUpdatedBy/On are set on CREATE.**

This is intentional design - it allows "last modified" queries without checking if the entity was ever updated.

| Operation | CreatedBy | CreatedOn | LastUpdatedBy | LastUpdatedOn |
|-----------|-----------|-----------|---------------|---------------|
| **Create** | SET | SET | SET | SET |
| **Update** | unchanged | unchanged | SET | SET |

**Requirements:**
- If entity has `[CreatedBy]` or `[LastUpdatedBy]`, you MUST provide `IAuditValueResolver`
- Without resolver + user audit fields = `InvalidOperationException` at runtime
- Time-only audit fields (`[CreatedOn]`, `[LastUpdatedOn]`) work without resolver (uses `DateTime.UtcNow`)

**Example:**
```csharp
// Entity with audit fields
[Table("orders")]
public class Order
{
    [Id] public long Id { get; set; }
    [Column("total")] public decimal Total { get; set; }

    [CreatedOn] [Column("created_at")] public DateTime CreatedAt { get; set; }
    [CreatedBy] [Column("created_by")] public string CreatedBy { get; set; }
    [LastUpdatedOn] [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    [LastUpdatedBy] [Column("updated_by")] public string UpdatedBy { get; set; }
}

// MUST provide resolver when using user audit fields
var helper = new EntityHelper<Order, long>(context, auditValueResolver: myResolver);
```

### Project Structure
- `pengdows.crud/` - Core implementation
  - `attributes/` - Entity mapping attributes
  - `dialects/` - Database-specific SQL generation
  - `connection/` - Connection management strategies
  - `exceptions/` - Custom exception types
  - `isolation/` - Transaction isolation handling
  - `tenant/` - Multi-tenancy support
- `pengdows.crud.Tests/` - Unit and integration tests
- `testbed/` - Database provider testing infrastructure

## Development Commands

**CRITICAL REQUIREMENTS**:
- **ALL CODE MUST BE WRITTEN USING TDD** - Write tests FIRST, then implementation
- **ALL CODE MUST BE TESTED** - No untested code is acceptable
- ALL unit tests must pass
- ALL integration tests must pass
- NO tests may be skipped
- When functionality is unclear, consult the wiki (`pengdows.crud.wiki/`) first or ask for clarification before making changes

### Build and Test
```bash
# Build entire solution
dotnet build pengdows.crud.sln

# Run all unit tests
dotnet test pengdows.crud.Tests/pengdows.crud.Tests.csproj

# Run all tests with coverage (as in CI)
dotnet test --configuration Release \
  --collect:"XPlat Code Coverage" \
  --results-directory ./TestResults \
  --settings coverage.runsettings

# Run integration tests
dotnet test pengdows.crud.IntegrationTests/pengdows.crud.IntegrationTests.csproj

# Run testbed for database provider testing (requires Docker)
cd testbed && dotnet run

# Run tests in Docker (no local .NET SDK required)
./tools/run-tests-in-container.sh

# Run specific test by name
dotnet test --filter "MethodName=TestMethodName"

# Run tests for specific class
dotnet test --filter "ClassName=EntityHelperTests"

# Build with Release configuration
dotnet build -c Release
```

### Package Management
```bash
# Restore packages
dotnet restore

# Pack projects for NuGet (local development)
dotnet pack pengdows.crud/pengdows.crud.csproj -c Release
dotnet pack pengdows.crud.abstractions/pengdows.crud.abstractions.csproj -c Release
dotnet pack pengdows.crud.fakeDb/pengdows.crud.fakeDb.csproj -c Release

# Publishing is handled automatically by GitHub Actions on main branch pushes
# See .github/workflows/deploy.yml for the automated build and publish process
```

### Testing Infrastructure and Guidelines

**Test-Driven Development (TDD) - MANDATORY:**

**YOU MUST FOLLOW TDD FOR ALL CODE CHANGES. THIS IS NON-NEGOTIABLE.**

The TDD workflow is:
1. **Write the test FIRST** - Before writing any implementation code
2. **Run the test** - Verify it fails (red)
3. **Write minimal implementation** - Make the test pass (green)
4. **Refactor** - Improve code while keeping tests green
5. **Repeat** - For every feature, bug fix, or change

**TDD Requirements:**
- Tests MUST be written before implementation code
- ALL code MUST have corresponding tests
- Tests for expected behavior must be authored before touching the implementation
- Tests must be comprehensive, including edge cases and error conditions
- Unit tests should complete in well under 30 seconds; if a run exceeds three minutes, stop it and diagnose the likely locking problem right away
- Database-specific features require integration tests in `pengdows.crud.IntegrationTests`
- NO implementation code without tests - this is a hard rule

**Test Coverage Requirements:**
- CI enforces minimum **83% line coverage** (see .github/workflows/deploy.yml)
- Target **90% test coverage** for new features and bug fixes
- Use meaningful test names that describe the behavior being tested
- Test both success and failure scenarios
- Include integration tests for database-specific functionality

**FakeDb Infrastructure:**
The `fakeDb` package provides mock database providers for testing without real database connections. If fakeDb doesn't support something needed for testing, **expand it to support the required functionality** rather than working around limitations.

#### Connection Breaking for Testing
The enhanced `FakeDbConnection` supports sophisticated connection failure simulation:

```csharp
// Connection that fails on open
var connection = (FakeDbConnection)factory.CreateConnection();
connection.SetFailOnOpen();
connection.Open(); // Throws InvalidOperationException

// Custom timeout exception
connection.SetCustomFailureException(new TimeoutException("Connection timeout"));
connection.SetFailOnOpen();

// Fail after N successful operations
connection.SetFailAfterOpenCount(3);

// Factory-level failure configuration
var factory = FakeDbFactory.CreateFailingFactory(
    SupportedDatabase.PostgreSql,
    ConnectionFailureMode.FailOnOpen);

// Helper for database context testing
using var context = ConnectionFailureHelper.CreateFailOnOpenContext();
```

Connection failure modes include:
- `FailOnOpen` - Connection fails when opening
- `FailOnCommand` - Connection fails when creating commands
- `FailOnTransaction` - Connection fails when beginning transactions
- `FailAfterCount` - Connection works for N operations then fails
- `Broken` - Connection is permanently broken

## Key Implementation Details

### Type Safety
- `TRowID` must be primitive integer, `Guid`, or `string` (nullable allowed)
- Automatic type coercion between .NET types and database types
- Enum support with configurable parsing behavior
- JSON serialization support for complex types

### SQL Generation
- Database-agnostic SQL with dialect-specific optimizations
- Automatic parameterization prevents SQL injection
- Support for MERGE statements where available (SQL Server, Oracle, Firebird, PostgreSQL 15+)
- Schema-aware operations with proper object name quoting

### Connection Management and DbMode

**pengdows.crud** handles connections with a strong bias toward performance, predictability, and safe concurrency.
At the heart of this is **DbMode**, which defines how each DatabaseContext manages its connection lifecycle.

**Overview:**
The philosophy is simple:
* Open connections late — only when needed
* Close connections early — as soon as possible
* Respect database-specific quirks (see Connection Pooling for SQLite and LocalDB rules)

**Advantages:**
* Prevents exhausting your connection pool
* Avoids leaking resources or unclosed connections
* Reduces cost in cloud environments by minimizing active resource usage

**DbMode Enum:**
```csharp
[Flags]
public enum DbMode
{
    Standard = 0,       // Recommended for production
    KeepAlive = 1,      // Keeps one sentinel connection open
    SingleWriter = 2,   // One pinned writer, concurrent ephemeral readers
    SingleConnection = 4, // All work goes through one pinned connection
    Best = 15
}
```

**Mode Descriptions:**
Use the lowest number (closest to Standard) possible for best results.

- **Standard**: Recommended for production. Each operation opens a new connection from the pool and closes it after use, unless inside a transaction. Fully supports parallelism and provider connection pooling.

- **KeepAlive**: Keeps a single sentinel connection open (never used for work) to prevent unloads in some embedded/local DBs. Otherwise behaves like Standard.

- **SingleWriter**: Holds one persistent write connection open. Acquires ephemeral read-only connections as needed. Used automatically for file-based SQLite/DuckDB and named in-memory databases that enable `Mode=Memory;Cache=Shared` so multiple connections share one database.

- **SingleConnection**: All work — reads and writes — is funneled through a single pinned connection. Used automatically for isolated in-memory SQLite/DuckDB where each `:memory:` connection would otherwise have its own private database.

**Best Practices:**
* **Use Standard in production** for scalability and correctness
* KeepAlive, SingleWriter, and SingleConnection are best suited for embedded/local DBs or dev/test
* Each DatabaseContext can be safely used as a singleton (via DI or subclassing)

**Benefits:**
* Avoids connection starvation and excessive licensing costs (per active connection)
* Plays well with provider-managed pooling
* Handles embedded/local DB quirks without manual intervention

**Integration with Transactions:**
* Inside a TransactionContext, the pinned connection stays open for the life of the transaction
* Outside transactions, connections are opened per-operation and closed immediately after

**Observability:**
* Tracks current and max open connections with thread-safe `Interlocked` counters
* Useful for tuning pool sizes and spotting load issues

**Timeout Recommendations:**
* Set connection timeouts as **low as reasonable** to avoid hanging on transient failures
* Because pengdows.crud reconnects for every call, long timeouts are unnecessary

**Connection Strategy Implementation:**
- Transaction scoping via TransactionContext
- Isolation level management per database
- Connection strategy patterns for different use cases

## API Reference and Patterns

**IMPORTANT:** All interfaces in `pengdows.crud.abstractions` now include comprehensive XML documentation with detailed descriptions, parameter explanations, usage examples, and code samples. When working with these interfaces, refer to the XML comments for complete API documentation and implementation guidance.

### EntityHelper<TEntity, TRowID> Key Methods

**CRUD Operations (All async, return Task):**
- `DeleteAsync(TRowID id, IDatabaseContext? context = null)` - Delete by ID, returns affected row count
- `DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null)` - Bulk delete, returns affected row count
- `RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null)` - Load multiple entities by IDs
- `UpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null)` - Update entity, returns affected row count
- `UpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context = null)` - Update with original loading
- `UpsertAsync(TEntity entity, IDatabaseContext? context = null)` - Insert or update, returns affected row count
- `CreateAsync(TEntity entity, IDatabaseContext context)` - Insert new entity, returns true if exactly 1 row affected

**Query Building (Return ISqlContainer for composition):**
- `BuildCreate(TEntity objectToCreate, IDatabaseContext? context = null)` - Generate INSERT statement
- `BuildBaseRetrieve(string alias, IDatabaseContext? context = null)` - Generate SELECT with no WHERE
- `BuildRetrieve(IReadOnlyCollection<TRowID>? listOfIds, string alias, IDatabaseContext? context = null)` - SELECT by IDs
- `BuildUpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null)` - Generate UPDATE statement
- `BuildDelete(TRowID id, IDatabaseContext? context = null)` - Generate DELETE statement
- `BuildUpsert(TEntity entity, IDatabaseContext? context = null)` - Generate provider-specific UPSERT

**Data Loading:**
- `LoadSingleAsync(ISqlContainer sc)` - Execute SQL and return single entity or null
- `LoadListAsync(ISqlContainer sc)` - Execute SQL and return list of entities

**Single Entity Retrieval:**
- `RetrieveOneAsync(TEntity objectToRetrieve, IDatabaseContext? context = null)` - Load by composite key values
- `RetrieveOneAsync(TRowID id, IDatabaseContext? context = null)` - Load by row ID

### SqlContainer Key Methods

**Query Execution:**
- `ExecuteNonQueryAsync(CommandType commandType = CommandType.Text)` - Execute INSERT/UPDATE/DELETE, returns row count
- `ExecuteScalarAsync<T>(CommandType commandType = CommandType.Text)` - Execute and return single value
- `ExecuteReaderAsync(CommandType commandType = CommandType.Text)` - Execute and return ITrackedReader

**Parameter Management:**
- `AddParameterWithValue<T>(DbType type, T value)` - Add parameter, returns DbParameter
- `AddParameterWithValue<T>(string? name, DbType type, T value)` - Add named parameter
- `CreateDbParameter<T>(string? name, DbType type, T value)` - Create parameter without adding
- `CreateDbParameter<T>(DbType type, T value)` - Create unnamed parameter
- `AddParameter(DbParameter parameter)` - Add pre-constructed parameter
- `AddParameters(IEnumerable<DbParameter> list)` - Add multiple parameters

**Query Building:**
- `Query` property - StringBuilder for building SQL
- `HasWhereAppended` - Indicates if WHERE clause already exists
- `ParameterCount` - Current count of parameters
- `WrapObjectName(string name)` - Quote identifiers safely
- `MakeParameterName(DbParameter dbParameter)` - Format parameter names
- `MakeParameterName(string parameterName)` - Format raw parameter names

**Command Management:**
- `CreateCommand(ITrackedConnection conn)` - Create DbCommand for connection
- `Clear()` - Clear query and parameters
- `WrapForStoredProc(ExecutionType executionType, bool includeParameters = true)` - Wrap for stored procedure execution

### DatabaseContext Key Methods

**Connection Management:**
- `GetConnection(ExecutionType executionType, bool isShared = false)` - Get tracked connection
- `CloseAndDisposeConnection(ITrackedConnection? conn)` - Return connection to pool
- `CloseAndDisposeConnectionAsync(ITrackedConnection? conn)` - Async version of connection cleanup

**Transaction Management:**
- `BeginTransaction(IsolationLevel? isolationLevel = null, ExecutionType executionType = ExecutionType.Write)` - Start transaction with native isolation level
- `BeginTransaction(IsolationProfile isolationProfile, ExecutionType executionType = ExecutionType.Write)` - Start transaction with portable isolation profile

**SQL Container Creation:**
- `CreateSqlContainer(string? query = null)` - Create new SQL builder

**Parameter Creation:** Use the `SqlContainer` helpers (see Parameter Management above) or call `CreateDbParameter` from the context when you need a parameter without the SQL container pattern.

**Identifier Handling:**
- `WrapObjectName(string name)` - Quote identifiers safely

**Connection Properties:**
- `ConnectionMode` - Which DbMode this connection uses
- `TypeMapRegistry` - Type mapping for compiled accessors and JSON handlers
- `DataSourceInfo` - Metadata from connection.GetSchema and provider heuristics
- `Product` - Detected database product (PostgreSQL, Oracle, etc.)
- `NumberOfOpenConnections` - Current open connection count
- `MaxNumberOfConnections` - Max observed concurrent connections

### Transaction Context (ITransactionContext)

Extends IDatabaseContext for transactional operations:

**Transaction State:**
- `WasCommitted` - Whether transaction was committed
- `WasRolledBack` - Whether transaction was rolled back
- `IsCompleted` - Whether transaction is completed
- `IsolationLevel` - Current isolation level

**Transaction Control:**
- `Commit()` - Commit transaction
- `Rollback()` - Rollback transaction
- `SavepointAsync(string name)` - Create savepoint
- `RollbackToSavepointAsync(string name)` - Rollback to savepoint

### Metadata Interfaces

**ITableInfo:**
- `Schema` - Table schema name
- `Name` - Table name
- `Columns` - Dictionary of column metadata
- `Id`, `Version`, `LastUpdatedBy/On`, `CreatedBy/On` - Audit columns
- `HasAuditColumns` - Whether audit columns are configured

**IColumnInfo:**
- `Name` - Column name and `PropertyInfo` - Property reflection info
- `IsId`, `IsPrimaryKey`, `PkOrder` - Primary key configuration
- `DbType` - Database type mapping
- `IsNonUpdateable`, `IsNonInsertable` - Update/insert restrictions
- `IsEnum`, `EnumType` - Enum handling configuration
- `IsJsonType`, `JsonSerializerOptions` - JSON serialization support
- `IsVersion`, `IsCreatedBy/On`, `IsLastUpdatedBy/On` - Audit field markers
- `MakeParameterValueFromField<T>(T objectToCreate)` - Parameter value creation

**ITypeMapRegistry:**
- `GetTableInfo<T>()` - Get table metadata for entity type

### Tracked Wrappers

**ITrackedConnection:**
Wraps IDbConnection with async support and locking:
- All standard IDbConnection methods plus `OpenAsync()`
- `GetLock()` - Returns async-compatible lock
- `GetSchema()` overloads for metadata access

**ITrackedReader:**
Extends IDataReader with async support:
- `ReadAsync()` - Async row reading
- Implements `IAsyncDisposable` for proper cleanup

### Common Test Patterns

**Creating Test Context with FakeDb:**
```csharp
var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
var context = new DatabaseContext($"Data Source=test;EmulatedProduct=Sqlite", factory);
using var helper = new EntityHelper<TestEntity, long>(context);
```

**Creating Test Context with Real SQLite:**
```csharp
public class SqlLiteContextTestBase
{
    protected SqlLiteContextTestBase()
    {
        TypeMap = new TypeMapRegistry();
        Context = new DatabaseContext("Data Source=:memory:", SqliteFactory.Instance, TypeMap);
    }
}
```

**Testing SQL Execution:**
```csharp
using var container = context.CreateSqlContainer("SELECT 1");
var result = await container.ExecuteScalarAsync<int>();
```

**Testing CRUD Operations:**
```csharp
var helper = new EntityHelper<TestEntity, int>(context);
var entity = new TestEntity { Name = "Test" };

// Insert
var createContainer = helper.BuildCreate(entity);
await createContainer.ExecuteNonQueryAsync();

// Update
var updateContainer = await helper.BuildUpdateAsync(entity);
var rowsAffected = await updateContainer.ExecuteNonQueryAsync();

// Delete
var rowsDeleted = await helper.DeleteAsync(entity.Id);

// Retrieve multiple
var entities = await helper.RetrieveAsync(new[] { 1, 2, 3 });

// Load with custom SQL
var customContainer = helper.BuildBaseRetrieve("a");
customContainer.Query.Append(" WHERE a.Name = ");
customContainer.Query.Append(customContainer.MakeParameterName("name"));
customContainer.AddParameterWithValue("name", DbType.String, "Test");
var results = await helper.LoadListAsync(customContainer);
```

### FakeDb Connection Breaking Patterns

**Basic Connection Failure:**
```csharp
var connection = (FakeDbConnection)factory.CreateConnection();
connection.SetFailOnOpen();
Assert.Throws<InvalidOperationException>(() => connection.Open());
```

**Factory-Level Configuration:**
```csharp
var factory = FakeDbFactory.CreateFailingFactory(
    SupportedDatabase.PostgreSql,
    ConnectionFailureMode.FailOnOpen);
```

**Using Connection Failure Helpers:**
```csharp
using var context = ConnectionFailureHelper.CreateFailOnOpenContext();
Assert.Throws<InvalidOperationException>(() => context.GetConnection(ExecutionType.Read));
```

## Working with the Codebase

**Key Principles:**
- All async operations return Task or Task<T>
- IDatabaseContext parameter is often optional (defaults to instance context)
- Use ISqlContainer for composable SQL building
- Always dispose contexts and containers (preferably with using/await using)
- Entity classes need proper attributes: [Table], [Id], [Column] etc.

**Common Mistakes to Avoid:**
- Don't assume method names from interfaces exist in implementations without verifying
- Always check actual method signatures in implementation files
- Use `RetrieveAsync` for multiple entities, `RetrieveOneAsync` for single entities by ID, `LoadSingleAsync` for custom SQL queries
- Don't forget to register entity types with TypeMapRegistry in tests
- Use correct `ExecutionType` (Read vs Write) for connections

**When Writing Code (TDD Process):**
1. **FIRST**: Write tests that define the expected behavior
2. **SECOND**: Run tests to confirm they fail
3. **THIRD**: Write the minimal implementation to make tests pass
4. **FOURTH**: Refactor while keeping tests green
5. **NEVER**: Write implementation code before tests

**Test Requirements:**
- Follow existing patterns for SQL dialect implementations
- Use the attribute-based entity mapping consistently
- Ensure new features work across all supported database providers
- Add comprehensive tests including edge cases and error conditions
- Maintain backwards compatibility in public APIs
- Use `await using` for proper async disposal
- Test both success and failure scenarios
- Every method, every branch, every edge case must be tested
