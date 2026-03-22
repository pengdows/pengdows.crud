---
name: pengdows-crud
description: Help with pengdows.crud - a SQL-first, strongly-typed data access layer for .NET. Use when implementing CRUD operations, entity mapping, database connections, transactions, or testing with FakeDb. Covers TableGateway, SqlContainer, DatabaseContext, attributes ([Table], [Column], [Id], [PrimaryKey]), and multi-database support.
allowed-tools: Read, Grep, Glob, Bash
---

# pengdows.crud Development Guide

pengdows.crud is a SQL-first, strongly-typed, testable data access layer for .NET 8+. No LINQ, no tracking, no surprises - explicit SQL control with database-agnostic features.

## What pengdows.crud is NOT

- **Not an ORM** — no LINQ, no change tracking, no migrations, no lazy loading
- **Not a Dapper replacement** — pengdows.crud is infrastructure; Dapper is a mapper
- **Not like EF Core** — DatabaseContext is a connection governance engine, not a unit of work
- **Not a repository pattern** — TableGateway is a database concept, not a domain concept
- **Not a query builder** — SQL is yours; the framework makes it safe, correct, and portable

## Quick Start

```csharp
// 1. Define entity with attributes
[Table("orders")]
public class Order
{
    [Id(false)]  // DB-generated surrogate key
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [PrimaryKey(1)]  // Business key
    [Column("order_number", DbType.String)]
    public string OrderNumber { get; set; }

    [Column("customer_id", DbType.Int64)]
    public long CustomerId { get; set; }

    [Column("total", DbType.Decimal)]
    public decimal Total { get; set; }
}

// 2. Extend TableGateway with custom methods
public interface IOrderGateway : ITableGateway<Order, long>
{
    ValueTask<Order?> GetByOrderNumberAsync(string orderNumber);
    ValueTask<List<Order>> GetCustomerOrdersAsync(long customerId, DateTime? since = null);
}

public class OrderGateway : TableGateway<Order, long>, IOrderGateway
{
    public OrderGateway(IDatabaseContext context) : base(context)
    {
    }

    public async ValueTask<Order?> GetByOrderNumberAsync(string orderNumber)
    {
        var lookup = new Order { OrderNumber = orderNumber };
        return await RetrieveOneAsync(lookup);
    }

    public async ValueTask<List<Order>> GetCustomerOrdersAsync(long customerId, DateTime? since = null)
    {
        var sc = BuildBaseRetrieve("o");

        sc.Query.Append(" WHERE ");
        sc.Query.Append(sc.WrapObjectName("o.customer_id"));
        sc.Query.Append(" = ");
        var param = sc.AddParameterWithValue("customerId", DbType.Int64, customerId);
        sc.Query.Append(sc.MakeParameterName(param));

        if (since.HasValue)
        {
            sc.Query.Append(" AND ");
            sc.Query.Append(sc.WrapObjectName("o.created_at"));
            sc.Query.Append(" >= ");
            var sinceParam = sc.AddParameterWithValue("since", DbType.DateTime, since.Value);
            sc.Query.Append(sc.MakeParameterName(sinceParam));
        }

        sc.Query.Append(" ORDER BY ");
        sc.Query.Append(sc.WrapObjectName("o.created_at"));
        sc.Query.Append(" DESC");

        return await LoadListAsync(sc);
    }
}

// 3. Register in DI as singletons
services.AddSingleton<IDatabaseContext>(sp =>
    new DatabaseContext(connectionString, SqlClientFactory.Instance));

services.AddSingleton<IOrderGateway>(sp =>
    new OrderGateway(sp.GetRequiredService<IDatabaseContext>()));

// 4. Inject and use
public class OrdersController : ControllerBase
{
    private readonly IOrderGateway _orderGateway;

    public OrdersController(IOrderGateway orderGateway)
    {
        _orderGateway = orderGateway;
    }

    [HttpGet("{orderNumber}")]
    public async Task<IActionResult> Get(string orderNumber)
    {
        var order = await _orderGateway.GetByOrderNumberAsync(orderNumber);
        return order is null ? NotFound() : Ok(order);
    }
}
```

## SQL Building: The Three-Tier API

TableGateway has three tiers of methods. Understanding which tier to use is key:

### Tier 1: Build Methods (SQL generation only, no execution)

`Build*` methods return an `ISqlContainer` holding generated SQL and parameters. Nothing is sent to the database. You inspect, modify, or execute the container yourself.

