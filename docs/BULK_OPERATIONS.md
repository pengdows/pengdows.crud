# Bulk Operations Guide

## Table of Contents

1. [Overview](#overview)
2. [Quick Start](#quick-start)
3. [Strategies](#strategies)
4. [API Reference](#api-reference)
5. [Performance Comparison](#performance-comparison)
6. [Database-Specific Behavior](#database-specific-behavior)
7. [Error Handling](#error-handling)
8. [Best Practices](#best-practices)
9. [Migration Guide](#migration-guide)

---

## Overview

Bulk operations allow you to efficiently insert, update, or upsert thousands of entities in a single operation. Instead of executing N individual database round-trips, bulk operations use batching, parallelization, or provider-specific optimizations to achieve 10-100x performance improvements.

### What's Available

| Operation | Method | Description |
|-----------|--------|-------------|
| Bulk Insert | `CreateManyAsync()` | Insert multiple entities |
| Bulk Update | `UpdateManyAsync()` | Update multiple entities |
| Bulk Upsert | `UpsertManyAsync()` | Insert or update multiple entities |

### When to Use Bulk Operations

✅ **Use bulk operations when:**
- Importing data from files or external APIs
- Processing batch jobs
- Seeding test data
- Synchronizing data between systems
- Inserting/updating 10+ entities at once

❌ **Don't use bulk operations when:**
- Operating on 1-5 entities (overhead exceeds benefit)
- Each operation requires complex business logic
- You need fine-grained transaction control per entity

---

## Quick Start

### Basic Usage

```csharp
using pengdows.crud;

var helper = new TableGateway<Product, int>(context);

// Generate or load your data
var products = GenerateProducts(10000);

// Bulk insert with default settings
var result = await helper.CreateManyAsync(products);

Console.WriteLine($"Inserted: {result.SuccessCount} rows");
Console.WriteLine($"Time: {result.Elapsed.TotalSeconds:F2}s");
Console.WriteLine($"Rate: {result.OperationsPerSecond:F0} ops/sec");
```

### With Options

```csharp
var result = await helper.CreateManyAsync(products, new BulkOptions
{
    Strategy = BulkStrategy.Batched,
    BatchSize = 500,
    ContinueOnError = true,
    Progress = new Progress<BulkProgress>(p =>
        Console.WriteLine($"Progress: {p.PercentComplete:F1}%"))
});

// Check for errors
if (result.FailureCount > 0)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"Failed at index {error.Index}: {error.Exception.Message}");
    }
}
```

### Update and Upsert

```csharp
// Bulk update existing entities
var existingProducts = await helper.RetrieveAsync(productIds);
foreach (var product in existingProducts)
{
    product.Price *= 1.1m; // 10% price increase
}
var updateResult = await helper.UpdateManyAsync(existingProducts);

// Bulk upsert (insert new, update existing)
var allProducts = LoadProductsFromFile();
var upsertResult = await helper.UpsertManyAsync(allProducts, new BulkOptions
{
    Strategy = BulkStrategy.Auto // Automatically picks best strategy
});
```

---

## Strategies

Bulk operations support multiple execution strategies, each with different performance characteristics and use cases.

### 1. Sequential

**Description:** Executes operations one at a time in order.

**When to use:**
- Small batches (< 10 entities)
- Debugging
- When order matters strictly

**Performance:** Baseline (1x)

```csharp
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    Strategy = BulkStrategy.Sequential
});
```

**Behavior:**
- Executes `CreateAsync()` for each entity in order
- Stops on first error (unless `ContinueOnError = true`)
- Predictable, safe, but slow

---

### 2. Batched

**Description:** Groups entities into batches and executes multi-row SQL statements.

**When to use:**
- Medium to large batches (10-10,000 entities)
- When database supports multi-row INSERT/UPDATE
- Default for most scenarios

**Performance:** 10-50x faster than Sequential

```csharp
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    Strategy = BulkStrategy.Batched,
    BatchSize = 1000 // Default: 1000
});
```

**Behavior:**
- Groups entities into batches of `BatchSize`
- Generates multi-row SQL:
  ```sql
  INSERT INTO products (name, price) VALUES
      ('Product 1', 10.00),
      ('Product 2', 20.00),
      ('Product 3', 30.00);
  ```
- If batch fails and `ContinueOnError = true`, retries entities individually
- Respects database parameter limits

**Example SQL Generated:**

```sql
-- PostgreSQL, SQL Server, MySQL, SQLite (standard)
INSERT INTO products (id, name, price, stock)
VALUES
    (@p0, @p1, @p2, @p3),
    (@p4, @p5, @p6, @p7),
    (@p8, @p9, @p10, @p11);

-- Oracle (INSERT ALL)
INSERT ALL
    INTO products (id, name, price, stock) VALUES (:p0, :p1, :p2, :p3)
    INTO products (id, name, price, stock) VALUES (:p4, :p5, :p6, :p7)
    INTO products (id, name, price, stock) VALUES (:p8, :p9, :p10, :p11)
SELECT 1 FROM DUAL;
```

---

### 3. Concurrent

**Description:** Executes operations in parallel with throttled concurrency.

**When to use:**
- High-latency database connections (cloud databases)
- I/O-bound operations
- When database can handle parallel connections
- Small to medium entities

**Performance:** 5-20x faster for network-bound operations

```csharp
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    Strategy = BulkStrategy.Concurrent,
    MaxConcurrency = 10 // Default: Environment.ProcessorCount * 2
});
```

**Behavior:**
- Executes up to `MaxConcurrency` operations in parallel
- Uses `SemaphoreSlim` for throttling
- Each operation opens its own connection from the pool
- Best for operations that spend time waiting on I/O

**Connection Pool Considerations:**
- Ensure connection pool size > `MaxConcurrency`
- Default pool sizes:
  - SQL Server: 100
  - PostgreSQL: 100
  - MySQL: 100
- Set in connection string: `Max Pool Size=50`

---

### 4. ProviderOptimized

**Description:** Uses database-specific bulk loading APIs for maximum performance.

**When to use:**
- Very large batches (10,000+ entities)
- When performance is critical
- On supported databases (PostgreSQL, SQL Server, DuckDB)

**Performance:** 50-100x faster than Sequential

```csharp
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    Strategy = BulkStrategy.ProviderOptimized
});
```

**Provider-Specific Implementations:**

| Database | API Used | Speedup |
|----------|----------|---------|
| PostgreSQL | `COPY` (binary format) | 50-100x |
| SQL Server | `SqlBulkCopy` | 50-100x |
| DuckDB | `COPY` | 50-100x |
| Others | Falls back to Batched | 10-50x |

**Behavior:**
- PostgreSQL: Uses Npgsql's `BeginBinaryImport()`
- SQL Server: Uses `SqlBulkCopy`
- DuckDB: Uses DuckDB.NET's COPY
- Falls back gracefully to Batched strategy if not available

**Limitations:**
- May bypass triggers (database-dependent)
- May not populate auto-increment IDs back to entities
- PostgreSQL COPY requires all non-nullable columns

---

### 5. Auto (Recommended)

**Description:** Automatically selects the best strategy based on database, entity count, and context.

**When to use:**
- Default choice for most scenarios
- When you want optimal performance without manual tuning

```csharp
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    Strategy = BulkStrategy.Auto // This is the default
});

// Or simply:
var result = await helper.CreateManyAsync(entities);
```

**Selection Logic:**

```
IF entityCount <= 5
    THEN Sequential (overhead not worth it)
ELSE IF entityCount > 10000 AND database IN (PostgreSQL, SqlServer, DuckDB)
    THEN ProviderOptimized (maximum performance)
ELSE IF entityCount > 10
    THEN Batched (good balance)
ELSE
    Sequential
```

---

## API Reference

### CreateManyAsync

Bulk insert multiple entities.

```csharp
Task<BulkResult> CreateManyAsync(
    IEnumerable<TEntity> entities,
    BulkOptions? options = null,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `entities`: Collection of entities to insert
- `options`: Optional bulk operation settings
- `cancellationToken`: Cancellation token

**Returns:** `BulkResult` with success/failure counts and timing

**Throws:**
- `ArgumentNullException`: If entities is null
- `ArgumentException`: If entities collection is empty
- `InvalidOperationException`: If audit fields required but no resolver provided
- Database-specific exceptions (unless `ContinueOnError = true`)

**Example:**
```csharp
var products = GenerateProducts(1000);
var result = await helper.CreateManyAsync(products);
```

---

### UpdateManyAsync

Bulk update multiple entities.

```csharp
Task<BulkResult> UpdateManyAsync(
    IEnumerable<TEntity> entities,
    BulkOptions? options = null,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `entities`: Collection of entities to update (must have valid IDs)
- `options`: Optional bulk operation settings
- `cancellationToken`: Cancellation token

**Returns:** `BulkResult` with affected row counts

**Behavior:**
- Requires entities to have valid ID values
- Updates `LastUpdatedOn`/`LastUpdatedBy` if audit fields present
- Respects `[NonUpdateable]` columns
- Version column handling (optimistic concurrency)

**Example:**
```csharp
var products = await helper.RetrieveAsync(productIds);
foreach (var p in products) p.Price *= 1.1m;
var result = await helper.UpdateManyAsync(products);
```

---

### UpsertManyAsync

Bulk insert new entities or update existing ones.

```csharp
Task<BulkResult> UpsertManyAsync(
    IEnumerable<TEntity> entities,
    BulkOptions? options = null,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `entities`: Collection of entities to upsert
- `options`: Optional bulk operation settings
- `cancellationToken`: Cancellation token

**Returns:** `BulkResult` with insert/update counts

**Conflict Detection:**
1. Uses `[PrimaryKey]` columns if defined
2. Falls back to `[Id]` column if writable
3. Throws if neither available

**Database-Specific SQL:**
- SQL Server: `MERGE`
- PostgreSQL: `INSERT ... ON CONFLICT`
- MySQL/MariaDB: `INSERT ... ON DUPLICATE KEY UPDATE`
- Oracle: `MERGE`
- Others: Individual upserts

**Example:**
```csharp
var products = LoadFromExternalSystem();
var result = await helper.UpsertManyAsync(products);
Console.WriteLine($"Inserted: {result.InsertCount}, Updated: {result.UpdateCount}");
```

---

### BulkOptions

Configuration for bulk operations.

```csharp
public class BulkOptions
{
    // Strategy selection
    public BulkStrategy Strategy { get; set; } = BulkStrategy.Auto;

    // Batching configuration
    public int BatchSize { get; set; } = 1000;

    // Concurrency configuration
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount * 2;

    // Error handling
    public bool ContinueOnError { get; set; } = false;

    // Progress reporting
    public IProgress<BulkProgress>? Progress { get; set; }

    // Context override
    public IDatabaseContext? Context { get; set; }
}
```

**Properties:**

- **Strategy**: Which execution strategy to use (default: Auto)
- **BatchSize**: Max entities per batch for Batched strategy (default: 1000)
  - Automatically adjusted for database parameter limits
  - Recommended range: 100-5000
- **MaxConcurrency**: Max parallel operations for Concurrent strategy (default: CPU count × 2)
  - Ensure connection pool can support this
- **ContinueOnError**: Whether to continue on errors or fail fast (default: false)
  - When true, collects errors in `BulkResult.Errors`
  - When false, throws on first error
- **Progress**: Callback for progress updates (default: null)
  - Called after each batch or entity
  - Useful for long-running operations
- **Context**: Override database context (default: null)
  - Uses instance context if not specified

---

### BulkResult

Result of a bulk operation.

```csharp
public class BulkResult
{
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public List<BulkError> Errors { get; init; }
    public TimeSpan Elapsed { get; init; }
    public double OperationsPerSecond { get; }
}
```

**Properties:**

- **SuccessCount**: Number of entities processed successfully
- **FailureCount**: Number of entities that failed
- **Errors**: List of errors (populated when `ContinueOnError = true`)
- **Elapsed**: Total time taken
- **OperationsPerSecond**: Calculated throughput (SuccessCount / Elapsed.TotalSeconds)

---

### BulkError

Information about a failed entity.

```csharp
public class BulkError
{
    public int Index { get; init; }
    public TEntity Entity { get; init; }
    public Exception Exception { get; init; }
}
```

**Properties:**

- **Index**: Position in the original collection
- **Entity**: The entity that failed
- **Exception**: The exception that was thrown

---

### BulkProgress

Progress information for long-running operations.

```csharp
public class BulkProgress
{
    public int Processed { get; init; }
    public int Total { get; init; }
    public int Succeeded { get; init; }
    public int Failed { get; init; }
    public double PercentComplete { get; }
}
```

**Properties:**

- **Processed**: Total entities processed so far
- **Total**: Total entities to process (0 if unknown)
- **Succeeded**: Entities processed successfully
- **Failed**: Entities that failed
- **PercentComplete**: Calculated percentage (Processed / Total × 100)

---

## Performance Comparison

### Benchmark Results

Inserting 10,000 entities on PostgreSQL (local connection):

| Strategy | Time | Ops/Sec | Speedup | Memory |
|----------|------|---------|---------|--------|
| Sequential | 45.2s | 221 | 1x | Low |
| Batched (1000) | 1.2s | 8,333 | 38x | Medium |
| Batched (5000) | 0.9s | 11,111 | 50x | High |
| Concurrent (10) | 8.1s | 1,235 | 6x | Medium |
| ProviderOptimized (COPY) | 0.5s | 20,000 | 90x | Low |

**Insights:**
- Batched strategy provides excellent performance with reasonable memory
- Provider-optimized is fastest but may have limitations
- Concurrent is good for high-latency connections
- Larger batch sizes trade memory for speed

---

### Recommendations by Scenario

| Scenario | Recommended Strategy | Batch Size |
|----------|---------------------|------------|
| Importing CSV (10K rows) | ProviderOptimized or Batched | 1000-5000 |
| Nightly sync job (1K rows) | Batched | 500-1000 |
| Real-time updates (100 rows) | Batched or Concurrent | 100-500 |
| Cloud database import | Concurrent | N/A (concurrency: 10-20) |
| Development seed data | Auto | Default |

---

## Database-Specific Behavior

### PostgreSQL

**Best Strategy:** ProviderOptimized (COPY)

```csharp
var result = await helper.CreateManyAsync(products, new BulkOptions
{
    Strategy = BulkStrategy.ProviderOptimized
});
```

**Features:**
- Binary COPY format (fastest)
- Falls back to text COPY if binary fails
- Multi-row VALUES for UPDATE
- `INSERT ... ON CONFLICT` for upsert

**Limitations:**
- COPY bypasses triggers
- Must provide all non-nullable columns
- Auto-generated IDs not returned

---

### SQL Server

**Best Strategy:** ProviderOptimized (SqlBulkCopy)

```csharp
var result = await helper.CreateManyAsync(products, new BulkOptions
{
    Strategy = BulkStrategy.ProviderOptimized
});
```

**Features:**
- SqlBulkCopy with table lock hint
- MERGE for upsert operations
- Temp tables for bulk update

**Limitations:**
- SqlBulkCopy bypasses triggers (unless BulkCopyOptions.FireTriggers)
- May require additional permissions

**Connection String Settings:**
```
Max Pool Size=100;Packet Size=32768
```

---

### Oracle

**Best Strategy:** Batched

```csharp
var result = await helper.CreateManyAsync(products, new BulkOptions
{
    Strategy = BulkStrategy.Batched,
    BatchSize = 500 // Oracle has lower parameter limits
});
```

**Features:**
- INSERT ALL syntax for batching
- MERGE for upsert operations
- Array binding (future optimization)

**Limitations:**
- Parameter limit: ~32,000 (varies by version)
- INSERT ALL requires SELECT FROM DUAL

---

### MySQL / MariaDB

**Best Strategy:** Batched

```csharp
var result = await helper.CreateManyAsync(products, new BulkOptions
{
    Strategy = BulkStrategy.Batched
});
```

**Features:**
- Multi-row VALUES
- `INSERT ... ON DUPLICATE KEY UPDATE` for upsert
- Extended INSERT syntax

**Connection String Settings:**
```
Max Pool Size=100;AllowLoadLocalInfile=true
```

---

### SQLite

**Best Strategy:** Batched

```csharp
var result = await helper.CreateManyAsync(products, new BulkOptions
{
    Strategy = BulkStrategy.Batched,
    BatchSize = 500 // Smaller batches for SQLite
});
```

**Features:**
- Multi-row VALUES (SQLite 3.7.11+)
- Transaction batching is critical

**Limitations:**
- Single-writer lock (concurrent strategy won't help)
- Parameter limit: 999 (use smaller batches)
- File I/O is the bottleneck

**Best Practice:**
```csharp
// Wrap in transaction for optimal SQLite performance
await using var tx = await context.BeginTransaction();
var result = await helper.CreateManyAsync(products);
await tx.Commit();
```

---

### DuckDB

**Best Strategy:** ProviderOptimized (COPY)

```csharp
var result = await helper.CreateManyAsync(products, new BulkOptions
{
    Strategy = BulkStrategy.ProviderOptimized
});
```

**Features:**
- COPY for analytical workloads
- Columnar storage optimizations
- Excellent batch INSERT performance

---

### CockroachDB

**Best Strategy:** Batched

```csharp
var result = await helper.CreateManyAsync(products, new BulkOptions
{
    Strategy = BulkStrategy.Batched
});
```

**Features:**
- PostgreSQL-compatible syntax
- Multi-row VALUES
- `INSERT ... ON CONFLICT`

**Limitations:**
- No COPY support
- Distributed transactions may have higher latency

---

### Firebird

**Best Strategy:** Batched

```csharp
var result = await helper.CreateManyAsync(products, new BulkOptions
{
    Strategy = BulkStrategy.Batched
});
```

**Features:**
- Multi-row VALUES (Firebird 3.0+)
- MERGE for upsert

---

## Error Handling

### Fail Fast (Default)

By default, bulk operations stop on the first error:

```csharp
try
{
    var result = await helper.CreateManyAsync(entities);
    Console.WriteLine($"Success: {result.SuccessCount}");
}
catch (Exception ex)
{
    Console.WriteLine($"Failed: {ex.Message}");
    // Some entities may have been inserted before the error
}
```

### Continue on Error

Process all entities and collect errors:

```csharp
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    ContinueOnError = true
});

Console.WriteLine($"Succeeded: {result.SuccessCount}");
Console.WriteLine($"Failed: {result.FailureCount}");

if (result.FailureCount > 0)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"Entity at index {error.Index} failed:");
        Console.WriteLine($"  Entity: {error.Entity}");
        Console.WriteLine($"  Error: {error.Exception.Message}");
    }
}
```

### Retrying Failed Entities

```csharp
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    ContinueOnError = true
});

if (result.FailureCount > 0)
{
    // Retry failed entities
    var failedEntities = result.Errors.Select(e => e.Entity);
    var retryResult = await helper.CreateManyAsync(failedEntities, new BulkOptions
    {
        Strategy = BulkStrategy.Sequential, // Slower but more reliable
        ContinueOnError = true
    });

    Console.WriteLine($"Retry succeeded: {retryResult.SuccessCount}");
}
```

---

## Best Practices

### 1. Choose the Right Strategy

```csharp
// Default: Let Auto decide
await helper.CreateManyAsync(entities);

// Small batches: Sequential is fine
if (entities.Count < 10)
    await helper.CreateManyAsync(entities, new BulkOptions { Strategy = BulkStrategy.Sequential });

// Large batches: Provider-optimized or Batched
if (entities.Count > 10000)
    await helper.CreateManyAsync(entities, new BulkOptions { Strategy = BulkStrategy.ProviderOptimized });

// High-latency connection: Concurrent
if (isCloudDatabase)
    await helper.CreateManyAsync(entities, new BulkOptions
    {
        Strategy = BulkStrategy.Concurrent,
        MaxConcurrency = 20
    });
```

---

### 2. Tune Batch Size

```csharp
// Small entities (few columns): Larger batches
await helper.CreateManyAsync(simpleEntities, new BulkOptions
{
    BatchSize = 5000
});

// Large entities (many columns, JSON fields): Smaller batches
await helper.CreateManyAsync(complexEntities, new BulkOptions
{
    BatchSize = 100
});

// Oracle: Respect parameter limits
await helper.CreateManyAsync(entities, new BulkOptions
{
    BatchSize = 500 // Oracle has ~32K parameter limit
});
```

---

### 3. Use Progress Reporting for Large Operations

```csharp
var progress = new Progress<BulkProgress>(p =>
{
    Console.Write($"\r{p.PercentComplete:F1}% complete ({p.Processed}/{p.Total})");
});

var result = await helper.CreateManyAsync(largeDataset, new BulkOptions
{
    Progress = progress
});

Console.WriteLine(); // New line after progress
```

---

### 4. Wrap in Transactions When Needed

```csharp
// All-or-nothing: Transaction ensures atomicity
await using var tx = await context.BeginTransaction();
try
{
    var result1 = await helper1.CreateManyAsync(orders);
    var result2 = await helper2.CreateManyAsync(orderItems);

    await tx.Commit();
}
catch
{
    // Auto-rollback on dispose
    throw;
}
```

---

### 5. Monitor Connection Pool Usage

```csharp
// For Concurrent strategy, ensure pool is large enough
var connectionString = $"...;Max Pool Size={maxConcurrency * 2}";

var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    Strategy = BulkStrategy.Concurrent,
    MaxConcurrency = 20 // Pool size should be at least 40
});
```

---

### 6. Handle Audit Fields

```csharp
// Ensure audit resolver is configured
var helper = new TableGateway<Order, int>(context, auditValueResolver);

// Audit fields are automatically populated:
// - CreatedBy, CreatedOn on INSERT
// - LastUpdatedBy, LastUpdatedOn on UPDATE
var result = await helper.CreateManyAsync(orders);
```

---

### 7. Validate Before Bulk Operations

```csharp
// Validate entities before sending to database
var validEntities = entities
    .Where(e => !string.IsNullOrEmpty(e.Name))
    .Where(e => e.Price > 0)
    .ToList();

if (validEntities.Count < entities.Count)
{
    Console.WriteLine($"Warning: {entities.Count - validEntities.Count} invalid entities skipped");
}

var result = await helper.CreateManyAsync(validEntities);
```

---

### 8. Chunk Very Large Datasets

```csharp
// For millions of entities, process in chunks
const int chunkSize = 50000;
var allResults = new List<BulkResult>();

foreach (var chunk in entities.Chunk(chunkSize))
{
    var result = await helper.CreateManyAsync(chunk);
    allResults.Add(result);

    Console.WriteLine($"Chunk complete: {result.SuccessCount} inserted");
}

var totalSuccess = allResults.Sum(r => r.SuccessCount);
Console.WriteLine($"Total: {totalSuccess} entities inserted");
```

---

### 9. Benchmark Your Specific Workload

```csharp
// Test different strategies with your data
var strategies = new[]
{
    BulkStrategy.Sequential,
    BulkStrategy.Batched,
    BulkStrategy.Concurrent,
    BulkStrategy.ProviderOptimized
};

foreach (var strategy in strategies)
{
    var sw = Stopwatch.StartNew();
    var result = await helper.CreateManyAsync(entities, new BulkOptions { Strategy = strategy });
    sw.Stop();

    Console.WriteLine($"{strategy}: {result.SuccessCount} rows in {sw.Elapsed.TotalSeconds:F2}s ({result.OperationsPerSecond:F0} ops/sec)");
}
```

---

## Migration Guide

### From Manual Loops

**Before:**
```csharp
foreach (var entity in entities)
{
    await helper.CreateAsync(entity);
}
```

**After:**
```csharp
var result = await helper.CreateManyAsync(entities);
```

---

### From Transaction-Wrapped Loops

**Before:**
```csharp
await using var tx = await context.BeginTransaction();
foreach (var entity in entities)
{
    await helper.CreateAsync(entity, context);
}
await tx.Commit();
```

**After:**
```csharp
await using var tx = await context.BeginTransaction();
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    Context = context // Use transaction context
});
await tx.Commit();
```

---

### From SqlBulkCopy (SQL Server)

**Before:**
```csharp
using (var bulkCopy = new SqlBulkCopy(connection))
{
    bulkCopy.DestinationTableName = "Products";
    var dataTable = ConvertToDataTable(entities);
    await bulkCopy.WriteToServerAsync(dataTable);
}
```

**After:**
```csharp
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    Strategy = BulkStrategy.ProviderOptimized // Uses SqlBulkCopy internally
});
```

---

### From PostgreSQL COPY

**Before:**
```csharp
using (var writer = await connection.BeginBinaryImportAsync("COPY products FROM STDIN BINARY"))
{
    foreach (var entity in entities)
    {
        await writer.StartRowAsync();
        await writer.WriteAsync(entity.Name);
        await writer.WriteAsync(entity.Price);
    }
    await writer.CompleteAsync();
}
```

**After:**
```csharp
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    Strategy = BulkStrategy.ProviderOptimized // Uses COPY internally
});
```

---

## FAQ

### Q: Does bulk insert return generated IDs?

**A:** Depends on strategy:
- **Sequential**: ✅ Yes (if database returns them)
- **Batched**: ⚠️ Database-dependent (PostgreSQL: yes with RETURNING, others: no)
- **ProviderOptimized**: ❌ No (SqlBulkCopy and COPY don't return IDs)

If you need IDs, use Sequential or retrieve them afterward:
```csharp
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    Strategy = BulkStrategy.Batched
});

