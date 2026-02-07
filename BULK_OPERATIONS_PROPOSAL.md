# Generic Bulk Operations Proposal

## Problem

Currently pengdows.crud has:
- ✅ Bulk **READ** operations (`RetrieveAsync`, `RetrieveStreamAsync`)
- ✅ Bulk **DELETE** operations (`DeleteAsync`)
- ❌ No bulk **CREATE** operations
- ❌ No bulk **UPDATE** operations
- ❌ No bulk **UPSERT** operations

Users must loop manually, which is inefficient:
```csharp
// Current approach - inefficient
foreach (var entity in entities)
{
    await helper.CreateAsync(entity);  // N round-trips!
}
```

## Proposed Solution

Add generic bulk operation methods with multiple strategies.

### API Design

```csharp
public partial class TableGateway<TEntity, TRowID>
{
    /// <summary>
    /// Bulk insert entities with configurable strategy.
    /// </summary>
    public Task<BulkResult> CreateManyAsync(
        IEnumerable<TEntity> entities,
        BulkOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk update entities with configurable strategy.
    /// </summary>
    public Task<BulkResult> UpdateManyAsync(
        IEnumerable<TEntity> entities,
        BulkOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk upsert entities with configurable strategy.
    /// </summary>
    public Task<BulkResult> UpsertManyAsync(
        IEnumerable<TEntity> entities,
        BulkOptions? options = null,
        CancellationToken cancellationToken = default);
}

public class BulkOptions
{
    /// <summary>
    /// Strategy to use for bulk operations.
    /// </summary>
    public BulkStrategy Strategy { get; set; } = BulkStrategy.Auto;

    /// <summary>
    /// Maximum number of entities per batch (for Batched strategy).
    /// Default: 1000
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Maximum concurrent operations (for Concurrent strategy).
    /// Default: Environment.ProcessorCount * 2
    /// </summary>
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount * 2;

    /// <summary>
    /// Continue on error or fail fast.
    /// Default: false (fail fast)
    /// </summary>
    public bool ContinueOnError { get; set; } = false;

    /// <summary>
    /// Progress callback for long-running operations.
    /// </summary>
    public IProgress<BulkProgress>? Progress { get; set; }

    /// <summary>
    /// Database context to use (optional override).
    /// </summary>
    public IDatabaseContext? Context { get; set; }
}

public enum BulkStrategy
{
    /// <summary>
    /// Automatically select best strategy for the provider.
    /// - SqlServer: SqlBulkCopy for CREATE, batched SQL for UPDATE
    /// - PostgreSQL: COPY for CREATE, batched SQL for UPDATE
    /// - Others: Batched SQL
    /// </summary>
    Auto,

    /// <summary>
    /// One operation at a time (safe, slow).
    /// </summary>
    Sequential,

    /// <summary>
    /// Batch multiple entities into single SQL statement.
    /// INSERT INTO table VALUES (row1), (row2), (row3)...
    /// </summary>
    Batched,

    /// <summary>
    /// Execute operations concurrently with throttling.
    /// Good for I/O-bound operations and high-latency connections.
    /// </summary>
    Concurrent,

    /// <summary>
    /// Use provider-specific fast path (SqlBulkCopy, COPY, etc.).
    /// Falls back to Batched if not available.
    /// </summary>
    ProviderOptimized
}

public class BulkResult
{
    /// <summary>
    /// Total number of entities processed successfully.
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// Total number of entities that failed.
    /// </summary>
    public int FailureCount { get; init; }

    /// <summary>
    /// Errors encountered (if ContinueOnError = true).
    /// </summary>
    public List<BulkError> Errors { get; init; } = new();

    /// <summary>
    /// Total time elapsed.
    /// </summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>
    /// Average operations per second.
    /// </summary>
    public double OperationsPerSecond => SuccessCount / Elapsed.TotalSeconds;
}

public class BulkError
{
    public int Index { get; init; }
    public TEntity Entity { get; init; }
    public Exception Exception { get; init; }
}

public class BulkProgress
{
    public int Processed { get; init; }
    public int Total { get; init; }
    public int Succeeded { get; init; }
    public int Failed { get; init; }
    public double PercentComplete => (double)Processed / Total * 100;
}
```

