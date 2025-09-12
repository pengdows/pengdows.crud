# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

pengdows.crud is a SQL-first, strongly-typed, testable data access layer for .NET 8. It's designed for developers who want full control over SQL without ORM magic. The project consists of multiple components:

- `pengdows.crud` - Core library with EntityHelper, DatabaseContext, and SQL dialects
- `pengdows.crud.abstractions` - Interfaces and enums
- `pengdows.crud.fakeDb` - a complete .net DbProvider for mocking low level calls.
- `pengdows.crud.Tests` - Comprehensive test suite
- `testbed` - Integration testing with real databases

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
- Connection strategies: Standard, KeepAlive, Shared, SingleWriter
- Multi-tenancy support via tenant resolution
### Primary Key vs. Row ID (Pseudo Key)
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

**IMPORTANT**: Whenever changes are made to this codebase, ALL unit tests should pass and ALL integration tests (in the "testbed" app) should pass. This ensures code quality and prevents regressions across all supported database providers.

### Build and Test
```bash
# Build entire solution
dotnet build pengdows.crud.sln

# Run all tests
dotnet test pengdows.crud.Tests/pengdows.crud.Tests.csproj

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

**Test-Driven Development (TDD):**
- TDD should be followed for all new features and bug fixes
- Test coverage should be raised to and maintained at **90%** minimum
- Write tests first, then implement functionality
- Ensure tests are comprehensive, including edge cases and error conditions

**Test Coverage Requirements:**
- Maintain minimum **90% test coverage** across all projects
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
public enum DbMode
{
    Standard = 0,       // Recommended for production
    KeepAlive = 1,      // Keeps one sentinel connection open
    SingleWriter = 2,   // One pinned writer, concurrent ephemeral readers
    SingleConnection = 4 // All work goes through one pinned connection
}
```

**Mode Descriptions:**
Use the lowest number (closest to Standard) possible for best results.

- **Standard**: Recommended for production. Each operation opens a new connection from the pool and closes it after use, unless inside a transaction. Fully supports parallelism and provider connection pooling.

- **KeepAlive**: Keeps a single sentinel connection open (never used for work) to prevent unloads in some embedded/local DBs. Otherwise behaves like Standard.

- **SingleWriter**: Holds one persistent write connection open. Acquires ephemeral read-only connections as needed. Used automatically for file-based SQLite.

- **SingleConnection**: All work — reads and writes — is funneled through a single pinned connection. Used automatically for in-memory SQLite.

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

**Parameter Creation:**
- `CreateDbParameter<T>(string? name, DbType type, T value)` - Create named parameter
- `CreateDbParameter<T>(DbType type, T value)` - Create positional parameter

**Identifier Handling:**
- `WrapObjectName(string name)` - Quote identifiers safely
- `MakeParameterName(DbParameter dbParameter)` - Format parameter names
- `MakeParameterName(string parameterName)` - Format raw parameter names

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

**When Adding Tests:**
- Follow existing patterns for SQL dialect implementations
- Use the attribute-based entity mapping consistently
- Ensure new features work across all supported database providers
- Add comprehensive tests including edge cases and error conditions
- Maintain backwards compatibility in public APIs
- Use `await using` for proper async disposal
- Test both success and failure scenarios
