# pengdows.crud - SQL-First Data Access for .NET 8

## What is pengdows.crud?

**pengdows.crud** is a lightweight, SQL-first data access library for .NET 8 that gives developers full control over their SQL while providing type safety, database abstraction, and comprehensive testing support. It's designed for teams who want to write SQL themselves but need the safety, testability, and cross-database compatibility that traditional ORMs struggle to provide.

## Philosophy: No Magic, Just Control

Unlike Entity Framework or other ORMs, pengdows.crud follows these core principles:

### 1. **SQL-First, Not Query Builder-First**
- You write the SQL yourself—no LINQ, no expression trees, no hidden query generation
- What you write is what gets executed (WYSIWYG SQL)
- Database-specific optimizations and features are always accessible
- No "impedance mismatch" between C# and SQL

### 2. **Primary Keys ≠ Pseudo Keys**
- Separates logical identifiers (row IDs) from physical database keys
- Composite primary keys supported natively
- Business logic works with simple IDs while database maintains complex constraints

### 3. **Open Late, Close Early**
- Connections open only when needed and close immediately after
- Prevents connection pool exhaustion
- Reduces cloud database costs (pay-per-connection pricing)
- Built-in connection strategy patterns for different scenarios

### 4. **Testable by Design**
- `pengdows.crud.fakeDb` provides complete mock database provider
- Unit tests run without database containers or connection strings
- Integration tests use Testcontainers for real database validation
- TDD workflow is mandatory for all library development

## Why Not Just Use Entity Framework?

| Concern | Entity Framework | pengdows.crud |
|---------|------------------|---------------|
| **SQL Control** | LINQ generates SQL (often suboptimal) | You write exact SQL |
| **Performance** | Change tracking overhead, N+1 queries | No tracking, explicit control |
| **Learning Curve** | Must learn LINQ, DbContext, migrations | Just SQL + attributes |
| **Database Features** | Limited to common denominator | Full access to vendor features |
| **Testing** | Requires InMemory provider or real DB | Complete mock provider (fakeDb) |
| **Predictability** | Query translation can surprise you | What you write is what executes |
| **Complexity** | Large API surface, many magic behaviors | Small, focused API |

**When to use EF instead**:
- Rapid prototyping where SQL optimization doesn't matter
- Teams unfamiliar with SQL who prefer LINQ
- Simple CRUD apps where ORM overhead is negligible

**When pengdows.crud shines**:
- High-performance applications where every query matters
- Complex SQL with CTEs, window functions, vendor-specific features
- Multi-database support (same code, different SQL dialects)
- Teams that know SQL and want control

## Core Architecture

### Three-Layer Design

```
┌─────────────────────────────────────────────────────────────┐
│  Application Layer                                          │
│  - Business Logic                                           │
│  - Services                                                 │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│  pengdows.crud Layer                                        │
│  ┌──────────────────┐  ┌──────────────────┐                │
│  │ TableGateway<T>  │  │ DatabaseContext  │                │
│  │ - CRUD ops       │  │ - Connection mgmt│                │
│  │ - SQL building   │  │ - Transactions   │                │
│  │ - Mapping        │  │ - Pooling        │                │
│  └──────────────────┘  └──────────────────┘                │
│                                                             │
│  ┌──────────────────┐  ┌──────────────────┐                │
│  │ SqlContainer     │  │ SqlDialect       │                │
│  │ - Query builder  │  │ - DB abstraction │                │
│  │ - Parameters     │  │ - Vendor SQL     │                │
│  └──────────────────┘  └──────────────────┘                │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│  ADO.NET Provider Layer                                     │
│  - DbProviderFactory (SqlClient, Npgsql, etc.)             │
│  - DbConnection / DbCommand / DbDataReader                  │
└─────────────────────────────────────────────────────────────┘
```

### Key Components

#### **1. TableGateway<TEntity, TRowID>**
The workhorse class for entity operations.

```csharp
// Define your entity with attributes
[Table("customers")]
public class Customer
{
    [Id]
    [Column("customer_id", DbType.Int32)]
    public int Id { get; set; }

    [Column("name", DbType.String)]
    public string Name { get; set; }

    [Column("email", DbType.String)]
    public string Email { get; set; }

    [CreatedOn]
    [Column("created_at", DbType.DateTime)]
    public DateTime CreatedAt { get; set; }
}

// Use it
var context = new DatabaseContext(connectionString, SqlClientFactory.Instance);
var helper = new TableGateway<Customer, int>(context);

// CRUD operations
var customer = await helper.RetrieveOneAsync(42);
await helper.UpdateAsync(customer);
await helper.DeleteAsync(42);

// Custom SQL
var container = helper.BuildBaseRetrieve("c");
container.Query.Append(" WHERE c.email LIKE ");
container.Query.Append(container.MakeParameterName("email"));
container.AddParameterWithValue("email", DbType.String, "%@example.com");
var results = await helper.LoadListAsync(container);
```

