# API Reference

Quick reference for pengdows.crud main interfaces and methods.

## ITableGateway<TEntity, TRowID>

> **Note:** `EntityHelper` is an alias for `TableGateway` (1.0 compatibility).

### CRUD Operations

All async, return `Task`:

```csharp
// Create
Task<bool> CreateAsync(TEntity entity, IDatabaseContext? context = null);

// Retrieve
Task<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context = null);
Task<TEntity?> RetrieveOneAsync(TEntity entity, IDatabaseContext? context = null);  // By [PrimaryKey]
Task<IReadOnlyList<TEntity>> RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null);

// Update
Task<int> UpdateAsync(TEntity entity, IDatabaseContext? context = null);
Task<int> UpdateAsync(TEntity entity, bool loadOriginal, IDatabaseContext? context = null);

// Delete
Task<int> DeleteAsync(TRowID id, IDatabaseContext? context = null);
Task<int> DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null);

// Upsert
Task<int> UpsertAsync(TEntity entity, IDatabaseContext? context = null);
```

### Query Building

Return `ISqlContainer` for composition:

```csharp
ISqlContainer BuildCreate(TEntity entity, IDatabaseContext? context = null);
ISqlContainer BuildBaseRetrieve(string alias, IDatabaseContext? context = null);
ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? ids, string alias, IDatabaseContext? context = null);
Task<ISqlContainer> BuildUpdateAsync(TEntity entity, IDatabaseContext? context = null);
ISqlContainer BuildDelete(TRowID id, IDatabaseContext? context = null);
ISqlContainer BuildUpsert(TEntity entity, IDatabaseContext? context = null);
```

### Data Loading

```csharp
Task<TEntity?> LoadSingleAsync(ISqlContainer container);
Task<List<TEntity>> LoadListAsync(ISqlContainer container);
```

## ISqlContainer

### Query Building

```csharp
StringBuilder Query { get; }              // SQL text builder
bool HasWhereAppended { get; }            // Whether WHERE exists
int ParameterCount { get; }               // Current parameter count
```

### Identifier Handling

```csharp
string WrapObjectName(string name);       // Quote identifiers per dialect
string MakeParameterName(string name);    // Format parameter name per dialect
string MakeParameterName(DbParameter p);  // Format from parameter object
```

### Parameter Management

```csharp
DbParameter AddParameterWithValue<T>(DbType type, T value);
DbParameter AddParameterWithValue<T>(string? name, DbType type, T value);
DbParameter CreateDbParameter<T>(DbType type, T value);
DbParameter CreateDbParameter<T>(string? name, DbType type, T value);
void AddParameter(DbParameter parameter);
void AddParameters(IEnumerable<DbParameter> list);
void SetParameterValue(string name, object? value);
object? GetParameterValue(string name);
T? GetParameterValue<T>(string name);
```

### Query Execution

```csharp
Task<int> ExecuteNonQueryAsync(CommandType type = CommandType.Text);
Task<T?> ExecuteScalarAsync<T>(CommandType type = CommandType.Text);
Task<ITrackedReader> ExecuteReaderAsync(CommandType type = CommandType.Text);
```

### Other

```csharp
void Clear();                              // Clear query and parameters
ISqlContainer WrapForStoredProc(ExecutionType type, bool includeParams = true);
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
ITransactionContext BeginTransaction(IsolationLevel? level = null, ExecutionType type = ExecutionType.Write);
ITransactionContext BeginTransaction(IsolationProfile profile, ExecutionType type = ExecutionType.Write);
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
