# Bulk Operations Database Compatibility Matrix

## Strategy Compatibility

| Database | Sequential | Batched INSERT | Batched UPDATE | Concurrent | Provider-Optimized |
|----------|------------|----------------|----------------|------------|-------------------|
| **PostgreSQL** | ✅ | ✅ | ✅ | ✅ | ✅ COPY |
| **SqlServer** | ✅ | ✅ | ✅ | ✅ | ✅ SqlBulkCopy |
| **Oracle** | ✅ | ⚠️ Different syntax | ✅ MERGE | ✅ | ⚠️ Future |
| **Firebird** | ✅ | ✅ | ✅ MERGE | ✅ | ⚠️ Future |
| **CockroachDB** | ✅ | ✅ | ✅ | ✅ | ⚠️ No COPY |
| **MariaDB** | ✅ | ✅ | ✅ ON DUPLICATE | ✅ | ⚠️ Future |
| **MySQL** | ✅ | ✅ | ✅ ON DUPLICATE | ✅ | ⚠️ Future |
| **SQLite** | ✅ | ✅ | ✅ | ✅ | ⚠️ Limited benefit |
| **DuckDB** | ✅ | ✅ | ✅ | ✅ | ✅ COPY |

Legend:
- ✅ Fully supported
- ⚠️ Requires special handling or limited benefit
- ❌ Not supported

---

## Strategy Details by Database

### 1. Sequential Strategy
**Compatibility: ✅ ALL DATABASES**

Simple foreach loop - works everywhere since it's just individual operations.

```csharp
foreach (var entity in entities)
{
    await CreateAsync(entity);
}
```

**Performance:** Baseline (1x)

---

### 2. Batched INSERT Strategy

#### ✅ Standard Multi-Row VALUES (Most Databases)

**PostgreSQL, SqlServer, Firebird, CockroachDB, MariaDB, MySQL, SQLite, DuckDB**

```sql
INSERT INTO table (col1, col2, col3) VALUES
    (val1, val2, val3),
    (val4, val5, val6),
    (val7, val8, val9);
```

**Implementation:**
```csharp
private ISqlContainer BuildCreateBatch(TEntity[] entities, ISqlDialect dialect)
{
    // Standard multi-row VALUES clause
    // Supported since:
    // - PostgreSQL: Always
    // - SQL Server: 2008+
    // - MySQL: 3.22+ (1999)
    // - SQLite: 3.7.11+ (2012)
    // - Firebird: 3.0+ (2016)
    // - DuckDB: Always
    // - CockroachDB: Always
}
```

**Performance:** 10-50x faster than Sequential

---

#### ⚠️ Oracle - Requires INSERT ALL Syntax

**Oracle uses different syntax:**

```sql
INSERT ALL
    INTO table (col1, col2, col3) VALUES (val1, val2, val3)
    INTO table (col1, col2, col3) VALUES (val4, val5, val6)
    INTO table (col1, col2, col3) VALUES (val7, val8, val9)
SELECT 1 FROM DUAL;
```

**Implementation:**
```csharp
private ISqlContainer BuildCreateBatchOracle(TEntity[] entities, ISqlDialect dialect)
{
    // Use INSERT ALL syntax
    // Still efficient, just different SQL
}
```

**Performance:** Similar to standard multi-row VALUES

---

### 3. Batched UPDATE Strategy

#### ✅ Temp Table + JOIN (PostgreSQL, SqlServer, Oracle)

**Best for large batches:**

```sql
-- Create temp table
CREATE TEMP TABLE tmp (id INT, col1 VARCHAR, col2 INT);

-- Insert batch data
INSERT INTO tmp VALUES (1, 'a', 10), (2, 'b', 20);

-- Update with JOIN
UPDATE main_table t
SET col1 = tmp.col1, col2 = tmp.col2
FROM tmp
WHERE t.id = tmp.id;
```

**Implementation:**
```csharp
private async Task<int> UpdateManyBatched(TEntity[] entities, ISqlDialect dialect)
{
    // 1. Create temp table
    // 2. Bulk insert into temp
    // 3. UPDATE ... FROM temp
    // 4. Drop temp table
}
```

---

#### ✅ MERGE Statement (Oracle, Firebird, SqlServer)

**Oracle, Firebird, SQL Server support MERGE:**

```sql
MERGE INTO main_table t
USING (VALUES (1, 'a', 10), (2, 'b', 20)) AS src(id, col1, col2)
ON t.id = src.id
WHEN MATCHED THEN
    UPDATE SET t.col1 = src.col1, t.col2 = src.col2;
```

---

#### ⚠️ Individual UPDATEs in Transaction (MySQL, MariaDB, SQLite)

**For databases without good batch UPDATE support:**

```csharp
// Transaction-wrapped individual updates
await using var tx = await ctx.BeginTransaction();
foreach (var entity in entities)
{
    await UpdateAsync(entity, ctx);
}
await tx.Commit();
```

**Still faster than without transaction:** 5-10x speedup

---

### 4. Concurrent Strategy
**Compatibility: ✅ ALL DATABASES**

Works on all databases - just parallel execution with throttling.

```csharp
using var semaphore = new SemaphoreSlim(maxConcurrency);
var tasks = entities.Select(async entity =>
{
    await semaphore.WaitAsync();
    try { await CreateAsync(entity); }
    finally { semaphore.Release(); }
});
await Task.WhenAll(tasks);
```

**Best for:**
- High-latency connections (cloud databases)
- I/O-bound workloads
- Databases with good connection pooling

**Performance:** 5-20x faster for network-bound operations

---

### 5. Provider-Optimized Strategy

#### ✅ PostgreSQL COPY

**Fastest bulk insert for PostgreSQL:**

