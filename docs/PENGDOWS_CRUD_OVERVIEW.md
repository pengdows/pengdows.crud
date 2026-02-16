# pengdows.crud 2.0 - SQL-First Data Access for .NET 8 / .NET 10

## What is pengdows.crud?

**pengdows.crud** is a lightweight, SQL-first data access library for .NET that gives developers full control over their SQL while providing type safety, database abstraction, and comprehensive testing support. It's designed for teams who want to write SQL themselves but need the safety, testability, and cross-database compatibility that traditional ORMs struggle to provide.

## What's New in 2.0

### Breaking Changes from 1.x
- **`EntityHelper<TEntity, TRowID>` renamed to `TableGateway<TEntity, TRowID>`** — interface: `ITableGateway`
- **Interface-first design mandate** — all public APIs exposed through `pengdows.crud.abstractions`
- **All async methods return `ValueTask`/`ValueTask<T>`** instead of `Task`/`Task<T>` (30-50% allocation reduction)
- **API baseline enforcement** — breaking interface changes detected automatically at build time

### New Features
- **.NET 10 multi-targeting** — `pengdows.crud` and `pengdows.crud.abstractions` target both `net8.0` and `net10.0`
- **Batch operations** — `BatchCreateAsync` and `BatchUpsertAsync` for multi-row INSERT/UPSERT
- **Pool governor** — Semaphore-based backpressure preventing connection pool exhaustion
- **RFC 9562 UUIDv7** — `Uuid7Optimized` with configurable clock modes for database-friendly sortable IDs
- **DbMode.Best** — Auto-selects optimal connection mode based on database type and connection string
- **Metrics and observability** — 25+ real-time metrics with event-based updates
- **Session settings** — Per-connection session initialization (ANSI_QUOTES, isolation defaults, search_path)
- **Isolation profiles** — Portable `IsolationProfile` abstraction across database engines
- **DataSource support** — Native `DbDataSource`/`NpgsqlDataSource` integration
- **Connection strategy pattern** — `IConnectionStrategy` with 4 implementations
- **Thread safety improvements** — `AsyncLocker` with contention tracking, serialized connection open
- **Bounded LRU cache** — Prevents unbounded memory growth in long-running applications
- **Advanced type registry** — Pluggable `AdvancedTypeRegistry` with 14+ specialized converters

## Philosophy: No Magic, Just Control

Unlike Entity Framework or other ORMs, pengdows.crud follows these core principles:

### 1. **SQL-First, Not Query Builder-First**
- You write the SQL yourself — no LINQ, no expression trees, no hidden query generation
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
│  │ - Batch ops      │  │ - Pool governor  │                │
│  │ - Streaming      │  │ - Metrics        │                │
│  │ - Mapping        │  │ - Session setup  │                │
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
│  - DbProviderFactory / DbDataSource                         │
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
var gateway = new TableGateway<Customer, int>(context);

// CRUD operations
var customer = await gateway.RetrieveOneAsync(42);
await gateway.UpdateAsync(customer);
await gateway.DeleteAsync(42);

// Custom SQL
var container = gateway.BuildBaseRetrieve("c");
container.Query.Append(" WHERE c.email LIKE ");
container.Query.Append(container.MakeParameterName("email"));
container.AddParameterWithValue("email", DbType.String, "%@example.com");
var results = await gateway.LoadListAsync(container);
```

#### **2. DatabaseContext**
Connection lifecycle, transaction management, metrics, and pool governance.

```csharp
// Create context (with auto-selected connection mode)
var context = new DatabaseContext(
    "Server=localhost;Database=mydb",
    SqlClientFactory.Instance,
    new DatabaseContextConfiguration { ConnectionMode = DbMode.Best }
);

// Or with NpgsqlDataSource (preferred for PostgreSQL)
var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
var context = new DatabaseContext(connectionString, dataSource);

// Transactions
using var transaction = context.BeginTransaction();
try
{
    await gateway.CreateAsync(entity, transaction);
    await gateway.UpdateAsync(otherEntity, transaction);
    transaction.Commit();
}
catch
{
    transaction.Rollback();
    throw;
}
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

