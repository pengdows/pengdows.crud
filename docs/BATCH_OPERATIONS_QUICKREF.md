# Batch Operations — Quick Reference

## Basic Usage

```csharp
// Insert
int affected = await gateway.BatchCreateAsync(entities);

// Update
int affected = await gateway.BatchUpdateAsync(entities);

// Upsert
int affected = await gateway.BatchUpsertAsync(entities);

// Delete by ID
int affected = await gateway.BatchDeleteAsync(ids);
```

All methods return `Task<int>` — total rows affected across all chunks.

---

## Convenience overloads (same as above)

```csharp
await gateway.CreateAsync(entities);   // → BatchCreateAsync
await gateway.UpdateAsync(entities);   // → BatchUpdateAsync
await gateway.UpsertAsync(entities);   // → BatchUpsertAsync
await gateway.DeleteAsync(ids);        // → BatchDeleteAsync
```

---

## Build without executing

```csharp
IReadOnlyList<ISqlContainer> chunks = gateway.BuildBatchCreate(entities);
IReadOnlyList<ISqlContainer> chunks = gateway.BuildBatchUpdate(entities);
IReadOnlyList<ISqlContainer> chunks = gateway.BuildBatchUpsert(entities);
IReadOnlyList<ISqlContainer> chunks = gateway.BuildBatchDelete(ids);

foreach (var chunk in chunks)
    await chunk.ExecuteNonQueryAsync();
```

---

## With transaction

```csharp
using var tx = context.BeginTransaction();
try
{
    await gateway.BatchCreateAsync(orders, tx);
    await gateway.BatchCreateAsync(orderItems, tx);
    tx.Commit();
}
catch
{
    tx.Rollback();
    throw;
}
```

---

## With cancellation

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
int affected = await gateway.BatchCreateAsync(entities, cancellationToken: cts.Token);
```

---

## With multi-tenancy

```csharp
var tenantCtx = tenantRegistry.GetContext(tenantId);
int affected = await gateway.BatchCreateAsync(entities, tenantCtx);
```

---

## Database strategy by operation

| Database | Batch INSERT | Batch UPDATE | Batch UPSERT |
|----------|-------------|--------------|--------------|
| PostgreSQL | Multi-row VALUES | UPDATE FROM VALUES | INSERT … ON CONFLICT |
| SQL Server | Multi-row VALUES | MERGE | MERGE (one per row) |
| Oracle | INSERT ALL … SELECT 1 FROM DUAL | one UPDATE per row | MERGE (one per row) |
| MySQL / MariaDB | Multi-row VALUES | one UPDATE per row | INSERT … ON DUPLICATE KEY UPDATE |
| SQLite / CockroachDB | Multi-row VALUES | one UPDATE per row | INSERT … ON CONFLICT |
| DuckDB | Multi-row VALUES | one UPDATE per row | INSERT … ON CONFLICT |
| Firebird | Multi-row VALUES | one UPDATE per row | MERGE (one per row) |

---

## Parameter limits

| Database | Limit | Notes |
|----------|-------|-------|
| SQL Server | 2,100 | Hard limit; chunks kept well below this |
| PostgreSQL | 32,767 | 16-bit protocol field |
| Oracle | 65,535 | 16-bit bind slot index |
| MySQL / MariaDB | 65,535 | |
| SQLite | 32,766 | 999 for SQLite < 3.32 |
| DuckDB / Firebird / Snowflake | 65,535 | |
| CockroachDB | 32,767 | |

Chunking uses 90% of the limit as headroom. NULL values are inlined as literals and do not
count against the parameter limit.

---

## Common errors

**Optimistic concurrency conflict on update**: `BatchUpdateAsync` returns the total rows
actually modified. If `affected < entities.Count` and you have `[Version]` columns, one or
more rows were modified by another process. Re-fetch and retry those entities.

**Upsert with no key**: `BatchUpsertAsync` requires either `[PrimaryKey]` columns or a
writable `[Id]` column. Throws `NotSupportedException` if neither is defined.

**Missing audit resolver**: If the entity has `[CreatedBy]` or `[LastUpdatedBy]` columns and
no `IAuditValueResolver` is registered, throws `InvalidOperationException` at execution time.

---

## See Full Documentation

- [Batch Operations Guide](BATCH_OPERATIONS.md)
- [Architecture and Internals](BATCH_OPERATIONS_ARCHITECTURE.md)
- [Database Compatibility Matrix](BATCH_OPERATIONS_COMPATIBILITY.md)
- [Future Work](FUTURE_WORK.md)