```csharp
// All synchronous except BuildUpdateAsync
ISqlContainer BuildCreate(entity);            // INSERT statement
ISqlContainer BuildBaseRetrieve("alias");     // SELECT with no WHERE (starting point for custom queries)
ISqlContainer BuildRetrieve(ids, "alias");    // SELECT ... WHERE id IN (...)
ISqlContainer BuildRetrieve(ids);             // SELECT ... WHERE id IN (...) (no alias)
ISqlContainer BuildRetrieve(entities, "a");   // SELECT ... WHERE pk columns match
ISqlContainer BuildRetrieve(entities);        // SELECT ... WHERE pk columns match (no alias)
ISqlContainer BuildDelete(id);                // DELETE ... WHERE id = @id
ISqlContainer BuildUpsert(entity);            // Dialect-specific INSERT-or-UPDATE

// Batch Build methods (new in 2.0)
IReadOnlyList<ISqlContainer> BuildBatchCreate(entities);
IReadOnlyList<ISqlContainer> BuildBatchUpdate(entities);
IReadOnlyList<ISqlContainer> BuildBatchUpsert(entities);
IReadOnlyList<ISqlContainer> BuildBatchDelete(ids);
IReadOnlyList<ISqlContainer> BuildBatchDelete(entities);

// ONLY async Build method (needs DB I/O when loadOriginal: true)
ISqlContainer sc = await BuildUpdateAsync(entity);                     // UPDATE statement
ISqlContainer sc = await BuildUpdateAsync(entity, loadOriginal: true); // Loads current row first
```

**BuildBaseRetrieve vs BuildRetrieve:** `BuildBaseRetrieve` generates `SELECT columns FROM table` with NO WHERE clause - it's the starting point when you need to add your own custom filtering. `BuildRetrieve` generates a complete query with a WHERE clause filtering by IDs or primary key values.

### Tier 2: Load Methods (execute a pre-built container, map results)

`Load*` methods take an `ISqlContainer` you already have (from a Build method or custom SQL) and execute it, mapping rows to entities:

```csharp
TEntity? result      = await LoadSingleAsync(container);     // First row or null
List<TEntity> list   = await LoadListAsync(container);       // All rows
IAsyncEnumerable<TEntity> stream = LoadStreamAsync(container); // Streamed rows (memory-efficient)
```

### Tier 3: Convenience Methods (Build + Execute in one call)

These combine Tier 1 + Tier 2 into a single call. Use when you don't need to customize the SQL:

```csharp
// Single-entity retrieval
TEntity? order = await RetrieveOneAsync(id);           // By [Id] column
TEntity? order = await RetrieveOneAsync(entityLookup); // By [PrimaryKey] columns

// Multi-entity retrieval
List<TEntity> orders = await RetrieveAsync(ids);                   // Returns list
IAsyncEnumerable<TEntity> orders = RetrieveStreamAsync(ids);       // Returns stream

// Write operations
bool created  = await CreateAsync(entity, context);    // INSERT, returns true if 1 row
int affected  = await UpdateAsync(entity);             // UPDATE, returns row count
int affected  = await DeleteAsync(id);                 // DELETE single, returns row count
int affected  = await DeleteAsync(ids);                 // DELETE batch, returns row count
int affected  = await UpsertAsync(entity);             // INSERT or UPDATE

// Collection operations (Delegates to Batch methods)
int affected = await CreateAsync(entities);            // Batch INSERT
int affected = await UpdateAsync(entities);            // Batch UPDATE
int affected = await UpsertAsync(entities);            // Batch UPSERT
int affected = await DeleteAsync(entities);            // Batch DELETE by primary key

// Explicit Batch Operations
int affected = await BatchCreateAsync(entities);
int affected = await BatchUpdateAsync(entities);
int affected = await BatchUpsertAsync(entities);
int affected = await BatchDeleteAsync(ids);
int affected = await BatchDeleteAsync(entities);
```


### WHERE Clause Helpers (modify an existing container)

These append WHERE clauses to a container you already have:

```csharp
// Appends WHERE column IN (@p0, @p1, ...) to an existing container
BuildWhere("e.Id", ids, existingContainer);

// Appends WHERE clause using [PrimaryKey] columns
BuildWhereByPrimaryKey(entities, existingContainer, "e");
```

### Typical Workflow