// Execute (returns ValueTask for reduced allocations)
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
// - Session settings (ANSI_QUOTES, isolation defaults)
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
| **MariaDB** | MySql.Data | MySQL-compatible, enhanced features |
| **SQLite** | Microsoft.Data.Sqlite | RETURNING (3.35+), simple transactions |
| **Firebird** | FirebirdSql.Data | RETURNING, MERGE, generators |
| **CockroachDB** | Npgsql | PostgreSQL-compatible, distributed SQL |
| **DuckDB** | DuckDB.NET | Analytical queries, in-memory/file-based |

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
    [IsEnum]                                  // Auto-converts enum <-> string/int
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
bool success = await gateway.CreateAsync(customer, context);

// Or build SQL manually
var container = gateway.BuildCreate(customer);
container.Query.Append(" RETURNING customer_id"); // PostgreSQL
var newId = await container.ExecuteScalarAsync<int>();
```

### Retrieve
```csharp
// By single ID
var customer = await gateway.RetrieveOneAsync(42);

// By multiple IDs
var customers = await gateway.RetrieveAsync(new[] { 1, 2, 3, 4, 5 });

// Custom SQL
var container = gateway.BuildBaseRetrieve("c");
container.Query.Append(" WHERE c.created_at > ");
container.Query.Append(container.MakeParameterName("date"));
container.AddParameterWithValue("date", DbType.DateTime, DateTime.Now.AddMonths(-1));
var recentCustomers = await gateway.LoadListAsync(container);
```

### Update
```csharp
// Update entity
customer.Email = "newemail@acme.com";
int rowsAffected = await gateway.UpdateAsync(customer);

// With optimistic concurrency check
int rowsAffected = await gateway.UpdateAsync(
    customer,
    loadOriginal: true  // Loads original to detect concurrent changes
);
```

### Delete
```csharp
// Single delete
await gateway.DeleteAsync(42);

// Bulk delete
await gateway.DeleteAsync(new[] { 1, 2, 3, 4, 5 });
```

### Upsert (Insert or Update)
```csharp
// Automatically chooses INSERT or UPDATE based on existence
int rowsAffected = await gateway.UpsertAsync(customer);

// Uses database-specific strategies:
// - SQL Server: MERGE
// - PostgreSQL: INSERT ... ON CONFLICT ... DO UPDATE
// - MySQL: INSERT ... ON DUPLICATE KEY UPDATE
// - SQLite: INSERT ... ON CONFLICT ... DO UPDATE (3.24+)
```

## Batch Operations

Multi-row INSERT and UPSERT for high-throughput scenarios:

```csharp
var customers = new List<Customer>
{
    new() { Name = "Acme", Email = "a@acme.com" },
    new() { Name = "Beta", Email = "b@beta.com" },
    new() { Name = "Gamma", Email = "g@gamma.com" }
};

// Batch insert - generates multi-row VALUES syntax
int affected = await gateway.BatchCreateAsync(customers, context);

// Batch upsert - dialect-specific multi-row upsert
int affected = await gateway.BatchUpsertAsync(customers, context);

// Or build without executing (for inspection/modification)
IReadOnlyList<ISqlContainer> chunks = gateway.BuildBatchCreate(customers);
IReadOnlyList<ISqlContainer> chunks = gateway.BuildBatchUpsert(customers);
```

**How it works:**
- Generates multi-row `INSERT INTO t (cols) VALUES (...), (...), (...)` syntax
- Auto-chunks based on dialect's `MaxParameterLimit` with 10% headroom
- NULL values inlined as literals (no parameter consumed)
- Database-specific upsert strategies:
  - PostgreSQL/CockroachDB: `INSERT ... ON CONFLICT DO UPDATE`
  - MySQL/MariaDB: `INSERT ... ON DUPLICATE KEY UPDATE`
  - SQL Server/Oracle/Firebird: falls back to individual upserts per entity

## Streaming API (Memory-Efficient Operations)

Stream large result sets without loading everything into memory.

### LoadStreamAsync - Stream Custom SQL Results

```csharp
// Traditional approach - loads ALL orders into memory
var container = gateway.BuildBaseRetrieve("o");
container.Query.Append(" WHERE o.status = 'Pending'");
var allOrders = await gateway.LoadListAsync(container);
// 50,000 orders x 5KB each = 250MB in memory

// Streaming approach - processes one at a time
await foreach (var order in gateway.LoadStreamAsync(container))
{
    await ProcessOrderAsync(order);
    // Only 1 order (5KB) in memory at a time
}
```

### RetrieveStreamAsync - Stream by ID List

```csharp
var orderIds = await searchService.GetMatchingOrderIdsAsync();

