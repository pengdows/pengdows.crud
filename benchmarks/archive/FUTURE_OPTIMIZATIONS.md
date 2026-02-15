# Future Optimization Roadmap

## Overview
This document outlines potential future optimizations for pengdows.crud 2.0 based on the analysis conducted during the 2026-02-07 optimization session.

## Current State (Post-Optimization)
- ✅ 4 major optimizations completed
- ✅ 8x faster than EF for bulk operations
- ✅ 30-82% less memory usage vs EF
- ✅ All 3,485 tests passing
- ✅ Zero regressions introduced

## Remaining Opportunities

### High Impact (Deferred Due to Complexity)

#### 1. Convert.ChangeType Caching with Compiled Delegates
**Status**: Deferred
**Expected Impact**: 3-5% improvement in type coercion-heavy workloads
**Complexity**: High
**Effort**: 2-3 days

**Description**:
Create a `ConcurrentDictionary<(Type source, Type target), Func<object, object>>` cache that stores compiled expression delegates for common type conversions.

**Implementation Approach**:
```csharp
private static readonly ConcurrentDictionary<(Type, Type), Func<object, object>> _conversionCache = new();

private static Func<object, object> CompileConverter(Type source, Type target)
{
    var param = Expression.Parameter(typeof(object), "value");
    var convert = Expression.Convert(
        Expression.Convert(param, source),
        target
    );
    var lambda = Expression.Lambda<Func<object, object>>(
        Expression.Convert(convert, typeof(object)),
        param
    );
    return lambda.Compile();
}

public static object CoerceWithCache(object value, Type target)
{
    var sourceType = value.GetType();
    var converter = _conversionCache.GetOrAdd(
        (sourceType, target),
        key => CompileConverter(key.Item1, key.Item2)
    );
    return converter(value);
}
```

**Files to Modify**:
- `pengdows.crud/TypeCoercionHelper.cs` (add cache)
- `pengdows.crud/TableGateway.Core.cs` (replace Convert.ChangeType calls)
- `pengdows.crud/TableGateway.Update.cs` (replace Convert.ChangeType calls)
- `pengdows.crud/TableGateway.Upsert.cs` (replace Convert.ChangeType calls)

**Test Requirements**:
- Test all numeric type conversions (int→long, long→int, etc.)
- Test nullable conversions
- Test Guid conversions
- Test enum conversions
- Verify thread safety
- Benchmark to confirm 3-5% gain

**Risks**:
- Expression compilation adds startup cost
- Cache unbounded growth if many unique type pairs
- Edge cases with nullable types

**Mitigation**:
- Implement cache size limit with LRU eviction
- Pre-compile common conversions at startup
- Comprehensive edge case testing

---

#### 2. ValueTask<T> for Async Hot Paths
**Status**: Deferred
**Expected Impact**: 10-15% allocation reduction in transaction scenarios
**Complexity**: High
**Effort**: 3-5 days

**Description**:
Convert hot async methods to `ValueTask<T>` to eliminate Task allocations when operations complete synchronously (e.g., when connection is already open in a transaction context).

**Current Limitation**:
Most async methods have multiple await calls for locks and connection management, so they rarely complete synchronously. This limits the benefit of ValueTask<T>.

**Potential Candidates**:
- `SqlContainer.ExecuteScalarAsync<T>()` - if result is cached
- `TableGateway.RetrieveOneAsync(TRowID)` - if connection already open
- Cache lookup methods that might complete synchronously

**Implementation Approach**:
```csharp
// Before:
public async Task<int> ExecuteNonQueryAsync(...)
{
    // implementation
}

// After:
public ValueTask<int> ExecuteNonQueryAsync(...)
{
    // Check if can complete synchronously
    if (CanCompleteSynchronously())
    {
        return new ValueTask<int>(SyncResult());
    }
    return new ValueTask<int>(ExecuteNonQueryAsyncCore(...));
}

private async Task<int> ExecuteNonQueryAsyncCore(...)
{
    // Original async implementation
}
```

**Files to Modify**:
- `pengdows.crud/SqlContainer.cs` (ExecuteNonQueryAsync, ExecuteScalarAsync)
- `pengdows.crud/TableGateway.Core.cs` (CreateAsync, UpdateAsync, DeleteAsync)
- `pengdows.crud.abstractions/ISqlContainer.cs` (interface signatures)
- **All callers** must be updated to handle ValueTask<T>

**Test Requirements**:
- Verify all async tests still pass
- Test synchronous completion path
- Test asynchronous completion path
- Verify no deadlocks with .GetAwaiter().GetResult()
- Benchmark allocation reduction

**Risks**:
- Breaking change to async signatures
- Potential for incorrect consumption (awaiting multiple times)
- Extensive testing required for all call sites

**Mitigation**:
- Phased rollout (start with internal methods)
- Add analyzer rules to prevent ValueTask misuse
- Comprehensive async test coverage
- Consider making it opt-in via feature flag

---

### Medium Impact (Incremental Improvements)

#### 3. Enhanced ArrayPool Configuration
**Status**: Not Started
**Expected Impact**: 1-2% reduction in pool contention
**Complexity**: Low
**Effort**: 0.5-1 day

**Description**:
Fine-tune ArrayPool rent/return patterns based on actual usage patterns observed in production telemetry.