```csharp
// Simple: Use convenience methods (Tier 3)
var order = await RetrieveOneAsync(orderId);

// Custom query: Build (Tier 1) -> modify -> Load (Tier 2)
var sc = BuildBaseRetrieve("o");
sc.Query.Append(" WHERE ");
sc.Query.Append(sc.WrapObjectName("o.status"));
sc.Query.Append(" = ");
var p = sc.AddParameterWithValue("status", DbType.String, "Active");
sc.Query.Append(sc.MakeParameterName(p));
var results = await LoadListAsync(sc);

// Stream large results: Build (Tier 1) -> Stream (Tier 2)
var sc = BuildBaseRetrieve("o");
sc.Query.Append(" ORDER BY ");
sc.Query.Append(sc.WrapObjectName("o.created_at"));
await foreach (var order in LoadStreamAsync(sc))
{
    await ProcessAsync(order);
}
```

### ISqlContainer: Execution Methods

All execution methods return `ValueTask` (not `Task`) for reduced allocations:

```csharp
ValueTask<int>             ExecuteNonQueryAsync(commandType);          // Row count
ValueTask<T>               ExecuteScalarRequiredAsync<T>(commandType); // Single value — throws if no rows or null
ValueTask<T?>              ExecuteScalarOrNullAsync<T>(commandType);   // Single value — null if no rows or DBNull
ValueTask<ScalarResult<T>> TryExecuteScalarAsync<T>(commandType);      // Unambiguous: None/Null/Value
ValueTask<ITrackedReader>  ExecuteReaderAsync(commandType);            // Data reader
```

All have `CancellationToken` overloads.

### ISqlContainer: Clone for Reuse

Clone a container to reuse its SQL structure with different parameter values or contexts:

```csharp
var template = BuildCreate(entity);
var clone = template.Clone();                    // Same context, update param values
var clone = template.Clone(transactionContext);  // Different context (e.g., transaction)
```

## DI Lifetime Rules - CRITICAL

| Component | Lifetime | Why |
|-----------|----------|-----|
| `DatabaseContext` | **Singleton** | Manages connection pool, metrics, DbMode state |
| `TableGateway<T,TId>` | **Singleton** | Stateless, caches compiled accessors |
| `IAuditValueResolver` | **Singleton** | Must be thread-safe/AsyncLocal-based (e.g. `IHttpContextAccessor` inside); cannot be Scoped because gateways are Singletons |

```csharp
// Correct DI registration
services.AddSingleton<IDatabaseContext>(sp =>
    new DatabaseContext(connectionString, SqlClientFactory.Instance));

// Extended gateway as singleton
services.AddSingleton<IOrderGateway>(sp =>
    new OrderGateway(sp.GetRequiredService<IDatabaseContext>(), sp.GetRequiredService<IAuditValueResolver>()));

// AuditResolver is SINGLETON - must be thread-safe (use IHttpContextAccessor/AsyncLocal internally)
services.AddSingleton<IAuditValueResolver, OidcAuditContextProvider>();
```

## Extending TableGateway - THE CORRECT PATTERN

**Inherit from TableGateway to add custom query methods.** Don't wrap it in a separate service class.

```csharp
public interface ICustomerGateway : ITableGateway<Customer, long>
{
    ValueTask<Customer?> GetByEmailAsync(string email);
    ValueTask<List<Customer>> GetActiveCustomersAsync();
    ValueTask<List<Customer>> SearchByNameAsync(string namePattern);
}

public class CustomerGateway : TableGateway<Customer, long>, ICustomerGateway
{
    public CustomerGateway(IDatabaseContext context, IAuditValueResolver resolver) : base(context, resolver)
    {
    }

    // Lookup by business key
    public async ValueTask<Customer?> GetByEmailAsync(string email)
    {
        var lookup = new Customer { Email = email };
        return await RetrieveOneAsync(lookup);
    }

    // Custom filtered query
    public async ValueTask<List<Customer>> GetActiveCustomersAsync()
    {
        var sc = BuildBaseRetrieve("c");

        sc.Query.Append(" WHERE ");
        sc.Query.Append(sc.WrapObjectName("c.is_active"));
        sc.Query.Append(" = ");
        var param = sc.AddParameterWithValue("active", DbType.Boolean, true);
        sc.Query.Append(sc.MakeParameterName(param));

        sc.Query.Append(" ORDER BY ");
        sc.Query.Append(sc.WrapObjectName("c.name"));

        return await LoadListAsync(sc);
    }

    // Search with LIKE
    public async ValueTask<List<Customer>> SearchByNameAsync(string namePattern)
    {
        var sc = BuildBaseRetrieve("c");

        sc.Query.Append(" WHERE ");
        sc.Query.Append(sc.WrapObjectName("c.name"));
        sc.Query.Append(" LIKE ");
        var param = sc.AddParameterWithValue("pattern", DbType.String, $"%{namePattern}%");
        sc.Query.Append(sc.MakeParameterName(param));

        return await LoadListAsync(sc);
    }
}
```

