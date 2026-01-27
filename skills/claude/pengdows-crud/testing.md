# Testing with FakeDb

The `pengdows.crud.fakeDb` package provides a complete fake ADO.NET provider for unit testing without real database connections. It simulates database behavior while allowing full control over responses and connection failures.

## Installation

```bash
dotnet add package pengdows.crud.fakeDb
```

## Basic Usage

### Simple Setup

```csharp
using pengdows.crud.fakeDb;

[Test]
public void TestBasicCrud()
{
    // Create fake factory for any supported database
    var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

    // Create context with fake provider
    var context = new DatabaseContext("Data Source=test;", factory);

    // Use normally - all database calls are simulated
    var gateway = new TableGateway<TestEntity, int>(context);

    var entity = new TestEntity { Name = "Test" };
    var container = gateway.BuildCreate(entity);

    // This succeeds without actual database
    var result = await container.ExecuteNonQueryAsync();
    Assert.Equal(1, result);
}
```

## Supported Database Emulation

```csharp
// Each emulates the SQL dialect and behavior of the target database
var sqlServerFactory = new fakeDbFactory(SupportedDatabase.SqlServer);
var postgresFactory = new fakeDbFactory(SupportedDatabase.PostgreSql);
var oracleFactory = new fakeDbFactory(SupportedDatabase.Oracle);
var mysqlFactory = new fakeDbFactory(SupportedDatabase.MySql);
var sqliteFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
var firebirdFactory = new fakeDbFactory(SupportedDatabase.Firebird);
```

**Benefits:**
- Uses correct SQL dialect for parameter naming (`@`, `:`, `?`)
- Emulates database-specific DataSourceInformation
- Provides appropriate schema metadata
- Simulates database version and capabilities

## Custom Data Responses

```csharp
var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);

// Simulate query results
factory.AddQueryResult("SELECT COUNT(*) FROM users", new object[][] {
    new object[] { 42 }
});

factory.AddQueryResult("SELECT id, name FROM users WHERE active = $1", new object[][] {
    new object[] { 1, "John Doe" },
    new object[] { 2, "Jane Smith" }
});

var context = new DatabaseContext("test", factory);
var container = context.CreateSqlContainer("SELECT COUNT(*) FROM users");
var count = await container.ExecuteScalarAsync<int>(); // Returns 42
```

## Connection Failure Simulation

### Basic Connection Failures

```csharp
[Test]
public void TestConnectionFailure()
{
    var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
    var connection = (fakeDbConnection)factory.CreateConnection();

    // Set connection to fail on open
    connection.SetFailOnOpen();

    var context = new DatabaseContext("test", factory);

    // This will throw InvalidOperationException
    Assert.Throws<InvalidOperationException>(() =>
    {
        using var conn = context.GetConnection(ExecutionType.Read);
        conn.Open();
    });
}
```

### Factory-Level Failure Configuration

```csharp
// Create factory that produces failing connections
var factory = fakeDbFactory.CreateFailingFactory(
    SupportedDatabase.PostgreSql,
    ConnectionFailureMode.FailOnOpen);

var context = new DatabaseContext("test", factory);

// All connections from this context will fail on open
Assert.Throws<InvalidOperationException>(() => context.GetConnection(ExecutionType.Read));
```

### Connection Failure Modes

```csharp
public enum ConnectionFailureMode
{
    FailOnOpen,        // Fail when connection.Open() is called
    FailOnCommand,     // Fail when creating commands
    FailOnTransaction, // Fail when beginning transactions
    FailAfterCount,    // Work for N operations, then fail
    Broken             // Connection is permanently broken
}
```

### Advanced Failure Scenarios

```csharp
[Test]
public void TestFailAfterMultipleSuccesses()
{
    var connection = (fakeDbConnection)factory.CreateConnection();

    // Allow 3 successful opens, then fail
    connection.SetFailAfterOpenCount(3);

    // First 3 opens succeed
    connection.Open(); connection.Close();
    connection.Open(); connection.Close();
    connection.Open(); connection.Close();

    // Fourth open fails
    Assert.Throws<InvalidOperationException>(() => connection.Open());
}

[Test]
public void TestCustomFailureException()
{
    var connection = (fakeDbConnection)factory.CreateConnection();

    // Throw custom exception instead of InvalidOperationException
    var customEx = new TimeoutException("Connection timed out");
    connection.SetCustomFailureException(customEx);
    connection.SetFailOnOpen();

    var thrownEx = Assert.Throws<TimeoutException>(() => connection.Open());
    Assert.Equal("Connection timed out", thrownEx.Message);
}
```

