# pengdows.crud Integration Tests

This directory contains comprehensive integration tests for pengdows.crud that demonstrate real-world scenarios and
verify functionality across multiple database providers.

## Overview

The integration tests are organized into focused categories, each testing specific aspects of pengdows.crud's
functionality:

```
📁 pengdows.crud.IntegrationTests/
├── 📁 Infrastructure/           # Test fixture, base classes, table creators, helpers
├── 📁 Core/                    # CRUD, mapping, hydration, audit, quoting, parameters
├── 📁 Advanced/                # Transactions, concurrency, batch operations
├── 📁 DatabaseSpecific/        # Database-specific features (PostgreSQL, SQL Server, MySQL, Oracle, SQLite, DB2)
├── 📁 ConnectionManagement/    # DbMode testing and async connection acquisition
├── 📁 ErrorHandling/           # Failure scenarios and exception classification
├── IntegrationMatrixTests.cs   # Runs the full testbed provider matrix under `dotnet test`
└── SpannerOmniIntegrationTests.cs
```

## Test Categories

### 🔧 Core Tests

**Location**: `Core/`

- **BasicCrudTests**: Create/Retrieve/Update/Delete/Upsert across all database providers
- **CompositeKeyTests**: Entities with multi-column primary keys
- **AuditFieldTests**: CreatedBy/On, LastUpdatedBy/On functionality
- **RoundTripTests** / **TypeHydrationTests**: Row round-trip fidelity and type hydration
- **ParameterBindingTests**, **QuotingTortureTests**, **SqlContainerReuseTests**, **StoredProcedureTests**,
  **MergeConflictTests**, **InsertReturningTests**, **DiagnosticsTests**, **TransactionResilienceTests**, and others

### ⚡ Advanced Tests

**Location**: `Advanced/`

- **TransactionTests**: Commit/rollback, isolation levels and profiles, savepoints, readonly transactions
- **ConcurrencyTests**: Parallel reads/writes/transactions, multiple contexts, stress tests
- **BatchOperationTests**: Bulk insert/update/delete/retrieve/upsert, chunked processing, large transactions

### 🎯 Database-Specific Tests

**Location**: `DatabaseSpecific/`

- **PostgreSQLFeatureTests**: JSONB operators, arrays, full-text search, `ON CONFLICT` upserts
- **SqlServerIdentityTests** / **SqlServerUuid7OrderingTests**: SQL Server identity population and `uniqueidentifier` ordering
- **MySqlSpatialRoundTripTests**: MySQL spatial types through the CRUD mapper
- **OracleArrayBindingRoundTripTests**: Oracle array binding for batch creates
- **SqliteCommandReaderLifetimeTests** / **SqliteValueDependentHydrationTests**: SQLite driver and affinity behavior
- **Db2SessionSettingsTests**: DB2 session settings

### 🔗 Connection Management Tests

**Location**: `ConnectionManagement/`

- **DbModeTests**: DbMode behavior, ExecutionType.Read vs ExecutionType.Write, readonly transactions,
  connection reuse within transactions, connection metrics
- **AsyncConnectionAcquisitionIntegrationTests**: Concurrent real reads exceeding the pool size

### ❌ Error Handling Tests

**Location**: `ErrorHandling/`

- **ConnectionFailureTests**: Open/command/transaction failures simulated with FakeDb
- **ConstraintViolationTests**: Primary key, unique, foreign key, not-null, and check constraint violations
- **ConcurrencyConflictTests**, **DeadlockConflictTests**, **ReadOnlyViolationTests**: Exception classification

## Running the Tests

### Prerequisites

1. **.NET 8 and .NET 10 SDKs** installed (the project targets `net8.0;net10.0`)
2. **Docker** for database containers (PostgreSQL, SQL Server, MySQL, etc.)

### Run All Integration Tests

```bash
# From the pengdows.crud.IntegrationTests directory (runs both target frameworks)
dotnet test

# Or from the solution root
dotnet test pengdows.crud.IntegrationTests/pengdows.crud.IntegrationTests.csproj

# A single target framework
dotnet test pengdows.crud.IntegrationTests/pengdows.crud.IntegrationTests.csproj -f net10.0

# Integration tests plus the testbed matrix for each framework in TESTBED_FRAMEWORKS
# (default "net8.0 net10.0")
./run-integration-tests.sh
```

`IntegrationMatrixTests` excludes Informix: its native driver needs `LD_LIBRARY_PATH` set before the
process starts, which is impossible inside vstest's testhost. Informix is covered by
`dotnet run --project testbed -f net8.0` (or `-f net10.0`) instead.

### Run Specific Test Categories

```bash
# Core CRUD functionality
dotnet test --filter "FullyQualifiedName~Core"

# Database-specific features
dotnet test --filter "FullyQualifiedName~DatabaseSpecific"

# PostgreSQL features only
dotnet test --filter "FullyQualifiedName~PostgreSQLFeatureTests"

# Transaction and concurrency tests
dotnet test --filter "FullyQualifiedName~Advanced"

# Connection management tests
dotnet test --filter "FullyQualifiedName~ConnectionManagement"

# Error handling scenarios
dotnet test --filter "FullyQualifiedName~ErrorHandling"
```

