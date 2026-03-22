# pengdows.crud Parameter Naming Convention

This document describes the predictable parameter naming patterns used throughout pengdows.crud's
TableGateway operations. Understanding these names is essential when reusing SQL containers via
`SetParameterValue()`.

## Parameter Prefixes at a Glance

| Prefix | Meaning | Counter method | Used in |
|--------|---------|----------------|---------|
| `i{n}` | INSERT value | `NextIns()` | `BuildCreate`, `BuildUpsert`, `BuildBatchCreate` |
| `s{n}` | SET value (UPDATE) | `NextSet()` | `BuildUpdateAsync`, `BuildBatchUpdate` |
| `w{n}` | WHERE value (retrieve) | `NextWhere()` | `BuildRetrieve` IN/ANY clause |
| `k{n}` | KEY (identity/pk) | `NextKey()` | `BuildDelete`, `BuildUpdateAsync` WHERE id, entity lookup |
| `v{n}` | VERSION (concurrency) | `NextVer()` | `BuildUpdateAsync` optimistic lock check |
| `j{n}` | JOIN condition | `NextJoin()` | Custom joins |
| `b{n}` | BATCH value | `NextBatch()` | `BuildBatchCreate`, `BuildBatchUpdate`, `BuildBatchUpsert` |

**Key rule**: always pass the base name (no `@`/`:`/`$` prefix) to `SetParameterValue()`.

---

## Per-Operation Detail

### BuildRetrieve (by ID collection)

```csharp
var sc = gateway.BuildRetrieve(new[] { 1 });
```

- **Single-element or multi-element IN clause**: `w0`, `w1`, `w2`, ... (one slot per bucket)
- **PostgreSQL ANY clause** (set-valued): `w0` holds the entire array

**Container reuse — single ID (scalar)**:
```csharp
// GlobalSetup — build once
_readSingleSc = gateway.BuildRetrieve(new[] { 1 });

// Hot loop — update the scalar id each iteration
_readSingleSc.SetParameterValue("w0", nextId);   // scalar int/long/Guid/string
result = await gateway.LoadSingleAsync(_readSingleSc);
```

**Container reuse — multiple IDs (array, PostgreSQL ANY)**:
```csharp
// PostgreSQL: whole array goes into w0
_readManySc = gateway.BuildRetrieve(new[] { 1, 2, 3 });
_readManySc.SetParameterValue("w0", new[] { 4, 5, 6 });
```

**Container reuse — multiple IDs (IN clause, other databases)**:
```csharp
// Non-PostgreSQL: bucketed slots w0, w1, w2, ...
// Reuse requires same element count (same bucket size).
// Prefer Clone() when count changes.
_readManySc.SetParameterValue("w0", id1);
_readManySc.SetParameterValue("w1", id2);
```

---

### BuildDelete

```csharp
var sc = gateway.BuildDelete(0);
```

The WHERE clause for the row ID uses `NextKey()` — parameter name `"k0"`.

```csharp
// GlobalSetup
_deleteSc = gateway.BuildDelete(0);   // dummy id establishes SQL shape

// Hot loop
_deleteSc.SetParameterValue("k0", idToDelete);
await _deleteSc.ExecuteNonQueryAsync();
```

---

### BuildUpdateAsync

```csharp
var sc = await gateway.BuildUpdateAsync(entity);
```

SET parameters are generated left-to-right across the entity's updateable columns using `NextSet()`:
`s0`, `s1`, `s2`, ...

The WHERE id is generated next using `NextKey()`. Its index follows immediately after the last SET
parameter. For a 5-column entity (s0–s4) the id slot is `k0` (the key counter starts at 0
independently).

| Clause | Names | Counter |
|--------|-------|---------|
| SET values | `s0`, `s1`, `s2`, ... | `NextSet()` |
| WHERE id | `k0` | `NextKey()` |
| WHERE version | `v0` | `NextVer()` (only if `[Version]` column exists) |

> **Important**: NULL column values are skipped in the SET clause. The index of `k0` is always 0
> in the key counter, regardless of how many SET parameters exist.

