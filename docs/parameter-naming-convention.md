# pengdows.crud Parameter Naming Convention

This document describes the predictable parameter naming patterns used throughout pengdows.crud's TableGateway operations.

## Overview

All parameter names in pengdows.crud follow a consistent pattern based on operation type and parameter order. This enables advanced scenarios like SQL container reuse while maintaining database portability.

## Parameter Naming Patterns by Operation

### RETRIEVE Operations
- **Method**: `BuildRetrieve()`, `RetrieveAsync()`
- **Single ID**: `"w0"` 
- **Multiple IDs**: `"w0"` (for ANY/IN clause parameter)
- **Pattern**: `w{index}` where `w` = "where"

**Examples:**
```csharp
// Single entity
var container = helper.BuildRetrieve(new[] { 123 });
container.SetParameterValue("w0", 456); // Update ID

// Multiple entities  
var container = helper.BuildRetrieve(new[] { 1, 2, 3 });
container.SetParameterValue("w0", new[] { 4, 5, 6 }); // Update ID array
```

### UPDATE Operations
- **Method**: `BuildUpdateAsync()`, `UpdateAsync()`
- **SET clause**: `"s0"`, `"s1"`, `"s2"`, etc. (one per updated column)
- **WHERE clause**: Uses key parameter patterns
- **Version check**: `"v0"`, `"v1"`, etc.
- **Pattern**: `s{index}` for SET, `v{index}` for version

**Examples:**
```csharp
var film = new Film { Id = 1, Title = "New Title", Length = 120 };
var container = await helper.BuildUpdateAsync(film);
container.SetParameterValue("s0", "Updated Title"); // First SET parameter
container.SetParameterValue("s1", 150);            // Second SET parameter
```

### CREATE Operations  
- **Method**: `BuildCreate()`, `CreateAsync()`
- **Parameters**: `"i0"`, `"i1"`, `"i2"`, etc. (one per insertable column)
- **Pattern**: `i{index}` where `i` = "insert"

**Examples:**
```csharp
var film = new Film { Title = "New Film", Length = 90 };
var container = helper.BuildCreate(film);
container.SetParameterValue("i0", "Different Title"); // First column value
container.SetParameterValue("i1", 100);               // Second column value
```

### DELETE Operations
- **Method**: `BuildDelete()`, `DeleteAsync()`
- **Single ID**: Uses ID parameter pattern
- **Multiple IDs**: Same as RETRIEVE operations (`"w0"`)

### UPSERT Operations
- **Method**: `BuildUpsert()`, `UpsertAsync()`
- **Parameters**: Combination of INSERT and UPDATE patterns based on database dialect

## ClauseCounters System

The `ClauseCounters` class provides consistent parameter naming across all operations:

```csharp
public string NextSet() => $"s{_set++}";     // s0, s1, s2... (UPDATE SET)
public string NextWhere() => $"w{_where++}"; // w0, w1, w2... (WHERE clause)
public string NextJoin() => $"j{_join++}";   // j0, j1, j2... (JOIN conditions)
public string NextKey() => $"k{_key++}";     // k0, k1, k2... (Key columns)
public string NextVer() => $"v{_ver++}";     // v0, v1, v2... (Version checks)
public string NextIns() => $"i{_ins++}";     // i0, i1, i2... (INSERT values)
```

## Database Portability

Parameter names are database-agnostic. The `MakeParameterName()` method adds the appropriate database-specific prefix:

- **PostgreSQL**: `$w0` → `"$w0"`
- **SQL Server**: `@w0` → `"@w0"`  
- **Oracle**: `:w0` → `":w0"`
- **MySQL**: `?` (positional parameters)

When using `SetParameterValue()`, always use the **base name without the database prefix**.

## Best Practices

### SQL Container Reuse
```csharp
private ISqlContainer? _cachedContainer;

public async Task<List<Film>> GetFilmsOptimized(int[] filmIds)
{
    if (_cachedContainer == null)
    {
        _cachedContainer = _filmHelper.BuildRetrieve(filmIds);
    }
    
    // Update parameter value for reuse
    _cachedContainer.SetParameterValue("w0", filmIds);
    return await _filmHelper.LoadListAsync(_cachedContainer);
}
```

### Parameter Inspection
```csharp
// Get current parameter value
var currentValue = container.GetParameterValue("w0");
var typedValue = container.GetParameterValue<int[]>("w0");
```

## Implementation Details

- **Zero-Based**: All parameter indices start at 0
- **Incremental**: Parameters are numbered sequentially per operation
- **Predictable**: Parameter names follow consistent patterns across all operations
- **Thread-Safe**: ClauseCounters instances are not shared between operations

This naming system provides both predictability for advanced usage and complete database portability.