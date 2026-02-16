# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

pengdows.crud 2.0 is a SQL-first, strongly-typed, testable data access layer for .NET 8. It's designed for developers who want full control over SQL without ORM magic. The project consists of multiple components:

- `pengdows.crud` - Core library with TableGateway, DatabaseContext, and SQL dialects
- `pengdows.crud.abstractions` - Interfaces and enums (all public APIs live here)
- `pengdows.crud.fakeDb` - A complete .NET DbProvider for mocking low-level calls
- `pengdows.crud.Tests` - Comprehensive unit test suite
- `pengdows.crud.IntegrationTests` - Database-specific integration tests
- `testbed` - Integration testing with real databases via Testcontainers
- `benchmarks/CrudBenchmarks/` - BenchmarkDotNet suite for performance validation
- `tools/` - Utilities (interface-api-check, verify-novendor, run-tests-in-container.sh)

## Breaking Changes from 1.0

- `EntityHelper<TEntity, TRowID>` renamed to `TableGateway<TEntity, TRowID>`
- Interface-first design mandate: all public APIs exposed through `pengdows.crud.abstractions`
- Several key interfaces refactored (see abstractions project)
- API baseline enforcement via `tools/interface-api-check`
- Separated integration tests into dedicated project

## Core Architecture

The library follows an interface-first, layered architecture:

### Main Entry Points
- **DatabaseContext** (`IDatabaseContext`): Primary connection management class wrapping ADO.NET DbProviderFactory
- **TableGateway<TEntity, TRowID>** (`ITableGateway<TEntity, TRowID>`): Generic CRUD operations for entities with strongly-typed row IDs
- **SqlContainer** (`ISqlContainer`): SQL query builder with parameterization support

### Key Patterns
- Program to interfaces; concrete types satisfy contracts in `pengdows.crud.abstractions`
- Entities use attributes for table/column mapping (`[Table]`, `[Column]`, `[Id]`, `[PrimaryKey]`)
- Audit fields via `[CreatedBy]`/`[CreatedOn]`, `[LastUpdatedBy]`/`[LastUpdatedOn]` attributes
- SQL dialect abstraction supports multiple databases (SQL Server, PostgreSQL, Oracle, MySQL, MariaDB, SQLite, DuckDB, Firebird, CockroachDB)
- Connection strategies: Standard, KeepAlive, SingleWriter, SingleConnection
- Multi-tenancy via context-per-tenant (not query filtering)

## Development Commands

**IMPORTANT**: All unit tests must pass and all integration tests (testbed) must pass. No tests may be skipped. CI enforces minimum **83% coverage**; target **95%** for new work.

### Build and Test
```bash
# Build entire solution
dotnet build pengdows.crud.sln -c Release

# Run all tests
dotnet test -c Release --results-directory TestResults --logger trx

# Run specific test by name
dotnet test --filter "MethodName=TestMethodName"

# Run tests for specific class
dotnet test --filter "ClassName=TableGatewayTests"

# Test with coverage (CI-like)
dotnet test -c Release --results-directory TestResults -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Exclude="[pengdows.crud.Tests]*;[pengdows.crud.abstractions]*;[pengdows.crud.fakeDb]*;[testbed]*"

# Run integration suite (requires Docker)
dotnet run -c Release --project testbed

# Verify API baseline
dotnet run --project tools/interface-api-check/InterfaceApiCheck.csproj -c Release -- \
  --generate \
  --baseline pengdows.crud.abstractions/ApiBaseline/interfaces.txt \
  --assembly pengdows.crud.abstractions/bin/Release/net8.0/pengdows.crud.abstractions.dll
```

### Package Management
```bash
# Restore packages
dotnet restore

# Pack projects for NuGet
dotnet pack pengdows.crud/pengdows.crud.csproj -c Release
dotnet pack pengdows.crud.abstractions/pengdows.crud.abstractions.csproj -c Release
dotnet pack pengdows.crud.fakeDb/pengdows.crud.fakeDb.csproj -c Release
```

## API Reference and Patterns

**IMPORTANT:** All interfaces in `pengdows.crud.abstractions` include comprehensive XML documentation. Refer to the XML comments for complete API documentation and implementation guidance.

### TableGateway<TEntity, TRowID> Key Methods