#### **2. DatabaseContext**
Connection lifecycle and transaction management.

```csharp
// Create context
var context = new DatabaseContext(
    "Server=localhost;Database=mydb",
    SqlClientFactory.Instance
);

// Transactions
using var transaction = context.BeginTransaction();
try
{
    await helper.CreateAsync(entity, transaction);
    await helper.UpdateAsync(otherEntity, transaction);
    transaction.Commit();
}
catch
{
    transaction.Rollback();
    throw;
}

// Connection modes
// - Standard: New connection per operation (default, best for production)
// - KeepAlive: Sentinel connection prevents unload (SQLite/LocalDB)
// - SingleWriter: One persistent write connection (file-based DBs)
// - SingleConnection: All operations share one connection (in-memory DBs)
```

#### **3. SqlContainer**
SQL query builder with safe parameterization.

```csharp
var container = context.CreateSqlContainer();
container.Query.Append("SELECT * FROM orders WHERE ");

// Safe parameterization (prevents SQL injection)
container.Query.Append(container.MakeParameterName("status"));
container.AddParameterWithValue("status", DbType.String, "Active");

container.Query.Append(" AND ");
container.Query.Append(container.MakeParameterName("created_after"));
container.AddParameterWithValue("created_after", DbType.DateTime, DateTime.Now.AddDays(-30));

// Execute
var reader = await container.ExecuteReaderAsync();
```

#### **4. SqlDialect**
Database-specific SQL generation.

```csharp
// Automatic dialect selection based on connection
var context = new DatabaseContext(pgConnectionString, NpgsqlDataSourceBuilder.Build());
// Dialect = PostgreSQL → Uses RETURNING, ANY(array), JSON operators

var context = new DatabaseContext(sqlConnectionString, SqlClientFactory.Instance);
// Dialect = SQL Server → Uses OUTPUT, MERGE, JSON_VALUE

// Dialects handle:
// - Parameter markers (@param vs :param vs ?)
// - Identifier quoting ([table] vs "table" vs `table`)
// - UPSERT strategies (MERGE vs ON CONFLICT vs REPLACE)
// - Pagination (OFFSET/FETCH vs LIMIT/OFFSET)
// - JSON operators (JSON_VALUE vs -> vs json_extract)
```

## Supported Databases

Comprehensive support for 9 database engines:

| Database | Provider | Features |
|----------|----------|----------|
| **SQL Server** | Microsoft.Data.SqlClient | MERGE, OUTPUT, JSON_VALUE, temporal tables |
| **PostgreSQL** | Npgsql | RETURNING, ANY(array), CTEs, JSON operators, LISTEN/NOTIFY |
| **Oracle** | Oracle.ManagedDataAccess | RETURNING INTO, MERGE, sequences, PL/SQL |
| **MySQL** | MySql.Data / MySqlConnector | ON DUPLICATE KEY UPDATE, JSON functions |
| **SQLite** | Microsoft.Data.Sqlite | RETURNING (3.35+), simple transactions |
| **Firebird** | FirebirdSql.Data | RETURNING, MERGE, generators |
| **CockroachDB** | Npgsql | PostgreSQL-compatible, distributed SQL |
| **DuckDB** | DuckDB.NET | Analytical queries, in-memory/file-based |
| **MariaDB** | MySql.Data | MySQL-compatible, enhanced features |

## Entity Mapping with Attributes

### Basic Mapping
```csharp
[Table("users", Schema = "auth")]  // Fully qualified table name
public class User
{
    [Id]                                      // Marks as row ID (pseudo key)
    [Column("user_id", DbType.Int32)]        // Physical column
    public int Id { get; set; }

    [Column("username", DbType.String, 50)]  // With max length
    public string Username { get; set; }

    [Column("is_active", DbType.Boolean)]
    public bool IsActive { get; set; }
}
```

### Composite Primary Keys
```csharp
[Table("order_items")]
public class OrderItem
{
    [Id]                                      // Row ID (for business logic)
    [Column("item_id", DbType.Int64)]
    public long Id { get; set; }

    [PrimaryKey(0)]                           // Physical PK part 1
    [Column("order_id", DbType.Int32)]
    public int OrderId { get; set; }

    [PrimaryKey(1)]                           // Physical PK part 2
    [Column("product_id", DbType.Int32)]
    public int ProductId { get; set; }
}
```

