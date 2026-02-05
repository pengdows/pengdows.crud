---
name: pengdows-crud
description: Help with pengdows.crud - a SQL-first, strongly-typed data access layer for .NET. Use when implementing CRUD operations, entity mapping, database connections, transactions, or testing with FakeDb. Covers TableGateway, SqlContainer, DatabaseContext, attributes ([Table], [Column], [Id], [PrimaryKey]), and multi-database support.
allowed-tools: Read, Grep, Glob, Bash
---

# pengdows.crud Development Guide

pengdows.crud is a SQL-first, strongly-typed, testable data access layer for .NET 8+. No LINQ, no tracking, no surprises - explicit SQL control with database-agnostic features.

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
    [Column("order_number", DbType.String, 50)]
    public string OrderNumber { get; set; }

    [Column("customer_id", DbType.Int64)]
    public long CustomerId { get; set; }

    [Column("total", DbType.Decimal)]
    public decimal Total { get; set; }
}

// 2. Extend TableGateway with custom methods
public interface IOrderGateway : ITableGateway<Order, long>
{
    Task<Order?> GetByOrderNumberAsync(string orderNumber);
    Task<List<Order>> GetCustomerOrdersAsync(long customerId, DateTime? since = null);
}

public class OrderGateway : TableGateway<Order, long>, IOrderGateway
{
    public OrderGateway(IDatabaseContext context) : base(context)
    {
    }

    public async Task<Order?> GetByOrderNumberAsync(string orderNumber)
    {
        var lookup = new Order { OrderNumber = orderNumber };
        return await RetrieveOneAsync(lookup);
    }

