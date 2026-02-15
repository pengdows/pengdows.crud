# Performance Opportunities Analysis

Based on Grok's code review suggestions. Each item rated by **Impact** and **Effort**.

---

## 1. OrderedDictionary: Lower Small-Mode Threshold

**Current**: Switches to hash-mode at **>8 items**
**Grok's Suggestion**: Switch at **>4 items**
**Location**: `pengdows.crud/collections/OrderedDictionary.cs` line 46

### Analysis
- **Impact**: ⭐⭐ (Low-Medium)
  - Most SQL queries have 1-10 parameters
  - Linear search on 5-8 items is ~10-20ns on modern CPUs
  - Hash lookup overhead is ~30-40ns (hashing + bucket lookup)
  - **Only beneficial if >4 parameters AND frequent lookups**

- **Effort**: ⭐ (Trivial)
  - Change `SmallCapacity = 8` to `SmallCapacity = 4`
  - Existing tests should validate behavior

### Verdict
**LOW PRIORITY** - The current threshold of 8 is well-tuned for SQL parameter use cases. Most queries have <10 parameters, and the linear search overhead is negligible.

**Recommendation**: Keep current threshold unless benchmarks show measurable regression for 5-8 parameter queries.

---

## 2. Use Span<T> in Hash-Mode Bucket Operations

**Current**: Array indexing with bounds checks
**Grok's Suggestion**: Use `Span<T>` to eliminate bounds checks and reduce allocations

### Analysis
- **Impact**: ⭐⭐⭐ (Medium)
  - Removes bounds checks in hot loops
  - Better vectorization opportunities for JIT
  - ~5-10% improvement in hash-mode operations

- **Effort**: ⭐⭐ (Low-Medium)
  - Replace array operations with span slices
  - Ensure `AggressiveInlining` attributes remain effective
  - Test thoroughly (different behavior in Debug vs Release)

### Verdict
**MEDIUM PRIORITY** - Measurable performance gain for queries with >8 parameters.

**Recommendation**: Implement if profiling shows OrderedDictionary in hot path for typical workloads.

---

## 3. TypeMapRegistry: Cache Globally Per Type

**Current**: Each `DatabaseContext` has its own `TypeMapRegistry` instance
**Grok's Suggestion**: Single global cache to avoid per-gateway cost

### Analysis
- **Impact**: ⭐⭐⭐⭐ (High - **BUT WRONG DIRECTION**)
  - **This would BREAK multi-tenancy!**
  - Different tenants may have different connection strings/databases
  - Dialect detection is per-context (e.g., SQL Server vs PostgreSQL)

- **Current Design**: ✅ **ALREADY OPTIMIZED**
  - TypeMapRegistry caches metadata **per type** within each context
  - Reflection scanning happens **once per type per context**
  - Cost: ~0.1-1ms per entity type on first access (negligible)

### Verdict
**DO NOT IMPLEMENT** - Current design is correct for multi-tenant architecture.

**Counter-Analysis**:
- Grok assumed single-tenant scenario
- Per-context caching is necessary for isolation
- Performance impact is insignificant (one-time reflection per type)

---

## 4. JsonAttribute: Pool JsonSerializerOptions

**Current**: New `JsonSerializerOptions` created per column in TypeMapRegistry
**Grok's Suggestion**: Pool or cache `JsonSerializerOptions` instances

### Analysis
- **Impact**: ⭐ (Very Low)
  - JsonSerializerOptions created **once per entity type** during registration
  - Not created per-operation
  - Memory cost: ~200-300 bytes per JSON column per entity type

- **Effort**: ⭐⭐ (Low-Medium)
  - Create static `ConcurrentDictionary<Type, JsonSerializerOptions>`
  - Key by property type or custom configuration

### Verdict
**LOW PRIORITY** - Already minimized (created once per type, not per operation).

**Recommendation**: Only implement if source generation becomes available (future .NET versions).

---

## 5. FakeParameterCollection: Replace LINQ with Loops

**Current**: Uses LINQ (`IndexOf`, `Contains`) in fakeDb parameter collection
**Grok's Suggestion**: Replace with explicit loops for O(1) operations