## Test Helper Utilities

### ConnectionFailureHelper

```csharp
[Test]
public void TestDatabaseContextFailureHandling()
{
    // Helper creates pre-configured failing context
    using var context = ConnectionFailureHelper.CreateFailOnOpenContext();

    // Test your error handling logic
    var ex = Assert.Throws<InvalidOperationException>(() =>
        context.GetConnection(ExecutionType.Read));

    Assert.Contains("Connection failed", ex.Message);
}

[Test]
public void TestTransactionFailures()
{
    using var context = ConnectionFailureHelper.CreateFailOnTransactionContext();

    Assert.Throws<InvalidOperationException>(() => context.BeginTransaction());
}
```

### Creating Contexts for Different Databases

```csharp
public static class TestHelpers
{
    public static IDatabaseContext CreatePostgresTestContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        return new DatabaseContext("Host=localhost;Database=test", factory);
    }

    public static IDatabaseContext CreateSqlServerTestContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        return new DatabaseContext("Server=localhost;Database=test", factory);
    }
}
```

## Testing Entity Operations

### CRUD Testing

```csharp
[Table("products")]
public class Product
{
    [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("name", DbType.String)] public string Name { get; set; } = "";
    [Column("price", DbType.Decimal)] public decimal Price { get; set; }
}

[Test]
public async Task TestProductCrud()
{
    var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
    var context = new DatabaseContext("Data Source=:memory:", factory);
    var gateway = new TableGateway<Product, int>(context);

    var product = new Product { Id = 1, Name = "Test Product", Price = 9.99m };

    // Test CREATE
    var createContainer = gateway.BuildCreate(product);
    var created = await createContainer.ExecuteNonQueryAsync();
    Assert.Equal(1, created);

    // Test UPDATE
    product.Price = 12.99m;
    var updateContainer = await gateway.BuildUpdateAsync(product);
    var updated = await updateContainer.ExecuteNonQueryAsync();
    Assert.Equal(1, updated);

    // Test DELETE
    var deleted = await gateway.DeleteAsync(product.Id);
    Assert.Equal(1, deleted);
}
```

### Testing Custom SQL

```csharp
[Test]
public async Task TestCustomQuery()
{
    var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);

    // Pre-configure expected query result
    factory.AddQueryResult(
        "SELECT COUNT(*) FROM products WHERE price > $1",
        new object[][] { new object[] { 15 } }
    );

    var context = new DatabaseContext("test", factory);
    var container = context.CreateSqlContainer();

    container.Query.Append("SELECT COUNT(*) FROM ");
    container.Query.Append(container.WrapObjectName("products"));
    container.Query.Append(" WHERE ");
    container.Query.Append(container.WrapObjectName("price"));
    container.Query.Append(" > ");
    container.Query.Append(container.MakeParameterName("price"));
    container.AddParameterWithValue("price", DbType.Decimal, 10.00m);

    var count = await container.ExecuteScalarAsync<int>();
    Assert.Equal(15, count);
}
```

## Testing Audit Fields

### Mock Audit Context

```csharp
public class TestAuditResolver : IAuditValueResolver
{
    public IAuditValues Resolve() => new TestAuditValues();
}

public class TestAuditValues : IAuditValues
{
    public object UserId => "test-user";
    public DateTime Timestamp => new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc);
}

[Test]
public async Task TestAuditFieldsPopulated()
{
    var services = new ServiceCollection();
    services.AddSingleton<IAuditValueResolver, TestAuditResolver>();
    var serviceProvider = services.BuildServiceProvider();

    var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
    var context = new DatabaseContext("test", factory);
    var gateway = new TableGateway<AuditedEntity, int>(context, serviceProvider);

    var entity = new AuditedEntity { Name = "Test" };
    var container = gateway.BuildCreate(entity);

    // Verify audit fields are included in generated SQL
    Assert.Contains("created_by", container.Query.ToString());
    Assert.Contains("created_at", container.Query.ToString());
}
```

