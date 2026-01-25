# pengdows.crud Integration Tests

This directory contains comprehensive integration tests for pengdows.crud that demonstrate real-world scenarios and
verify functionality across multiple database providers.

## Overview

The integration tests are organized into focused categories, each testing specific aspects of pengdows.crud's
functionality:

```
📁 pengdows.crud.IntegrationTests/
├── 📁 Infrastructure/           # Test infrastructure and base classes
├── 📁 Core/                    # Basic CRUD operations
├── 📁 Advanced/                # Transactions, concurrency, bulk operations
├── 📁 DatabaseSpecific/        # Database-specific features (PostgreSQL, SQL Server, etc.)
├── 📁 ConnectionManagement/    # DbMode testing and connection optimization
├── 📁 ErrorHandling/           # Failure scenarios and error recovery
└── 📁 Performance/             # Large datasets and optimization validation
```

## Test Categories

### 🔧 Core Tests

**Location**: `Core/`

- **BasicCrudTests**: Comprehensive CRUD operations across all database providers
- **EntityMappingTests**: Attribute-based mapping and type coercion
- **AuditFieldTests**: CreatedBy/On, LastUpdatedBy/On functionality

### ⚡ Advanced Tests

**Location**: `Advanced/`

- **TransactionTests**: Transaction isolation, rollback, savepoints, concurrent access, readonly transactions
- **ConcurrencyTests**: Parallel operations, deadlock handling
- **BulkOperationTests**: Large dataset operations, batch processing

### 🎯 Database-Specific Tests

**Location**: `DatabaseSpecific/`

- **PostgreSQLFeatureTests**: JSONB operators, arrays, full-text search, native upserts
- **SqlServerFeatureTests**: Indexed views, MERGE statements, session settings
- **MySQLFeatureTests**: MySQL-specific syntax and optimizations
- **OracleFeatureTests**: Oracle-specific features and procedures

### 🔗 Connection Management Tests

**Location**: `ConnectionManagement/`

- **DbModeTests**: Standard, KeepAlive, SingleWriter, SingleConnection modes
- **ReadOnlyConnectionTests**: ReadOnly connection behavior and readonly transaction handling
- **ExecutionTypeTests**: ExecutionType.Read vs ExecutionType.Write connection management
- **PoolingTests**: Connection pool optimization and behavior
- **IsolationTests**: Transaction isolation across different providers

### ❌ Error Handling Tests

**Location**: `ErrorHandling/`

- **ConnectionFailureTests**: Network failures, timeout scenarios using FakeDb
- **ConstraintViolationTests**: Primary key, foreign key, unique constraint violations
- **TimeoutTests**: Command and connection timeout handling

## Running the Tests

### Prerequisites

1. **.NET 8 SDK** installed
2. **Docker** for database containers (PostgreSQL, SQL Server, MySQL, etc.)
3. **Optional**: Oracle Database for Oracle-specific tests

### Run All Integration Tests

```bash
# From the pengdows.crud.IntegrationTests directory
dotnet test

# Or from the solution root
dotnet test pengdows.crud.IntegrationTests/pengdows.crud.IntegrationTests.csproj
```

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

```bash
# Include Oracle tests (requires Oracle database)
export INCLUDE_ORACLE=true

# Test only specific providers
export TESTBED_ONLY="PostgreSql,SqlServer"

# Exclude specific providers
export TESTBED_EXCLUDE="Oracle,Firebird"
```

#### Docker Configuration

The tests automatically start database containers using Testcontainers. Ensure Docker is running:

```bash
# Verify Docker is running
docker info

# Pull required images (optional - done automatically)
docker pull postgres:15-alpine
docker pull mcr.microsoft.com/mssql/server:2022-latest
docker pull mysql:8.0
docker pull mariadb:10.9
```

## Test Architecture

### Database Test Base

All integration tests inherit from `DatabaseTestBase` which provides:

- **Automatic container management** for multiple database providers
- **Parallel test execution** across different databases
- **Consistent setup/teardown** for each test scenario
- **Flexible provider selection** for focused testing

```csharp
public class MyIntegrationTests : DatabaseTestBase
{
    public MyIntegrationTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task MyTest_WorksAcrossAllProviders()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Test implementation that runs against each database
            var helper = new EntityHelper<MyEntity, long>(context);
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
[Fact]
public async Task CRUD_Operations_WorkConsistently()
{
    await RunTestAgainstAllProvidersAsync(async (provider, context) =>
    {
        // Test runs against SQLite, PostgreSQL, SQL Server, MySQL, etc.
        var helper = new EntityHelper<TestEntity, long>(context);

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
[Fact]
public async Task PostgreSQL_JSONB_NativeOperators()
{
    await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
    {
        // Use PostgreSQL-specific JSONB operators
        using var container = context.CreateSqlContainer(@"
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
[Fact]
public async Task ReadOnlyTransaction_ReadCommitted_AllowsReadOperations()
{
    await RunTestAgainstAllProvidersAsync(async (provider, context) =>
    {
        // Start readonly transaction
        using var readonlyTransaction = await context.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, ExecutionType.Read);

        // Perform read operations within readonly transaction
        var retrieved = await helper.RetrieveOneAsync(entity.Id, readonlyTransaction);
        Assert.NotNull(retrieved);

        await readonlyTransaction.CommitAsync();
    });
}

[Fact]
public async Task ExecutionType_Read_UsesReadOptimizedConnection()
{
    // Get read connection explicitly
    using var readConnection = context.GetConnection(ExecutionType.Read);
    await readConnection.OpenAsync();

    // Execute read operation
    using var container = context.CreateSqlContainer("SELECT * FROM TestTable");
    using var command = container.CreateCommand(readConnection);
    using var reader = await command.ExecuteReaderAsync();
    // ... process results
}
```

### 4. Error Scenario Testing

Error handling tests use FakeDb to simulate failures:

```csharp
[Fact]
public async Task Connection_Failure_HandledGracefully()
{
    var factory = FakeDbFactory.CreateFailingFactory(
        SupportedDatabase.Sqlite,
        ConnectionFailureMode.FailOnOpen);

    using var context = new DatabaseContext("Data Source=test", factory);

    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
    {
        // This should fail due to simulated connection failure
        var helper = new EntityHelper<TestTable, long>(context);
        await helper.CreateAsync(new TestTable(), context);
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
- Provides performance comparisons with EF/Dapper

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