### Environment Configuration

#### Database Provider Selection

Tests derived from `DatabaseTestBase` run against SQLite, PostgreSQL, SQL Server, MySQL, MariaDB, Firebird,
CockroachDB, DuckDB, Oracle, YugabyteDB, and TiDB by default.

```bash
# Include Snowflake tests (requires Snowflake credentials)
export INCLUDE_SNOWFLAKE=true

# DatabaseTestBase tests: restrict to specific providers (SupportedDatabase enum names, case-insensitive)
export INTEGRATION_ONLY="PostgreSql,SqlServer"

# IntegrationMatrixTests / testbed: restrict or exclude by testbed container or provider name
export TESTBED_ONLY="PostgreSQL,SQL Server"
export TESTBED_EXCLUDE="Oracle,Firebird"

# Optional diagnostics and container control
export INTEGRATION_TRACE=true                # verbose per-provider trace output
export TESTBED_STARTUP_TIMEOUT_SECONDS=300   # override container startup wait
export TESTBED_KEEP_CONTAINERS=true          # keep testbed containers running after the run
```

The testbed (`dotnet run --project testbed -f net8.0|net10.0`) also accepts `--only`/`--exclude` and these
opt-in databases, which `IntegrationMatrixTests` does not enable:

- `INCLUDE_SAPHANA=true` — SAP HANA (Docker image, needs 16-32GB RAM)
- `INCLUDE_INTERBASE=true` — InterBase (a pre-registered, licensed, already-running container; also needs
  `LD_LIBRARY_PATH` pointing to a directory containing `libgds.so`)
- `INCLUDE_ACCESS=true` — Microsoft Access (Windows only; requires the Access Database Engine Redistributable)

#### Snowflake Configuration

Snowflake tests use the external Snowflake account (no Docker image). Provide the required environment variables
(or use `./run-snowflake-integration-tests.sh`, which sets `INCLUDE_SNOWFLAKE`, `INTEGRATION_ONLY` and
`TESTBED_ONLY` for you):

```bash
export SNOWFLAKE_ACCOUNT="your_account_identifier"
export SNOWFLAKE_USER="your_user"
export SNOWFLAKE_PASSWORD="your_password"
export SNOWFLAKE_WAREHOUSE="your_warehouse"
# Optional role
export SNOWFLAKE_ROLE="your_role"
# Required unless SNOWFLAKE_CREATE_DATABASE=true
export SNOWFLAKE_DATABASE="your_database"
# Optional: create a throwaway test database instead of a throwaway schema in SNOWFLAKE_DATABASE
export SNOWFLAKE_CREATE_DATABASE=true
# Optional: schema inside the created database when SNOWFLAKE_CREATE_DATABASE=true (default: PUBLIC)
export SNOWFLAKE_SCHEMA="your_schema"
# Optional: prefix for generated test database/schema names (default: PENGDOWS_TEST)
export SNOWFLAKE_TEST_PREFIX="PENGDOWS_TEST"
# Optional: admin connection database (defaults to SNOWFLAKE_DATABASE if provided)
export SNOWFLAKE_ADMIN_DATABASE="your_admin_database"
```

#### Docker Configuration

The tests automatically start database containers using Testcontainers. Ensure Docker is running:

```bash
# Verify Docker is running
docker info

# Pull required images (optional - done automatically)
docker pull postgres:latest
docker pull mcr.microsoft.com/mssql/server:latest
docker pull mysql:latest
docker pull mariadb:latest
```

## Test Architecture

### Database Test Base