## Audit Handling

**CRITICAL: Audit handling is structural, not optional.**

You declare intent with attributes. The framework enforces it.
You cannot accidentally skip setting audit fields.

- `[CreatedBy]`, `[CreatedOn]` — set on CREATE only, never modified on UPDATE
- `[LastUpdatedBy]`, `[LastUpdatedOn]` — set on both CREATE and UPDATE
- Both created AND updated fields are populated on CREATE — "last modified"
  queries work correctly on rows that were never updated
- Resolver called **once per batch**, not once per entity
- Throws `InvalidOperationException` at execution time if `IAuditValueResolver`
  is missing and the entity has user audit fields (`[CreatedBy]`, `[LastUpdatedBy]`)
- Timestamp-only fields (`[CreatedOn]`, `[LastUpdatedOn]`) work without a resolver

### IAuditValueResolver

Register as **singleton** and pass to TableGateway for entities with audit fields. Because gateways are singletons, the resolver must also be a singleton — use `IHttpContextAccessor` or `AsyncLocal<T>` internally to access per-request state safely:

```csharp
// Register singleton resolver (thread-safe via IHttpContextAccessor internally)
services.AddHttpContextAccessor();
services.AddSingleton<IAuditValueResolver, OidcAuditContextProvider>();

// OIDC/OAuth implementation
public class OidcAuditContextProvider : IAuditValueResolver
{
    private readonly IHttpContextAccessor _accessor;

    public OidcAuditContextProvider(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public IAuditValues Resolve()
    {
        var user = _accessor.HttpContext?.User;
        var id = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        return new AuditValues { UserId = id };
    }
}
```

### Audit Fields on Entity

```csharp
[Table("orders")]
public class Order
{
    [Id] [Column("id")] public long Id { get; set; }

    [CreatedBy] [Column("created_by")] public string CreatedBy { get; set; }
    [CreatedOn] [Column("created_at")] public DateTime CreatedAt { get; set; }
    [LastUpdatedBy] [Column("updated_by")] public string UpdatedBy { get; set; }
    [LastUpdatedOn] [Column("updated_at")] public DateTime UpdatedAt { get; set; }
}
```

**Important:** Both CreatedBy/On AND LastUpdatedBy/On are SET on CREATE.

---

## CRITICAL: Multi-Tenancy — tenant = database

Each tenant gets a completely isolated `DatabaseContext` with its own pool governor,
connection pool, dialect, and session settings.

**Different tenants can use completely different database engines.**
One tenant on Oracle, another on PostgreSQL, another on SQLite — all from the
same running application, all governed correctly.

**One API serves hundreds of tenants. Zero code changes to add a tenant.**
Adding a tenant is a configuration change, not a deployment.

```csharp
// appsettings.json
{
  "MultiTenant": {
    "ApplicationName": "MyApp",
    "Tenants": [
      {
        "Name": "tenant-a",
        "DatabaseContextConfiguration": {
          "ConnectionString": "Host=pg-a;Database=wp_a",
          "ProviderName": "Npgsql"
        }
      },
      {
        "Name": "tenant-b",
        "DatabaseContextConfiguration": {
          "ConnectionString": "Data Source=oracle-b",
          "ProviderName": "Oracle.ManagedDataAccess.Client"
        }
      }
    ]
  }
}

// Registration — one line
services.AddMultiTenancy(configuration);

// Usage — resolve tenant context, pass to any CRUD method
var tenantCtx = _tenantRegistry.GetContext(tenantId);
var order = await _gateway.RetrieveOneAsync(orderId, tenantCtx);
await _gateway.CreateAsync(newOrder, tenantCtx);
await _gateway.BatchUpdateAsync(entities, tenantCtx);
```