## Implementation Strategies

### 1. Sequential (Naive but Safe)

```csharp
private async Task<BulkResult> CreateManySequential(
    IEnumerable<TEntity> entities,
    BulkOptions options,
    CancellationToken ct)
{
    var sw = Stopwatch.StartNew();
    var result = new BulkResult();
    var processed = 0;
    var total = entities.TryGetNonEnumeratedCount(out var count) ? count : 0;

    foreach (var entity in entities)
    {
        try
        {
            await CreateAsync(entity, options.Context ?? _context, ct);
            result.SuccessCount++;
        }
        catch (Exception ex)
        {
            result.FailureCount++;
            result.Errors.Add(new BulkError { Index = processed, Entity = entity, Exception = ex });

            if (!options.ContinueOnError)
                throw;
        }

        processed++;
        options.Progress?.Report(new BulkProgress
        {
            Processed = processed,
            Total = total,
            Succeeded = result.SuccessCount,
            Failed = result.FailureCount
        });
    }

    result.Elapsed = sw.Elapsed;
    return result;
}
```

### 2. Batched SQL (Efficient, Cross-Provider)

```csharp
private async Task<BulkResult> CreateManyBatched(
    IEnumerable<TEntity> entities,
    BulkOptions options,
    CancellationToken ct)
{
    var sw = Stopwatch.StartNew();
    var result = new BulkResult();
    var ctx = options.Context ?? _context;
    var dialect = GetDialect(ctx);

    // Process in batches
    foreach (var batch in entities.Chunk(options.BatchSize))
    {
        try
        {
            // Build multi-row INSERT
            var sc = BuildCreateBatch(batch, ctx, dialect);
            var affected = await sc.ExecuteNonQueryAsync(ct);
            result.SuccessCount += batch.Length;
        }
        catch (Exception ex)
        {
            // On batch failure, either fail fast or try individually
            if (options.ContinueOnError)
            {
                foreach (var entity in batch)
                {
                    try
                    {
                        await CreateAsync(entity, ctx, ct);
                        result.SuccessCount++;
                    }
                    catch (Exception innerEx)
                    {
                        result.FailureCount++;
                        result.Errors.Add(new BulkError { Entity = entity, Exception = innerEx });
                    }
                }
            }
            else
            {
                throw;
            }
        }
    }

    result.Elapsed = sw.Elapsed;
    return result;
}

private ISqlContainer BuildCreateBatch(TEntity[] entities, IDatabaseContext ctx, ISqlDialect dialect)
{
    var sc = ctx.CreateSqlContainer();

    // Build multi-row VALUES clause
    // INSERT INTO table (col1, col2, col3) VALUES
    //   (v1, v2, v3),
    //   (v4, v5, v6),
    //   (v7, v8, v9);

    var insertable = GetCachedInsertableColumns();
    var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);

    sb.Append("INSERT INTO ");
    sb.Append(BuildWrappedTableName(dialect));
    sb.Append(" (");

    // Column names
    for (var i = 0; i < insertable.Count; i++)
    {
        if (i > 0) sb.Append(", ");
        sb.Append(BuildWrappedColumnName(dialect, insertable[i].Name));
    }

    sb.Append(") VALUES ");

    // Rows
    var paramIndex = 0;
    for (var rowIdx = 0; rowIdx < entities.Length; rowIdx++)
    {
        if (rowIdx > 0) sb.Append(", ");
        sb.Append('(');

        for (var colIdx = 0; colIdx < insertable.Count; colIdx++)
        {
            if (colIdx > 0) sb.Append(", ");

            var paramName = $"p{paramIndex++}";
            sb.Append(dialect.MakeParameterName(paramName));

            var value = insertable[colIdx].MakeParameterValueFromField(entities[rowIdx]);
            sc.AddParameterWithValue(paramName, insertable[colIdx].DbType, value);
        }

        sb.Append(')');
    }

    sc.Query.Append(sb.ToString());
    return sc;
}
```

### 3. Concurrent with Throttling