## Advanced Testing Scenarios

### Testing Transaction Behavior

```csharp
[Test]
public async Task TestTransactionRollback()
{
    var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
    var context = new DatabaseContext("test", factory);

    await using var transaction = context.BeginTransaction();

    // Simulate operations within transaction
    var container = transaction.CreateSqlContainer("INSERT INTO test VALUES (1)");
    await container.ExecuteNonQueryAsync();

    // Don't commit - transaction should rollback on dispose

    // Verify transaction was rolled back
    Assert.True(transaction.WasRolledBack);
}
```

### Testing Parameter Limits

```csharp
[Test]
public void TestTooManyParameters()
{
    var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
    var context = new DatabaseContext("test", factory);
    var gateway = new TableGateway<Product, int>(context);

    // Create more IDs than SQLite parameter limit (999)
    var manyIds = Enumerable.Range(1, 1500).ToArray();

    // Should throw TooManyParametersException
    Assert.Throws<TooManyParametersException>(() =>
        gateway.BuildRetrieve(manyIds));
}
```

### Simulating Slow Connections

```csharp
[Test]
public async Task TestSlowConnection()
{
    var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
    var connection = (fakeDbConnection)factory.CreateConnection();

    // Add delay to connection operations
    connection.SetOpenDelay(TimeSpan.FromMilliseconds(500));

    var context = new DatabaseContext("test", factory);
    var stopwatch = Stopwatch.StartNew();

    using var conn = context.GetConnection(ExecutionType.Read);
    conn.Open();

    stopwatch.Stop();
    Assert.True(stopwatch.ElapsedMilliseconds >= 500);
}
```

## Best Practices

### Test Organization

```csharp
public abstract class DatabaseTestBase
{
    protected IDatabaseContext CreateTestContext(SupportedDatabase dbType)
    {
        var factory = new fakeDbFactory(dbType);
        return new DatabaseContext($"test-{dbType}", factory);
    }

    protected TableGateway<T, TId> CreateGateway<T, TId>(IDatabaseContext context)
        where T : class, new()
    {
        return new TableGateway<T, TId>(context);
    }
}

public class ProductTests : DatabaseTestBase
{
    [Test]
    public void TestProductCreation()
    {
        using var context = CreateTestContext(SupportedDatabase.Sqlite);
        var gateway = CreateGateway<Product, int>(context);

        // Your test logic here
    }
}
```

### Parameterized Testing

```csharp
public class CrossDatabaseTests
{
    public static IEnumerable<object[]> SupportedDatabases()
    {
        yield return new object[] { SupportedDatabase.SqlServer };
        yield return new object[] { SupportedDatabase.PostgreSql };
        yield return new object[] { SupportedDatabase.MySql };
        yield return new object[] { SupportedDatabase.Sqlite };
        yield return new object[] { SupportedDatabase.Oracle };
        yield return new object[] { SupportedDatabase.Firebird };
    }

    [Theory]
    [MemberData(nameof(SupportedDatabases))]
    public void TestCrudOperationsAcrossDatabases(SupportedDatabase dbType)
    {
        var factory = new fakeDbFactory(dbType);
        var context = new DatabaseContext("test", factory);

        // Test that your code works across all supported databases
        var gateway = new TableGateway<Product, int>(context);
        var product = new Product { Name = "Test", Price = 9.99m };

        var container = gateway.BuildCreate(product);
        Assert.NotNull(container.Query.ToString());
        Assert.True(container.ParameterCount > 0);
    }
}
```

## Limitations

### What FakeDb Cannot Test

- Actual database constraints (foreign keys, unique indexes)
- Database-specific SQL syntax errors
- Real network connectivity issues
- Actual transaction isolation behavior
- Database performance characteristics
- Provider-specific quirks and bugs

### When to Use Integration Tests

**Use FakeDb for:**
- Unit testing business logic
- Testing error handling paths
- Verifying SQL generation
- Testing connection management
- Rapid test execution

**Use real databases for:**
- End-to-end integration testing
- Performance testing
- Testing database constraints
- Validating actual SQL syntax
- Testing provider-specific features