**How it works:**
- `TenantContextRegistry` is a singleton — lazy-creates one `DatabaseContext` per tenant
- `DatabaseContext` per tenant is created on first access, cached forever
- SQL dialect, parameter markers, session settings, pool governor — all automatically
  correct for that tenant's database engine
- Pass tenant context to any CRUD method — the gateway handles everything else
- `Invalidate(tenantId)` evicts a stale context when tenant config changes

## Core Concepts

### [Id] vs [PrimaryKey] - CRITICAL DISTINCTION

**[Id] - Surrogate/Pseudo Key:**
- Single column only
- Used by TableGateway for CRUD operations
- `[Id(false)]` = DB-generated (identity/autoincrement)
- `[Id(true)]` or `[Id]` = client-provided value

**[PrimaryKey] - Business/Natural Key:**
- Can be composite (multiple columns with order)
- Enforced as UNIQUE constraint
- Used by `RetrieveOneAsync(entity)` for lookup
- NEVER put on same column as [Id]

```csharp
[Table("order_items")]
public class OrderItem
{
    [Id(false)]               // Surrogate for FK references
    [Column("id")]
    public long Id { get; set; }

    [PrimaryKey(1)]           // Business key part 1
    [Column("order_id")]
    public int OrderId { get; set; }

    [PrimaryKey(2)]           // Business key part 2
    [Column("product_id")]
    public int ProductId { get; set; }
}
```

### Custom SQL with SqlContainer

**IMPORTANT:** Always use `WrapObjectName()` for column names and aliases to ensure proper quoting per database dialect.

```csharp
// Inside your extended gateway class
public async ValueTask<List<Order>> GetRecentLargeOrdersAsync(decimal minTotal)
{
    var sc = BuildBaseRetrieve("o");

    sc.Query.Append(" WHERE ");
    sc.Query.Append(sc.WrapObjectName("o.total"));
    sc.Query.Append(" >= ");
    var totalParam = sc.AddParameterWithValue("minTotal", DbType.Decimal, minTotal);
    sc.Query.Append(sc.MakeParameterName(totalParam));

    sc.Query.Append(" AND ");
    sc.Query.Append(sc.WrapObjectName("o.created_at"));
    sc.Query.Append(" >= ");
    var dateParam = sc.AddParameterWithValue("since", DbType.DateTime, DateTime.UtcNow.AddDays(-30));
    sc.Query.Append(sc.MakeParameterName(dateParam));

    sc.Query.Append(" ORDER BY ");
    sc.Query.Append(sc.WrapObjectName("o.total"));
    sc.Query.Append(" DESC");

    return await LoadListAsync(sc);
}
```

**WrapObjectName behavior by database:**
- SQL Server: `[o].[total]`
- PostgreSQL: `"o"."total"`
- MySQL: `` `o`.`total` ``
- Oracle: `"o"."total"`

## Connection Management (DbMode)

Use lowest number possible:

| Mode | Use Case |
|------|----------|
| `Standard` (0) | **Production default** - pool per operation |
| `KeepAlive` (1) | Sentinel connection that is NEVER used for operations — prevents engine/proxy idle timeout. Use cases: LocalDB, Aurora Serverless scaling-to-zero, RDS Proxy idle disconnection, long-running Lambda. NOT for connection reuse or efficiency. |
| `SingleWriter` (2) | File-based SQLite/DuckDB |
| `SingleConnection` (4) | In-memory `:memory:` databases |
| `Best` (15) | Auto-select optimal mode for the database |

```csharp
// Using IDatabaseContextConfiguration to specify DbMode with a factory
services.AddSingleton<IDatabaseContext>(sp =>
    new DatabaseContext(
        new DatabaseContextConfiguration { ConnectionString = connStr, DbMode = DbMode.SingleWriter },
        SqlClientFactory.Instance));

// Or using the string-based constructor (also supports DbMode directly)
services.AddSingleton<IDatabaseContext>(sp =>
    new DatabaseContext(connStr, "Microsoft.Data.SqlClient", DbMode.SingleWriter));
```

## Transactions

Transactions are **operation-scoped** - create inside methods, never store as fields.

