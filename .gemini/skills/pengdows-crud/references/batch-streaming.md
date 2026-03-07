# Batch Operations & Streaming

High-throughput and memory-efficient operations for large data volumes.

## Batch Operations

Multi-row statements reduce round-trips by grouping multiple entities into a single command.

| Operation | Method | Behavior |
|-----------|--------|----------|
| **INSERT** | `BatchCreateAsync` | `INSERT INTO t (cols) VALUES (...), (...), ...` |
| **UPDATE** | `BatchUpdateAsync` | Dialect-specific (e.g., PostgreSQL `UPDATE ... FROM VALUES`, SQL Server `MERGE`). |
| **UPSERT** | `BatchUpsertAsync` | Dialect-specific (e.g., MySQL `ON DUPLICATE KEY UPDATE`, PostgreSQL `ON CONFLICT`). |
| **DELETE** | `BatchDeleteAsync` | `DELETE FROM t WHERE id IN (...)` or composite key equivalent. |

**Automatic Chunking:**
Batches are automatically split into chunks based on both the database's **parameter limits** (e.g., 2,100 for SQL Server, 32k for PostgreSQL) and **row limits** per batch. **NULL values are inlined as literals** and do not consume parameters, increasing effective chunk size.

**Audit Fields:**
For batch operations, `IAuditValueResolver` is called **once per batch**, and the same values are applied to all entities in that batch for consistency and performance.

## Streaming

Iterate over large result sets with constant memory overhead using `IAsyncEnumerable<T>`.

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
2. **Wrap batches in a transaction:** For atomicity and performance (highly recommended for SQLite and SQL Server).
3. **Use cancellation tokens:** All async methods accept tokens for proper cleanup.
4. **Prefer streaming for large exports:** Use `LoadStreamAsync` or `RetrieveStreamAsync` for items > 10,000 to avoid memory pressure and materialization overhead.
5. **Dispose Tracked Readers:** When streaming, the underlying reader is a lease on the connection. Always ensure the loop completes or the stream is disposed to release the connection.
