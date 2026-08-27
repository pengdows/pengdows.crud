---
name: pengdows-crud
description: Help with pengdows.crud - a high-performance, SQL-first data access framework and coordinated execution architecture for .NET 8+. Use when implementing CRUD operations, entity mapping, database connections, transactions, or testing with fakeDb and testbed. Covers DatabaseContext, TableGateway, PrimaryKeyTableGateway, SqlContainer, attributes ([Table], [Column], [Id], [PrimaryKey], [Version], audit fields), PoolGovernor, DbMode, and multi-database support.
allowed-tools: Read, Grep, Glob, Bash
---

# pengdows.crud Development Guide

`pengdows.crud` is an opinionated, high-performance, SQL-first data access framework for .NET 8+. It is built on a **database-first** philosophy, treating the database schema as a primary, expertly-designed artifact.

No LINQ, no tracking, no surprises — explicit SQL control with coordinated database-agnostic features.

---

## Core Thesis: Coordinated Execution Architecture

`pengdows.crud` is not a disconnected collection of convenience helpers; it is an **integrated execution architecture**. 

`DatabaseContext` is a **singleton execution authority** bound to a specific provider and connection string. Because `DatabaseContext` owns the execution lifecycle end-to-end, it coordinates:
- Connection lifecycle ("open late, close early" ephemeral pooling)
- Admission control & fairness (`PoolGovernor` read/write slot budgets and turnstile scheduling)
- Dialect capability abstraction (portable MERGE/upsert, output parameter vs. RETURNING clause identity handling)
- Transaction connection pinning and savepoint management
- ANSI session setting normalization
- Dialect fingerprint caching across multi-tenant environments
- End-to-end telemetry and metric attribution

---

## Do NOT Map to Conventional Meanings (Guardrails)

| Name / Concept | What it is NOT in pengdows.crud | What it ACTUALLY is in pengdows.crud |
|---|---|---|
| `DatabaseContext` | NOT Entity Framework's `DbContext` (not a scoped unit of work, no entity tracking). | **Singleton execution coordinator** bound to a connection string. |
| `KeepAlive` mode | NOT RDS Proxy / network connection pool keep-alive. | `Standard` mode + **1 idle sentinel connection** kept open solely to prevent unload for databases that behave like SQL Server LocalDB (not SQLite/DuckDB — those coerce to SingleWriter instead). Never used for queries. |
| `SingleWriter` mode | NOT a persistent single writer connection. | **Ephemeral pooled connections** where write admission is serialized via `PoolGovernor` (`MaxConcurrentWrites = 1`) with a writer-preference turnstile. Readers remain concurrent and ephemeral. |
| `[Id]` vs `[PrimaryKey]` | NOT interchangeable. | `[Id]` is a single surrogate pseudokey for row-id operations. `[PrimaryKey]` is a natural/business key (can be composite). **Mutually exclusive on any property.** |
| `TransactionContext` | NOT a generic "unit of work". | An **operation-scoped transaction container** that pins a dedicated connection for its lifetime. |
| `TransactionScope` | NOT supported (MSDTC escalation hazard). | **Strictly forbidden.** Always use `await context.BeginTransactionAsync()`. |
| Audit Fields | NOT null for `[LastUpdatedOn]` on create. | **BOTH `CreatedBy/On` AND `LastUpdatedBy/On` are set on CREATE** (UTC). Automatically snapshot and restored if a write fails before acceptance. |

---

## The Testing & Learning Architecture

`pengdows.crud` evolves by **turning discovered engine failure modes into executable invariants**:
- **`pengdows.crud.fakeDb` (Lifecycle Laboratory)**: A complete ADO.NET provider designed to test state transitions, slot contention, transaction rollbacks, cancellation races, and disposal leases without network I/O.
- **`testbed/` (Multi-Engine Conformance)**: 13+ real database containers managed via Testcontainers (`SqlServer`, `PostgreSql`, `MySQL`, `MariaDB`, `Oracle`, `Firebird`, `DuckDb`, `Sqlite`, `CockroachDB`, `YugabyteDB`, `TiDB`, `Snowflake`, `Db2`).
- **Defect Absorption**: When real engines expose edge cases (e.g. version banner parsing, MERGE RHS alias qualifications, Firebird MATCHING syntax), generic dialect capabilities are added to `ISqlDialect` and permanently locked down with regression tests.
- **Validation Harness & Coverage Ratchets**: BenchmarkDotNet release gates actively validate query plans (`SHOWPLAN`/`STATISTICS XML`) against real indexes. Minimum 83% CI line coverage (targeting 95%).