await foreach (var order in gateway.RetrieveStreamAsync(orderIds))
{
    await ExportToFileAsync(order);
    // Process and discard, never accumulating
}
```

### Streaming with Cancellation

```csharp
var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromMinutes(5));

try
{
    await foreach (var order in gateway.RetrieveStreamAsync(orderIds, null, cts.Token))
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
    await gateway.CreateAsync(order, transaction);

    foreach (var item in order.Items)
    {
        await itemGateway.CreateAsync(item, transaction);
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

// Or portable isolation profiles (maps to optimal native level per database)
using var tx = context.BeginTransaction(IsolationProfile.SafeNonBlockingReads);

// Supported profiles:
// - SafeNonBlockingReads: MVCC snapshot, no dirty reads, no blocking
// - StrictConsistency:    Serializable, fully isolated (financial/critical logic)
// - FastWithRisks:        ReadUncommitted / dirty reads (almost never recommended)
```

### Savepoints
```csharp
using var transaction = context.BeginTransaction();

await gateway.CreateAsync(customer, transaction);

await transaction.SavepointAsync("customer_created");

try
{
    await gateway.CreateAsync(order, transaction);
}
catch
{
    // Rollback just the order, keep customer
    await transaction.RollbackToSavepointAsync("customer_created");
}

transaction.Commit();
```

## Pool Governor

The pool governor prevents connection pool exhaustion by limiting concurrent connections with semaphore-based backpressure.

```csharp
var config = new DatabaseContextConfiguration
{
    ConnectionMode = DbMode.Best,
    PoolGovernorEnabled = true,
    MaxConcurrentReads = 20,
    MaxConcurrentWrites = 10,
    PoolAcquireTimeout = TimeSpan.FromSeconds(5)
};

var context = new DatabaseContext(connectionString, factory, config);
```

**Features:**
- Separate read and write permits for fine-grained control
- Throws `PoolSaturatedException` when timeout expires
- Turnstile fairness prevents writer starvation under reader pressure
- Real-time metrics: in-use, peak, queued, timeouts, cancellations
- RAII pattern via `PoolPermit` struct ensures permits are always released

## UUIDv7 (RFC 9562)

High-performance, time-sortable UUID generator optimized for database indexes:

```csharp
// Generate a UUIDv7
var id = Uuid7Optimized.NewUuid7();

// Non-blocking generation for latency-sensitive code
if (Uuid7Optimized.TryNewUuid7(out var id))
{
    // Use id
}

// Configure clock mode for your deployment
Uuid7Optimized.Configure(new Uuid7Options
{
    ClockMode = Uuid7ClockMode.NtpSynced  // Default
});
```

**Clock modes:**
| Mode | Accuracy | Use Case |
|------|----------|----------|
| `PtpSynced` | ±0.1-1.0ms | PTP-synchronized clusters (EKS Nitro, on-prem PTP) |
| `NtpSynced` | ±1-10ms | Most cloud environments (default) |
| `SingleInstance` | N/A | Single-writer services, embedded systems |

**Why UUIDv7 over UUIDv4?**
- Chronologically sortable (no B-tree index fragmentation)
- Contains timestamp (useful for debugging/auditing)
- Up to 4096 IDs per millisecond per thread
- Thread-local counters (lock-free, zero CAS contention)

## Metrics and Observability

DatabaseContext tracks 25+ real-time metrics with minimal overhead:

```csharp
// Subscribe to metrics updates
context.MetricsUpdated += (sender, metrics) =>
{
    Console.WriteLine($"Commands: {metrics.CommandsExecuted}, P95: {metrics.P95CommandMs}ms");
};

// Or poll on demand
var metrics = context.GetMetrics();
```

**Metrics tracked:**

| Category | Metrics |
|----------|---------|
| **Connections** | Current, peak, opened, closed, long-lived, avg hold/open/close time |
| **Commands** | Executed, failed, timed-out, cancelled, avg duration, P95, P99 |
| **Rows** | Total read, total affected, max parameters observed |
| **Prepared Statements** | Cached, evicted |
| **Transactions** | Active, max concurrent, avg duration |
| **Read/Write Split** | Separate `DatabaseRoleMetrics` for read vs write operations |

```csharp
// Quick observability check
var openConns = context.NumberOfOpenConnections;  // Current count
var peakConns = context.PeakOpenConnections;      // Peak observed
var dbProduct = context.Product;                   // Detected database
var mode = context.ConnectionMode;                 // Current DbMode
```

## Connection Modes and Strategies

### DbMode Enum

```csharp
public enum DbMode
{
    Standard = 0,         // Recommended for production
    KeepAlive = 1,        // Keeps one sentinel connection open
    SingleWriter = 2,     // Persistent writer + ephemeral readers
    SingleConnection = 4, // All work through one pinned connection
    Best = 15             // Auto-select optimal mode for the database
}
```

### Mode Descriptions

| Mode | Best For | Behavior |
|------|----------|----------|
| **Standard** | Production workloads | New connection per operation, relies on provider pooling |
| **KeepAlive** | SQL Server LocalDB | Sentinel connection prevents database unload |
| **SingleWriter** | File-based SQLite/DuckDB | One persistent write connection, ephemeral readers |
| **SingleConnection** | In-memory `:memory:` databases | All operations share one pinned connection |
| **Best** | Everywhere (recommended) | Auto-selects based on database type and connection string |

### Best Mode Auto-Selection

| Database Type | Connection Pattern | Auto-Selected Mode |
|--------------|-------------------|-------------------|
| SQLite/DuckDB | `:memory:` (isolated) | `SingleConnection` |
| SQLite/DuckDB | File-based or shared memory | `SingleWriter` |
| SQL Server | LocalDB instance | `KeepAlive` |
| PostgreSQL, SQL Server, MySQL, Oracle, CockroachDB | Any | `Standard` |

```csharp
// Recommended: let pengdows.crud choose
var context = new DatabaseContext(connectionString, factory,
    new DatabaseContextConfiguration { ConnectionMode = DbMode.Best });
```

### Connection Strategy Pattern

Each `DbMode` maps to an `IConnectionStrategy` implementation:
- `StandardConnectionStrategy` — Ephemeral connections from pool
- `KeepAliveConnectionStrategy` — Sentinel + ephemeral work connections
- `SingleWriterConnectionStrategy` — Persistent writer + ephemeral readers
- `SingleConnectionStrategy` — All work on single pinned connection

## Session Settings

Per-connection session initialization ensures consistent behavior across connections:

```csharp
// Automatic: dialect detects and applies session settings on first connection open
// Examples:
// - MySQL: SET sql_mode='ANSI_QUOTES,...'
// - SQL Server: SET ARITHABORT ON, isolation level defaults
// - PostgreSQL: SET search_path, timezone
```

**How it works:**
- Dialect evaluates current session settings vs expected settings
- Generates only the SET statements needed (delta-based)
- Applied once per physical connection via `ConnectionLocalState.SessionSettingsApplied`
- Skips execution if already compliant (zero overhead for warm connections)

## Testing Infrastructure

### Unit Testing with fakeDb

**fakeDb** is a complete ADO.NET provider implementation that wires up ADO.NET control flow for isolated unit testing.

**What fakeDb does:**
- Implements full `DbProviderFactory`, `DbConnection`, `DbCommand`, `DbDataReader` APIs
- Returns empty result sets by default (customizable via constructor)
- Simulates connection failures, timeouts, and error conditions
- Allows SQL generation and parameter verification without a database
- Enables fast, isolated unit tests without Docker or connection strings

**What fakeDb does NOT do:**
- No database semantics (no triggers, referential integrity, constraints)
- No transaction isolation (BeginTransaction succeeds but doesn't enforce isolation)
- No query execution (INSERT/UPDATE/DELETE return success, SELECT returns empty or mocked data)
- No vendor-specific behavior (stored procedures, JSON functions, date formatting)

```csharp
public class CustomerServiceTests
{
    [Fact]
    public async Task CreateCustomer_ValidData_SucceedsWithoutException()
    {
        // Arrange - No database needed!
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:", factory);
        var gateway = new TableGateway<Customer, int>(context);

        var customer = new Customer { Name = "Test", Email = "test@example.com" };

        // Act - Executes against fakeDb
        var success = await gateway.CreateAsync(customer, context);

        // Assert - Verifies code paths without database
        Assert.True(success);
    }

    [Fact]
    public void BuildCreate_ValidEntity_GeneratesCorrectSQL()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext("Host=fake", factory);
        var gateway = new TableGateway<Customer, int>(context);

        var customer = new Customer { Name = "Acme" };

        // Act
        var container = gateway.BuildCreate(customer);

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
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:15-alpine")
            .Build();

        await _postgres.StartAsync();

        _context = new DatabaseContext(
            _postgres.GetConnectionString(),
            NpgsqlDataSource.Create(_postgres.GetConnectionString())
        );

        await SetupSchemaAsync();
    }

    [Fact]
    public async Task CreateAndRetrieve_Customer_Roundtrips()
    {
        var gateway = new TableGateway<Customer, int>(_context);
        var customer = new Customer { Name = "Test", Email = "test@example.com" };

        await gateway.CreateAsync(customer, _context);
        var retrieved = await gateway.RetrieveOneAsync(customer.Id);

        Assert.NotNull(retrieved);
        Assert.Equal("Test", retrieved.Name);
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
public void ConnectionFailure_ThrowsExpectedException()
{
    var factory = fakeDbFactory.CreateFailingFactory(
        SupportedDatabase.PostgreSql,
        ConnectionFailureMode.FailOnOpen);

    var context = new DatabaseContext("Host=fake", factory);

    Assert.Throws<InvalidOperationException>(() =>
    {
        using var container = context.CreateSqlContainer("SELECT 1");
        container.ExecuteScalarAsync<int>().GetAwaiter().GetResult();
    });
}
```

## Performance Optimizations

### 1. ValueTask Throughout
All async methods return `ValueTask`/`ValueTask<T>` instead of `Task`/`Task<T>`, reducing allocations by 30-50% for synchronous completion paths.

### 2. Reader Plan Caching
```csharp
// First query: Builds plan by introspecting reader schema
var customers = await gateway.LoadListAsync(container);
// Cached: Column ordinals, type extractors, property setters

// Subsequent queries with same schema: Reuses plan
var moreCustomers = await gateway.LoadListAsync(container);
// No reflection, no string lookups, pure delegate calls
```

### 3. SQL Template Caching
```csharp
// First retrieve by ID: Builds SQL template
var customer = await gateway.RetrieveOneAsync(42);
// Cached: "SELECT ... FROM customers WHERE customer_id = @p0"

// Subsequent retrieves: Reuses template
var other = await gateway.RetrieveOneAsync(99);
// No string building, just parameter swap
```

### 4. Compiled Property Setters
```csharp
// Replaces slow reflection with fast compiled delegates
// reader.GetString(0) -> customer.Name = value  (direct call, no reflection)

// Performance: ~1,700ns per row vs ~9,700ns with pure reflection (5.7x faster)
```

### 5. Parameter Bucketing
```csharp
// Instead of generating unique SQL for every ID count:
// WHERE id IN (@p0, @p1, @p2)          -- 3 IDs
// WHERE id IN (@p0, @p1, @p2, @p3)     -- 4 IDs

// Buckets to powers of 2 and reuses last value:
// WHERE id IN (@p0, @p1, @p2, @p3)     -- 3 IDs uses 4-param template
// Keeps cache bounded while maintaining performance
```

### 6. Set-Valued Parameters (PostgreSQL, SQL Server)
```csharp
// Traditional: WHERE id IN (@p0, @p1, @p2, ..., @p99)  -- 100 parameters
// Set-valued: WHERE id = ANY(@p0)  -- 1 array parameter

// Benefits:
// - Fewer parameters (SQL Server limit: 2100, Oracle: 1000)
// - Better query plan caching (database sees same SQL)
// - Faster parameter binding
```

### 7. DbParameter Pooling
Parameters are pooled and reused via `ConcurrentQueue`, avoiding repeated `DbProviderFactory.CreateParameter()` calls on hot paths.

### 8. Bounded LRU Cache
Query plans, prepared statements, and compiled accessors use bounded caches with LRU eviction, preventing unbounded memory growth in long-running applications.

## Multi-Tenancy Support

pengdows.crud supports **tenant-per-database only**. It ships a lightweight multi-tenant registry that
creates one `DatabaseContext` per tenant and caches them for reuse.

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
        var gateway = new TableGateway<Order, int>(ctx);
        return await gateway.RetrieveOneAsync(id);
    }
}
```

**Key points:**
- Only tenant-per-database is supported (no shared-schema or row-level tenancy)
- Each tenant can use a **different database engine** (SQL Server, PostgreSQL, SQLite, etc.)
- Contexts are cached per tenant in `TenantContextRegistry`
- SQL automatically generated with tenant's dialect (parameter markers, quoting)

## NuGet Packages

```xml
<!-- Core library (net8.0 + net10.0) -->
<PackageReference Include="pengdows.crud" Version="2.0.0" />

<!-- Interfaces only (net8.0 + net10.0, for library projects) -->
<PackageReference Include="pengdows.crud.abstractions" Version="2.0.0" />

<!-- Mock provider for testing (net8.0) -->
<PackageReference Include="pengdows.crud.fakeDb" Version="2.0.0" />
```

## Code Quality and Testing Standards

### Test-Driven Development (Mandatory)
- **Red**: Write failing test first
- **Green**: Implement minimal code to pass
- **Refactor**: Improve while keeping tests green
- **Coverage**: 83% minimum (CI enforced), 95% target for new features

### Test Count (as of v2.0)
- **Unit Tests**: 4,300+ tests (all passing)
- **Integration Tests**: Across 9 databases via Testcontainers
- **Benchmarks**: BenchmarkDotNet suite with automatic container lifecycle

### CI/CD Pipeline
- Build on push to main
- Run all unit tests (must pass)
- Code coverage check (>=83%)
- API baseline verification (breaking interface changes detected)
- Run integration tests (Testcontainers)
- Publish to NuGet on successful builds

## Comparison to Other Libraries

| Feature | EF Core | Dapper | PetaPoco | pengdows.crud |
|---------|---------|--------|----------|---------------|
| **SQL Control** | LINQ only | Raw SQL | Raw SQL | Raw SQL |
| **Type Safety** | Strong | Dynamic | Dynamic | Strong |
| **Change Tracking** | Built-in | Manual | Manual | Manual |
| **Multi-DB Support** | 8+ DBs | Any ADO.NET | Any ADO.NET | 9 DBs |
| **Testability** | InMemory provider | Mockable | Mockable | fakeDb |
| **Learning Curve** | High | Low | Low | Medium |
| **Performance** | Good | Excellent | Very Good | Excellent |
| **Streaming** | IAsyncEnumerable | None | None | IAsyncEnumerable |
| **Batch Operations** | SaveChanges | None | None | BatchCreate/BatchUpsert |
| **Transaction Mgmt** | Built-in | Manual | Manual | Built-in |
| **Connection Mgmt** | Automatic | Manual | Manual | Strategies + Governor |
| **Metrics** | None | None | None | 25+ real-time metrics |
| **ValueTask** | No | No | No | Yes (all async) |
| **Code Size** | Very Large | Small | Small | Medium |

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

### 3. Create Context and Gateway
```csharp
var context = new DatabaseContext(
    "Server=localhost;Database=mydb",
    SqlClientFactory.Instance,
    new DatabaseContextConfiguration { ConnectionMode = DbMode.Best }
);

var gateway = new TableGateway<Product, int>(context);
```

### 4. Use It
```csharp
// Create
var product = new Product { Name = "Widget", Price = 9.99m };
await gateway.CreateAsync(product, context);

// Read
var allProducts = await gateway.RetrieveAsync(new[] { 1, 2, 3 });

// Update
product.Price = 12.99m;
await gateway.UpdateAsync(product);

// Delete
await gateway.DeleteAsync(product.Id);

// Batch insert
var products = new List<Product> { /* ... */ };
await gateway.BatchCreateAsync(products, context);

// Custom query
var container = gateway.BuildBaseRetrieve("p");
container.Query.Append(" WHERE p.price > ");
container.Query.Append(container.MakeParameterName("min_price"));
container.AddParameterWithValue("min_price", DbType.Decimal, 10m);
var expensiveProducts = await gateway.LoadListAsync(container);
```

## Links and Resources

- **GitHub**: https://github.com/pengdows/pengdows.crud
- **NuGet**: https://www.nuget.org/packages/pengdows.crud/
- **Supported Databases**: SQL Server, PostgreSQL, Oracle, MySQL, MariaDB, SQLite, Firebird, CockroachDB, DuckDB

---

**pengdows.crud 2.0** - SQL control, .NET safety, and testability.