```csharp
// Inside your extended gateway
public async ValueTask<bool> CancelOrderAsync(long orderId)
{
    await using var txn = await Context.BeginTransactionAsync();
    try
    {
        var order = await RetrieveOneAsync(orderId);
        if (order == null || order.Status == OrderStatus.Shipped)
        {
            await txn.RollbackAsync();
            return false;
        }

        order.Status = OrderStatus.Cancelled;
        var affected = await UpdateAsync(order);

        if (affected > 0)
        {
            await txn.CommitAsync();
            return true;
        }

        await txn.RollbackAsync();
        return false;
    }
    catch
    {
        await txn.RollbackAsync();
        throw;
    }
}

// With isolation level
using var txn = Context.BeginTransaction(IsolationLevel.Serializable);

// Async version with profile
await using var txn = await Context.BeginTransactionAsync(IsolationProfile.SafeNonBlockingReads);
```

**Resource Safety:**
Always use `await using` for `ITransactionContext` and `ITrackedReader` to ensure connections are released correctly.

**CRITICAL: Do NOT use `TransactionScope`**

`TransactionScope` is incompatible with pengdows.crud's connection management. The "open late, close early" philosophy means each operation opens/closes its own connection, which causes:

1. **Distributed transaction promotion** - Second connection within `TransactionScope` promotes to MSDTC
2. **Performance overhead** - MSDTC has significant overhead and may not work in cloud environments
3. **Broken semantics** - Connections closing between operations lose transactional guarantees

Always use `Context.BeginTransaction()` which pins the connection for the transaction's lifetime.

---

## CRITICAL: Exception Handling — Catch Framework Exceptions, Not Provider Exceptions

pengdows.crud translates provider-specific database failures into a uniform
exception hierarchy. Do NOT catch provider-specific exceptions. They are
different for every database and will break when you change providers.

```csharp
// WRONG — provider-specific, breaks when you change databases
catch (SqlException ex) when (ex.Number == 2627) { }         // SQL Server only
catch (NpgsqlException ex) when (ex.SqlState == "23505") { } // PostgreSQL only
catch (MySqlException ex) when (ex.Number == 1062) { }       // MySQL only

// CORRECT — uniform across all 13 databases
catch (UniqueConstraintViolationException ex) { }
catch (ForeignKeyViolationException ex) { }
catch (DeadlockException ex) { /* retry logic */ }
catch (ConcurrencyConflictException ex) { /* reload and retry */ }
catch (CommandTimeoutException ex) { /* timeout handling */ }
```

### Exception Hierarchy

```
DatabaseException (root — all translated DB failures)
    carries: Database, SqlState, ErrorCode, ConstraintName, IsTransient
    InnerException preserves raw provider exception for diagnostics
├── ConstraintViolationException
│   ├── UniqueConstraintViolationException  — duplicate key / PK / unique value
│   ├── ForeignKeyViolationException        — missing reference or blocked delete
│   ├── NotNullViolationException           — required field missing
│   └── CheckConstraintViolationException   — database rule rejected values
├── DeadlockException                       — DB chose this transaction as victim
├── SerializationConflictException          — serializable/distributed write conflict
├── CommandTimeoutException                 — command timed out
└── ConcurrencyConflictException            — version-guarded UPDATE returned 0 rows
                                              (framework-generated, not provider-translated)
```

### Key Design Points

- `InnerException` preserves the raw provider exception — diagnostic detail is not lost
- `ConcurrencyConflictException` is auto-thrown by `UpdateAsync` when a `[Version]` column
  is present and UPDATE affects 0 rows — it is NOT a translated provider exception
- Non-database and internal exceptions propagate unchanged — this normalizes
  database failures, it does not wrap everything
- Translation is per provider family: SQL Server, PostgreSQL/CockroachDB/YugabyteDB,
  MySQL/MariaDB/TiDB, SQLite each have dedicated translators

### Common Patterns

```csharp
// Optimistic concurrency retry
try
{
    await gateway.UpdateAsync(entity);
}
catch (ConcurrencyConflictException)
{
    entity = await gateway.RetrieveOneAsync(entity.Id);
    // reapply changes and retry
}

// Duplicate insert handling
try
{
    await gateway.CreateAsync(entity);
}
catch (UniqueConstraintViolationException ex)
{
    // ex.ConstraintName tells you which constraint fired
    logger.LogWarning("Duplicate: {Constraint}", ex.ConstraintName);
}

// Deadlock retry
try
{
    await gateway.UpdateAsync(entity);
}
catch (DeadlockException)
{
    await Task.Delay(jitter);
    await gateway.UpdateAsync(entity); // retry once
}
```

## Version Column (Optimistic Concurrency)

```csharp
[Version]
[Column("version")]
public int Version { get; set; }
```