**CRUD Operations (All async, return Task):**
- `CreateAsync(TEntity entity, IDatabaseContext? context = null)` - Insert new entity, returns true if exactly 1 row affected
- `DeleteAsync(TRowID id, IDatabaseContext? context = null)` - Delete by ID, returns affected row count
- `DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null)` - Bulk delete, returns affected row count
- `RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null)` - Load multiple entities by IDs
- `UpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null)` - Update entity, returns affected row count
- `UpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context = null)` - Update with original loading
- `UpsertAsync(TEntity entity, IDatabaseContext? context = null)` - Insert or update, returns affected row count

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
- `WrapForStoredProc(ExecutionType executionType, bool includeParameters = true, bool captureReturn = false)` - Wrap for stored procedure execution

### DatabaseContext Key Methods

**Connection Management:**
- `GetConnection(ExecutionType executionType, bool isShared = false)` - Get tracked connection
- `CloseAndDisposeConnection(ITrackedConnection? conn)` - Return connection to pool
- `CloseAndDisposeConnectionAsync(ITrackedConnection? conn)` - Async version of connection cleanup

**Transaction Management:**
- `BeginTransaction(IsolationLevel? isolationLevel = null, ExecutionType executionType = ExecutionType.Write, bool? readOnly = null)` - Start transaction with native isolation level
- `BeginTransaction(IsolationProfile isolationProfile, ExecutionType executionType = ExecutionType.Write, bool? readOnly = null)` - Start transaction with portable isolation profile

**SQL Container Creation:**
- `CreateSqlContainer(string? query = null)` - Create new SQL builder

**Identifier Handling:**
- `WrapObjectName(string name)` - Quote identifiers safely
- `MakeParameterName(DbParameter dbParameter)` - Format parameter names

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

## Connection Management and DbMode

**Philosophy:** Open connections late, close them early. Respect database-specific quirks.

**DbMode Enum:**
```csharp
[Flags]
public enum DbMode
{
    Standard = 0,        // Recommended for production
    KeepAlive = 1,       // Keeps one sentinel connection open
    SingleWriter = 2,    // One pinned writer, concurrent ephemeral readers
    SingleConnection = 4 // All work through one pinned connection
}
```

- **Standard**: Each operation opens a new connection from the pool and closes it after use. Fully supports parallelism.
- **KeepAlive**: Keeps a single sentinel connection open to prevent unloads. Otherwise behaves like Standard.
- **SingleWriter**: Persistent write connection + ephemeral read connections. Auto-selected for file-based SQLite/DuckDB.
- **SingleConnection**: All work on a single pinned connection. Auto-selected for in-memory SQLite/DuckDB.

## Common Test Patterns

**Creating Test Context with FakeDb:**
```csharp
var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
var helper = new TableGateway<TestEntity, long>(context);
```

**Testing SQL Execution:**
```csharp
using var container = context.CreateSqlContainer("SELECT 1");
var result = await container.ExecuteScalarAsync<int>();
```

**Testing CRUD Operations:**
```csharp
var helper = new TableGateway<TestEntity, int>(context);
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

## Key Implementation Details

### Type Safety
- `TRowID` must be primitive integer, `Guid`, or `string` (nullable allowed)
- Automatic type coercion between .NET types and database types
- Enum support with configurable parsing behavior (string or numeric via DbType)
- JSON serialization support for complex types

### SQL Generation
- Database-agnostic SQL with dialect-specific optimizations
- Automatic parameterization prevents SQL injection
- Provider-specific UPSERT: MERGE (SQL Server/Oracle), ON CONFLICT (PostgreSQL), ON DUPLICATE KEY (MySQL/MariaDB)
- Schema-aware operations with proper object name quoting

### Working with the Codebase

**Key Principles:**
- All async operations return Task or Task<T>
- IDatabaseContext parameter is often optional (defaults to instance context)
- Use ISqlContainer for composable SQL building
- Always dispose contexts and containers (preferably with `using`/`await using`)
- Entity classes need proper attributes: `[Table]`, `[Id]`, `[Column]`, etc.
- Program to interfaces (`IDatabaseContext`, `ITableGateway`, `ISqlContainer`)

**Common Mistakes to Avoid:**
- Don't expose public constructors on implementation types (except DatabaseContext)
- Use `RetrieveAsync` for multiple entities, `RetrieveOneAsync` for single by ID/key, `LoadSingleAsync` for custom SQL
- Don't forget `TypeMapRegistry` in tests (though auto-registration handles most cases)
- Use correct `ExecutionType` (Read vs Write) for connections
- Don't confuse `[Id]` (pseudo key/row ID) with `[PrimaryKey]` (business key) - see AGENTS.md for details
