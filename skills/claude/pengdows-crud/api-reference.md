# API Reference

Quick reference for pengdows.crud main interfaces and methods.

## ITableGateway<TEntity, TRowID>

### Tier 1: Build Methods (return ISqlContainer, no execution)

All synchronous except `BuildUpdateAsync`:

```csharp
// INSERT statement
ISqlContainer BuildCreate(TEntity entity, IDatabaseContext? context = null);

// SELECT with no WHERE (starting point for custom queries)
ISqlContainer BuildBaseRetrieve(string alias, IDatabaseContext? context = null);

// SELECT with WHERE clause by IDs
ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? ids, string alias, IDatabaseContext? context = null);
ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? ids, IDatabaseContext? context = null);

// SELECT with WHERE clause by entity primary keys
ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? entities, string alias, IDatabaseContext? context = null);
ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? entities, IDatabaseContext? context = null);

// UPDATE statement (async because loadOriginal may need DB I/O)
Task<ISqlContainer> BuildUpdateAsync(TEntity entity, IDatabaseContext? context = null);
Task<ISqlContainer> BuildUpdateAsync(TEntity entity, bool loadOriginal, IDatabaseContext? context = null);
Task<ISqlContainer> BuildUpdateAsync(TEntity entity, IDatabaseContext? context, CancellationToken ct);
Task<ISqlContainer> BuildUpdateAsync(TEntity entity, bool loadOriginal, IDatabaseContext? context, CancellationToken ct);

// DELETE statement
ISqlContainer BuildDelete(TRowID id, IDatabaseContext? context = null);

// Dialect-specific UPSERT
ISqlContainer BuildUpsert(TEntity entity, IDatabaseContext? context = null);
```

### WHERE Clause Helpers (modify existing container)

```csharp
ISqlContainer BuildWhere(string wrappedColumnName, IEnumerable<TRowID> ids, ISqlContainer sc);
void BuildWhereByPrimaryKey(IReadOnlyCollection<TEntity>? entities, ISqlContainer sc, string alias = "");
```

### Tier 2: Load Methods (execute pre-built ISqlContainer)

```csharp
Task<TEntity?> LoadSingleAsync(ISqlContainer sc);
Task<TEntity?> LoadSingleAsync(ISqlContainer sc, CancellationToken ct);

Task<List<TEntity>> LoadListAsync(ISqlContainer sc);
Task<List<TEntity>> LoadListAsync(ISqlContainer sc, CancellationToken ct);

IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc);
IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc, CancellationToken ct);
```

### Tier 3: Convenience Methods (Build + Execute)

```csharp
// Create
Task<bool> CreateAsync(TEntity entity);
Task<bool> CreateAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken ct = default);

// Retrieve single
Task<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context = null);
Task<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context, CancellationToken ct);
Task<TEntity?> RetrieveOneAsync(TEntity entity, IDatabaseContext? context = null);  // By [PrimaryKey]
Task<TEntity?> RetrieveOneAsync(TEntity entity, IDatabaseContext? context, CancellationToken ct);

// Retrieve multiple
Task<List<TEntity>> RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null);
Task<List<TEntity>> RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context, CancellationToken ct);

// Retrieve streamed (memory-efficient for large sets)
IAsyncEnumerable<TEntity> RetrieveStreamAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null);
IAsyncEnumerable<TEntity> RetrieveStreamAsync(IEnumerable<TRowID> ids, IDatabaseContext? context, CancellationToken ct);

// Update
Task<int> UpdateAsync(TEntity entity, IDatabaseContext? context = null);
Task<int> UpdateAsync(TEntity entity, IDatabaseContext? context, CancellationToken ct);
Task<int> UpdateAsync(TEntity entity, bool loadOriginal, IDatabaseContext? context = null);
Task<int> UpdateAsync(TEntity entity, bool loadOriginal, IDatabaseContext? context, CancellationToken ct);

// Delete
Task<int> DeleteAsync(TRowID id, IDatabaseContext? context = null);
Task<int> DeleteAsync(TRowID id, IDatabaseContext? context, CancellationToken ct);
Task<int> DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null);
Task<int> DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context, CancellationToken ct);

// Upsert
Task<int> UpsertAsync(TEntity entity, IDatabaseContext? context = null);
Task<int> UpsertAsync(TEntity entity, IDatabaseContext? context, CancellationToken ct);
```

