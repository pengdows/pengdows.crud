# Bulk Operations Quick Reference

## Basic Usage

```csharp
// Simple bulk insert
var result = await helper.CreateManyAsync(entities);

// With options
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    Strategy = BulkStrategy.Batched,
    BatchSize = 1000,
    ContinueOnError = true,
    Progress = new Progress<BulkProgress>(p => 
        Console.WriteLine($"{p.PercentComplete:F1}%"))
});
```

## Strategies Cheat Sheet

| Strategy | When to Use | Performance |
|----------|-------------|-------------|
| **Auto** | Default (smart selection) | Varies |
| **Sequential** | < 10 entities, debugging | 1x (baseline) |
| **Batched** | 10-10K entities | 10-50x |
| **Concurrent** | High-latency connections | 5-20x |
| **ProviderOptimized** | 10K+ on PostgreSQL/SQL Server | 50-100x |

## Common Patterns

### Import from File
```csharp
var products = LoadFromCsv("products.csv");
var result = await helper.CreateManyAsync(products, new BulkOptions
{
    Strategy = BulkStrategy.ProviderOptimized,
    Progress = new Progress<BulkProgress>(p => 
        Console.WriteLine($"Imported {p.Processed}/{p.Total}"))
});
```

### Bulk Update with Error Handling
```csharp
var result = await helper.UpdateManyAsync(entities, new BulkOptions
{
    ContinueOnError = true
});

if (result.FailureCount > 0)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"Failed: {error.Entity.Id} - {error.Exception.Message}");
    }
}
```

### Bulk Upsert
```csharp
var result = await helper.UpsertManyAsync(entities, new BulkOptions
{
    Strategy = BulkStrategy.Batched,
    BatchSize = 500
});
Console.WriteLine($"Inserted: {result.InsertCount}, Updated: {result.UpdateCount}");
```

### Concurrent Insert (Cloud DB)
```csharp
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    Strategy = BulkStrategy.Concurrent,
    MaxConcurrency = 20
});
```

## Database-Specific Tips

| Database | Best Strategy | Batch Size | Notes |
|----------|--------------|------------|-------|
| PostgreSQL | ProviderOptimized | 1000-5000 | COPY is fastest |
| SQL Server | ProviderOptimized | 1000-5000 | SqlBulkCopy is fastest |
| Oracle | Batched | 500-1000 | Parameter limit ~32K |
| MySQL | Batched | 1000-5000 | Multi-row VALUES |
| SQLite | Batched | 500 | Parameter limit 999 |
| DuckDB | ProviderOptimized | 1000-5000 | COPY for analytics |

## Configuration Options

```csharp
new BulkOptions
{
    // Strategy selection (default: Auto)
    Strategy = BulkStrategy.Auto,

    // Batch size for Batched strategy (default: 1000)
    BatchSize = 1000,

    // Max concurrency for Concurrent strategy (default: CPU * 2)
    MaxConcurrency = Environment.ProcessorCount * 2,

    // Continue on error or fail fast (default: false)
    ContinueOnError = false,

    // Progress callback (default: null)
    Progress = new Progress<BulkProgress>(...),

    // Override context (default: null, uses instance context)
    Context = customContext
}
```

## Performance Quick Comparison

### Inserting 10,000 Entities

| Strategy | Time | Speedup |
|----------|------|---------|
| Sequential | ~45s | 1x |
| Batched (1K) | ~1.2s | 38x |
| Concurrent (10) | ~8s | 6x |
| ProviderOptimized | ~0.5s | 90x |

## Common Errors and Solutions

### "Parameter limit exceeded"
**Solution:** Reduce batch size
```csharp
BatchSize = 500 // or lower
```

### "Connection pool exhausted"
**Solution:** Increase pool size or reduce concurrency
```csharp
// Connection string
"...;Max Pool Size=100"

// Options
MaxConcurrency = 10 // Lower than pool size
```

### "Duplicate key violation"
**Solution:** Use UpsertManyAsync or ContinueOnError
```csharp
await helper.UpsertManyAsync(entities); // Insert or update
// OR
ContinueOnError = true // Skip duplicates
```

### "SqlBulkCopy triggers not firing"
**Solution:** Use Batched strategy instead
```csharp
Strategy = BulkStrategy.Batched // Fires triggers
```

## API Quick Reference

### Methods

```csharp
// Bulk insert
Task<BulkResult> CreateManyAsync(
    IEnumerable<TEntity> entities,
    BulkOptions? options = null,
    CancellationToken ct = default)

// Bulk update
Task<BulkResult> UpdateManyAsync(
    IEnumerable<TEntity> entities,
    BulkOptions? options = null,
    CancellationToken ct = default)

// Bulk upsert
Task<BulkResult> UpsertManyAsync(
    IEnumerable<TEntity> entities,
    BulkOptions? options = null,
    CancellationToken ct = default)
```

### Result Properties

```csharp
result.SuccessCount        // Entities processed successfully
result.FailureCount        // Entities that failed
result.Errors              // List of BulkError (if ContinueOnError)
result.Elapsed             // Total time
result.OperationsPerSecond // Calculated throughput
```

### Progress Callback

```csharp
new Progress<BulkProgress>(p =>
{
    Console.WriteLine($"Progress: {p.PercentComplete:F1}%");
    Console.WriteLine($"Processed: {p.Processed}/{p.Total}");
    Console.WriteLine($"Success: {p.Succeeded}, Failed: {p.Failed}");
})
```

## Best Practices Checklist

- ✅ Use Auto strategy by default
- ✅ Set appropriate batch size for your database
- ✅ Monitor connection pool usage with Concurrent
- ✅ Use Progress callback for long operations
- ✅ Handle errors appropriately (fail fast vs continue)
- ✅ Validate entities before bulk operations
- ✅ Wrap in transactions when needed
- ✅ Benchmark your specific workload

## See Full Documentation

- [User Guide](BULK_OPERATIONS.md) - Complete usage guide
- [Architecture](BULK_OPERATIONS_ARCHITECTURE.md) - Internal design
- [Compatibility](BULK_OPERATIONS_COMPATIBILITY.md) - Database support matrix
