# pengdows.crud Version 2.0 Planning

This document tracks **breaking changes being considered for version 2.0**.

Version 1.1 maintains backward compatibility. Version 2.0 will be a clean break opportunity.

---

## Proposed Renames for Version 2.0

### Rename 1: DatabaseContext → DatabaseCoordinator or DatabaseEndpoint

**Problem:**

`DatabaseContext` creates cognitive baggage due to Entity Framework's `DbContext`:

**EF DbContext implications (ALL FALSE for pengdows.crud):**
- Short-lived (scoped per request)
- NOT thread-safe
- Owns change tracking
- Owns identity map
- Per unit-of-work
- Connection-per-context

**Actual pengdows.crud characteristics:**
- Long-lived (singleton per connection string)
- Concurrent callers supported (serialized at connection level)
- Stateless (no change tracking)
- Execution coordinator / connection lifecycle owner
- Policy + strategy container

**Result**: Even with perfect documentation, reviewers pattern-match the name and apply EF mental models, causing endless confusion.

---

### Option A: `DatabaseCoordinator`

**Pros:**
- **Semantically accurate** - Reflects orchestration/coordination role
- **Descriptive** - Clear that it coordinates database operations
- **Avoids ORM mental models** - Zero EF cognitive baggage
- **Professional terminology** - Familiar in distributed systems/architecture
- **Matches actual implementation** - It coordinates connection strategies, transactions, and execution

**Cons:**
- **Verbose** - 19 characters (vs 15 for DatabaseContext)
- **Slightly abstract** - "Coordinator" may be less immediately intuitive to beginners

**Example usage:**
```csharp
// Version 2.0
services.AddSingleton<DatabaseCoordinator>(sp =>
    new DatabaseCoordinator(connectionString, NpgsqlFactory.Instance));

var coordinator = new DatabaseCoordinator(connectionString, factory);
using var tx = coordinator.BeginTransaction();
var container = coordinator.CreateSqlContainer();
```

**Verdict**: **Strong choice.** Accurately conveys the coordination/orchestration nature without ORM baggage.

---

### Option B: `DatabaseEndpoint`

**Pros:**
- **Concise** - 16 characters (similar to original)
- **API-centric terminology** - Endpoint implies connection point/gateway
- **Familiar in microservices** - Common term for service boundaries
- **Conveys singleton nature** - Endpoints are typically long-lived
- **Clean mental model** - Single point of access to database

**Cons:**
- **Potentially misleading** - "Endpoint" often implies HTTP/network boundary
- **API connotation** - May suggest REST/RPC rather than database access
- **Less descriptive** - Doesn't convey orchestration/coordination role as clearly

**Example usage:**
```csharp
// Version 2.0
services.AddSingleton<DatabaseEndpoint>(sp =>
    new DatabaseEndpoint(connectionString, NpgsqlFactory.Instance));

var endpoint = new DatabaseEndpoint(connectionString, factory);
using var tx = endpoint.BeginTransaction();
var container = endpoint.CreateSqlContainer();
```

**Verdict**: **Weaker choice.** While concise, "endpoint" has strong HTTP/API connotations that may create new confusion.

---

### Recommendation: `DatabaseCoordinator`

**Rationale:**
- Accurately describes what the class does (coordinates execution, strategies, transactions)
- Zero cognitive overlap with EF or other ORMs
- 4 extra characters is minor cost for semantic clarity
- Industry-standard term in distributed systems and orchestration patterns

**Alternative considered**: DatabaseRuntime (previous draft) - Good but less descriptive of orchestration role

---

## Rename 2: EntityHelper → TableSql or SqlCatalog

**Problem:**

`EntityHelper<TEntity, TRowId>` undersells what the class does:
- "Helper" suggests utility/assistant rather than primary API
- Doesn't convey SQL generation and execution capabilities
- Generic "helper" suffix is overused in .NET (XxxHelper everywhere)