### Analysis
- **Impact**: ⭐ (Negligible)
  - fakeDb is for **unit testing only** - performance irrelevant
  - Tests run in milliseconds regardless

- **Effort**: ⭐⭐ (Low-Medium)
  - Would add code complexity
  - No benefit for production code (fakeDb never deployed)

### Verdict
**DO NOT IMPLEMENT** - Test infrastructure performance is not critical.

**Reasoning**: Premature optimization in test-only code. Keep tests readable over performant.

---

## 6. Batch Audit Resolutions in Bulk Operations

**Current**: `IAuditValueResolver.Resolve()` called once per entity in bulk operations
**Grok's Suggestion**: Resolve once, apply to entire batch

### Analysis
- **Impact**: ⭐⭐⭐⭐ (High for bulk operations)
  - Current: `Resolve()` called N times for N entities
  - Proposed: `Resolve()` called once, reused for batch
  - **Savings**: ~1-5μs per entity (resolver overhead)
  - **Example**: 1000-entity batch = ~1-5ms savings

- **Effort**: ⭐⭐⭐ (Medium)
  - Add batch-aware methods to TableGateway
  - Maintain backward compatibility
  - Handle edge case where audit values change mid-batch (clock drift, user switching)

### Verdict
**HIGH PRIORITY for 2.1** - Significant win for bulk inserts/updates.

**Implementation Plan**:
```csharp
// New method signature
public async Task<int> CreateBatchAsync(
    IEnumerable<TEntity> entities,
    IDatabaseContext? context = null)
{
    var auditValues = _auditValueResolver?.Resolve(); // Once per batch
    foreach (var entity in entities)
    {
        ApplyAuditValues(entity, auditValues); // Reuse resolved values
        // ... INSERT logic
    }
}
```

---

## 7. Use ValueTask for High-Allocation Async Methods

**Current**: All async methods return `Task` or `Task<T>`
**Grok's Suggestion**: Use `ValueTask<T>` for methods called frequently

### Analysis
- **Impact**: ⭐⭐⭐ (Medium-High)
  - `Task<T>` allocates ~96 bytes per call on heap
  - `ValueTask<T>` can avoid allocation if synchronously completed
  - **Hot paths**: `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `RetrieveAsync`
  - **Savings**: ~50-200 bytes per CRUD operation

- **Effort**: ⭐⭐⭐⭐ (High)
  - Breaking change to public API (requires major version bump)
  - Must update all implementations and tests
  - Consumer code may need adjustments (ValueTask restrictions)

### Verdict
**CONSIDER for 3.0** - Requires major version (breaking change).

**Trade-offs**:
- ✅ Reduces allocations by 30-50% for sync-heavy workloads
- ❌ Breaking change (all consumers must update)
- ❌ ValueTask has restrictions (can't await multiple times)
- ❌ More complex error handling

---

## Summary & Recommendations

### Implement Now (2.0.x patch)
- None - all critical optimizations already in place

### Consider for 2.1 (Minor Release)
1. ⭐⭐⭐⭐ **Batch audit resolution** - High impact, medium effort
2. ⭐⭐⭐ **Span<T> in OrderedDictionary** - Medium impact if profiling confirms

### Consider for 3.0 (Major Release)
1. ⭐⭐⭐ **ValueTask migration** - Breaking change, high impact for high-throughput scenarios

### Do Not Implement
1. ❌ **Global TypeMapRegistry cache** - Breaks multi-tenancy
2. ❌ **FakeDb LINQ → loops** - Test-only code, no production benefit
3. ❌ **Lower small-mode threshold** - Current tuning is optimal
4. ❌ **Pool JsonSerializerOptions** - Already minimized

---

## Benchmark Priority

If pursuing optimizations, benchmark these scenarios first:

1. **Bulk operations** (1000+ entities) - audit batching impact
2. **High-parameter queries** (>10 params) - OrderedDictionary + Span<T> impact
3. **Sync-heavy workloads** (in-memory SQLite) - ValueTask impact

Current 1.67x gap vs Dapper is **architectural** (safety + abstraction), not micro-optimization opportunities.