    public async Task<List<Order>> GetCustomerOrdersAsync(long customerId, DateTime? since = null)
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

## DI Lifetime Rules - CRITICAL

| Component | Lifetime | Why |
|-----------|----------|-----|
| `DatabaseContext` | **Singleton** | Manages connection pool, metrics, DbMode state |
| `TableGateway<T,TId>` | **Singleton** | Stateless, caches compiled accessors |
| `IAuditValueResolver` | **Scoped** | Must resolve current user from request context |

```csharp
// Correct DI registration
services.AddSingleton<IDatabaseContext>(sp =>
    new DatabaseContext(connectionString, SqlClientFactory.Instance));

// Extended gateway as singleton
services.AddSingleton<IOrderGateway>(sp =>
    new OrderGateway(sp.GetRequiredService<IDatabaseContext>()));

// AuditResolver is SCOPED - resolves current user per request
services.AddScoped<IAuditValueResolver, OidcAuditContextProvider>();
```

## Extending TableGateway - THE CORRECT PATTERN

**Inherit from TableGateway to add custom query methods.** Don't wrap it in a separate service class.

```csharp
public interface ICustomerGateway : ITableGateway<Customer, long>
{
    Task<Customer?> GetByEmailAsync(string email);
    Task<List<Customer>> GetActiveCustomersAsync();
    Task<List<Customer>> SearchByNameAsync(string namePattern);
}

public class CustomerGateway : TableGateway<Customer, long>, ICustomerGateway
{
    public CustomerGateway(IDatabaseContext context) : base(context)
    {
    }

    // Lookup by business key
    public async Task<Customer?> GetByEmailAsync(string email)
    {
        var lookup = new Customer { Email = email };
        return await RetrieveOneAsync(lookup);
    }

    // Custom filtered query
    public async Task<List<Customer>> GetActiveCustomersAsync()
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
    public async Task<List<Customer>> SearchByNameAsync(string namePattern)
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

### IAuditValueResolver

Register as **scoped** and pass to TableGateway for entities with audit fields:

```csharp
// Register scoped resolver
services.AddHttpContextAccessor();
services.AddScoped<IAuditValueResolver, OidcAuditContextProvider>();

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

## Multi-Tenancy

Use `TenantContextRegistry` as singleton to manage per-tenant DatabaseContext instances:

```csharp
// Register TenantContextRegistry as singleton
services.AddSingleton<TenantContextRegistry>();

// Pull tenant-specific DatabaseContext from registry
public class TenantService
{
    private readonly TenantContextRegistry _registry;

    public TenantService(TenantContextRegistry registry)
    {
        _registry = registry;
    }

    public IDatabaseContext GetContextForTenant(string tenantId)
    {
        return _registry.GetContext(tenantId);
    }
}
```

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

### TableGateway Methods

> **Note:** `TableGateway` is an alias for `TableGateway` (kept for 1.0 compatibility).

| Method | Purpose |
|--------|---------|
| `CreateAsync(entity)` | INSERT, returns true if 1 row affected |
| `RetrieveOneAsync(id)` | SELECT by [Id] column |
| `RetrieveOneAsync(entity)` | SELECT by [PrimaryKey] columns |
| `RetrieveAsync(ids)` | SELECT multiple by IDs |
| `UpdateAsync(entity)` | UPDATE by [Id], returns rows affected |
| `DeleteAsync(id)` | DELETE by [Id], returns rows affected |
| `UpsertAsync(entity)` | INSERT or UPDATE, returns rows affected |

### Query Building Methods

```csharp
// Build SQL without executing (use inside extended gateway)
var container = BuildCreate(entity);
var container = BuildBaseRetrieve("alias");
var container = await BuildUpdateAsync(entity);
var container = BuildDelete(id);
var container = BuildUpsert(entity);

// Execute and load
var list = await LoadListAsync(container);
var single = await LoadSingleAsync(container);
```

### Custom SQL with SqlContainer

**IMPORTANT:** Always use `WrapObjectName()` for column names and aliases to ensure proper quoting per database dialect.

```csharp
// Inside your extended gateway class
public async Task<List<Order>> GetRecentLargeOrdersAsync(decimal minTotal)
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
| `KeepAlive` (1) | Embedded DBs needing sentinel connection |
| `SingleWriter` (2) | File-based SQLite/DuckDB |
| `SingleConnection` (4) | In-memory `:memory:` databases |

```csharp
services.AddSingleton<IDatabaseContext>(sp =>
    new DatabaseContext(connStr, factory, null, DbMode.SingleWriter));
```

## Transactions

Transactions are **operation-scoped** - create inside methods, never store as fields.

```csharp
// Inside your extended gateway
public async Task<bool> CancelOrderAsync(long orderId)
{
    using var txn = Context.BeginTransaction();
    try
    {
        var order = await RetrieveOneAsync(orderId);
        if (order == null || order.Status == OrderStatus.Shipped)
        {
            txn.Rollback();
            return false;
        }

        order.Status = OrderStatus.Cancelled;
        var affected = await UpdateAsync(order);

        if (affected > 0)
        {
            txn.Commit();
            return true;
        }

        txn.Rollback();
        return false;
    }
    catch
    {
        txn.Rollback();
        throw;
    }
}

// With isolation level
using var txn = Context.BeginTransaction(IsolationLevel.Serializable);

// With savepoints
await txn.SavepointAsync("checkpoint1");
await txn.RollbackToSavepointAsync("checkpoint1");
```

**CRITICAL: Do NOT use `TransactionScope`**

`TransactionScope` is incompatible with pengdows.crud's connection management. The "open late, close early" philosophy means each operation opens/closes its own connection, which causes:

1. **Distributed transaction promotion** - Second connection within `TransactionScope` promotes to MSDTC
2. **Performance overhead** - MSDTC has significant overhead and may not work in cloud environments
3. **Broken semantics** - Connections closing between operations lose transactional guarantees

Always use `Context.BeginTransaction()` which pins the connection for the transaction's lifetime.

## Version Column (Optimistic Concurrency)

```csharp
[Version]
[Column("version")]
public int Version { get; set; }
```

- **Create:** Auto-set to 1
- **Update:** Increments and adds `WHERE version = @current`
- **Conflict:** Returns 0 rows affected

## Parameter Naming Convention

| Operation | Pattern | Example |
|-----------|---------|---------|
| INSERT | `i{n}` | `i0`, `i1`, `i2` |
| UPDATE SET | `s{n}` | `s0`, `s1`, `s2` |
| WHERE | `w{n}` | `w0`, `w1`, `w2` |
| VERSION | `v{n}` | `v0`, `v1` |

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
            container.ExecuteScalarAsync<int>().GetAwaiter().GetResult();
        });
    }
}
```

## Supported Databases

SQL Server, PostgreSQL, Oracle, MySQL, MariaDB, SQLite, Firebird, CockroachDB, DuckDB, DB2, Snowflake, Informix, SAP HANA

Each uses optimal SQL syntax (MERGE vs ON CONFLICT vs ON DUPLICATE KEY UPDATE).

## TDD Requirements

**ALL code changes MUST follow TDD:**
1. Write test FIRST
2. Run test - verify it fails
3. Write minimal implementation
4. Refactor while green
5. Repeat

- Minimum 83% coverage (CI enforced)
- Target 90%+ for new features
- NO skipped tests

## Core Invariants

1. **DatabaseContext is SINGLETON** - one per connection string
2. **TableGateway is SINGLETON** - stateless, caches compiled accessors
3. **Extend TableGateway** - put custom query methods in inherited class, not wrapper service
4. **IAuditValueResolver is SCOPED** - must resolve current user per request
5. **TenantContextRegistry is SINGLETON** - manages per-tenant contexts
6. **Transactions are operation-scoped** - create inside methods, never store as fields
7. **ITrackedReader is a lease** - pins connection until disposed, dispose promptly
8. **DbMode.Best auto-selects** - SQLite :memory: = SingleConnection
9. **Always use WrapObjectName()** - for column names and aliases in custom SQL
10. **NEVER use TransactionScope** - incompatible with connection management, use Context.BeginTransaction()
