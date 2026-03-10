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

// UPDATE statement
Task<ISqlContainer> BuildUpdateAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken ct = default);
Task<ISqlContainer> BuildUpdateAsync(TEntity entity, bool loadOriginal, IDatabaseContext? context = null, CancellationToken ct = default);

// DELETE statement
ISqlContainer BuildDelete(TRowID id, IDatabaseContext? context = null);

// Dialect-specific UPSERT
ISqlContainer BuildUpsert(TEntity entity, IDatabaseContext? context = null);

// Batch Build methods
IReadOnlyList<ISqlContainer> BuildBatchCreate(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null);
IReadOnlyList<ISqlContainer> BuildBatchUpdate(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null);
IReadOnlyList<ISqlContainer> BuildBatchUpsert(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null);
IReadOnlyList<ISqlContainer> BuildBatchDelete(IEnumerable<TRowID> ids, IDatabaseContext? context = null);
IReadOnlyList<ISqlContainer> BuildBatchDelete(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null);
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
Task<bool> CreateAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken ct = default);
Task<int> CreateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default); // Delegates to BatchCreateAsync

// Retrieve single
Task<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context = null, CancellationToken ct = default);
Task<TEntity?> RetrieveOneAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken ct = default);  // By [PrimaryKey]

// Retrieve multiple
Task<List<TEntity>> RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null, CancellationToken ct = default);

// Retrieve streamed (memory-efficient for large sets)
IAsyncEnumerable<TEntity> RetrieveStreamAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null, CancellationToken ct = default);

// Update
Task<int> UpdateAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken ct = default);
Task<int> UpdateAsync(TEntity entity, bool loadOriginal, IDatabaseContext? context = null, CancellationToken ct = default);
Task<int> UpdateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default); // Delegates to BatchUpdateAsync

// Delete
Task<int> DeleteAsync(TRowID id, IDatabaseContext? context = null, CancellationToken ct = default);
Task<int> DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null, CancellationToken ct = default);
Task<int> DeleteAsync(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default); // By primary key

// Upsert
Task<int> UpsertAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken ct = default);
Task<int> UpsertAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default); // Delegates to BatchUpsertAsync

// Explicit Batch Operations
Task<int> BatchCreateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default);
Task<int> BatchUpdateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default);
Task<int> BatchUpsertAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default);
Task<int> BatchDeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null, CancellationToken ct = default);
Task<int> BatchDeleteAsync(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default);
```

### Other Members

```csharp
string WrappedTableName { get; }                    // Fully qualified, quoted table name
EnumParseFailureMode EnumParseBehavior { get; set; } // Enum parse failure handling
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
void AddParameters(IList<DbParameter> list);

// Get/Set values
void SetParameterValue(string name, object? value);
object? GetParameterValue(string name);
T GetParameterValue<T>(string name);
```

### Query Execution (all return ValueTask)

```csharp
ValueTask<int> ExecuteNonQueryAsync(CommandType type = CommandType.Text);
ValueTask<int> ExecuteNonQueryAsync(CommandType type, CancellationToken ct);
ValueTask<int> ExecuteNonQueryAsync(ExecutionType execType, CommandType type = CommandType.Text);

// Scalar — unambiguous distinguishing between None/Null/Value:
ValueTask<T>               ExecuteScalarRequiredAsync<T>(CommandType type = CommandType.Text); // throws if no rows or null
ValueTask<T?>              ExecuteScalarOrNullAsync<T>(CommandType type = CommandType.Text);   // null if no rows or DBNull
ValueTask<ScalarResult<T>> TryExecuteScalarAsync<T>(CommandType type = CommandType.Text);      // status: None, Null, or Value

ValueTask<ITrackedReader> ExecuteReaderAsync(CommandType type = CommandType.Text);
ValueTask<ITrackedReader> ExecuteReaderAsync(CommandType type, CancellationToken ct);
```

All execution methods also have `ExecutionType` overloads for explicit read/write pool routing.

### Clone and Lifecycle

```csharp
ISqlContainer Clone();                        // Clone with same context
ISqlContainer Clone(IDatabaseContext? context); // Clone with different context
void Clear();                                  // Clear query and parameters
void Dispose();                                // Release resources
```

### Stored Procedure Support

```csharp
string WrapForStoredProc(ExecutionType type, bool includeParameters = true, bool captureReturn = false);
```

## IDatabaseContext

### Transaction Management

```csharp
ITransactionContext BeginTransaction(IsolationLevel? level = null, ExecutionType type = ExecutionType.Write, bool? readOnly = null);
ITransactionContext BeginTransaction(IsolationProfile profile, ExecutionType type = ExecutionType.Write, bool? readOnly = null);

Task<ITransactionContext> BeginTransactionAsync(IsolationLevel? level = null, ExecutionType type = ExecutionType.Write, bool? readOnly = null, CancellationToken ct = default);
Task<ITransactionContext> BeginTransactionAsync(IsolationProfile profile, ExecutionType type = ExecutionType.Write, bool? readOnly = null, CancellationToken ct = default);
```

### SQL Container Creation

```csharp
ISqlContainer CreateSqlContainer(string? query = null);
```

### Properties

```csharp
ISqlDialect Dialect { get; }                   // SQL dialect in use for this context
SupportedDatabase Product { get; }             // Detected database product
DbMode ConnectionMode { get; }                 // Connection strategy
DbDataSource? DataSource { get; }              // Native data source (e.g. NpgsqlDataSource), or null if not available
TimeSpan? ModeLockTimeout { get; }             // Lock timeout; null = wait indefinitely
long NumberOfOpenConnections { get; }
long PeakOpenConnections { get; }
int? ReaderPlanCacheSize { get; }              // Plan cache size for reader connections
int MaxParameterLimit { get; }                 // Provider-specific parameter limit
DatabaseMetrics Metrics { get; }               // Real-time metrics snapshot
string Name { get; set; }                      // Logical name for this context (for logging/multi-tenancy)
Guid RootId { get; }                           // Unique identity of this context instance
ReadWriteMode ReadWriteMode { get; }           // Read-only or read-write
bool IsReadOnlyConnection { get; }             // True if context was opened read-only
bool PrepareStatements { get; }                // Whether statements are auto-prepared
string DatabaseProductName { get; }            // Database product name string
```

### Additional Methods

```csharp
string GetBaseSessionSettings();               // Session SQL applied to every new connection
string GetReadOnlySessionSettings();           // Session SQL applied to read-only connections
string GenerateParameterName();                // Generate a unique parameter name
```

## ITransactionContext

Extends `IDatabaseContext`:

### Transaction Control

```csharp
void Commit();
Task CommitAsync(CancellationToken ct = default);
void Rollback();
Task RollbackAsync(CancellationToken ct = default);
Task SavepointAsync(string name);
Task RollbackToSavepointAsync(string name);
```

### Transaction State

```csharp
Guid TransactionId { get; }
bool WasCommitted { get; }
bool WasRolledBack { get; }
bool IsCompleted { get; }
IsolationLevel IsolationLevel { get; }
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
    DuckDB = 256,
    YugabyteDb = 512,
    TiDb = 1024,
    Snowflake = 2048,
    AuroraMySql = 4096,
    AuroraPostgreSql = 8192
}
```

## IAuditValueResolver / IAuditValues

```csharp
public interface IAuditValueResolver { IAuditValues Resolve(); }

public interface IAuditValues
{
    object UserId { get; init; }
    DateTime UtcNow { get; }                 // Always UTC
    DateTimeOffset? TimestampOffset { get; } // Optional UTC offset
    T As<T>();                               // Cast UserId
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
