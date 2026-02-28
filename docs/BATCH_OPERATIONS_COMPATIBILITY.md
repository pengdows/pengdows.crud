# Batch Operations Database Compatibility Matrix

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

#### Oracle

No provider-optimized path implemented. Uses batched INSERT ALL, which is already efficient.
See [Future Work](FUTURE_WORK.md) for array binding notes.

---

#### MySQL / MariaDB

No provider-optimized path implemented. Uses batched multi-row INSERT with ON DUPLICATE KEY
UPDATE for upserts. `LOAD DATA INFILE` is an option for bulk loads but requires server-side
file permissions. See [Future Work](FUTURE_WORK.md).

---

#### SQLite

No provider-optimized path and none planned. SQLite is single-writer; the bottleneck is the
write lock, not the parameter count. Batched INSERT in a transaction is already optimal.

---

#### Firebird

No provider-optimized path implemented. Uses batched multi-row INSERT. Firebird 4.0 introduced
a native batch API (`FbBatchCommand`) that could be used in a future provider-specific path.
See [Future Work](FUTURE_WORK.md).

---

## Strategy by Database (current implementation)

| Database | Batch INSERT | Batch UPDATE | Batch UPSERT |
|----------|-------------|--------------|--------------|
| PostgreSQL | Multi-row VALUES | UPDATE FROM VALUES | INSERT … ON CONFLICT |
| SQL Server | Multi-row VALUES | MERGE | MERGE |
| Oracle | INSERT ALL | one UPDATE per row | MERGE (one per row) |
| MySQL / MariaDB | Multi-row VALUES | one UPDATE per row | INSERT … ON DUPLICATE KEY UPDATE |
| SQLite / CockroachDB | Multi-row VALUES | one UPDATE per row | INSERT … ON CONFLICT |
| DuckDB | Multi-row VALUES | one UPDATE per row | INSERT … ON CONFLICT |
| Firebird | Multi-row VALUES | one UPDATE per row | MERGE (one per row) |