---

## Quick Start

```csharp
// 1. Define entity with attributes
[Table("orders")]
public class Order
{
    [Id(false)]  // DB-generated surrogate key (false = omit from INSERT)
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [PrimaryKey(1)]  // Business natural key
    [Column("order_number", DbType.String, 50)]
    public string OrderNumber { get; set; } = string.Empty;

    [Column("customer_id", DbType.Int64)]
    public long CustomerId { get; set; }

    [Column("total", DbType.Decimal)]
    public decimal Total { get; set; }

    [Version]
    [Column("version", DbType.Int32)]
    public int Version { get; set; }

    [CreatedOn]
    [Column("created_at", DbType.DateTime)]
    public DateTime CreatedAt { get; set; }

    [LastUpdatedOn]
    [Column("updated_at", DbType.DateTime)]
    public DateTime UpdatedAt { get; set; }
}

// 2. Extend TableGateway with custom queries
public interface IOrderGateway : ITableGateway<Order, long>
{
    ValueTask<Order?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct = default);
    ValueTask<List<Order>> GetCustomerOrdersAsync(long customerId, DateTime? since = null, CancellationToken ct = default);
}

public class OrderGateway : TableGateway<Order, long>, IOrderGateway
{
    public OrderGateway(IDatabaseContext context, IAuditValueResolver resolver) 
        : base(context, resolver)
    {
    }

    public async ValueTask<Order?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct = default)
    {
        var lookup = new Order { OrderNumber = orderNumber };
        return await RetrieveOneAsync(lookup, cancellationToken: ct);
    }

    public async ValueTask<List<Order>> GetCustomerOrdersAsync(long customerId, DateTime? since = null, CancellationToken ct = default)
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

        return await LoadListAsync(sc, ct);
    }
}

// 3. Register in DI as singletons
services.AddSingleton<IDatabaseContext>(sp =>
    new DatabaseContext(connectionString, SqlClientFactory.Instance));

services.AddSingleton<IAuditValueResolver, AppAuditValueResolver>();

services.AddSingleton<IOrderGateway>(sp =>
    new OrderGateway(
        sp.GetRequiredService<IDatabaseContext>(),
        sp.GetRequiredService<IAuditValueResolver>()));
```

---

## SQL Building: The Three-Tier API

### Tier 1: Build Methods (SQL generation only, no DB I/O)
`Build*` methods return an `ISqlContainer` holding generated SQL and parameters without executing it:

```csharp
ISqlContainer BuildCreate(entity);
ISqlContainer BuildBaseRetrieve("alias");   // SELECT with no WHERE — starting point for custom queries
ISqlContainer BuildRetrieve(ids, "alias");  // SELECT ... WHERE id IN (...)
ISqlContainer BuildRetrieve(entities, "a"); // SELECT ... WHERE pk columns match
ISqlContainer BuildDelete(id);              // DELETE ... WHERE id = @id
ISqlContainer BuildUpsert(entity);          // Dialect-specific UPSERT
ValueTask<ISqlContainer> sc = await BuildUpdateAsync(entity); // UPDATE statement (only async Build method)

// Batch Build methods
IReadOnlyList<ISqlContainer> BuildBatchCreate(entities);
IReadOnlyList<ISqlContainer> BuildBatchUpdate(entities);
IReadOnlyList<ISqlContainer> BuildBatchUpsert(entities);
IReadOnlyList<ISqlContainer> BuildBatchDelete(ids);
```

### Tier 2: Load Methods (Execute a pre-built container)
`Load*` methods execute an `ISqlContainer` and map rows to entities:

```csharp
ValueTask<TEntity?> result = await LoadSingleAsync(container);
ValueTask<List<TEntity>> list = await LoadListAsync(container);
IAsyncEnumerable<TEntity> stream = LoadStreamAsync(container); // Memory-efficient streaming
```

### Tier 3: Convenience Methods (Build + Execute)
```csharp
// Single-entity operations
ValueTask<bool> created = await CreateAsync(entity);
ValueTask<int> affected = await UpdateAsync(entity);
ValueTask<int> affected = await DeleteAsync(id);
ValueTask<int> affected = await UpsertAsync(entity);
ValueTask<TEntity?> order = await RetrieveOneAsync(id);           // By [Id]
ValueTask<TEntity?> order = await RetrieveOneAsync(entityLookup); // By [PrimaryKey]

// Multi-entity operations
ValueTask<List<TEntity>> orders = await RetrieveAsync(ids);
IAsyncEnumerable<TEntity> stream = RetrieveStreamAsync(ids);

// Batch operations (chunked by MaxParameterLimit)
ValueTask<int> affected = await BatchCreateAsync(entities);
ValueTask<int> affected = await BatchUpdateAsync(entities);
ValueTask<int> affected = await BatchUpsertAsync(entities);
ValueTask<int> affected = await BatchDeleteAsync(ids);
```