### Other Members

```csharp
string WrappedTableName { get; }                    // Fully qualified, quoted table name
EnumParseFailureMode EnumParseBehavior { get; set; } // Enum parse failure handling
string MakeParameterName(DbParameter p);             // Format parameter name per dialect
Action<object, object?> GetOrCreateSetter(PropertyInfo prop); // Compiled setter
TEntity MapReaderToObject(ITrackedReader reader);    // Row-to-entity mapping
```

## ISqlContainer

### Query Building

```csharp
ISqlQueryBuilder Query { get; }           // SQL text builder (pooled, zero-alloc appends)
bool HasWhereAppended { get; set; }       // Whether WHERE exists
int ParameterCount { get; }               // Current parameter count
string QuotePrefix { get; }               // Dialect quote prefix
string QuoteSuffix { get; }               // Dialect quote suffix
string CompositeIdentifierSeparator { get; } // Identifier separator
```

### Identifier Handling

```csharp
string WrapObjectName(string name);       // Split on '.', wrap each segment, reassemble — use for tables/columns/aliases
string WrapSimpleName(string name);       // Wrap the whole string as one token — no splitting on '.'
string MakeParameterName(string name);    // Format parameter name per dialect
string MakeParameterName(DbParameter p);  // Format from parameter object
```

### Parameter Management

```csharp
// Create without adding
DbParameter CreateDbParameter<T>(string? name, DbType type, T value);
DbParameter CreateDbParameter<T>(DbType type, T value);

// Create and add
DbParameter AddParameterWithValue<T>(DbType type, T value);
DbParameter AddParameterWithValue<T>(string? name, DbType type, T value);
DbParameter AddParameterWithValue<T>(DbType type, T value, ParameterDirection direction);
DbParameter AddParameterWithValue<T>(string? name, DbType type, T value, ParameterDirection direction);

// Add pre-constructed
void AddParameter(DbParameter parameter);
void AddParameters(IEnumerable<DbParameter> list);

// Get/Set values
void SetParameterValue(string name, object? value);
object? GetParameterValue(string name);
T GetParameterValue<T>(string name);
```

### Query Execution (all return ValueTask)

```csharp
ValueTask<int> ExecuteNonQueryAsync(CommandType type = CommandType.Text);
ValueTask<int> ExecuteNonQueryAsync(CommandType type, CancellationToken ct);

// Scalar — three variants depending on how you want to handle no-rows / null:
ValueTask<T>               ExecuteScalarRequiredAsync<T>(CommandType type = CommandType.Text);   // throws if no rows or null
ValueTask<T>               ExecuteScalarRequiredAsync<T>(CommandType type, CancellationToken ct);
ValueTask<T?>              ExecuteScalarOrNullAsync<T>(CommandType type = CommandType.Text);      // null if no rows or DBNull
ValueTask<T?>              ExecuteScalarOrNullAsync<T>(CommandType type, CancellationToken ct);
ValueTask<ScalarResult<T>> TryExecuteScalarAsync<T>(CommandType type = CommandType.Text);         // unambiguous: None/Null/Value
ValueTask<ScalarResult<T>> TryExecuteScalarAsync<T>(CommandType type, CancellationToken ct);

ValueTask<ITrackedReader> ExecuteReaderAsync(CommandType type = CommandType.Text);
ValueTask<ITrackedReader> ExecuteReaderAsync(CommandType type, CancellationToken ct);
```

All execution methods also have `ExecutionType` overloads for explicit read/write pool routing.

### Clone and Lifecycle

```csharp
ISqlContainer Clone();                        // Clone with same context
ISqlContainer Clone(IDatabaseContext? context); // Clone with different context
void Clear();                                  // Clear query and parameters
DbCommand CreateCommand(ITrackedConnection conn); // Create command for connection
```

### Stored Procedure Support

```csharp
string WrapForStoredProc(ExecutionType type, bool includeParameters = true, bool captureReturn = false);
```

## IDatabaseContext

### Connection Management

```csharp
void CloseAndDisposeConnection(ITrackedConnection? conn);
ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? conn);
```

Note: Direct connection access is internal-only; use `ISqlContainer` for execution.

### Transaction Management

