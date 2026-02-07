# Bulk Operations Architecture

## Table of Contents

1. [Overview](#overview)
2. [Design Goals](#design-goals)
3. [Architecture Diagram](#architecture-diagram)
4. [Component Breakdown](#component-breakdown)
5. [Strategy Implementation](#strategy-implementation)
6. [Dialect-Specific SQL Generation](#dialect-specific-sql-generation)
7. [Parameter Management](#parameter-management)
8. [Error Handling Pipeline](#error-handling-pipeline)
9. [Performance Considerations](#performance-considerations)
10. [Extension Points](#extension-points)

---

## Overview

The bulk operations system provides high-performance batch processing of CRUD operations through multiple execution strategies. It's designed to be:

- **Database-agnostic**: Works across all 9 supported databases
- **Strategy-based**: Multiple approaches optimized for different scenarios
- **Extensible**: Easy to add new strategies or provider-specific optimizations
- **Type-safe**: Full compile-time type checking with generics
- **Well-tested**: Comprehensive unit and integration tests

---

## Design Goals

### 1. Performance First
- 10-100x speedup over naive loops
- Minimal allocations in hot paths
- Zero-copy when possible
- Efficient SQL generation using StringBuilderLite

### 2. Flexibility
- Multiple strategies for different use cases
- Configurable behavior (batch size, concurrency, error handling)
- Progress reporting for long-running operations

### 3. Correctness
- Consistent behavior across databases
- Proper error handling and recovery
- Transaction support
- Audit field handling

### 4. Maintainability
- Clean separation of concerns
- Well-documented code
- Testable design
- Following existing pengdows.crud patterns

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                        TableGateway<TEntity, TRowID>             │
│                                                                   │
│  Public API:                                                      │
│  - CreateManyAsync(entities, options)                            │
│  - UpdateManyAsync(entities, options)                            │
│  - UpsertManyAsync(entities, options)                            │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                    BulkOperationCoordinator                      │
│                                                                   │
│  Responsibilities:                                                │
│  - Validate inputs                                                │
│  - Select strategy (if Auto)                                      │
│  - Initialize BulkResult                                          │
│  - Delegate to strategy implementation                            │
│  - Aggregate results                                              │
└────────────────────────────┬────────────────────────────────────┘
                             │
                ┌────────────┴────────────┐
                ▼                         ▼
┌─────────────────────────┐  ┌─────────────────────────┐
│  Strategy Selection     │  │  Progress Tracking      │
│                         │  │                         │
│  - Auto (heuristics)    │  │  - Batch progress       │
│  - User-specified       │  │  - Entity counts        │
│  - Fallback logic       │  │  - Error tracking       │
└───────────┬─────────────┘  └─────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Strategy Implementations                      │
└─────────────────────────────────────────────────────────────────┘
            │
    ┌───────┴────────┬───────────┬───────────────┬──────────────┐
    ▼                ▼           ▼               ▼              ▼
┌─────────┐  ┌──────────┐  ┌──────────┐  ┌────────────┐  ┌──────────┐
│Sequential│  │ Batched  │  │Concurrent│  │Provider    │  │  Auto    │
│          │  │          │  │          │  │Optimized   │  │          │
└────┬─────┘  └────┬─────┘  └────┬─────┘  └─────┬──────┘  └────┬─────┘
     │             │             │              │              │
     │             ▼             │              ▼              │
     │    ┌──────────────┐      │     ┌────────────────┐     │
     │    │ SQL Builder  │      │     │ Provider API   │     │
     │    │              │      │     │                │     │
     │    │ - Multi-row  │      │     │ - SqlBulkCopy  │     │
     │    │   VALUES     │      │     │ - COPY         │     │
     │    │ - Chunking   │      │     │ - Fallback     │     │
     │    └──────┬───────┘      │     └────────┬───────┘     │
     │           │              │              │              │
     └───────────┴──────────────┴──────────────┴──────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                     Dialect-Specific Layer                       │
│                                                                   │
│  ISqlDialect extensions:                                          │
│  - BuildBatchInsertSql()                                          │
│  - BuildBatchUpdateSql()                                          │
│  - GetMaxParametersPerBatch()                                     │
│  - SupportsProviderOptimizedBulk()                                │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                     Database Execution                           │
│                                                                   │
│  - SqlContainer                                                   │
│  - Parameter binding                                              │
│  - Command execution                                              │
│  - Result mapping                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Component Breakdown

### 1. TableGateway Additions

**Location:** `TableGateway.BulkOperations.cs` (new partial file)

```csharp
public partial class TableGateway<TEntity, TRowID>
{
    /// <summary>
    /// Bulk insert entities using specified strategy.
    /// </summary>
    public async Task<BulkResult> CreateManyAsync(
        IEnumerable<TEntity> entities,
        BulkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Input validation
        if (entities == null) throw new ArgumentNullException(nameof(entities));

        var entityList = entities as IList<TEntity> ?? entities.ToList();
        if (entityList.Count == 0) throw new ArgumentException("Entities cannot be empty", nameof(entities));

        options ??= new BulkOptions();

        // Select strategy
        var strategy = options.Strategy == BulkStrategy.Auto
            ? SelectAutoStrategy(entityList.Count, _context.Product)
            : options.Strategy;

        // Delegate to strategy implementation
        return strategy switch
        {
            BulkStrategy.Sequential => await CreateManySequential(entityList, options, cancellationToken),
            BulkStrategy.Batched => await CreateManyBatched(entityList, options, cancellationToken),
            BulkStrategy.Concurrent => await CreateManyConcurrent(entityList, options, cancellationToken),
            BulkStrategy.ProviderOptimized => await CreateManyProviderOptimized(entityList, options, cancellationToken),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };
    }

    private BulkStrategy SelectAutoStrategy(int count, SupportedDatabase product)
    {
        // Small batches: not worth overhead
        if (count <= 5) return BulkStrategy.Sequential;

        // Large batches on supported databases: use provider optimizations
        if (count > 10000)
        {
            if (product is SupportedDatabase.PostgreSql or SupportedDatabase.SqlServer or SupportedDatabase.DuckDB)
            {
                return BulkStrategy.ProviderOptimized;
            }
        }

        // Default: batched for good balance
        if (count > 10) return BulkStrategy.Batched;

        return BulkStrategy.Sequential;
    }
}
```

---

### 2. BulkOptions Class

**Location:** `BulkOptions.cs`

```csharp
/// <summary>
/// Configuration for bulk operations.
/// </summary>
public class BulkOptions
{
    public BulkStrategy Strategy { get; set; } = BulkStrategy.Auto;
    public int BatchSize { get; set; } = 1000;
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount * 2;
    public bool ContinueOnError { get; set; } = false;
    public IProgress<BulkProgress>? Progress { get; set; }
    public IDatabaseContext? Context { get; set; }

    // Internal: Adjusted batch size based on parameter limits
    internal int EffectiveBatchSize { get; set; }

    internal void AdjustBatchSizeForDialect(ISqlDialect dialect, int columnCount)
    {
        var maxParams = dialect.GetMaxParametersPerBatch();
        var maxRowsPerBatch = maxParams / columnCount;

        EffectiveBatchSize = Math.Min(BatchSize, maxRowsPerBatch);
    }
}
```

---

### 3. BulkResult Class

**Location:** `BulkResult.cs`

```csharp
/// <summary>
/// Result of a bulk operation.
/// </summary>
public class BulkResult
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<BulkError> Errors { get; init; } = new();

    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public double OperationsPerSecond => SuccessCount / Math.Max(0.001, Elapsed.TotalSeconds);

    internal void Stop() => _stopwatch.Stop();
}
```

---

## Strategy Implementation

### Sequential Strategy

**Characteristics:**
- Simplest implementation
- One operation at a time
- Predictable, safe
- Slowest performance

```csharp
private async Task<BulkResult> CreateManySequential(
    IList<TEntity> entities,
    BulkOptions options,
    CancellationToken ct)
{
    var result = new BulkResult();
    var ctx = options.Context ?? _context;
    var totalCount = entities.Count;

    for (var i = 0; i < entities.Count; i++)
    {
        try
        {
            await CreateAsync(entities[i], ctx, ct);
            result.SuccessCount++;
        }
        catch (Exception ex)
        {
            result.FailureCount++;
            result.Errors.Add(new BulkError
            {
                Index = i,
                Entity = entities[i],
                Exception = ex
            });

            if (!options.ContinueOnError)
            {
                result.Stop();
                throw;
            }
        }

        // Report progress
        options.Progress?.Report(new BulkProgress
        {
            Processed = i + 1,
            Total = totalCount,
            Succeeded = result.SuccessCount,
            Failed = result.FailureCount
        });
    }

    result.Stop();
    return result;
}
```

---

### Batched Strategy

**Characteristics:**
- Groups entities into batches
- Multi-row SQL statements
- Best balance of performance vs complexity
- Works on all databases (with dialect variations)

```csharp
private async Task<BulkResult> CreateManyBatched(
    IList<TEntity> entities,
    BulkOptions options,
    CancellationToken ct)
{
    var result = new BulkResult();
    var ctx = options.Context ?? _context;
    var dialect = GetDialect(ctx);

    // Adjust batch size for parameter limits
    var insertableColumns = GetCachedInsertableColumns();
    options.AdjustBatchSizeForDialect(dialect, insertableColumns.Count);

    var processedCount = 0;
    var totalCount = entities.Count;

    // Process in batches
    for (var offset = 0; offset < entities.Count; offset += options.EffectiveBatchSize)
    {
        var batchSize = Math.Min(options.EffectiveBatchSize, entities.Count - offset);
        var batch = new ArraySegment<TEntity>(entities.ToArray(), offset, batchSize);

        try
        {
            // Build multi-row INSERT
            var sc = BuildCreateBatch(batch, ctx, dialect, insertableColumns);
            var affected = await sc.ExecuteNonQueryAsync(ct);

            result.SuccessCount += batchSize;
            processedCount += batchSize;

            // Report progress
            options.Progress?.Report(new BulkProgress
            {
                Processed = processedCount,
                Total = totalCount,
                Succeeded = result.SuccessCount,
                Failed = result.FailureCount
            });
        }
        catch (Exception ex)
        {
            // Batch failed - try individual inserts if ContinueOnError
            if (options.ContinueOnError)
            {
                await ProcessBatchIndividually(batch, result, ctx, ct);
                processedCount += batchSize;
            }
            else
            {
                result.Stop();
                throw;
            }
        }
    }

    result.Stop();
    return result;
}

private ISqlContainer BuildCreateBatch(
    ArraySegment<TEntity> entities,
    IDatabaseContext ctx,
    ISqlDialect dialect,
    IReadOnlyList<IColumnInfo> insertableColumns)
{
    var sc = ctx.CreateSqlContainer();
    var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);

    // Use dialect-specific SQL generation
    var sql = dialect.BuildBatchInsertSql(
        BuildWrappedTableName(dialect),
        insertableColumns.Select(c => BuildWrappedColumnName(dialect, c.Name)).ToList(),
        entities.Count);

    sc.Query.Append(sql);

    // Bind parameters
    var paramIndex = 0;
    foreach (var entity in entities)
    {
        foreach (var column in insertableColumns)
        {
            var paramName = $"p{paramIndex++}";
            var value = column.MakeParameterValueFromField(entity);
            sc.AddParameterWithValue(paramName, column.DbType, value);
        }
    }

    return sc;
}

private async Task ProcessBatchIndividually(
    ArraySegment<TEntity> batch,
    BulkResult result,
    IDatabaseContext ctx,
    CancellationToken ct)
{
    foreach (var entity in batch)
    {
        try
        {
            await CreateAsync(entity, ctx, ct);
            result.SuccessCount++;
        }
        catch (Exception ex)
        {
            result.FailureCount++;
            result.Errors.Add(new BulkError { Entity = entity, Exception = ex });
        }
    }
}
```

---

### Concurrent Strategy

**Characteristics:**
- Parallel execution with throttling
- Uses SemaphoreSlim for concurrency control
- Best for I/O-bound operations
- Requires adequate connection pool size

```csharp
private async Task<BulkResult> CreateManyConcurrent(
    IList<TEntity> entities,
    BulkOptions options,
    CancellationToken ct)
{
    var result = new BulkResult();
    var ctx = options.Context ?? _context;
    var totalCount = entities.Count;

    using var semaphore = new SemaphoreSlim(options.MaxConcurrency);
    var tasks = new List<Task>(entities.Count);

    var processedCount = 0;

    for (var i = 0; i < entities.Count; i++)
    {
        var index = i;
        var entity = entities[i];

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
                    result.Errors.Add(new BulkError
                    {
                        Index = index,
                        Entity = entity,
                        Exception = ex
                    });
                }

                if (!options.ContinueOnError)
                {
                    throw;
                }
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

    result.Stop();
    return result;
}
```

---

### ProviderOptimized Strategy

**Characteristics:**
- Database-specific APIs (SqlBulkCopy, COPY, etc.)
- Fastest performance (50-100x)
- May bypass triggers
- Falls back to Batched if not available

```csharp
private async Task<BulkResult> CreateManyProviderOptimized(
    IList<TEntity> entities,
    BulkOptions options,
    CancellationToken ct)
{
    var ctx = options.Context ?? _context;
    var product = ctx.Product;

    // Dispatch to provider-specific implementation
    return product switch
    {
        SupportedDatabase.PostgreSql => await CreateManyUsingPostgresCopy(entities, options, ct),
        SupportedDatabase.SqlServer => await CreateManyUsingSqlBulkCopy(entities, options, ct),
        SupportedDatabase.DuckDB => await CreateManyUsingDuckDbCopy(entities, options, ct),
        _ => await CreateManyBatched(entities, options, ct) // Fallback
    };
}

private async Task<BulkResult> CreateManyUsingSqlBulkCopy(
    IList<TEntity> entities,
    BulkOptions options,
    CancellationToken ct)
{
    var result = new BulkResult();
    var ctx = options.Context ?? _context;

    // Get connection (respecting transactions)
    var connection = await ctx.GetConnectionAsync(ExecutionType.Write, ct);

    try
    {
        using var bulkCopy = new SqlBulkCopy((SqlConnection)connection.UnderlyingConnection)
        {
            DestinationTableName = BuildWrappedTableName(GetDialect(ctx)),
            BatchSize = options.BatchSize,
            BulkCopyTimeout = 0 // No timeout
        };

        // Map columns
        var insertableColumns = GetCachedInsertableColumns();
        foreach (var column in insertableColumns)
        {
            bulkCopy.ColumnMappings.Add(column.Name, column.Name);
        }

        // Convert entities to DataTable
        var dataTable = ConvertToDataTable(entities, insertableColumns);

        // Execute bulk copy
        await bulkCopy.WriteToServerAsync(dataTable, ct);

        result.SuccessCount = entities.Count;
        options.Progress?.Report(new BulkProgress
        {
            Processed = entities.Count,
            Total = entities.Count,
            Succeeded = entities.Count,
            Failed = 0
        });
    }
    catch (Exception ex)
    {
        // On failure, try fallback if ContinueOnError
        if (options.ContinueOnError)
        {
            return await CreateManyBatched(entities, options, ct);
        }
        throw;
    }
    finally
    {
        await ctx.CloseAndDisposeConnectionAsync(connection);
    }

    result.Stop();
    return result;
}

private DataTable ConvertToDataTable(
    IList<TEntity> entities,
    IReadOnlyList<IColumnInfo> columns)
{
    var table = new DataTable();

    // Add columns
    foreach (var column in columns)
    {
        var dataType = column.PropertyInfo.PropertyType;
        dataType = Nullable.GetUnderlyingType(dataType) ?? dataType;
        table.Columns.Add(column.Name, dataType);
    }

    // Add rows
    foreach (var entity in entities)
    {
        var row = table.NewRow();
        foreach (var column in columns)
        {
            var value = column.PropertyInfo.GetValue(entity);
            row[column.Name] = value ?? DBNull.Value;
        }
        table.Rows.Add(row);
    }

    return table;
}
```

---

## Dialect-Specific SQL Generation

### ISqlDialect Extensions

Add new methods to ISqlDialect interface:

```csharp
public interface ISqlDialect
{
    // Existing methods...

    /// <summary>
    /// Whether this dialect supports batched INSERT with multi-row VALUES.
    /// </summary>
    bool SupportsBatchInsert { get; }

    /// <summary>
    /// Maximum number of parameters per query (database-specific).
    /// </summary>
    int MaxParametersPerBatch { get; }

    /// <summary>
    /// Build multi-row INSERT SQL for this dialect.
    /// </summary>
    string BuildBatchInsertSql(
        string tableName,
        IReadOnlyList<string> columnNames,
        int rowCount);

    /// <summary>
    /// Whether this dialect has provider-optimized bulk insert.
    /// </summary>
    bool SupportsProviderOptimizedBulkInsert { get; }
}
```

---

### Standard Implementation (PostgreSQL, SQL Server, MySQL, etc.)

```csharp
public override string BuildBatchInsertSql(
    string tableName,
    IReadOnlyList<string> columnNames,
    int rowCount)
{
    var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);

    sb.Append("INSERT INTO ");
    sb.Append(tableName);
    sb.Append(" (");

    // Column names
    for (var i = 0; i < columnNames.Count; i++)
    {
        if (i > 0) sb.Append(", ");
        sb.Append(columnNames[i]);
    }

    sb.Append(") VALUES ");

    // Rows
    var paramIndex = 0;
    for (var row = 0; row < rowCount; row++)
    {
        if (row > 0) sb.Append(", ");
        sb.Append('(');

        for (var col = 0; col < columnNames.Count; col++)
        {
            if (col > 0) sb.Append(", ");
            sb.Append(MakeParameterName($"p{paramIndex++}"));
        }

        sb.Append(')');
    }

    return sb.ToString();
}
```

---

### Oracle-Specific Implementation

```csharp
public override string BuildBatchInsertSql(
    string tableName,
    IReadOnlyList<string> columnNames,
    int rowCount)
{
    var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);

    sb.Append("INSERT ALL ");

    var paramIndex = 0;
    for (var row = 0; row < rowCount; row++)
    {
        sb.Append("INTO ");
        sb.Append(tableName);
        sb.Append(" (");

        for (var i = 0; i < columnNames.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(columnNames[i]);
        }

        sb.Append(") VALUES (");

        for (var col = 0; col < columnNames.Count; col++)
        {
            if (col > 0) sb.Append(", ");
            sb.Append(MakeParameterName($"p{paramIndex++}"));
        }

        sb.Append(") ");
    }

    sb.Append("SELECT 1 FROM DUAL");

    return sb.ToString();
}
```

---

## Parameter Management

### Parameter Limit Handling

Different databases have different parameter limits:

| Database | Parameter Limit | Strategy |
|----------|----------------|----------|
| SQL Server | 2,100 | Adjust batch size |
| PostgreSQL | ~32,000 | Rarely hit |
| Oracle | ~32,000 | Adjust batch size |
| MySQL | ~65,000 | Rarely hit |
| SQLite | 999 | Small batches (500) |

**Implementation:**

```csharp
public int MaxParametersPerBatch => this.DatabaseType switch
{
    SupportedDatabase.SqlServer => 2100,
    SupportedDatabase.Oracle => 32000,
    SupportedDatabase.PostgreSql => 32000,
    SupportedDatabase.MySql => 65000,
    SupportedDatabase.MariaDb => 65000,
    SupportedDatabase.Sqlite => 999,
    SupportedDatabase.DuckDB => 999,
    SupportedDatabase.Firebird => 1000,
    SupportedDatabase.CockroachDb => 32000,
    _ => 1000 // Conservative default
};

private void AdjustBatchSizeForDialect(ISqlDialect dialect, int columnCount)
{
    var maxParams = dialect.MaxParametersPerBatch;
    var maxRowsPerBatch = maxParams / columnCount;

    // Leave some headroom for other parameters
    maxRowsPerBatch = (int)(maxRowsPerBatch * 0.9);

    options.EffectiveBatchSize = Math.Min(options.BatchSize, maxRowsPerBatch);
}
```

---

## Error Handling Pipeline

### Error Categories

1. **Validation Errors** (before execution)
   - Null entities
   - Empty collection
   - Invalid configuration

2. **Batch Errors** (during execution)
   - SQL syntax errors
   - Constraint violations
   - Network errors

3. **Individual Errors** (per-entity)
   - Data type mismatches
   - Duplicate keys
   - Foreign key violations

### Error Recovery Strategy

```
┌─────────────────────────┐
│   Execute Batch         │
└───────────┬─────────────┘
            │
    ┌───────▼────────┐
    │ Batch Success? │
    └───────┬────────┘
            │
    ┌───────▼────────┐           ┌─────────────────┐
    │  Yes: Record   │           │  No: Error      │
    │  Success Count │           │  Encountered    │
    └───────┬────────┘           └────────┬────────┘
            │                             │
            │                    ┌────────▼────────┐
            │                    │ ContinueOnError?│
            │                    └────────┬────────┘
            │                             │
            │                    ┌────────▼────────┐
            │                    │  Yes: Retry     │
            │                    │  Individually   │
            │                    └────────┬────────┘
            │                             │
            │                    ┌────────▼────────┐
            │                    │ Record Errors   │
            │                    │ Continue Loop   │
            │                    └─────────────────┘
            │
            └─────────────────────────────────────────────┐
                                                          │
                                                   ┌──────▼──────┐
                                                   │  Complete   │
                                                   │  Return     │
                                                   │  BulkResult │
                                                   └─────────────┘
```

---

## Performance Considerations

### Memory Allocation

**Hot Path Optimizations:**

1. **StringBuilderLite** for SQL generation
   ```csharp
   var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
   ```

2. **ArraySegment** to avoid copying
   ```csharp
   var batch = new ArraySegment<TEntity>(entities.ToArray(), offset, batchSize);
   ```

3. **Object pooling** for frequently allocated objects
   ```csharp
   private static readonly ObjectPool<List<DbParameter>> ParameterListPool = ...;
   ```

4. **Span<T>** for zero-copy operations
   ```csharp
   ReadOnlySpan<char> columnName = ...;
   ```

---

### Connection Pool Management

**Concurrent Strategy Considerations:**

```csharp
// Ensure pool size accommodates concurrency
var poolSize = options.MaxConcurrency * 2; // Safety margin

// Connection string
var connectionString = $"...;Max Pool Size={poolSize}";

// Monitor pool exhaustion
if (context.NumberOfOpenConnections > poolSize * 0.8)
{
    // Warn: approaching pool exhaustion
}
```

---

### Batching Trade-offs

| Batch Size | Memory | Network | Speed |
|------------|--------|---------|-------|
| 100 | Low | More roundtrips | Slower |
| 1000 | Medium | Balanced | Fast |
| 5000 | High | Fewer roundtrips | Fastest |
| 10000 | Very High | Minimal | Diminishing returns |

**Recommendation:** 1000 for balanced performance

---

## Extension Points

### Custom Strategies

Users can implement custom strategies by inheriting:

```csharp
public abstract class BulkStrategyBase
{
    public abstract Task<BulkResult> ExecuteCreateAsync(
        IList<TEntity> entities,
        BulkOptions options,
        CancellationToken ct);
}

// Example: Custom batching with compression
public class CompressedBatchStrategy : BulkStrategyBase
{
    public override async Task<BulkResult> ExecuteCreateAsync(...)
    {
        // Custom implementation with compression
    }
}
```

---

### Provider-Specific Optimizations

Adding support for new provider-specific APIs:

```csharp
// In dialect implementation
public override bool SupportsProviderOptimizedBulkInsert => true;

// In TableGateway
private async Task<BulkResult> CreateManyUsingCustomProvider(...)
{
    // Provider-specific implementation
}
```

---

### Progress Reporting Extensions

Custom progress tracking:

```csharp
public class DetailedProgress : IProgress<BulkProgress>
{
    public void Report(BulkProgress value)
    {
        // Custom metrics
        Console.WriteLine($"Throughput: {CalculateThroughput(value)} rows/sec");
        Console.WriteLine($"ETA: {CalculateETA(value)}");
    }
}
```

---

## Testing Strategy

### Unit Tests

1. **Strategy Selection Tests**
   - Auto selection logic
   - User override
   - Fallback behavior

2. **SQL Generation Tests**
   - Multi-row VALUES
   - Oracle INSERT ALL
   - Parameter binding
   - Column escaping

3. **Error Handling Tests**
   - ContinueOnError=true/false
   - Batch failure recovery
   - Error collection

4. **Progress Reporting Tests**
   - Callback invocation
   - Accuracy of counts

### Integration Tests

1. **Database-Specific Tests**
   - One test per database
   - Provider-specific features
   - Parameter limits

2. **Performance Tests**
   - Compare strategies
   - Verify speedup claims
   - Memory profiling

3. **Concurrency Tests**
   - Thread safety
   - Connection pool behavior
   - Deadlock prevention

---

## Future Enhancements

### Phase 2 Features

1. **Streaming Support**
   ```csharp
   await foreach (var batch in GenerateBatchesAsync())
   {
       await helper.CreateManyAsync(batch);
   }
   ```

2. **Resumable Operations**
   ```csharp
   var checkpoint = new BulkCheckpoint();
   var result = await helper.CreateManyAsync(entities, new BulkOptions
   {
       Checkpoint = checkpoint // Resume on failure
   });
   ```

3. **Advanced Error Recovery**
   ```csharp
   var result = await helper.CreateManyAsync(entities, new BulkOptions
   {
       RetryPolicy = new ExponentialBackoffRetry(maxAttempts: 3)
   });
   ```

4. **Metrics and Observability**
   ```csharp
   var result = await helper.CreateManyAsync(entities, new BulkOptions
   {
       Metrics = new PrometheusMetrics()
   });
   ```

---

## See Also

- [Bulk Operations User Guide](BULK_OPERATIONS.md)
- [Database Compatibility Matrix](BULK_OPERATIONS_COMPATIBILITY.md)
- [Performance Benchmarks](../benchmarks/BulkOperationsBenchmarks.cs)
- [Integration Tests](../pengdows.crud.IntegrationTests/BulkOperationsTests.cs)
