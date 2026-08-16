# API Reference

Quick reference for `pengdows.crud` public abstractions and interfaces.

---

## ITableGateway<TEntity, TRowID>

### Tier 1: Build Methods (return ISqlContainer, no DB execution)

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

// UPDATE statement (only async Build method)
ValueTask<ISqlContainer> BuildUpdateAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
ValueTask<ISqlContainer> BuildUpdateAsync(TEntity entity, bool loadOriginal, IDatabaseContext? context = null, CancellationToken cancellationToken = default);

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
ValueTask<TEntity?> LoadSingleAsync(ISqlContainer sc, CancellationToken cancellationToken = default);
ValueTask<List<TEntity>> LoadListAsync(ISqlContainer sc, CancellationToken cancellationToken = default);
IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc, CancellationToken cancellationToken = default);
```

### Tier 3: Convenience Methods (Build + Execute)

```csharp
// Create
ValueTask<bool> CreateAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
ValueTask<int> CreateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken cancellationToken = default);

// Retrieve single
ValueTask<TEntity?> RetrieveOneAsync(TRowID id, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
ValueTask<TEntity?> RetrieveOneAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken cancellationToken = default); // By [PrimaryKey]

// Retrieve multiple
ValueTask<List<TEntity>> RetrieveAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
IAsyncEnumerable<TEntity> RetrieveStreamAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null, CancellationToken cancellationToken = default);

// Update
ValueTask<int> UpdateAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
ValueTask<int> UpdateAsync(TEntity entity, bool loadOriginal, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
ValueTask<int> UpdateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken cancellationToken = default);

// Delete
ValueTask<int> DeleteAsync(TRowID id, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
ValueTask<int> DeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
ValueTask<int> DeleteAsync(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null, CancellationToken cancellationToken = default);

// Upsert
ValueTask<int> UpsertAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
ValueTask<int> UpsertAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken cancellationToken = default);

// Explicit Batch Operations
ValueTask<int> BatchCreateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
ValueTask<int> BatchUpdateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
ValueTask<int> BatchUpsertAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
ValueTask<int> BatchDeleteAsync(IEnumerable<TRowID> ids, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
ValueTask<int> BatchDeleteAsync(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
```

### Other Gateway Properties

```csharp
string WrappedTableName { get; }
EnumParseFailureMode EnumParseBehavior { get; }
AuditCreationPolicy AuditCreationPolicy { get; set; }
```

---

## IPrimaryKeyTableGateway<TEntity>

For entities keyed only by `[PrimaryKey]` without a surrogate `[Id]`:

```csharp
// Tier 1 — Build
ISqlContainer BuildCreate(TEntity entity, IDatabaseContext? context = null);
ISqlContainer BuildBaseRetrieve(string alias, IDatabaseContext? context = null);
ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? entities, string alias, IDatabaseContext? context = null);
ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? entities, IDatabaseContext? context = null);
ISqlContainer BuildUpsert(TEntity entity, IDatabaseContext? context = null);
ValueTask<ISqlContainer> BuildUpdateAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
IReadOnlyList<ISqlContainer> BuildBatchCreate(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null);
IReadOnlyList<ISqlContainer> BuildBatchUpdate(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null);
IReadOnlyList<ISqlContainer> BuildBatchUpsert(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null);
IReadOnlyList<ISqlContainer> BuildBatchDelete(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null);

// Tier 2 — Load
ValueTask<TEntity?> LoadSingleAsync(ISqlContainer sc, CancellationToken cancellationToken = default);
ValueTask<List<TEntity>> LoadListAsync(ISqlContainer sc, CancellationToken cancellationToken = default);
IAsyncEnumerable<TEntity> LoadStreamAsync(ISqlContainer sc, CancellationToken cancellationToken = default);