**Current State**:
- Uses ArrayPool<string>.Shared and ArrayPool<Type>.Shared
- Generic pool sizes (default configuration)
- No custom pool configuration

**Potential Improvements**:
- Create custom ArrayPool instances for hot paths
- Pre-warm pools with commonly used sizes
- Adjust pool sizes based on workload patterns

**Implementation**:
```csharp
private static readonly ArrayPool<string> _namePool =
    ArrayPool<string>.Create(maxArrayLength: 32, maxArraysPerBucket: 50);

// Use custom pool instead of Shared
var names = _namePool.Rent(fieldCount);
```

**Measurement Required**:
- Profile production workload
- Measure contention on Shared pools
- Determine optimal pool sizes
- Benchmark custom vs Shared pools

---

#### 4. Static Lambda Optimization
**Status**: Not Started
**Expected Impact**: <1% allocation reduction
**Complexity**: Low
**Effort**: 0.5 day

**Description**:
Convert non-static lambdas that don't capture variables to static lambdas to avoid delegate allocation.

**Example Opportunities**:
```csharp
// Before (captures 'dialect'):
return _wrappedTableNameCache.GetOrAdd(dialect, d => BuildTableName(d));

// After (static):
return _wrappedTableNameCache.GetOrAdd(dialect, static d => BuildTableName(d));
```

**Files to Review**:
- All TableGateway*.cs files
- SqlContainer.cs
- Look for lambda expressions in GetOrAdd patterns

**Note**: Many lambdas in the codebase are already in cached GetOrAdd patterns that only execute once, so the benefit is minimal.

---

### Low Impact (Micro-optimizations)

#### 5. SIMD Vectorization for Reader Mapping
**Status**: Research Phase
**Expected Impact**: 2-5% for large result sets
**Complexity**: Very High
**Effort**: 5-10 days

**Description**:
Use SIMD (System.Runtime.Intrinsics) to parallelize field reading and mapping for result sets with many rows.

**Potential Use Cases**:
- Bulk field reads from DbDataReader
- Parallel type coercion for multiple fields
- Batch null checks

**Prerequisites**:
- Profile to identify bottlenecks
- Verify SIMD provides measurable benefit
- Ensure cross-platform compatibility

**Risk**: Very high complexity for marginal gain. Only pursue if profiling shows reader mapping is a significant bottleneck.

---

#### 6. Connection String Parsing Cache
**Status**: Not Started
**Expected Impact**: <1% startup improvement
**Complexity**: Low
**Effort**: 0.25 day

**Description**:
Cache parsed connection string components to avoid repeated parsing.

**Current State**:
Connection strings are parsed on each DatabaseContext creation.

**Potential Improvement**:
```csharp
private static readonly ConcurrentDictionary<string, ParsedConnectionString> _connectionStringCache = new();

public ParsedConnectionString ParseConnectionString(string connectionString)
{
    return _connectionStringCache.GetOrAdd(connectionString, cs => Parse(cs));
}
```

**Note**: Only beneficial if many DatabaseContext instances are created with the same connection string.

---

## Optimization Decision Matrix

| Optimization | Impact | Complexity | Effort | Priority | Status |
|--------------|--------|------------|--------|----------|--------|
| Convert.ChangeType Caching | High (3-5%) | High | 2-3 days | Medium | Deferred |
| ValueTask<T> | High (10-15%) | Very High | 3-5 days | Low | Deferred |
| ArrayPool Configuration | Low (1-2%) | Low | 0.5-1 day | Low | Not Started |
| Static Lambdas | Very Low (<1%) | Low | 0.5 day | Low | Not Started |
| SIMD Vectorization | Medium (2-5%) | Very High | 5-10 days | Very Low | Research |
| Connection String Caching | Very Low (<1%) | Low | 0.25 day | Very Low | Not Started |

---

## Recommended Next Steps

### If Pursuing Further Optimization:

1. **Profile Production Workload First**
   - Identify actual bottlenecks with real data
   - Measure impact of existing optimizations
   - Validate assumptions about hot paths

2. **Start with Convert.ChangeType Caching**
   - Highest impact remaining optimization
   - Well-defined scope
   - Measurable benefit

3. **Consider ValueTask<T> if Transaction-Heavy**
   - Only if profiling shows significant Task allocation in transaction scenarios
   - Requires extensive testing
   - Breaking change to async signatures

4. **Defer Micro-optimizations**
   - ArrayPool tuning, static lambdas, connection string caching have minimal impact
   - Only pursue if profiling identifies specific bottlenecks

### Monitoring and Validation:

- Deploy current optimizations to production
- Monitor memory usage, GC pressure, and response times
- Validate 10-15% improvement in real-world scenarios
- Use production telemetry to guide next optimization priorities

---

## Conclusion

The completed optimizations (pre-sized collections, Span<char>, string interning, Guid.TryFormat) deliver significant measurable benefits with low risk. Remaining optimizations have higher complexity and lower incremental benefit.

**Recommendation**:
- ✅ Deploy current optimizations to production
- 📊 Monitor and validate real-world impact
- 🔍 Profile production workload before pursuing additional optimizations
- 🎯 Only implement Convert.ChangeType caching if profiling confirms it's a bottleneck

The law of diminishing returns has kicked in. Further optimization should be driven by production telemetry, not speculation.