**Actual capabilities:**
- SQL query generation (SELECT, INSERT, UPDATE, DELETE, MERGE)
- Type-safe parameterization
- Entity-to-SQL mapping
- CRUD operations with async execution
- Schema-aware operations

---

### Option A: `TableSql<TEntity, TRowId>`

**Pros:**
- **Concise** - 8 characters base name
- **Descriptive** - Table (entity) + SQL (operations)
- **Clear scope** - Operations on a single table
- **Intuitive** - Obvious what it does
- **Good autocomplete** - TableSql* groups related types

**Cons:**
- **Ordering** - "Table" before "Sql" may feel backwards to some (Sql-first library)
- **Less formal** - Compound word without clear semantic

**Example usage:**
```csharp
var tableSql = new TableSql<Order, int>(coordinator);
var order = await tableSql.RetrieveOneAsync(orderId);
await tableSql.CreateAsync(newOrder);
```

**Verdict**: **Strong choice.** Clear, concise, descriptive.

---

### Option B: `SqlCatalog<TEntity, TRowId>`

**Pros:**
- **Professional terminology** - "Catalog" implies collection/registry of operations
- **Sql-first** - Starts with Sql (aligns with library philosophy)
- **Architectural pattern** - Service catalog is recognized pattern
- **Suggests completeness** - Catalog implies comprehensive set of operations

**Cons:**
- **Potential confusion** - "Catalog" often refers to metadata/schema (database catalog, table catalog)
- **Less obvious** - Not immediately clear what operations it provides
- **Overloaded term** - Catalog has multiple meanings in database context

**Example usage:**
```csharp
var catalog = new SqlCatalog<Order, int>(coordinator);
var order = await catalog.RetrieveOneAsync(orderId);
await catalog.CreateAsync(newOrder);
```

**Verdict**: **Weaker choice.** "Catalog" has strong metadata/schema connotations in database world.

---

### Recommendation: `TableSql<TEntity, TRowId>`

**Rationale:**
- Clear and concise
- Immediately obvious (table operations via SQL)
- Avoids overloaded terminology (catalog = metadata in DB context)
- Natural grouping with Table* prefix
- Easier to explain to new users

**Alternative considered**: EntitySql - Too close to Entity Framework terminology

---

## Summary of Proposed Renames

| Current Name | Proposed Name | Interface Name |
|--------------|---------------|----------------|
| `DatabaseContext` | `DatabaseCoordinator` | `IDatabaseCoordinator` |
| `EntityHelper<TEntity,TRowId>` | `TableSql<TEntity,TRowId>` | `ITableSql<TEntity,TRowId>` |

---

## Migration Strategy for 2.0

### Phase 1: Introduce New Names (2.0.0-beta)

**DatabaseCoordinator compatibility shim:**
```csharp
// New preferred name
public partial class DatabaseCoordinator : SafeAsyncDisposableBase, IDatabaseCoordinator
{
    // ... existing implementation
}

// Compatibility shim (obsolete warning)
[Obsolete("DatabaseContext has been renamed to DatabaseCoordinator. Use DatabaseCoordinator for new code. This alias will be removed in v3.0.")]
public sealed class DatabaseContext : DatabaseCoordinator
{
    public DatabaseContext(string connectionString, DbProviderFactory factory)
        : base(connectionString, factory)
    {
    }

    // Forward all constructors...
}

// Interface shim
[Obsolete("IDatabaseContext has been renamed to IDatabaseCoordinator. This alias will be removed in v3.0.")]
public interface IDatabaseContext : IDatabaseCoordinator { }
```