- **Create:** Auto-set to 1
- **Update:** Increments and adds `WHERE version = @current`
- **Conflict:** `UpdateAsync` automatically throws `ConcurrencyConflictException`

## Parameter Naming Convention

| Operation | Pattern | Example |
|-----------|---------|---------|
| INSERT values | `i{n}` | `i0`, `i1`, `i2` |
| UPDATE SET clause | `s{n}` | `s0`, `s1`, `s2` |
| WHERE (IN/ANY retrieve) | `w{n}` | `w0`, `w1`, `w2` |
| WHERE key/id lookup | `k{n}` | `k0`, `k1` |
| Optimistic lock version | `v{n}` | `v0`, `v1` |
| JOIN conditions | `j{n}` | `j0`, `j1` |
| Batch row values | `b{n}` | `b0`, `b1`, `b2` |

```csharp
// Reuse container with updated parameters
container.SetParameterValue("w0", newId);
container.SetParameterValue("s0", newValue);
```

## Testing with FakeDb

```csharp
public class OrderGatewayTests
{
    private readonly fakeDbFactory _factory;
    private readonly IDatabaseContext _context;
    private readonly OrderGateway _gateway;

    public OrderGatewayTests()
    {
        _factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        _context = new DatabaseContext("Data Source=test", _factory);
        _gateway = new OrderGateway(_context);
    }

    [Fact]
    public async Task GetCustomerOrdersAsync_BuildsCorrectSql()
    {
        var orders = await _gateway.GetCustomerOrdersAsync(123);
        Assert.NotNull(orders);
    }

    [Fact]
    public void ConnectionFailure_ThrowsException()
    {
        var factory = fakeDbFactory.CreateFailingFactory(
            SupportedDatabase.PostgreSql,
            ConnectionFailureMode.FailOnOpen);
        var context = new DatabaseContext("test", factory);

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var container = context.CreateSqlContainer("SELECT 1");
            container.ExecuteScalarRequiredAsync<int>().GetAwaiter().GetResult();
        });
    }
}
```

## Supported Databases

SQL Server, PostgreSQL, Oracle, MySQL, MariaDB, SQLite, DuckDB, Firebird, CockroachDB, YugabyteDB, TiDB, Snowflake
(+ AuroraMySql and AuroraPostgreSql — auto-detected at runtime, no extra setup)

Each uses optimal SQL syntax (MERGE vs ON CONFLICT vs ON DUPLICATE KEY UPDATE).

## Testing Requirements

Tests are required. Coverage minimums are enforced in CI.

- Minimum **83% branch coverage** (CI blocks merge if below)
- Target **95%+** for new features and public API changes
- Write tests once the design stabilizes
- NO skipped tests
- `pengdows.crud.fakeDb` enables unit testing without a real database
- Integration tests run against all 13 real databases

## Core Invariants

1. **DatabaseContext is SINGLETON** - one per connection string
2. **TableGateway is SINGLETON** - stateless, caches compiled accessors
3. **Extend TableGateway** - put custom query methods in inherited class, not wrapper service
4. **IAuditValueResolver is SINGLETON** - must be thread-safe/AsyncLocal-based; use IHttpContextAccessor or AsyncLocal internally to access per-request state
5. **TenantContextRegistry is SINGLETON** - manages per-tenant contexts
6. **Transactions are operation-scoped** - create inside methods, never store as fields
7. **ITrackedReader is a lease** - pins connection until disposed, dispose promptly
8. **DbMode.Best auto-selects** - SQLite/DuckDB `:memory:` = SingleConnection; file-based SQLite/DuckDB = SingleWriter; LocalDB = KeepAlive; all others = Standard
9. **Always use WrapObjectName()** - for column names and aliases in custom SQL
10. **NEVER use TransactionScope** - incompatible with connection management, use Context.BeginTransaction()
11. **Execution methods return ValueTask** - not Task, for reduced allocations
12. **All async methods have CancellationToken overloads** - pass tokens through for proper cancellation
13. **Multi-tenancy: tenant = database** — `TenantContextRegistry` is singleton; pass tenant context to CRUD methods; one gateway serves all tenants; dialect, pool, and session settings are all per-tenant automatically
14. **Audit handling is structural** — declare with attributes, framework enforces; `IAuditValueResolver` missing + user audit fields = `InvalidOperationException`; cannot be accidentally skipped