### Audit Fields
```csharp
[Table("documents")]
public class Document
{
    [Id]
    [Column("doc_id", DbType.Int32)]
    public int Id { get; set; }

    [CreatedBy]                               // Auto-populated on insert
    [Column("created_by_user", DbType.String)]
    public string? CreatedBy { get; set; }

    [CreatedOn]                               // Auto-populated on insert
    [Column("created_at", DbType.DateTime)]
    public DateTime? CreatedAt { get; set; }

    [LastUpdatedBy]                           // Auto-populated on update
    [Column("modified_by_user", DbType.String)]
    public string? ModifiedBy { get; set; }

    [LastUpdatedOn]                           // Auto-populated on update
    [Column("modified_at", DbType.DateTime)]
    public DateTime? ModifiedAt { get; set; }

    [Version]                                 // Optimistic concurrency
    [Column("row_version", DbType.Binary)]
    public byte[]? RowVersion { get; set; }
}
```

### Special Column Behaviors
```csharp
[Table("products")]
public class Product
{
    [Id]
    [Column("product_id", DbType.Int32)]
    public int Id { get; set; }

    [Column("sku", DbType.String)]
    [NonUpdateable]                           // Never included in UPDATEs
    public string Sku { get; set; }

    [Column("internal_code", DbType.String)]
    [NonInsertable]                           // Never included in INSERTs
    public string? InternalCode { get; set; }

    [Column("status", DbType.String)]
    [IsEnum]                                  // Auto-converts enum ↔ string/int
    public ProductStatus Status { get; set; }

    [Column("metadata", DbType.String)]
    [IsJsonType]                              // Auto-serializes objects
    public ProductMetadata? Metadata { get; set; }
}

public enum ProductStatus { Active, Discontinued, Pending }

public class ProductMetadata
{
    public string Color { get; set; }
    public int Weight { get; set; }
}
```

## CRUD Operations

### Create
```csharp
var customer = new Customer
{
    Name = "Acme Corp",
    Email = "contact@acme.com"
};

// Simple insert
bool success = await helper.CreateAsync(customer, context);

// Or build SQL manually
var container = helper.BuildCreate(customer);
container.Query.Append(" RETURNING customer_id"); // PostgreSQL
var newId = await container.ExecuteScalarAsync<int>();
```

### Retrieve
```csharp
// By single ID
var customer = await helper.RetrieveOneAsync(42);

// By multiple IDs
var customers = await helper.RetrieveAsync(new[] { 1, 2, 3, 4, 5 });

// Custom SQL
var container = helper.BuildBaseRetrieve("c");
container.Query.Append(" WHERE c.created_at > ");
container.Query.Append(container.MakeParameterName("date"));
container.AddParameterWithValue("date", DbType.DateTime, DateTime.Now.AddMonths(-1));
var recentCustomers = await helper.LoadListAsync(container);
```

### Update
```csharp
// Update entity
customer.Email = "newemail@acme.com";
int rowsAffected = await helper.UpdateAsync(customer);

// With optimistic concurrency check
int rowsAffected = await helper.UpdateAsync(
    customer,
    loadOriginal: true  // Loads original to detect concurrent changes
);
```

### Delete
```csharp
// Single delete
await helper.DeleteAsync(42);

// Bulk delete
await helper.DeleteAsync(new[] { 1, 2, 3, 4, 5 });
```

### Upsert (Insert or Update)
```csharp
// Automatically chooses INSERT or UPDATE based on existence
int rowsAffected = await helper.UpsertAsync(customer);

// Uses database-specific strategies:
// - SQL Server: MERGE
// - PostgreSQL: INSERT ... ON CONFLICT ... DO UPDATE
// - MySQL: INSERT ... ON DUPLICATE KEY UPDATE
// - SQLite: INSERT ... ON CONFLICT ... DO UPDATE (3.24+)
```

## 🆕 Streaming API (Memory-Efficient Operations)

**New in 1.1**: Stream large result sets without loading everything into memory.

### LoadStreamAsync - Stream Custom SQL Results

```csharp
// Traditional approach - loads ALL orders into memory
var container = helper.BuildBaseRetrieve("o");
container.Query.Append(" WHERE o.status = 'Pending'");
var allOrders = await helper.LoadListAsync(container);
// ❌ 50,000 orders × 5KB each = 250MB in memory

foreach (var order in allOrders)
{
    await ProcessOrderAsync(order);
}

// Streaming approach - processes one at a time
await foreach (var order in helper.LoadStreamAsync(container))
{
    await ProcessOrderAsync(order);
    // ✅ Only 1 order (5KB) in memory at a time

    // Can break early without loading rest
    if (shouldStop) break;
}
```

### RetrieveStreamAsync - Stream by ID List

```csharp
// Get 100,000 order IDs from search service
var orderIds = await searchService.GetMatchingOrderIdsAsync();

// Traditional approach - loads ALL 100K orders
var orders = await helper.RetrieveAsync(orderIds);
// ❌ 100,000 orders in memory

foreach (var order in orders)
{
    await ExportToFileAsync(order);
}

// Streaming approach
await foreach (var order in helper.RetrieveStreamAsync(orderIds))
{
    await ExportToFileAsync(order);
    // ✅ Process and discard, never accumulating
}
```