```csharp
using (var writer = await conn.BeginBinaryImportAsync("COPY table FROM STDIN (FORMAT BINARY)"))
{
    foreach (var entity in entities)
    {
        await writer.StartRowAsync();
        await writer.WriteAsync(entity.Col1);
        await writer.WriteAsync(entity.Col2);
    }
    await writer.CompleteAsync();
}
```

**Performance:** 50-100x faster than individual inserts

**Library:** Npgsql has built-in COPY support

---

#### ✅ SQL Server SqlBulkCopy

**Fastest bulk insert for SQL Server:**

```csharp
using (var bulkCopy = new SqlBulkCopy(connection))
{
    bulkCopy.DestinationTableName = "TableName";
    bulkCopy.BatchSize = 1000;

    // Map columns
    bulkCopy.ColumnMappings.Add("Col1", "Col1");

    // Write from DataTable or IDataReader
    await bulkCopy.WriteToServerAsync(dataTable);
}
```

**Performance:** 50-100x faster than individual inserts

**Library:** Microsoft.Data.SqlClient built-in

---

#### ✅ DuckDB COPY

**Similar to PostgreSQL:**

```sql
COPY table FROM 'data.csv' (FORMAT CSV);
```

Or programmatic COPY with DuckDB.NET

**Performance:** 50-100x faster

---

#### ⚠️ Oracle - Future Implementation

**Options:**
1. Oracle SQL*Loader (external tool)
2. Array binding (OracleCommand with array parameters)
3. Direct path API

**For now:** Use batched INSERT ALL (still very fast)

---

#### ⚠️ MySQL/MariaDB - Future Implementation

**Options:**
1. LOAD DATA INFILE (requires file permissions)
2. Prepared statements with batch execution

**For now:** Use batched multi-row INSERT (already very efficient)

---

#### ⚠️ SQLite - Limited Benefit

**SQLite is file-based and single-writer:**
- Provider-optimized wouldn't help much
- Batched INSERT in transaction is already optimal
- Single-writer lock is the bottleneck

**For now:** Use batched INSERT in transaction

---

#### ⚠️ Firebird - Future Implementation

**Options:**
1. Array DML (execute with parameter arrays)
2. Batch API (Firebird 4.0+)

**For now:** Use batched multi-row INSERT

---

## Recommended Strategy by Database

| Database | Best Strategy | 2nd Best | Notes |
|----------|---------------|----------|-------|
| PostgreSQL | ProviderOptimized (COPY) | Batched | COPY is 50-100x faster |
| SqlServer | ProviderOptimized (SqlBulkCopy) | Batched | SqlBulkCopy is 50-100x faster |
| Oracle | Batched (INSERT ALL) | Concurrent | INSERT ALL is very efficient |
| Firebird | Batched | Concurrent | Multi-row INSERT since 3.0 |
| CockroachDB | Batched | Concurrent | No COPY, but batched is great |
| MariaDB | Batched | Concurrent | Multi-row INSERT very efficient |
| MySQL | Batched | Concurrent | Multi-row INSERT very efficient |
| SQLite | Batched | Sequential | Single-writer limits concurrency |
| DuckDB | ProviderOptimized (COPY) | Batched | COPY for analytical workloads |

---

## Implementation Priority

### Phase 1 (Immediate) - Core Strategies
1. ✅ Sequential - Works everywhere
2. ✅ Batched (standard multi-row VALUES) - Works on 8/9 databases
3. ⚠️ Batched (Oracle INSERT ALL) - Special case for Oracle

### Phase 2 (Short-term) - Concurrent
4. ✅ Concurrent with throttling - Works everywhere

### Phase 3 (Medium-term) - Provider Optimizations
5. ✅ PostgreSQL COPY
6. ✅ SQL Server SqlBulkCopy
7. ✅ DuckDB COPY

### Phase 4 (Long-term) - Additional Optimizations
8. ⚠️ Oracle array binding
9. ⚠️ MySQL LOAD DATA
10. ⚠️ Firebird batch API

---

## Auto Strategy Selection Logic

```csharp
private BulkStrategy SelectAutoStrategy(SupportedDatabase product, int entityCount)
{
    // Provider-optimized for large batches on supported databases
    if (entityCount > 1000)
    {
        switch (product)
        {
            case SupportedDatabase.PostgreSql:
            case SupportedDatabase.SqlServer:
            case SupportedDatabase.DuckDB:
                return BulkStrategy.ProviderOptimized; // COPY/SqlBulkCopy
        }
    }

    // Batched for medium to large batches
    if (entityCount > 10)
    {
        return BulkStrategy.Batched;
    }

    // Sequential for small batches (overhead not worth it)
    return BulkStrategy.Sequential;
}
```

---

## Dialect-Specific SQL Generation

We'll need to extend ISqlDialect to support batch operations:

```csharp
public interface ISqlDialect
{
    // Existing methods...

    // New methods for bulk operations
    bool SupportsBatchInsert { get; }
    int MaxParametersPerBatch { get; }
    string BuildBatchInsertSql(string tableName, IReadOnlyList<string> columnNames, int rowCount);
    bool SupportsProviderOptimizedBulkInsert { get; }
}
```

**Implementation per dialect:**
- Standard: Multi-row VALUES
- Oracle: INSERT ALL syntax
- Others: Standard syntax with parameter limits respected

---

## Summary

✅ **ALL strategies work on ALL databases** with appropriate dialect handling:

1. **Sequential**: Universal ✅
2. **Batched**: Universal with Oracle requiring INSERT ALL syntax ⚠️
3. **Concurrent**: Universal ✅
4. **ProviderOptimized**: Available for PostgreSQL, SqlServer, DuckDB ✅

The proposal is **fully compatible** with all your supported databases!
