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
Task<bool> CreateAsync(TEntity entity, IDatabaseContext context);
Task<bool> CreateAsync(TEntity entity, IDatabaseContext context, CancellationToken ct);

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
StringBuilder Query { get; }              // SQL text builder
bool HasWhereAppended { get; set; }       // Whether WHERE exists
int ParameterCount { get; }               // Current parameter count
string QuotePrefix { get; }               // Dialect quote prefix
string QuoteSuffix { get; }               // Dialect quote suffix
string CompositeIdentifierSeparator { get; } // Identifier separator
```

### Identifier Handling

```csharp
string WrapObjectName(string name);       // Quote identifiers per dialect
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

ValueTask<T?> ExecuteScalarAsync<T>(CommandType type = CommandType.Text);
ValueTask<T?> ExecuteScalarAsync<T>(CommandType type, CancellationToken ct);

ValueTask<ITrackedReader> ExecuteReaderAsync(CommandType type = CommandType.Text);
ValueTask<ITrackedReader> ExecuteReaderAsync(CommandType type, CancellationToken ct);
```

**Extension method (on SqlContainer):**
```csharp
ValueTask<T?> ExecuteScalarWriteAsync<T>(CommandType type = CommandType.Text, CancellationToken ct = default);
```

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
Task CloseAndDisposeConnectionAsync(ITrackedConnection? conn);
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
DbMode ConnectionMode { get; }
ITypeMapRegistry TypeMapRegistry { get; }
IDataSourceInfo DataSourceInfo { get; }
SupportedDatabase Product { get; }
int NumberOfOpenConnections { get; }
int PeakOpenConnections { get; }
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

| Operation | Pattern | Examples |
|-----------|---------|----------|
| INSERT | `i{n}` | `i0`, `i1`, `i2` |
| UPDATE SET | `s{n}` | `s0`, `s1`, `s2` |
| WHERE | `w{n}` | `w0`, `w1`, `w2` |
| JOIN | `j{n}` | `j0`, `j1`, `j2` |
| KEY | `k{n}` | `k0`, `k1`, `k2` |
| VERSION | `v{n}` | `v0`, `v1` |

Use base name without prefix when calling `SetParameterValue()`:

```csharp
container.SetParameterValue("w0", newId);      // Correct
container.SetParameterValue("@w0", newId);     // Wrong - no prefix
```

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
public enum SupportedDatabase
{
    SqlServer,
    PostgreSql,
    Oracle,
    MySql,
    MariaDb,
    Sqlite,
    Firebird,
    CockroachDb,
    Db2,
    Snowflake,
    Informix,
    SapHana
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
    string UserId { get; }
}
```

## TenantContextRegistry

For multi-tenancy:

```csharp
public class TenantContextRegistry
{
    IDatabaseContext GetContext(string tenantId);
    void RegisterContext(string tenantId, IDatabaseContext context);
}
```