Most integration tests inherit from `DatabaseTestBase` and join the `"IntegrationTests"` collection, whose
`IntegrationTestFixture` starts the database containers once (via testbed's `ParallelTestOrchestrator`) and
hands out cached `IDatabaseContext` instances. `DatabaseTestBase` provides:

- **Shared container management** through `IntegrationTestFixture`
- **Per-provider execution** of each test (providers run one after another; failures are aggregated)
- **Consistent setup/teardown** via `SetupDatabaseAsync`/`CleanupDatabaseAsync` overrides
- **Flexible provider selection** via `GetSupportedProviders()` and `INTEGRATION_ONLY`
- **Automatic skipping** when none of the requested providers could be initialized

```csharp
[Collection("IntegrationTests")]
public class MyIntegrationTests : DatabaseTestBase
{
    public MyIntegrationTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture) { }

    [SkippableFact]
    public async Task MyTest_WorksAcrossAllProviders()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Test implementation that runs against each database
            var helper = new TableGateway<MyEntity, long>(context);
            // ... test logic
        });
    }
}
```

### Provider-Specific Tests

Some tests only run against specific database providers:

```csharp
protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
{
    // Only test PostgreSQL-specific features
    return new[] { SupportedDatabase.PostgreSql };
}
```

## Key Testing Patterns

### 1. Cross-Database Compatibility

Most tests run against all supported database providers to ensure consistent behavior:

```csharp
[SkippableFact]
public async Task CRUD_Operations_WorkConsistently()
{
    await RunTestAgainstAllProvidersAsync(async (provider, context) =>
    {
        // Test runs against SQLite, PostgreSQL, SQL Server, MySQL, etc.
        var helper = new TableGateway<TestEntity, long>(context);

        var entity = new TestEntity { Name = $"Test-{provider}" };
        await helper.CreateAsync(entity, context);

        var retrieved = await helper.RetrieveOneAsync(entity.Id, context);
        Assert.NotNull(retrieved);
        Assert.Equal(entity.Name, retrieved.Name);
    });
}
```

### 2. Database-Specific Feature Testing

Advanced tests showcase unique database capabilities:

```csharp
[SkippableFact]
public async Task PostgreSQL_JSONB_NativeOperators()
{
    await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
    {
        // Use PostgreSQL-specific JSONB operators
        await using var container = context.CreateSqlContainer(@"
            SELECT * FROM products
            WHERE specifications->>'brand' = @brand");
        container.AddParameterWithValue("brand", DbType.String, "Apple");

        // ... assertions
    });
}
```

### 3. ReadOnly Connection and Transaction Testing

Tests for readonly connections and transactions demonstrate ExecutionType behavior:

```csharp
[SkippableFact]
public async Task ReadOnlyTransaction_ReadCommitted_AllowsReadOperations()
{
    await RunTestAgainstAllProvidersAsync(async (provider, context) =>
    {
        // Start readonly transaction
        await using var readonlyTransaction = await context.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, ExecutionType.Read);

        // Perform read operations within readonly transaction
        var retrieved = await helper.RetrieveOneAsync(entity.Id, readonlyTransaction);
        Assert.NotNull(retrieved);

        await readonlyTransaction.CommitAsync();
    });
}

[SkippableFact]
public async Task ExecutionType_Read_UsesReadOptimizedConnection()
{
    await RunTestAgainstAllProvidersAsync(async (provider, context) =>
    {
        // Execute a read operation on a read connection
        await using var container = context.CreateSqlContainer("SELECT * FROM TestTable");
        await using var reader = await container.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text);
        // ... process results
    });
}
```

### 4. Error Scenario Testing

Error handling tests use FakeDb to simulate failures:

```csharp
[Fact]
public void Connection_Failure_HandledGracefully()
{
    var factory = fakeDbFactory.CreateFailingFactory(
        SupportedDatabase.Sqlite,
        ConnectionFailureMode.FailOnOpen);

    // The simulated failure surfaces while DatabaseContext initializes its connection
    Assert.Throws<ConnectionFailedException>(() =>
    {
        using var context = new DatabaseContext("Data Source=test.db", factory);
    });
}
```

## Benefits of This Architecture

### ✅ **Granular Test Failure Detection**

- Individual test methods focus on specific functionality
- Easy to identify exactly what broke when tests fail
- Better debugging and troubleshooting experience

### ✅ **Comprehensive Coverage**

- Tests real-world scenarios, not just happy paths
- Covers database-specific optimizations and features
- Validates error handling and edge cases

### ✅ **Cross-Database Validation**

- Ensures consistent behavior across all supported databases
- Catches provider-specific bugs early
- Validates dialect implementations

### ✅ **Performance Validation**

- Demonstrates database-specific optimizations
- Validates that pengdows.crud leverages native features

### ✅ **Documentation Through Tests**

- Tests serve as executable documentation
- Show best practices for using pengdows.crud features
- Demonstrate real-world usage patterns

## Comparison with Previous Integration Tests

| Aspect                  | Old Monolithic Tests     | New Granular Tests              |
|-------------------------|--------------------------|---------------------------------|
| **Failure Detection**   | Single mega-test failure | Specific test method failure    |
| **Coverage**            | Basic CRUD only          | Comprehensive scenarios         |
| **Debugging**           | Hard to isolate issues   | Easy to identify problems       |
| **Database Features**   | Generic operations       | Database-specific optimizations |
| **Error Scenarios**     | Limited                  | Comprehensive failure testing   |
| **Documentation Value** | Low                      | High - tests as examples        |
| **Maintenance**         | Difficult                | Easy to update/extend           |

## Future Enhancements

The integration test architecture is designed to be easily extensible:

1. **New Database Providers**: Add support by implementing provider-specific test cases
2. **Additional Features**: Create new test categories for new pengdows.crud features
3. **Performance Benchmarks**: Integrate with BenchmarkDotNet for automated performance validation
4. **Load Testing**: Add stress tests for high-concurrency scenarios
5. **Migration Testing**: Add tests for database schema changes and migrations

This comprehensive integration test suite ensures pengdows.crud maintains high quality and reliability across all
supported database providers and real-world usage scenarios.