```csharp
// Seed the container in GlobalSetup with a representative entity
_updateSc = await gateway.BuildUpdateAsync(new BenchEntity
{
    Id = 1, Name = "seed", Age = 30, Salary = 50000.0,
    IsActive = true, CreatedAt = DateTime.UtcNow.ToString("O")
});

// Hot loop — update only the fields that change
// Column order: name=s0, age=s1, salary=s2, is_active=s3, created_at=s4
_updateSc.SetParameterValue("s2", newSalary);       // salary (3rd updatable col)
_updateSc.SetParameterValue("k0", targetId);         // WHERE id = ?
await _updateSc.ExecuteNonQueryAsync();
```

---

### BuildCreate

```csharp
var sc = gateway.BuildCreate(entity);
```

INSERT value parameters use `NextIns()`: `i0`, `i1`, `i2`, ...

Column order follows entity declaration order, excluding non-insertable columns
(non-writable `[Id(false)]` columns are omitted from INSERT).

```csharp
var sc = gateway.BuildCreate(entity);
sc.SetParameterValue("i0", "New Name");   // first insertable column
sc.SetParameterValue("i1", 25);           // second insertable column
```

> **Tip**: For CreateAsync benchmarks, entity fields change each iteration — just call
> `gateway.BuildCreate(entity)` fresh each time rather than trying to reuse.

---

### BuildUpsert

Parameters follow the same `i{n}` pattern as INSERT, pre-computed by the dialect from the
cached template's `UpsertParameterNames`. The exact SQL depends on the database:
- PostgreSQL/CockroachDB: `INSERT ... ON CONFLICT DO UPDATE SET ...`
- MySQL/MariaDB: `INSERT ... ON DUPLICATE KEY UPDATE ...`
- SQL Server/Oracle: `MERGE ...`

---

### Batch operations (BuildBatchCreate / BuildBatchUpdate / BuildBatchUpsert)

Batch operations use `NextBatch()`: `b0`, `b1`, `b2`, ... sequentially across all rows.

NULL values are inlined (not parameterized). Auto-chunking respects `MaxParameterLimit`.

```
// 3 rows, 2 columns each → b0, b1, b2, b3, b4, b5
INSERT INTO t (col1, col2) VALUES (@b0, @b1), (@b2, @b3), (@b4, @b5)
```

---

## SetParameterValue — rules and gotchas

```csharp
// Always use the base name, never the database prefix:
sc.SetParameterValue("w0", value);    // ✓ correct
sc.SetParameterValue("@w0", value);   // ✗ wrong — don't include the prefix
sc.SetParameterValue(":w0", value);   // ✗ wrong

// For BuildRetrieve reuse, always pass a SCALAR value:
sc.SetParameterValue("w0", 42);             // ✓ scalar int
sc.SetParameterValue("w0", new[] { 42 });   // ✗ int[] — throws for SQLite/DuckDB/SQL Server

// Exception: PostgreSQL ANY(@w0) expects the array:
sc.SetParameterValue("w0", new[] { 1, 2, 3 });  // ✓ only on PostgreSQL set-valued containers
```

## How to discover parameter names at runtime

```csharp
var sc = await gateway.BuildUpdateAsync(entity);
// Print all parameter names to understand slot order:
foreach (var p in sc.Parameters)
    Console.WriteLine($"{p.ParameterName} = {p.Value}");
```

## Database portability

Parameter names (`w0`, `k0`, `s2`) are database-agnostic. `MakeParameterName()` adds the
correct database-specific prefix before the name is embedded in SQL:

| Database | Prefix | In SQL |
|----------|--------|--------|
| SQL Server | `@` | `@w0` |
| PostgreSQL | `$` | `$w0` (positional index) |
| Oracle | `:` | `:w0` |
| MySQL/MariaDB | positional | `?` |
| SQLite | `@` | `@w0` |
| DuckDB | `$` | `$w0` |

The base name (without prefix) is always what you pass to `SetParameterValue()`.

## Implementation: ClauseCounters

All counter state lives in a `ClauseCounters` struct (value type, no heap allocation). Each
Build* method creates a fresh `ClauseCounters` instance — counters never carry over between calls.

Pre-built string caches avoid allocation for typical entity sizes:

| Cache | Size |
|-------|------|
| `i{n}` INSERT cache | 64 |
| `s{n}` SET cache | 64 |
| `w{n}` WHERE cache | 64 |
| `b{n}` BATCH cache | 256 |
| `k{n}` KEY cache | 16 |
| `j{n}` JOIN cache | 16 |
| `v{n}` VERSION cache | 8 |

For entities with more columns than the cache size, names fall back to string interpolation.