**TableSql compatibility shim:**
```csharp
// New preferred name
public partial class TableSql<TEntity, TRowId> : ITableSql<TEntity, TRowId>
    where TEntity : class, new()
    where TRowId : notnull
{
    // ... existing implementation
}

// Compatibility shim (obsolete warning)
[Obsolete("EntityHelper has been renamed to TableSql. Use TableSql for new code. This alias will be removed in v3.0.")]
public sealed class EntityHelper<TEntity, TRowId> : TableSql<TEntity, TRowId>
    where TEntity : class, new()
    where TRowId : notnull
{
    public EntityHelper(IDatabaseCoordinator coordinator)
        : base(coordinator)
    {
    }

    // Forward all constructors...
}

// Interface shim
[Obsolete("IEntityHelper has been renamed to ITableSql. This alias will be removed in v3.0.")]
public interface IEntityHelper<TEntity, TRowId> : ITableSql<TEntity, TRowId>
    where TEntity : class, new()
    where TRowId : notnull
{ }
```

**Result:**
- Existing code continues working (with warnings)
- New code uses new names
- Documentation updated to prefer new names
- Migration path is clear

### Phase 2: Deprecation Period (2.0.0 → 2.9.x)

- All examples use new names
- README shows both names initially, then only new names
- XML docs on old names point to new names
- Release notes highlight the renames

### Phase 3: Remove Compatibility Shims (3.0.0)

- Remove `DatabaseContext`, `IDatabaseContext` type aliases
- Remove `EntityHelper`, `IEntityHelper` type aliases
- Clean breaking change
- Users had full major version to migrate

---

## Impact Analysis

### Files to Rename

**Core library (DatabaseCoordinator):**
- `pengdows.crud/DatabaseContext.cs` → `DatabaseCoordinator.cs`
- All partial files: `DatabaseContext.*.cs` → `DatabaseCoordinator.*.cs`
- `pengdows.crud.abstractions/IDatabaseContext.cs` → `IDatabaseCoordinator.cs`

**Core library (TableSql):**
- `pengdows.crud/EntityHelper.cs` → `TableSql.cs`
- All partial files: `EntityHelper.*.cs` → `TableSql.*.cs`
- `pengdows.crud.abstractions/IEntityHelper.cs` → `ITableSql.cs`

**Compatibility shims (new files):**
- New `pengdows.crud/DatabaseContext.cs` (type alias, obsolete)
- New `pengdows.crud.abstractions/IDatabaseContext.cs` (interface alias, obsolete)
- New `pengdows.crud/EntityHelper.cs` (type alias, obsolete)
- New `pengdows.crud.abstractions/IEntityHelper.cs` (interface alias, obsolete)

**Tests:**
- Update all test instantiations to use new names
- Keep some tests using old names to verify compatibility shims
- Test files themselves may be renamed:
  - `DatabaseContextTests.cs` → `DatabaseCoordinatorTests.cs`
  - `EntityHelperTests.cs` → `TableSqlTests.cs`

**Documentation:**
- README.md
- ARCHITECTURE.md
- CLAUDE.md
- PENGDOWS_CRUD_OVERVIEW.md
- All XML docs
- All code examples
- Wiki pages

### Breaking Changes for Users

**Code changes required:**
```csharp
// Before (v1.x)
services.AddSingleton<DatabaseContext>(sp => ...);
var context = new DatabaseContext(...);
var helper = new EntityHelper<Order, int>(context);

// After (v2.x with compatibility shim - WORKS with warnings)
services.AddSingleton<DatabaseContext>(sp => ...);  // Warning
var context = new DatabaseContext(...);  // Warning
var helper = new EntityHelper<Order, int>(context);  // Warning

// After (v2.x preferred)
services.AddSingleton<DatabaseCoordinator>(sp => ...);
var coordinator = new DatabaseCoordinator(...);
var tableSql = new TableSql<Order, int>(coordinator);

// After (v3.x - breaking, aliases removed)
services.AddSingleton<DatabaseCoordinator>(sp => ...);  // Required
var coordinator = new DatabaseCoordinator(...);  // Required
var tableSql = new TableSql<Order, int>(coordinator);  // Required
```

