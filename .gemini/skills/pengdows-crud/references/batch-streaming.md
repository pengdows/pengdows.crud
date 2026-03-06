# Batch Operations & Streaming

High-throughput and memory-efficient operations for large data volumes.

## Batch Operations

Multi-row statements reduce round-trips by grouping multiple entities into a single command.

| Operation | Method | Behavior |
|-----------|--------|----------|
| **INSERT** | `BatchCreateAsync` | `INSERT INTO t (cols) VALUES (...), (...), ...` |
| **UPDATE** | `BatchUpdateAsync` | Dialect-specific (e.g., PostgreSQL `UPDATE ... FROM VALUES`). |
| **UPSERT** | `BatchUpsertAsync` | Dialect-specific (e.g., MySQL `ON DUPLICATE KEY UPDATE`). |
| **DELETE** | `BatchDeleteAsync` | `DELETE FROM t WHERE id IN (...)` |

**Automatic Chunking:**
Batches are automatically split into chunks based on the database's parameter limits (e.g., 2,100 for SQL Server, 32k for PostgreSQL). **NULL values are inlined as literals** and do not consume parameters, increasing effective chunk size.

## Streaming

Iterate over large result sets with constant memory overhead.

```csharp
// 1. Process custom SQL results
var sc = BuildBaseRetrieve("o");
sc.Query.Append(" WHERE o.status = 'Pending'");
await foreach (var order in gateway.LoadStreamAsync(sc))
{
    // Process one order at a time
}

// 2. Stream by ID list
var ids = new[] { 1L, 2L, 3L, ... };
await foreach (var order in gateway.RetrieveStreamAsync(ids))
{
    // Process one item at a time
}
```

## Best Practices

1. **Let the library chunk for you:** Pass the full list to batch methods; don't slice it manually.
2. **Wrap batches in a transaction:** For atomicity and performance (especially in SQLite).
3. **Use cancellation tokens:** All async methods accept tokens for proper cleanup.
4. **Prefer streaming for large exports:** Use `LoadStreamAsync` for items > 10,000 to avoid memory pressure.