### Streaming with Cancellation

```csharp
var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromMinutes(5));  // Timeout after 5 minutes

try
{
    await foreach (var order in helper.RetrieveStreamAsync(orderIds, null, cts.Token))
    {
        await ProcessOrderAsync(order);
    }
}
catch (OperationCanceledException)
{
    _logger.LogWarning("Order processing cancelled after timeout");
}
```

### When to Use Streaming vs List

| Use `LoadListAsync` / `RetrieveAsync` | Use `LoadStreamAsync` / `RetrieveStreamAsync` |
|---------------------------------------|----------------------------------------------|
| Small result sets (< 1,000 rows) | Large result sets (> 10,000 rows) |
| Need to sort/filter in memory | Processing one item at a time |
| Multiple iterations over same data | Single-pass processing (export, ETL) |
| Need random access to items | Sequential access sufficient |
| Working set fits in memory | Memory constraints |

## Transaction Management

### Basic Transactions
```csharp
using var transaction = context.BeginTransaction();
try
{
    await helper.CreateAsync(order, transaction);

    foreach (var item in order.Items)
    {
        await itemHelper.CreateAsync(item, transaction);
    }

    transaction.Commit();
}
catch
{
    transaction.Rollback();
    throw;
}
```

### Isolation Levels
```csharp
// Database-native isolation levels
using var tx = context.BeginTransaction(IsolationLevel.Serializable);

// Or portable isolation profiles
using var tx = context.BeginTransaction(IsolationProfile.ReadCommitted);

// Supported profiles:
// - ReadUncommitted: Dirty reads allowed
// - ReadCommitted: Default for most databases
// - RepeatableRead: Prevents non-repeatable reads
// - Serializable: Full isolation
// - Snapshot: SQL Server/PostgreSQL snapshot isolation
```

### Savepoints
```csharp
using var transaction = context.BeginTransaction();

await helper.CreateAsync(customer, transaction);

await transaction.SavepointAsync("customer_created");

try
{
    await helper.CreateAsync(order, transaction);
}
catch
{
    // Rollback just the order, keep customer
    await transaction.RollbackToSavepointAsync("customer_created");
}

transaction.Commit();
```

## Testing Infrastructure

### Unit Testing with fakeDb

**fakeDb** is a complete ADO.NET provider implementation that wires up ADO.NET control flow for isolated unit testing.

**What fakeDb does:**
- ✅ Implements full `DbProviderFactory`, `DbConnection`, `DbCommand`, `DbDataReader` APIs
- ✅ Returns empty result sets by default (customizable via constructor)
- ✅ Simulates connection failures, timeouts, and error conditions
- ✅ Allows SQL generation and parameter verification without a database
- ✅ Enables fast, isolated unit tests without Docker or connection strings

