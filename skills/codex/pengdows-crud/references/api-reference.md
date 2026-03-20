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
ISqlContainer BuildBaseRetrieve(string alias, string[]? extraSelectExpressions, IDatabaseContext? context = null);

// SELECT with WHERE clause by IDs
ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? ids, string alias, IDatabaseContext? context = null);
ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? ids, IDatabaseContext? context = null);

// SELECT with WHERE clause by entity primary keys
ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? entities, string alias, IDatabaseContext? context = null);
ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? entities, IDatabaseContext? context = null);

// UPDATE statement
ValueTask<ISqlContainer> BuildUpdateAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<ISqlContainer> BuildUpdateAsync(TEntity entity, bool loadOriginal, IDatabaseContext? context = null, CancellationToken ct = default);

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
ValueTask<TEntity?> LoadSingleAsync(ISqlContainer sc);
ValueTask<TEntity?> LoadSingleAsync(ISqlContainer sc, CancellationToken ct);

ValueTask<List<TEntity>> LoadListAsync(ISqlContainer sc);
ValueTask<List<TEntity>> LoadListAsync(ISqlContainer sc, CancellationToken ct);

IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc);
IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc, CancellationToken ct);
```

### Tier 3: Convenience Methods (Build + Execute)

```csharp
// Create
ValueTask<bool> CreateAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> CreateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default); // Delegates to BatchCreateAsync

// Retrieve single
ValueTask<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<TEntity?> RetrieveOneAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken ct = default);  // By [PrimaryKey]

// Retrieve multiple
ValueTask<List<TEntity>> RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null, CancellationToken ct = default);

// Retrieve streamed (memory-efficient for large sets)
IAsyncEnumerable<TEntity> RetrieveStreamAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null, CancellationToken ct = default);

// Update
ValueTask<int> UpdateAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> UpdateAsync(TEntity entity, bool loadOriginal, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> UpdateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default); // Delegates to BatchUpdateAsync

// Delete
ValueTask<int> DeleteAsync(TRowID id, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> DeleteAsync(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default); // By primary key

// Upsert
ValueTask<int> UpsertAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> UpsertAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default); // Delegates to BatchUpsertAsync

// Explicit Batch Operations
ValueTask<int> BatchCreateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> BatchUpdateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> BatchUpsertAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> BatchDeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> BatchDeleteAsync(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default);

