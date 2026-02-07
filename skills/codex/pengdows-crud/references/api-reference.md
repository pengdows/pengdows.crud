# API Reference

Quick reference for pengdows.crud main interfaces and methods.

## ITableGateway<TEntity, TRowID>

> **Note:** `TableGateway` is an alias for `TableGateway` (1.0 compatibility).

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

For multi-tenancy with **different database types per tenant**:

```csharp
public class TenantContextRegistry
{
    IDatabaseContext GetContext(string tenantId);
    void RegisterContext(string tenantId, IDatabaseContext context);
}
```

**How the optional context parameter works:**
- **Omit context:** Uses the default context from TableGateway constructor
- **Pass context:** Uses that context instead

**Usage Pattern - Different Databases Per Tenant:**

```csharp
// DI registration - single TableGateway instance for all tenants
services.AddSingleton<ITableGateway<Order, long>>(sp =>
    new OrderGateway(defaultContext));  // Used when context param omitted

// Non-multi-tenant: omit context parameter (uses defaultContext)
var order = await gateway.RetrieveOneAsync(orderId);

// Multi-tenant: resolve tenant context and pass to CRUD methods
var registry = services.GetRequiredService<ITenantContextRegistry>();
var tenantCtx = registry.GetContext("enterprise-client");  // Any database type

// Get gateway from DI and pass tenant context to methods
var gateway = services.GetRequiredService<ITableGateway<Order, long>>();

// All operations use tenant's database
var order = await gateway.RetrieveOneAsync(orderId, tenantCtx);
await gateway.CreateAsync(newOrder, tenantCtx);
await gateway.UpdateAsync(order, tenantCtx);
await gateway.DeleteAsync(orderId, tenantCtx);
```

**All CRUD methods accept optional context parameter:**
- `CreateAsync(entity, tenantContext)` - Inserts to tenant's database
- `RetrieveOneAsync(id, tenantContext)` - Selects from tenant's database
- `RetrieveAsync(ids, tenantContext)` - Selects multiple from tenant's database
- `UpdateAsync(entity, tenantContext)` - Updates in tenant's database
- `DeleteAsync(id, tenantContext)` - Deletes from tenant's database
- `UpsertAsync(entity, tenantContext)` - Upserts to tenant's database

**This enables:**
- ✅ Physical database separation (no tenant_id WHERE filtering needed)
- ✅ Single TableGateway instance for all tenants (singleton-safe)
- ✅ Each tenant uses different database type (PostgreSQL, SQL Server, MySQL, etc.)
- ✅ SQL automatically generated with tenant's dialect (parameter markers, quoting)
- ✅ Connection pooling per tenant context
- ✅ Type safety across all database types