```csharp
private async Task<BulkResult> CreateManyConcurrent(
    IEnumerable<TEntity> entities,
    BulkOptions options,
    CancellationToken ct)
{
    var sw = Stopwatch.StartNew();
    var result = new BulkResult();
    var ctx = options.Context ?? _context;

    using var semaphore = new SemaphoreSlim(options.MaxConcurrency);
    var tasks = new List<Task>();
    var processedCount = 0;
    var totalCount = entities.TryGetNonEnumeratedCount(out var count) ? count : 0;

    foreach (var entity in entities)
    {
        await semaphore.WaitAsync(ct);

        var task = Task.Run(async () =>
        {
            try
            {
                await CreateAsync(entity, ctx, ct);
                Interlocked.Increment(ref result.SuccessCount);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref result.FailureCount);
                lock (result.Errors)
                {
                    result.Errors.Add(new BulkError { Entity = entity, Exception = ex });
                }

                if (!options.ContinueOnError)
                    throw;
            }
            finally
            {
                semaphore.Release();

                var processed = Interlocked.Increment(ref processedCount);
                options.Progress?.Report(new BulkProgress
                {
                    Processed = processed,
                    Total = totalCount,
                    Succeeded = result.SuccessCount,
                    Failed = result.FailureCount
                });
            }
        }, ct);

        tasks.Add(task);
    }

    await Task.WhenAll(tasks);

    result.Elapsed = sw.Elapsed;
    return result;
}
```

### 4. Provider-Optimized (SqlBulkCopy, etc.)

```csharp
private async Task<BulkResult> CreateManyProviderOptimized(
    IEnumerable<TEntity> entities,
    BulkOptions options,
    CancellationToken ct)
{
    var ctx = options.Context ?? _context;
    var product = ctx.Product;

    // Dispatch to provider-specific implementation
    return product switch
    {
        SupportedDatabase.SqlServer => await CreateManyUsingSqlBulkCopy(entities, options, ct),
        SupportedDatabase.PostgreSql => await CreateManyUsingCopy(entities, options, ct),
        _ => await CreateManyBatched(entities, options, ct) // Fallback
    };
}
```

## Benefits

1. **Performance**: 10-100x faster than naive loops
2. **Flexibility**: Choose strategy based on use case
3. **Cross-provider**: Works on all databases
4. **Error handling**: Continue on error or fail fast
5. **Progress tracking**: Monitor long-running operations
6. **Backward compatible**: Existing single-entity methods unchanged

## Usage Examples

```csharp
// Simple bulk insert
var entities = GenerateTestData(10000);
var result = await helper.CreateManyAsync(entities);
Console.WriteLine($"Inserted {result.SuccessCount} rows in {result.Elapsed.TotalSeconds:F2}s");

// With custom options
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    Strategy = BulkStrategy.Batched,
    BatchSize = 500,
    ContinueOnError = true,
    Progress = new Progress<BulkProgress>(p =>
        Console.WriteLine($"Progress: {p.PercentComplete:F1}%"))
});

// Concurrent for high-latency connections
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    Strategy = BulkStrategy.Concurrent,
    MaxConcurrency = 10
});
```

## Implementation Plan

1. **Phase 1**: Add `CreateManyAsync` with Sequential and Batched strategies
2. **Phase 2**: Add `UpdateManyAsync` and `UpsertManyAsync`
3. **Phase 3**: Add Concurrent strategy
4. **Phase 4**: Add provider-specific optimizations (SqlBulkCopy, COPY)
5. **Phase 5**: Comprehensive benchmarks

## Open Questions

1. Should we support transactions spanning multiple batches?
2. How to handle audit fields (CreatedBy/On) in bulk operations?
3. Should batched UPDATE use temp tables or multi-row syntax?
4. What's the optimal default batch size?

## Related Work

- Entity Framework: `AddRange()`, `UpdateRange()`
- Dapper.Contrib: `Insert(entities)`
- BulkExtensions libraries (EFCore.BulkExtensions, etc.)

## Next Steps

Would you like me to:
1. Implement Phase 1 (CreateManyAsync with Sequential + Batched)?
2. Create comprehensive unit tests?
3. Add benchmarks comparing strategies?
