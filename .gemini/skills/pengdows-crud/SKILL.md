---
name: pengdows-crud
description: SQL-first, strongly-typed data access layer for .NET. Use when implementing CRUD operations, entity mapping, database connections, transactions, batching, streaming, or testing with fakeDb.
---

# pengdows.crud Development Guide

pengdows.crud is a high-performance, SQL-first framework for .NET 8+ that provides explicit SQL control with database-agnostic features like portable upserts, batching, and intelligent connection management.

## Quick Start

```csharp
[Table("orders")]
public class Order
{
    [Id(false)] [Column("id", DbType.Int64)] public long Id { get; set; }
    [PrimaryKey(1)] [Column("order_number", DbType.String, 50)] public string OrderNumber { get; set; }
    [Column("total", DbType.Decimal)] public decimal Total { get; set; }
}

public interface IOrderGateway : ITableGateway<Order, long>
{
    Task<Order?> GetByOrderNumberAsync(string orderNumber);
}

public class OrderGateway : TableGateway<Order, long>, IOrderGateway
{
    public OrderGateway(IDatabaseContext context) : base(context) { }

    public async Task<Order?> GetByOrderNumberAsync(string orderNumber)
    {
        var lookup = new Order { OrderNumber = orderNumber };
        return await RetrieveOneAsync(lookup);
    }
}
```

## Three-Tier API

| Tier | Methods | Purpose |
|------|---------|---------|
| **1. Build** | `BuildCreate`, `BuildBaseRetrieve`, `BuildDelete`, `BuildUpsert`, `BuildUpdateAsync` | SQL generation only; no DB I/O |
| **2. Load** | `LoadSingleAsync`, `LoadListAsync`, `LoadStreamAsync` | Execute a pre-built `ISqlContainer` |
| **3. Convenience** | `CreateAsync`, `RetrieveOneAsync`, `UpdateAsync`, `DeleteAsync`, `UpsertAsync` | Build + Execute in one call |

## Batch & Streaming

- **Batch Operations:** Multi-row INSERT/UPSERT via `BatchCreateAsync` and `BatchUpsertAsync`. Auto-chunks based on database parameter limits.
- **Streaming:** Process large result sets item-by-item via `LoadStreamAsync` or `RetrieveStreamAsync` without loading all into memory.

## Core Principles

1. **SQL-First:** No LINQ or tracking. You write SQL; what you write is what executes.
2. **[Id] vs [PrimaryKey]:** `[Id]` is a surrogate/pseudo key (single column); `[PrimaryKey]` is a natural/business key (can be composite). They are mutually exclusive on a column.
3. **Open Late, Close Early:** Connections are acquired only at the moment of execution and released immediately.
4. **Testable by Design:** Use `pengdows.crud.fakeDb` for fast, isolated unit tests.

## DI Lifetime & Connection Management

- **DatabaseContext:** **Singleton** - Manages connection pool, metrics, and `DbMode`.
- **TableGateway:** **Singleton** - Stateless, caches compiled accessors.
- **DbMode.Best:** Auto-selects optimal mode (e.g., `SingleWriter` for SQLite, `Standard` for PostgreSQL).
- **Pool Governor:** Prevents pool exhaustion via semaphore-based backpressure.

## Core Invariants

1. **Transactions are operation-scoped:** Use `using var tx = context.BeginTransaction();` inside methods.
2. **ITrackedReader is a lease:** Pins connection until disposed. Long-lived readers can block writers in `SingleWriter` mode.
3. **NEVER use TransactionScope:** Incompatible with connection management; use `context.BeginTransaction()`.
4. **Audit Behavior:** Both `CreatedBy/On` AND `LastUpdatedBy/On` are set on CREATE.

## Reference Files

- **Mapping & Attributes:** See [references/entity-mapping.md](references/entity-mapping.md) for `[Id]`, `[PrimaryKey]`, audit fields, and custom types.
- **SQL & Operations:** See [references/sql-operations.md](references/sql-operations.md) for `SqlContainer`, custom SQL, and Three-Tier API details.
- **Batching & Streaming:** See [references/batch-streaming.md](references/batch-streaming.md) for high-throughput and memory-efficient operations.
- **Connections & Transactions:** See [references/connections.md](references/connections.md) for `DbMode`, `IsolationProfile`, and `ITenantContextRegistry`.
- **Testing:** See [references/testing.md](references/testing.md) for unit testing with `fakeDb` and integration testing.
- **Metrics & Observability:** See [references/metrics.md](references/metrics.md) for real-time diagnostics and observability.