// Tier 3 — Convenience
ValueTask<bool> CreateAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
ValueTask<TEntity?> RetrieveOneAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
ValueTask<int> UpdateAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
ValueTask<int> DeleteAsync(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
ValueTask<int> UpsertAsync(TEntity entity, IDatabaseContext? context = null, CancellationToken cancellationToken = default);
```

---

## ISqlContainer

### Query Building & Quoting

```csharp
ISqlQueryBuilder Query { get; }           // High-performance pooled string builder
bool HasWhereAppended { get; set; }       // Tracks WHERE clause state
int ParameterCount { get; }               // Number of bound parameters
string QuotePrefix { get; }               // Dialect quote prefix
string QuoteSuffix { get; }               // Dialect quote suffix
string CompositeIdentifierSeparator { get; }

string WrapObjectName(string name);       // Wraps identifiers per dialect rules
string MakeParameterName(string name);    // Formats parameter name per dialect
string MakeParameterName(DbParameter p);
```

### Parameter Management

```csharp
DbParameter AddParameterWithValue<T>(string? name, DbType type, T value);
DbParameter AddParameterWithValue<T>(DbType type, T value);
DbParameter CreateDbParameter<T>(string? name, DbType type, T value);
void AddParameter(DbParameter parameter);
void AddParameters(IEnumerable<DbParameter> list);
void SetParameterValue(string name, object? value);
object? GetParameterValue(string name);
T GetParameterValue<T>(string name);
```

### Query Execution (All return ValueTask)

```csharp
ValueTask<int> ExecuteNonQueryAsync(CommandType type = CommandType.Text, CancellationToken cancellationToken = default);
ValueTask<int> ExecuteNonQueryAsync(ExecutionType execType, CommandType type = CommandType.Text, CancellationToken cancellationToken = default);

ValueTask<T> ExecuteScalarRequiredAsync<T>(CommandType type = CommandType.Text, CancellationToken cancellationToken = default);
ValueTask<T?> ExecuteScalarOrNullAsync<T>(CommandType type = CommandType.Text, CancellationToken cancellationToken = default);
ValueTask<ScalarResult<T>> TryExecuteScalarAsync<T>(CommandType type = CommandType.Text, CancellationToken cancellationToken = default);

ValueTask<ITrackedReader> ExecuteReaderAsync(CommandType type = CommandType.Text, CancellationToken cancellationToken = default);
ValueTask<ITrackedReader> ExecuteReaderAsync(ExecutionType execType, CommandType type = CommandType.Text, CancellationToken cancellationToken = default);
```

### Container Lifecycle

```csharp
ISqlContainer Clone();                          // Clone with same context
ISqlContainer Clone(IDatabaseContext? context); // Clone with different context (e.g. transaction)
void Clear();                                  // Clear query and parameters
void Dispose();                                // Return pooled builders and resources
```

---

## IDatabaseContext

### Transaction Management

```csharp
ITransactionContext BeginTransaction(
    IsolationLevel? isolationLevel = null,
    ExecutionType executionType = ExecutionType.Write);

ITransactionContext BeginTransaction(
    IsolationProfile isolationProfile,
    ExecutionType executionType = ExecutionType.Write);

ValueTask<ITransactionContext> BeginTransactionAsync(
    IsolationLevel? isolationLevel = null,
    ExecutionType executionType = ExecutionType.Write,
    CancellationToken cancellationToken = default);

ValueTask<ITransactionContext> BeginTransactionAsync(
    IsolationProfile isolationProfile,
    ExecutionType executionType = ExecutionType.Write,
    CancellationToken cancellationToken = default);
```

### Core Properties

```csharp
ISqlDialect Dialect { get; }                   // Dialect in use for this context
SupportedDatabase Product { get; }             // Detected database product
DbMode ConnectionMode { get; }                 // Connection strategy
TimeSpan? ModeLockTimeout { get; }             // Lock timeout; null = wait indefinitely
long NumberOfOpenConnections { get; }
long PeakOpenConnections { get; }
int? ReaderPlanCacheSize { get; }              // Plan cache size for reader connections
int MaxParameterLimit { get; }                 // Dialect max parameter limit
int MaxOutputParameters { get; }
DatabaseMetrics Metrics { get; }               // Real-time metrics snapshot
string Name { get; }                           // Logical context name
Guid RootId { get; }                           // Unique context identity
ReadWriteMode ReadWriteMode { get; }           // ReadWrite vs ReadOnly
bool IsReadOnlyConnection { get; }
CommandPrepareMode PrepareMode { get; }
string DatabaseProductName { get; }
```

---

## ITransactionContext

Extends `IDatabaseContext`:

```csharp
void Commit();
Task CommitAsync(CancellationToken cancellationToken = default);
void Rollback();
Task RollbackAsync(CancellationToken cancellationToken = default);
Task SavepointAsync(string name, CancellationToken cancellationToken = default);
Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default);

Guid TransactionId { get; }
bool WasCommitted { get; }
bool WasRolledBack { get; }
bool IsCompleted { get; }
IsolationLevel IsolationLevel { get; }
```

---

## Parameter Naming Convention

| Prefix | Used in | Generated by |
|---|---|---|
| `i{n}` | INSERT values | `BuildCreate`, `BuildUpsert`, batch |
| `s{n}` | UPDATE SET clause | `BuildUpdateAsync`, batch |
| `w{n}` | WHERE IN / retrieve filters | `BuildRetrieve`, `BuildWhere` |
| `k{n}` | WHERE id / business key lookup | `BuildDelete`, `BuildUpdateAsync` WHERE, `RetrieveOneAsync` |
| `v{n}` | Optimistic lock version predicate | `BuildUpdateAsync` (with `[Version]` column) |
| `j{n}` | JOIN predicates | Custom SQL |
| `b{n}` | Batch row parameters | `BuildBatchCreate/Update/Upsert` |