// Count Operations
ValueTask<long> CountAllAsync(IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<long> CountWhereAsync(ISqlContainer sc, CancellationToken ct = default);
ValueTask<long> CountWhereNullAsync(string columnName, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<long> CountWhereEqualsAsync<TValue>(string columnName, TValue value, IDatabaseContext? context = null, CancellationToken ct = default);
```

### Other Members

```csharp
string WrappedTableName { get; }                    // Fully qualified, quoted table name
EnumParseFailureMode EnumParseBehavior { get; set; } // Enum parse failure handling
Action<object, object?> GetOrCreateSetter(PropertyInfo prop); // Compiled setter
TEntity MapReaderToObject(ITrackedReader reader);    // Row-to-entity mapping
```

## IPrimaryKeyTableGateway<TEntity>

For entities with **no surrogate `[Id]` column** — all operations use `[PrimaryKey]` columns. Throws `SqlGenerationException` at construction if no `[PrimaryKey]` defined.

### Tier 1: Build Methods

```csharp
ISqlContainer BuildCreate(TEntity entity, IDatabaseContext? context = null);
ISqlContainer BuildBaseRetrieve(string alias, string[]? extraSelectExpressions = null);
ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? objects, string alias = "");
ValueTask<ISqlContainer> BuildUpdateAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<ISqlContainer> BuildUpdateAsync(TEntity entity, bool loadOriginal, IDatabaseContext? context = null, CancellationToken ct = default);
ISqlContainer BuildUpsert(TEntity entity, IDatabaseContext? context = null);
IReadOnlyList<ISqlContainer> BuildBatchCreate(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null);
IReadOnlyList<ISqlContainer> BuildBatchUpdate(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null);
IReadOnlyList<ISqlContainer> BuildBatchUpsert(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null);
IReadOnlyList<ISqlContainer> BuildBatchDelete(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null);
```

### Tier 2: Load Methods

```csharp
ValueTask<TEntity?> LoadSingleAsync(ISqlContainer sc);
ValueTask<TEntity?> LoadSingleAsync(ISqlContainer sc, CancellationToken ct);
ValueTask<List<TEntity>> LoadListAsync(ISqlContainer sc);
ValueTask<List<TEntity>> LoadListAsync(ISqlContainer sc, CancellationToken ct);
IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc);
IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc, CancellationToken ct);
```

### Tier 3: Convenience Methods

```csharp
ValueTask<bool> CreateAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> CreateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<TEntity?> RetrieveOneAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> UpdateAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> UpdateAsync(TEntity entity, bool loadOriginal, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> UpdateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> DeleteAsync(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> UpsertAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> UpsertAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> BatchCreateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> BatchUpdateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> BatchUpsertAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default);
ValueTask<int> BatchDeleteAsync(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null, CancellationToken ct = default);
```

### Key Differences from `ITableGateway<TEntity, TRowID>`

| Feature | `ITableGateway<T,TId>` | `IPrimaryKeyTableGateway<T>` |
|---------|------------------------|------------------------------|
| WHERE basis | `[Id]` column (TRowID) | `[PrimaryKey]` columns |
| `DeleteAsync(id)` | Yes | No — only entity collection |
| `RetrieveAsync(ids)` | Yes | No — only by entity list |
| `RetrieveOneAsync(id)` | Yes | No — only by entity |
| `loadOriginal` flag | Reloads by TRowID | Accepted but ignored |
| Type param | `<TEntity, TRowID>` | `<TEntity>` only |

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

ValueTask<ITransactionContext> BeginTransactionAsync(IsolationProfile profile, ExecutionType type = ExecutionType.Write, CancellationToken ct = default);
ValueTask<ITransactionContext> BeginTransactionAsync(IsolationLevel? level = null, ExecutionType type = ExecutionType.Write, bool? readOnly = null, CancellationToken ct = default);
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
EventHandler<DatabaseMetrics> MetricsUpdated;  // Subscribe for real-time metric notifications
string Name { get; set; }                      // Logical name for this context (for logging/multi-tenancy)
Guid RootId { get; }                           // Unique identity of this context instance
ReadWriteMode ReadWriteMode { get; }           // Read-only or read-write
bool IsReadOnlyConnection { get; }             // True if context was opened read-only
CommandPrepareMode PrepareMode { get; }        // Statement preparation mode (Auto/Always/Never)
bool SupportsInsertReturning { get; }          // True if the database supports INSERT ... RETURNING
bool SupportsNamedParameters { get; }          // True if the database uses named (not positional) parameters
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
ValueTask CommitAsync(CancellationToken ct = default);
void Rollback();
ValueTask RollbackAsync(CancellationToken ct = default);
ValueTask SavepointAsync(string name, CancellationToken ct = default);
ValueTask RollbackToSavepointAsync(string name, CancellationToken ct = default);
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

## Exception Hierarchy

All database and framework errors are typed `DatabaseException` subclasses (namespace `pengdows.crud.exceptions`):

```
DatabaseException (abstract)           Properties: Database, SqlState, ErrorCode, ConstraintName, IsTransient
├── DatabaseOperationException
│   ├── ConstraintViolationException (abstract)
│   │   ├── UniqueConstraintViolationException
│   │   ├── ForeignKeyViolationException
│   │   ├── NotNullViolationException
│   │   └── CheckConstraintViolationException
│   ├── TransientWriteConflictException (abstract, IsTransient = true)
│   │   ├── DeadlockException
│   │   └── SerializationConflictException
│   ├── ConcurrencyConflictException        — [Version] UPDATE returned 0 rows affected
│   ├── CommandTimeoutException             — command timed out (IsTransient = true)
│   ├── ConnectionException                 — connection-level failure (provider translators)
│   └── TransactionException               — begin/commit/rollback failure
├── SqlGenerationException                  — entity metadata programmer error (TypeMapRegistry)
└── DataMappingException                    — strict-mode coercion failure (DataReaderMapper)
```

**Key throw sites:**
- `SqlGenerationException` — thrown by `TypeMapRegistry` at entity registration/gateway construction for missing `[Table]`, empty column name, invalid enum `DbType`, duplicate columns, no `[Id]`/`[PrimaryKey]`, PK order errors, invalid `[Version]`/audit field types. Uses `SupportedDatabase.Unknown`.
- `DataMappingException` — thrown in strict mode (`MapperOptions.Strict = true`) when column→property coercion fails. Uses `SupportedDatabase.Unknown`.
- `ConnectionException` — thrown by provider translators for connection-level failures.
- `TransactionException` — thrown by `TransactionContext` for begin/commit/rollback failures. After failure, `IsCompleted = true` and the connection is released; `Dispose` will not attempt a second rollback.
- `OperationCanceledException` — **never** wrapped; propagates as-is.

## ITenantContextRegistry

For multi-tenancy:

```csharp
public interface ITenantContextRegistry
{
    IDatabaseContext GetContext(string tenant);
}
```