**Migration effort:**
```bash
# Find-replace across codebase
DatabaseContext → DatabaseCoordinator
IDatabaseContext → IDatabaseCoordinator
EntityHelper → TableSql
IEntityHelper → ITableSql

# Variable name suggestions (optional but recommended)
context → coordinator
ctx → coordinator
helper → tableSql
_helper → _tableSql
```

**Estimated time**: 10-30 minutes for typical codebase (depends on size).

---

## Alternative Names Considered (For Reference)

### For DatabaseContext:
- **DatabaseRuntime** - Good, conveys long-lived nature, but less descriptive of orchestration
- **DatabaseEngine** - Too heavy/"DBA-ish", implies more than it does
- **DatabaseSession** - Ambiguous (often per-request in web contexts)
- **DatabaseScope** - Misleading (nudges toward DI scoped lifetime)
- **DatabaseGateway** - Pattern name is accurate but less clear

### For EntityHelper:
- **EntitySql** - Too close to Entity Framework terminology
- **TableOperations** - Verbose, generic
- **SqlTable** - Reverses Sql-first ordering (table before SQL)
- **CrudHelper** - Undersells capabilities, still uses "Helper"
- **TableAccess** - Generic, doesn't convey SQL generation

---

## Decision Criteria for 2.0

**Proceed with renames if:**
- Version 2.0 is planned within 12 months
- Other breaking changes justify major version bump
- User base is small enough that migration is manageable
- Long-term clarity > short-term compatibility

**Keep current names if:**
- No other breaking changes planned
- User base is too large for coordinated migration
- Compatibility is paramount
- Willing to accept documentation overhead

---

## Notes for Future Maintainers

These renames were extensively discussed after multiple reviewers (human and AI) pattern-matched `DatabaseContext` onto EF's `DbContext` despite comprehensive documentation.

**Root cause**: Name creates cognitive trap that documentation cannot fully overcome.

**Evidence**: Even after 700+ lines of architecture documentation explicitly stating the differences, reviewers continued applying EF lifetime patterns.

**Conclusion for DatabaseContext → DatabaseCoordinator**:
The name "Context" is actively harmful in .NET database space due to EF's widespread use. "Coordinator" accurately describes the orchestration role without ORM baggage.

**Conclusion for EntityHelper → TableSql**:
"Helper" undersells the class's role as the primary SQL generation and execution API. "TableSql" is concise, descriptive, and aligns with the library's SQL-first philosophy.

---

## Timeline (Tentative)

- **v1.1**: Current version, names stay, documentation enhanced with terminology clarifications
- **v2.0-beta**: Introduce new names, add compatibility shims with obsolete warnings
- **v2.0**: Release with both names, prefer new names in all docs
- **v2.x**: Deprecation period, migration guides, community support
- **v3.0**: Remove compatibility shims

**Estimated timeline**: 18-24 months from v1.1 release to v3.0.

---

## References

- Codex analysis: "The confusion isn't because your design is unusual. It's because EF poisoned the word 'Context' for an entire generation of .NET developers."
- User decision: DatabaseCoordinator or DatabaseEndpoint, TableSql or SqlCatalog
- EF comparison documentation added in v1.1 as interim measure
- Terminology clarifications added to README.md, ARCHITECTURE.md, DatabaseContext.cs XML docs

---

## Final Recommendations

Based on semantic accuracy, clarity, and avoidance of conflicting terminology:

1. **DatabaseContext → DatabaseCoordinator** ✅
   - Clear orchestration semantics
   - Zero EF overlap
   - Professional terminology

2. **EntityHelper → TableSql** ✅
   - Concise and descriptive
   - Avoids "Helper" underselling
   - Clear scope (table operations via SQL)

**Alternative pairing** (if DatabaseEndpoint is preferred):
- DatabaseContext → DatabaseEndpoint
- EntityHelper → TableSql

The critical requirement is eliminating the "Context" suffix due to EF cognitive baggage. Between Coordinator and Endpoint, **Coordinator** is the stronger choice for accurately describing the class's orchestration role.