// Retrieve by unique key to get IDs
var inserted = await helper.RetrieveAsync(...);
```

---

### Q: Are triggers fired?

**A:** Depends on strategy and database:
- **Sequential, Batched**: ✅ Yes (normal INSERT/UPDATE)
- **ProviderOptimized**:
  - SqlBulkCopy: ❌ No (unless `SqlBulkCopyOptions.FireTriggers`)
  - PostgreSQL COPY: ❌ No
  - DuckDB COPY: ❌ No

---

### Q: Can I use bulk operations with multi-tenancy?

**A:** Yes, pass the tenant-specific context:

```csharp
var tenantContext = contextResolver.GetContext(tenantId);
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    Context = tenantContext
});
```

---

### Q: What about optimistic concurrency (version columns)?

**A:** Supported for `UpdateManyAsync`:

```csharp
// Version column is automatically checked and incremented
var result = await helper.UpdateManyAsync(entities);

if (result.FailureCount > 0)
{
    // Some entities had version conflicts (were modified by another process)
    foreach (var error in result.Errors)
    {
        if (error.Exception is DbUpdateConcurrencyException)
        {
            // Handle conflict
        }
    }
}
```

---

### Q: Can I cancel long-running bulk operations?

**A:** Yes, use CancellationToken:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

try
{
    var result = await helper.CreateManyAsync(entities, cancellationToken: cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Bulk operation was cancelled");
}
```

---

### Q: How do I handle duplicates?

**A:** Use `UpsertManyAsync` or `ContinueOnError`:

```csharp
// Option 1: Upsert (update on conflict)
var result = await helper.UpsertManyAsync(entities);

// Option 2: Continue on error and collect duplicates
var result = await helper.CreateManyAsync(entities, new BulkOptions
{
    ContinueOnError = true
});

var duplicates = result.Errors
    .Where(e => e.Exception.Message.Contains("duplicate") ||
                e.Exception.Message.Contains("unique constraint"))
    .Select(e => e.Entity);
```

---

## See Also

- [Connection Management](CONNECTION_MANAGEMENT.md)
- [Transaction Guide](TRANSACTIONS.md)
- [Performance Tuning](PERFORMANCE_TUNING.md)
- [Multi-Tenancy](MULTI_TENANCY.md)