---

## PrimaryKeyTableGateway (Entities with No Surrogate `[Id]`)

For junction tables or business entities keyed solely by composite or natural `[PrimaryKey]` columns:

```csharp
[Table("order_items")]
public class OrderItem
{
    [PrimaryKey(1)]
    [Column("order_id", DbType.Int64)]
    public long OrderId { get; set; }

    [PrimaryKey(2)]
    [Column("product_id", DbType.Int64)]
    public long ProductId { get; set; }

    [Column("quantity", DbType.Int32)]
    public int Quantity { get; set; }
}

public class OrderItemGateway : PrimaryKeyTableGateway<OrderItem>
{
    public OrderItemGateway(IDatabaseContext context) : base(context) { }
}
```

---

## Connection Management & DbMode

| Mode | Value | Production Use Case | Connection Lifecycle |
|---|---|---|---|
| `Standard` | 0 | **Default for production servers** (PostgreSQL, SQL Server, MySQL, Oracle) | Ephemeral pooled connection per operation. |
| `KeepAlive` | 1 | Embedded DBs needing sentinel connection (LocalDB only — SQLite/DuckDB coerce KeepAlive to SingleWriter instead) | Standard lifecycle + 1 idle sentinel connection to prevent engine unload. |
| `SingleWriter` | 2 | File-based SQLite / DuckDB | Ephemeral connections with governor-serialized write admission (`MaxConcurrentWrites = 1`) + writer-starvation turnstile. Concurrent ephemeral readers. |
| `SingleConnection` | 4 | In-memory `:memory:` databases | All operations funneled through 1 persistent connection locked with `RealAsyncLocker`. |
| `Best` | 15 | **Auto-selection heuristic** | Automatically selects safest mode (e.g., `:memory:` $\to$ `SingleConnection`, file SQLite $\to$ `SingleWriter`, LocalDB $\to$ `KeepAlive`, others $\to$ `Standard`). |

---

## Transactions

Transactions are **operation-scoped containers owning a pinned connection**:

```csharp
await using var tx = await context.BeginTransactionAsync(IsolationProfile.SafeNonBlockingReads);

await gateway.CreateAsync(order, tx);
await tx.SavepointAsync("sp1");

try
{
    await gateway.UpdateAsync(orderDetail, tx);
}
catch
{
    await tx.RollbackToSavepointAsync("sp1");
}

await tx.CommitAsync();
```

---

## DI Lifetime Invariants

1. `DatabaseContext` is **SINGLETON** (one per connection string).
2. `TableGateway<T, TId>` / `PrimaryKeyTableGateway<T>` is **SINGLETON** (stateless, caches compiled setters/mappers).
3. `IAuditValueResolver` is **SINGLETON** (must be thread-safe, e.g. using `IHttpContextAccessor`).
4. `ITenantContextRegistry` is **SINGLETON** (manages context-per-tenant).
5. `ITrackedReader` is a **LEASE** (pins connection/governor permit until disposed).
6. **Execution methods return `ValueTask`** (for zero/low allocation execution paths).

---

## Canonical Architecture References

- [dal-taxonomy-and-comparison.md](file:///home/alaricd/prj/pengdows/pengdows.crud/docs/positioning/dal-taxonomy-and-comparison.md) — 2D architectural taxonomy and head-to-head comparison with EF Core, Dapper, Hibernate, jOOQ, sqlx, and other DALs.
- [architecture.md](file:///home/alaricd/prj/pengdows/pengdows.crud/docs/architecture.md) — Two-level locking, lifecycle, lease model, and concurrency contracts.
- [product-thesis.md](file:///home/alaricd/prj/pengdows/pengdows.crud/docs/positioning/product-thesis.md) — The 10 foundational principles and emergent capabilities.
- [connection-modes.md](file:///home/alaricd/prj/pengdows/pengdows.crud/docs/connection/connection-modes.md) — Exhaustive DbMode specifications and pool governor mechanics.
- [core-invariants.md](file:///home/alaricd/prj/pengdows/pengdows.crud/docs/core-invariants.md) — Complete checklist of non-negotiable architectural rules.