```csharp
ITransactionContext BeginTransaction(
    IsolationLevel? level = null,
    ExecutionType type = ExecutionType.Write,
    bool? readOnly = null);

ITransactionContext BeginTransaction(
    IsolationProfile profile,
    ExecutionType type = ExecutionType.Write,
    bool? readOnly = null);
```

### SQL Container Creation

```csharp
ISqlContainer CreateSqlContainer(string? query = null);
```

### Properties

```csharp
ISqlDialect Dialect { get; }                   // SQL dialect in use for this context
TimeSpan? ModeLockTimeout { get; }             // Mode/transaction lock timeout; null = wait indefinitely
DbMode ConnectionMode { get; }
IDataSourceInformation DataSourceInfo { get; }
SupportedDatabase Product { get; }
long NumberOfOpenConnections { get; }
long PeakOpenConnections { get; }
int? ReaderPlanCacheSize { get; }              // Plan cache size for reader connections
```

## ITransactionContext

Extends `IDatabaseContext`:

### Transaction State

```csharp
bool WasCommitted { get; }
bool WasRolledBack { get; }
bool IsCompleted { get; }
IsolationLevel IsolationLevel { get; }
```

### Transaction Control

```csharp
void Commit();
void Rollback();
Task SavepointAsync(string name);
Task RollbackToSavepointAsync(string name);
```

## Parameter Naming Convention

| Prefix | Used in | Build method(s) |
|--------|---------|-----------------|
| `i{n}` | INSERT values | `BuildCreate`, `BuildUpsert`, batch |
| `s{n}` | UPDATE SET clause | `BuildUpdateAsync`, batch |
| `w{n}` | WHERE (retrieve IN/ANY) | `BuildRetrieve` |
| `k{n}` | WHERE id/key | `BuildDelete`, `BuildUpdateAsync` WHERE, entity lookup |
| `v{n}` | Optimistic lock version | `BuildUpdateAsync` (only with `[Version]` column) |
| `j{n}` | JOIN conditions | Custom SQL |
| `b{n}` | Batch row values | `BuildBatchCreate/Update/Upsert` |

**Critical distinctions**:
- `BuildRetrieve` → id slot is `w0` (scalar for single-element reuse, array for PostgreSQL ANY)
- `BuildDelete` → id slot is `k0`
- `BuildUpdateAsync` → SET slots are `s0`…`sN`, then WHERE id is `k0` (key counter, always starts at 0)

Always pass the base name (no `@`/`:`/`$`) to `SetParameterValue()`:

```csharp
// BuildRetrieve reuse — scalar value, not array:
_readSc.SetParameterValue("w0", nextId);        // ✓ scalar
_readSc.SetParameterValue("w0", new[]{nextId}); // ✗ throws on non-PostgreSQL

// BuildDelete reuse:
_deleteSc.SetParameterValue("k0", idToDelete);

// BuildUpdateAsync reuse — SET params then key:
_updateSc.SetParameterValue("s2", newSalary);   // 3rd updatable column
_updateSc.SetParameterValue("k0", targetId);    // WHERE id
```

See `docs/parameter-naming-convention.md` for full per-operation detail.

## ExecutionType

```csharp
public enum ExecutionType
{
    Read,   // Read-only operation
    Write   // Modifying operation
}
```

## Supported Databases

```csharp
[Flags]
public enum SupportedDatabase
{
    Unknown = 0,
    PostgreSql = 1,
    SqlServer = 2,
    Oracle = 4,
    Firebird = 8,
    CockroachDb = 16,
    MariaDb = 32,
    MySql = 64,
    Sqlite = 128,
    DuckDB = 256
}
```

## IAuditValueResolver

```csharp
public interface IAuditValueResolver
{
    IAuditValues Resolve();
}

public interface IAuditValues
{
    object UserId { get; init; }
    DateTime UtcNow { get; }                 // Always UTC
    DateTimeOffset? TimestampOffset { get; } // Always UTC offset; null falls back to UtcNow
    T As<T>() { return (T)UserId; }
}
```

## ITenantContextRegistry

For multi-tenancy:

```csharp
public interface ITenantContextRegistry
{
    IDatabaseContext GetContext(string tenant);
}
```

`TenantContextRegistry` is the concrete implementation, constructed via DI with `IServiceProvider`, `ITenantConnectionResolver`, `IDatabaseContextFactory`, and `ILoggerFactory`.
