# Batch Operations

## Table of Contents

1. [Overview](#overview)
2. [Quick Start](#quick-start)
3. [API Reference](#api-reference)
4. [How Chunking Works](#how-chunking-works)
5. [Database-Specific Behavior](#database-specific-behavior)
6. [Transactions](#transactions)
7. [Audit Fields](#audit-fields)
8. [Best Practices](#best-practices)
9. [FAQ](#faq)

---

## Overview

Batch operations generate multi-row SQL statements to reduce round-trips when writing multiple
entities. Instead of N individual `INSERT`/`UPDATE`/`DELETE` statements, the library groups
rows into chunks and sends each chunk as a single statement.

Chunking is automatic and parameter-limit-aware — the batch is split at whatever size keeps
the parameter count within the database's hard limit for the statement type.

### What's implemented

| Operation | Execute method | Build-only method |
|-----------|---------------|-------------------|
| Batch INSERT | `BatchCreateAsync` | `BuildBatchCreate` |
| Batch UPDATE | `BatchUpdateAsync` | `BuildBatchUpdate` |
| Batch UPSERT | `BatchUpsertAsync` | `BuildBatchUpsert` |
| Batch DELETE (by ID) | `BatchDeleteAsync` | `BuildBatchDelete` |

Convenience overloads that accept `IReadOnlyList<TEntity>` or `IEnumerable<TRowID>` and
delegate to the batch methods above:

```csharp
CreateAsync(IReadOnlyList<TEntity>)    // → BatchCreateAsync
UpdateAsync(IReadOnlyList<TEntity>)    // → BatchUpdateAsync
UpsertAsync(IReadOnlyList<TEntity>)    // → BatchUpsertAsync
DeleteAsync(IEnumerable<TRowID>)       // → BatchDeleteAsync
```

---

## Quick Start

```csharp
var customers = new List<Customer>
{
    new() { Name = "Acme", Email = "acme@example.com" },
    new() { Name = "Beta", Email = "beta@example.com" },
    new() { Name = "Gamma", Email = "gamma@example.com" },
};

// Insert — multi-row VALUES, auto-chunked to stay within parameter limits
int rowsInserted = await gateway.BatchCreateAsync(customers);

// Update — dialect-specific batch UPDATE, falls back to one per row where unsupported
int rowsUpdated = await gateway.BatchUpdateAsync(customers);

// Upsert — dialect-specific multi-row upsert
int rowsAffected = await gateway.BatchUpsertAsync(customers);

// Delete by ID list
int rowsDeleted = await gateway.BatchDeleteAsync(new[] { 1L, 2L, 3L });
```

All methods return `Task<int>` — the total number of rows affected across all chunks.

### Using the convenience overloads

The single-entity overloads of `CreateAsync`, `UpdateAsync`, `UpsertAsync`, and `DeleteAsync`
accept a list and dispatch to the batch path automatically:

```csharp
int affected = await gateway.CreateAsync(customers);
int affected = await gateway.UpdateAsync(customers);
int affected = await gateway.UpsertAsync(customers);
int affected = await gateway.DeleteAsync(ids);
```

### Building without executing

The `Build*` methods return `IReadOnlyList<ISqlContainer>` — one container per chunk — without
sending anything to the database. Use this when you need to inspect the SQL, add custom clauses,
or execute chunks inside a manually managed transaction.

```csharp
IReadOnlyList<ISqlContainer> chunks = gateway.BuildBatchCreate(customers);

foreach (var chunk in chunks)
{
    Console.WriteLine(chunk.Query.ToString()); // inspect
    await chunk.ExecuteNonQueryAsync();
}
```

---

## API Reference

### `BatchCreateAsync`

```csharp
Task<int> BatchCreateAsync(
    IReadOnlyList<TEntity> entities,
    IDatabaseContext? context = null,
    CancellationToken cancellationToken = default)
```

Generates `INSERT INTO t (cols) VALUES (...), (...), ...` per chunk. Oracle uses
`INSERT ALL INTO t (...) VALUES (...) INTO t (...) VALUES (...) SELECT 1 FROM DUAL`.

Returns the total rows affected across all chunks. Returns `0` for an empty list without
touching the database.

Single-entity fast path: a list of one entity is dispatched to `CreateAsync(entity)`.

---

### `BatchUpdateAsync`

```csharp
Task<int> BatchUpdateAsync(
    IReadOnlyList<TEntity> entities,
    IDatabaseContext? context = null,
    CancellationToken cancellationToken = default)
```

Generates a dialect-specific batch UPDATE statement per chunk:

- **PostgreSQL**: `UPDATE t SET … FROM (VALUES (…), (…)) AS src(…) WHERE t.key = src.key`
- **SQL Server**: `MERGE t USING (VALUES (…), (…)) AS src(…) ON … WHEN MATCHED THEN UPDATE …`
- **All others**: One `UPDATE` statement per entity (no native batch UPDATE syntax)

Returns the total rows affected. An entity whose version column mismatches contributes 0 to
that count — check whether `rowsUpdated < entities.Count` to detect concurrency conflicts.

---

### `BatchUpsertAsync`

```csharp
Task<int> BatchUpsertAsync(
    IReadOnlyList<TEntity> entities,
    IDatabaseContext? context = null,
    CancellationToken cancellationToken = default)
```

Generates a dialect-specific multi-row upsert per chunk:

- **PostgreSQL / CockroachDB / SQLite / DuckDB**: `INSERT … ON CONFLICT (key) DO UPDATE SET …`
- **MySQL / MariaDB**: `INSERT … ON DUPLICATE KEY UPDATE …`
- **SQL Server / Oracle / Firebird**: Falls back to one `BuildUpsert` per entity (MERGE is not
  easily batched across all providers)

Requires either `[PrimaryKey]` columns or a writable `[Id]` column to determine the conflict
key. Throws `NotSupportedException` if neither is present.

---

### `BatchDeleteAsync`

```csharp
Task<int> BatchDeleteAsync(
    IEnumerable<TRowID> ids,
    IDatabaseContext? context = null,
    CancellationToken cancellationToken = default)

Task<int> BatchDeleteAsync(
    IReadOnlyCollection<TEntity> entities,
    IDatabaseContext? context = null,
    CancellationToken cancellationToken = default)
```

Generates `DELETE FROM t WHERE id IN (…)` per chunk. The IN-list is split so that
the number of parameters stays within the dialect's limit.

---

### `BuildBatchCreate` / `BuildBatchUpdate` / `BuildBatchUpsert` / `BuildBatchDelete`

Each `Build*` method has the same signature pattern as its `Async` counterpart but returns
`IReadOnlyList<ISqlContainer>` instead of executing. Empty input returns an empty list.

---

## How Chunking Works

All batch methods call an internal `ChunkList` helper:

```
usable = floor(MaxParameterLimit × 0.9)   // 10% headroom
rowsPerChunk = floor(usable / paramsPerRow)
rowsPerChunk = min(rowsPerChunk, MaxRowsPerBatch)
```

Where:
- `MaxParameterLimit` — from `IDatabaseContext.MaxParameterLimit`, set per dialect
- `paramsPerRow` — number of bound columns per entity for that operation
- `MaxRowsPerBatch` — dialect-specific row ceiling (e.g. Oracle INSERT ALL)

**NULL values do not consume a parameter** — they are inlined as `NULL` literals, which can
significantly increase effective chunk size for sparse entities.

### Parameter limits by database

| Database | `MaxParameterLimit` | Notes |
|----------|---------------------|-------|
| SQL Server | 2,100 | Hard limit; exceeding it throws |
| PostgreSQL | 32,767 | 16-bit message protocol field |
| Oracle | 65,535 | 16-bit bind variable slot index |
| MySQL / MariaDB | 65,535 | |
| SQLite | 32,766 | 999 for SQLite < 3.32 |
| DuckDB | 65,535 | |
| Firebird | 65,535 | |
| CockroachDB | 32,767 | Same protocol as PostgreSQL |
| Snowflake | 65,535 | |

---

## Database-Specific Behavior

### PostgreSQL

- **INSERT**: Standard multi-row `VALUES`
- **UPDATE**: `UPDATE … FROM (VALUES …) AS src` — full batch UPDATE
- **UPSERT**: `INSERT … ON CONFLICT (key) DO UPDATE SET …`
- Triggers fire normally on all paths

### SQL Server

- **INSERT**: Standard multi-row `VALUES`
- **UPDATE**: `MERGE … USING (VALUES …) AS src ON … WHEN MATCHED THEN UPDATE` — full batch UPDATE
- **UPSERT**: Falls back to one MERGE per entity

### Oracle

- **INSERT**: `INSERT ALL INTO t (…) VALUES (…) … SELECT 1 FROM DUAL`
- **UPDATE**: Falls back to one `UPDATE` per entity (no native equivalent)
- **UPSERT**: Falls back to one MERGE per entity
- Parameter limit: 65,535 (16-bit bind slot index); practical ceiling is usually PGA memory

### MySQL / MariaDB

- **INSERT**: Standard multi-row `VALUES`
- **UPDATE**: Falls back to one `UPDATE` per entity
- **UPSERT**: `INSERT … ON DUPLICATE KEY UPDATE …`

### SQLite

- **INSERT**: Standard multi-row `VALUES`
- **UPDATE**: Falls back to one `UPDATE` per entity
- **UPSERT**: `INSERT … ON CONFLICT (key) DO UPDATE SET …`
- Single-writer lock means concurrent callers will serialize regardless; wrapping in a
  transaction is the most effective optimization

### DuckDB

- **INSERT**: Standard multi-row `VALUES`
- **UPDATE**: Falls back to one `UPDATE` per entity
- **UPSERT**: `INSERT … ON CONFLICT (key) DO UPDATE SET …`

### CockroachDB

- Same protocol as PostgreSQL; uses ON CONFLICT for upsert
- Distributed transactions may add latency for large batches

### Firebird

- **INSERT**: Standard multi-row `VALUES` (Firebird 3.0+)
- **UPDATE**: Falls back to one `UPDATE` per entity
- **UPSERT**: Falls back to one MERGE per entity

---

## Transactions

Pass a transaction context as the `context` parameter to execute all chunks within the same
transaction. The library does not automatically wrap a batch in a transaction — that is the
caller's responsibility.

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

Using a transaction around a batch is also the most effective SQLite performance optimization,
since it avoids a fsync per statement.

---

## Audit Fields

When an entity has `[CreatedBy]` / `[CreatedOn]` / `[LastUpdatedBy]` / `[LastUpdatedOn]`
columns, the audit resolver is called **once per batch**, not once per entity. The same
resolved values are applied to all entities in the batch. This is intentional — within a
single batch operation, all rows share the same actor and timestamp.

If the entity declares user audit fields (`[CreatedBy]`, `[LastUpdatedBy]`) and no
`IAuditValueResolver` is registered, an `InvalidOperationException` is thrown at execution
time.

---

## Best Practices

**Let the library chunk for you.** Pass the full list and rely on automatic parameter-limit
splitting rather than manually slicing before calling. The chunking logic accounts for NULL
inlining and dialect row limits; manual slicing may produce sub-optimal chunk sizes.

**Wrap in a transaction for atomicity.** The default behavior — no transaction — means a
failure mid-batch leaves earlier chunks committed. If all-or-nothing is required, pass a
transaction context.

**Wrap in a transaction for SQLite performance.** Each statement without an explicit
transaction is its own implicit transaction with an fsync. A single surrounding transaction
eliminates this overhead.

**Use `BuildBatch*` for custom SQL control.** When you need to add `RETURNING` clauses,
custom `WHERE` conditions, or execute chunks with different contexts, build the containers
manually and execute them yourself.

**Check returned row count for update conflicts.** `BatchUpdateAsync` returns the total rows
actually modified. If you have `[Version]` columns, a count less than `entities.Count`
indicates one or more optimistic concurrency conflicts — re-fetch those entities and retry.

---

## FAQ

### Does batch insert return generated IDs?

No. Multi-row `VALUES` statements do not return generated IDs across all databases. If you
need the IDs assigned by the database, either:
- Use `CreateAsync(entity)` per entity (the single-entity path uses `RETURNING`/`OUTPUT`
  where supported), or
- Insert the batch then retrieve by a known unique key

### Are triggers fired?

Yes, for all current batch paths — multi-row `VALUES`, `INSERT ALL`, and `MERGE` all go
through the normal statement execution path. No path uses `COPY` or `SqlBulkCopy`, which
bypass triggers.

### Can I use batch operations with multi-tenancy?

Yes. Pass the tenant-specific `IDatabaseContext` as the `context` parameter:

```csharp
var tenantCtx = tenantRegistry.GetContext(tenantId);
int affected = await gateway.BatchCreateAsync(entities, tenantCtx);
```

### What about optimistic concurrency (version columns)?

`BatchUpdateAsync` includes the `WHERE version = @current` clause per row and increments
the version in the SET clause. If the row was modified by another process, that entity's
update affects 0 rows. The method returns the total rows affected — compare to
`entities.Count` to detect conflicts.

### Can I cancel a batch operation?

Yes. Pass a `CancellationToken`:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
int affected = await gateway.BatchCreateAsync(entities, cancellationToken: cts.Token);
```

The token is checked before each chunk is executed. A cancelled batch leaves already-executed
chunks committed unless you wrap in a transaction.

### How do I handle duplicate key violations?

Use `BatchUpsertAsync` if you want insert-or-update semantics. If you want insert-only and
need to skip or identify duplicates, there is no built-in `ContinueOnError` — that is
tracked as future work. The current options are:
- Deduplicate before calling the batch method
- Catch the database exception and handle it in the caller

---

## See Also

- [Batch Operations Architecture](BATCH_OPERATIONS_ARCHITECTURE.md)
- [Database Compatibility Matrix](BATCH_OPERATIONS_COMPATIBILITY.md)
- [Batch Operations Quick Reference](BATCH_OPERATIONS_QUICKREF.md)
- [Future Work](FUTURE_WORK.md)
- [Transactions](transactions.md)