**What fakeDb does NOT do:**
- ❌ No database semantics (no triggers, referential integrity, constraints)
- ❌ No transaction isolation (BeginTransaction succeeds but doesn't enforce isolation)
- ❌ No query execution (INSERT/UPDATE/DELETE return success, SELECT returns empty or mocked data)
- ❌ No vendor-specific behavior (stored procedures, JSON functions, date formatting)

**Use fakeDb for:**
- Unit testing code paths (success/failure flows)
- Verifying SQL generation correctness
- Testing error handling and resilience
- Fast feedback during TDD (thousands of tests in seconds)

**Still need integration tests for:**
- Database-specific behavior (PostgreSQL JSONB, SQL Server MERGE, etc.)
- Transaction isolation and concurrency
- Constraint violations and referential integrity
- Actual data roundtripping and type conversion
- Performance characteristics and query plans

**Example:** fakeDb verifies your code *constructs* correct SQL and handles errors, but integration tests verify the SQL *executes* correctly on real databases.

```csharp
public class CustomerServiceTests
{
    [Fact]
    public async Task CreateCustomer_ValidData_SucceedsWithoutException()
    {
        // Arrange - No database needed!
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var helper = new TableGateway<Customer, int>(context);

        var customer = new Customer { Name = "Test", Email = "test@example.com" };

        // Act - Executes against fakeDb
        var success = await helper.CreateAsync(customer, context);

        // Assert - Verifies code paths without database
        Assert.True(success);
    }

    [Fact]
    public void BuildCreate_ValidEntity_GeneratesCorrectSQL()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext("Host=fake", factory);
        var helper = new TableGateway<Customer, int>(context);

        var customer = new Customer { Name = "Acme" };

        // Act
        var container = helper.BuildCreate(customer);

        // Assert - Verify SQL generation
        Assert.Contains("INSERT INTO", container.Query.ToString());
        Assert.Contains("customers", container.Query.ToString());
        Assert.Equal(1, container.ParameterCount);
    }
}
```

### Integration Testing with Testcontainers

```csharp
public class CustomerIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private DatabaseContext _context = null!;

    public async Task InitializeAsync()
    {
        // Spin up real PostgreSQL in Docker
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:15-alpine")
            .Build();

        await _postgres.StartAsync();

        _context = new DatabaseContext(
            _postgres.GetConnectionString(),
            NpgsqlDataSource.Create(_postgres.GetConnectionString())
        );

        // Run migrations
        await SetupSchemaAsync();
    }

    [Fact]
    public async Task CreateAndRetrieve_Customer_Roundtrips()
    {
        // Arrange
        var helper = new TableGateway<Customer, int>(_context);
        var customer = new Customer { Name = "Test", Email = "test@example.com" };

        // Act - Real database operations
        await helper.CreateAsync(customer, _context);
        var retrieved = await helper.RetrieveOneAsync(customer.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Test", retrieved.Name);
        Assert.Equal("test@example.com", retrieved.Email);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
```

### Connection Failure Testing

```csharp
[Fact]
public void OpenConnection_WhenDatabaseDown_ThrowsExpectedException()
{
    // Arrange - fakeDb can simulate failures
    var connection = (FakeDbConnection)factory.CreateConnection();
    connection.SetFailOnOpen();

    // Act & Assert
    Assert.Throws<InvalidOperationException>(() => connection.Open());
}

[Fact]
public async Task CreateCustomer_WhenConnectionBreaks_HandlesGracefully()
{
    // Arrange
    var factory = FakeDbFactory.CreateFailingFactory(
        SupportedDatabase.PostgreSql,
        ConnectionFailureMode.FailOnCommand
    );

    var context = new DatabaseContext("Host=fake", factory);
    var helper = new TableGateway<Customer, int>(context);

    // Act & Assert - Verify error handling
    await Assert.ThrowsAsync<InvalidOperationException>(
        async () => await helper.CreateAsync(customer, context)
    );
}
```

## Performance Optimizations

### 1. Reader Plan Caching
```csharp
// First query: Builds plan by introspecting reader schema
var customers = await helper.LoadListAsync(container);
// Cached: Column ordinals, type extractors, property setters

// Subsequent queries with same schema: Reuses plan
var moreCustomers = await helper.LoadListAsync(container);
// ✅ No reflection, no string lookups, pure delegate calls
```

### 2. SQL Template Caching
```csharp
// First retrieve by ID: Builds SQL template
var customer = await helper.RetrieveOneAsync(42);
// Cached: "SELECT ... FROM customers WHERE customer_id = @p0"

// Subsequent retrieves: Reuses template
var other = await helper.RetrieveOneAsync(99);
// ✅ No string building, just parameter swap
```

### 3. Compiled Property Setters
```csharp
// Replaces slow reflection with fast compiled delegates
// reader.GetString(0) → customer.Name = value  (direct call, no reflection)

// Performance: ~1,700ns per row vs ~9,700ns with pure reflection (5.7x faster)
// Benchmark: ReaderMappingBenchmark on AMD Ryzen 9 5950X, .NET 8.0.22
// See benchmarks/CrudBenchmarks/ReaderMappingBenchmark.cs for methodology
```

### 4. Parameter Bucketing
```csharp
// Instead of generating unique SQL for every ID count:
// WHERE id IN (@p0, @p1, @p2)          -- 3 IDs
// WHERE id IN (@p0, @p1, @p2, @p3)     -- 4 IDs
// ... (unbounded cache entries)

// Buckets to powers of 2 and reuses last value:
// WHERE id IN (@p0, @p1, @p2, @p3)     -- 3 IDs uses 4-param template
// WHERE id IN (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7)  -- 5 IDs uses 8-param

// Keeps cache bounded while maintaining performance
```

### 5. Set-Valued Parameters (PostgreSQL, SQL Server)
```csharp
// Traditional: WHERE id IN (@p0, @p1, @p2, ..., @p99)  -- 100 parameters
// Set-valued: WHERE id = ANY(@p0)  -- 1 array parameter

// Benefits:
// - Fewer parameters (SQL Server limit: 2100, Oracle: 1000)
// - Better query plan caching (database sees same SQL)
// - Faster parameter binding
```

## Multi-Tenancy Support

pengdows.crud supports **tenant-per-database only**. It ships a lightweight multi-tenant registry that
creates one `DatabaseContext` per tenant and caches them for reuse. Tenants are configured with a
`DatabaseContextConfiguration` that includes provider name and connection string.

### Configuration (appsettings.json)

```json
{
  "MultiTenant": {
    "Tenants": [
      {
        "Name": "tenant-a",
        "DatabaseContextConfiguration": {
          "ProviderName": "Npgsql",
          "ConnectionString": "Host=pg;Database=tenant_a;Username=app;Password=secret",
          "DbMode": "Best",
          "ReadWriteMode": "ReadWrite"
        }
      },
      {
        "Name": "tenant-b",
        "DatabaseContextConfiguration": {
          "ProviderName": "Sqlite",
          "ConnectionString": "Data Source=tenant_b.db",
          "DbMode": "SingleWriter",
          "ReadWriteMode": "ReadWrite"
        }
      }
    ]
  }
}
```

### DI Setup

```csharp
// Register provider factories by name (must match ProviderName in tenant config)
services.AddKeyedSingleton<DbProviderFactory>("Npgsql", (_, _) => NpgsqlFactory.Instance);
services.AddKeyedSingleton<DbProviderFactory>("Sqlite", (_, _) => SqliteFactory.Instance);

// Register tenant resolver + registry
services.AddMultiTenancy(configuration);
```

### Usage

```csharp
public class TenantOrderService
{
    private readonly ITenantContextRegistry _registry;

    public TenantOrderService(ITenantContextRegistry registry)
    {
        _registry = registry;
    }

    public async Task<Order?> GetOrderAsync(string tenantId, int id)
    {
        var ctx = _registry.GetContext(tenantId);
        var helper = new TableGateway<Order, int>(ctx);
        return await helper.RetrieveOneAsync(id);
    }
}
```

**Behavior notes:**
- Only tenant-per-database is supported (no shared-schema or row-level tenancy helpers).
- Contexts are cached per tenant in `TenantContextRegistry`.
- Each tenant context still follows the singleton-per-connection-string rule.
- When using SQLite `:memory:` per tenant, use `DbMode.SingleConnection` or a shared in-memory connection string.

## Connection Modes and Strategies

### Standard (Default - Production Recommended)
```csharp
var context = new DatabaseContext(connectionString, factory);
// - Opens connection per operation
// - Closes immediately after
// - Relies on provider connection pooling
// - Best for: Production workloads, cloud databases
```

### KeepAlive (Embedded Databases)
```csharp
var context = new DatabaseContext(
    "Data Source=mydb.db",
    SqliteFactory.Instance,
    new DatabaseContextConfiguration { ConnectionMode = DbMode.KeepAlive }
);
// - Keeps one sentinel connection open
// - Prevents database unload (SQLite, LocalDB)
// - Best for: SQLite, SQL Server LocalDB
```

### SingleWriter (File-Based Shared Databases)
```csharp
var context = new DatabaseContext(
    "Data Source=mydb.db;Cache=Shared",
    SqliteFactory.Instance,
    new DatabaseContextConfiguration { ConnectionMode = DbMode.SingleWriter }
);
// - One persistent write connection
// - Ephemeral read connections as needed
// - Best for: SQLite with Cache=Shared, DuckDB files
```

### SingleConnection (In-Memory Databases)
```csharp
var context = new DatabaseContext(
    "Data Source=:memory:",
    SqliteFactory.Instance
);
// Auto-detects and uses SingleConnection mode
// - All operations share one connection
// - Connection stays open for lifetime of context
// - Best for: SQLite :memory:, testing
```

### Best Mode Auto-Selection

Use `DbMode.Best` to automatically select the optimal connection mode for your database:

```csharp
var context = new DatabaseContext(
    connectionString,
    factory,
    new DatabaseContextConfiguration { DbMode = DbMode.Best }
);
```

**Best-mode selection rules** (based on detected database type and connection string):

| Database Type | Connection String Pattern | Auto-Selected Mode | Reason |
|--------------|---------------------------|-------------------|---------|
| **SQLite / DuckDB** | `Data Source=:memory:` (isolated) | `SingleConnection` | Each `:memory:` connection = separate database. Required for correctness. |
| **SQLite / DuckDB** | `Mode=Memory;Cache=Shared` (shared in-memory) | `SingleWriter` | Multiple connections share database. Optimal write coordination. |
| **SQLite / DuckDB** | File-based (e.g., `mydb.db`) | `SingleWriter` | Prevents lock contention. WAL enables many readers + one writer. |
| **Firebird** | Embedded mode | `SingleConnection` | Embedded Firebird requires single connection. Required for correctness. |
| **SQL Server** | LocalDB instance | `KeepAlive` | Prevents database unload. Sentinel connection keeps instance alive. |
| **PostgreSQL** | Any | `Standard` | Full server supports high concurrency. Best throughput. |
| **SQL Server** | Non-LocalDB | `Standard` | Client-server architecture. Connection pooling handles concurrency. |
| **MySQL / MariaDB** | Any | `Standard` | Full server supports high concurrency. Best throughput. |
| **Oracle** | Any | `Standard` | Full server supports high concurrency. Best throughput. |
| **CockroachDB** | Any | `Standard` | Distributed database. Designed for high concurrency. |
| **Unknown Provider** | Any | `Standard` | Safe default. Relies on provider pooling. |

**Coercion Rules** (when explicit mode is unsafe):

| Database Type | Requested Mode | Coerced To | Reason |
|--------------|----------------|-----------|---------|
| SQLite/DuckDB isolated `:memory:` | Any except `SingleConnection` | `SingleConnection` | **REQUIRED** - Each connection = separate database |
| SQLite/DuckDB file or shared | `Standard` or `KeepAlive` | `SingleWriter` | **UNSAFE** - Lock contention without coordination |
| Firebird embedded | Any except `SingleConnection` | `SingleConnection` | **REQUIRED** - Embedded mode limitation |
| SQL Server LocalDB | Any except `KeepAlive` | `KeepAlive` | **REQUIRED** - Prevents instance unload |

**Mode Mismatch Warnings** (safe but suboptimal):

pengdows.crud logs warnings when you explicitly choose a mode that's safe but not optimal:

```
[LogWarning] ConnectionModeMismatch: SingleConnection mode used with PostgreSQL.
Client-server databases support full concurrency; consider Standard mode for better throughput.
```

Examples of mismatch warnings:
- **PostgreSQL + SingleConnection**: Safe but limits concurrency (one connection for all work)
- **SQL Server + SingleWriter**: Safe but limits concurrency (one writer, ephemeral readers)
- **SQLite file + Standard**: Safe with WAL but may cause lock contention without WAL

**Key Insight**: `DbMode.Best` eliminates guesswork. It selects the most functional safe mode based on database capabilities and connection string analysis.

## NuGet Packages

```xml
<!-- Core library -->
<PackageReference Include="pengdows.crud" Version="1.1.0" />

<!-- Interfaces only (for library projects) -->
<PackageReference Include="pengdows.crud.abstractions" Version="1.1.0" />

<!-- Mock provider for testing -->
<PackageReference Include="pengdows.crud.fakeDb" Version="1.1.0" />
```

## Code Quality & Testing Standards

### Test-Driven Development (Mandatory)
- **Red**: Write failing test first
- **Green**: Implement minimal code to pass
- **Refactor**: Improve while keeping tests green
- **Coverage**: 83% minimum (CI enforced), 90% target

### Test Count (as of v1.1)
- **Unit Tests**: 2,951+ tests (all passing)
- **Integration Tests**: 85 tests across 8 databases (83 passing, 2 skipped)
- **Execution Time**: ~6 seconds for unit, varies for integration

### CI/CD Pipeline
- Build on push to main
- Run all unit tests (must pass)
- Code coverage check (≥83%)
- Run integration tests (Testcontainers)
- Publish to NuGet on successful builds

## Real-World Usage Example

```csharp
// Dependency Injection Setup (ASP.NET Core)
public void ConfigureServices(IServiceCollection services)
{
    // Register data source (better than factory for performance)
    services.AddSingleton(sp =>
    {
        var connectionString = Configuration.GetConnectionString("Default");
        return new NpgsqlDataSourceBuilder(connectionString).Build();
    });

    // Register context (singleton per connection string)
    services.AddSingleton<IDatabaseContext>(sp =>
    {
        var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
        return new DatabaseContext(
            Configuration.GetConnectionString("Default"),
            dataSource
        );
    });

    // Register audit resolver
    services.AddScoped<IAuditValueResolver, UserAuditResolver>();

    // Register entity helpers
    services.AddScoped<ITableGateway<Customer, int>>(sp =>
    {
        var context = sp.GetRequiredService<IDatabaseContext>();
        var audit = sp.GetRequiredService<IAuditValueResolver>();
        return new TableGateway<Customer, int>(context, audit);
    });
}

// Service Layer
public class CustomerService
{
    private readonly ITableGateway<Customer, int> _helper;
    private readonly IDatabaseContext _context;

    public CustomerService(
        ITableGateway<Customer, int> helper,
        IDatabaseContext context)
    {
        _helper = helper;
        _context = context;
    }

    public async Task<Customer?> GetCustomerAsync(int id)
    {
        return await _helper.RetrieveOneAsync(id);
    }

    public async Task<List<Customer>> SearchCustomersAsync(
        string searchTerm,
        int limit = 100)
    {
        var container = _helper.BuildBaseRetrieve("c");
        container.Query.Append(" WHERE c.name ILIKE ");
        container.Query.Append(container.MakeParameterName("search"));
        container.AddParameterWithValue(
            "search",
            DbType.String,
            $"%{searchTerm}%"
        );
        container.Query.Append(" LIMIT ");
        container.Query.Append(limit);

        return await _helper.LoadListAsync(container);
    }

    public async Task<Customer> CreateCustomerAsync(Customer customer)
    {
        using var transaction = _context.BeginTransaction();

        try
        {
            await _helper.CreateAsync(customer, transaction);

            // Audit log
            await LogAuditAsync(
                $"Created customer {customer.Id}",
                transaction
            );

            transaction.Commit();
            return customer;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // Export large dataset using streaming
    public async Task ExportCustomersToFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(filePath);
        await writer.WriteLineAsync("Id,Name,Email,CreatedAt");

        var container = _helper.BuildBaseRetrieve("c");

        // Stream millions of customers without memory issues
        await foreach (var customer in _helper.LoadStreamAsync(
            container,
            cancellationToken))
        {
            await writer.WriteLineAsync(
                $"{customer.Id},{customer.Name},{customer.Email},{customer.CreatedAt}"
            );
        }
    }
}
```

## Comparison to Other Libraries

| Feature | EF Core | Dapper | PetaPoco | pengdows.crud |
|---------|---------|--------|----------|---------------|
| **SQL Control** | ❌ LINQ only | ✅ Raw SQL | ✅ Raw SQL | ✅ Raw SQL |
| **Type Safety** | ✅ Strong | ⚠️ Dynamic | ⚠️ Dynamic | ✅ Strong |
| **Change Tracking** | ✅ Built-in | ❌ Manual | ❌ Manual | ❌ Manual |
| **Multi-DB Support** | ✅ 8+ DBs | ✅ Any ADO.NET | ✅ Any ADO.NET | ✅ 9 DBs |
| **Testability** | ⚠️ InMemory | ✅ Mockable | ✅ Mockable | ✅ fakeDb |
| **Learning Curve** | High | Low | Low | Medium |
| **Performance** | Good | Excellent | Very Good | Excellent |
| **Streaming** | ✅ IAsyncEnumerable | ❌ None | ❌ None | ✅ IAsyncEnumerable |
| **Transaction Mgmt** | ✅ Built-in | ⚠️ Manual | ⚠️ Manual | ✅ Built-in |
| **Connection Mgmt** | ✅ Automatic | ⚠️ Manual | ⚠️ Manual | ✅ Strategies |
| **Code Size** | Very Large | Small | Small | Medium |

## When to Choose pengdows.crud

### ✅ Great Fit For:
- **Performance-critical applications** where query optimization matters
- **Teams comfortable with SQL** who want control
- **Multi-database applications** needing vendor-specific features
- **High-concurrency systems** with connection pool constraints
- **Cloud deployments** minimizing connection costs
- **Legacy database integration** with complex schemas
- **Test-driven development** requiring fast, isolated tests
- **Large data exports/ETL** needing memory-efficient streaming

### ❌ Poor Fit For:
- Rapid prototypes where ORM scaffolding saves time
- Teams unfamiliar with SQL
- Simple CRUD apps where performance doesn't matter
- Projects requiring GraphQL auto-generation from models
- Scenarios where change tracking is essential

## Getting Started

### 1. Install Package
```bash
dotnet add package pengdows.crud
dotnet add package pengdows.crud.fakeDb  # For testing
```

### 2. Define Entity
```csharp
[Table("products")]
public class Product
{
    [Id]
    [Column("product_id", DbType.Int32)]
    public int Id { get; set; }

    [Column("name", DbType.String)]
    public string Name { get; set; }

    [Column("price", DbType.Decimal)]
    public decimal Price { get; set; }
}
```

### 3. Create Context and Helper
```csharp
var context = new DatabaseContext(
    "Server=localhost;Database=mydb",
    SqlClientFactory.Instance
);

var helper = new TableGateway<Product, int>(context);
```

### 4. Use It
```csharp
// Create
var product = new Product { Name = "Widget", Price = 9.99m };
await helper.CreateAsync(product, context);

// Read
var allProducts = await helper.RetrieveAsync(new[] { 1, 2, 3 });

// Update
product.Price = 12.99m;
await helper.UpdateAsync(product);

// Delete
await helper.DeleteAsync(product.Id);

// Custom query
var container = helper.BuildBaseRetrieve("p");
container.Query.Append(" WHERE p.price > ");
container.Query.Append(container.MakeParameterName("min_price"));
container.AddParameterWithValue("min_price", DbType.Decimal, 10m);
var expensiveProducts = await helper.LoadListAsync(container);
```

## Links and Resources

- **GitHub**: (Repository URL would go here)
- **NuGet**: https://www.nuget.org/packages/pengdows.crud/
- **Documentation**: (Wiki/docs URL would go here)
- **Supported Databases**: SQL Server, PostgreSQL, Oracle, MySQL, SQLite, Firebird, CockroachDB, DuckDB, MariaDB

---

**pengdows.crud** - When you need SQL control with .NET safety and testability.
