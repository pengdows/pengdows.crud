# Batch Operations

`pengdows.crud` 2.0 exposes batch APIs on both gateway types. The implemented surface is smaller and more concrete than some older design notes in this repo.

## Cheatsheet

### `TableGateway<TEntity, TRowID>`

```csharp
int affected = await gateway.BatchCreateAsync(entities);
int affected = await gateway.BatchUpdateAsync(entities);
int affected = await gateway.BatchUpsertAsync(entities);
int affected = await gateway.BatchDeleteAsync(ids);
int affected = await gateway.BatchDeleteAsync(entities);
```

Convenience overloads:

```csharp
await gateway.CreateAsync(entities);
await gateway.UpdateAsync(entities);
await gateway.UpsertAsync(entities);
await gateway.DeleteAsync(ids);
await gateway.DeleteAsync(entities);
```

Build without executing:

```csharp
IReadOnlyList<ISqlContainer> creates = gateway.BuildBatchCreate(entities);
IReadOnlyList<ISqlContainer> updates = gateway.BuildBatchUpdate(entities);
IReadOnlyList<ISqlContainer> upserts = gateway.BuildBatchUpsert(entities);
IReadOnlyList<ISqlContainer> deletesById = gateway.BuildBatchDelete(ids);
IReadOnlyList<ISqlContainer> deletesByEntity = gateway.BuildBatchDelete(entities);
```

### `PrimaryKeyTableGateway<TEntity>`

```csharp
int affected = await gateway.BatchCreateAsync(entities);
int affected = await gateway.BatchUpdateAsync(entities);
int affected = await gateway.BatchUpsertAsync(entities);
int affected = await gateway.BatchDeleteAsync(entities);
```

Build without executing:

```csharp
IReadOnlyList<ISqlContainer> creates = gateway.BuildBatchCreate(entities);
IReadOnlyList<ISqlContainer> updates = gateway.BuildBatchUpdate(entities);
IReadOnlyList<ISqlContainer> upserts = gateway.BuildBatchUpsert(entities);
IReadOnlyList<ISqlContainer> deletes = gateway.BuildBatchDelete(entities);
```

Notes:
- Execute methods return `ValueTask<int>`; build methods return `IReadOnlyList<ISqlContainer>`.
- Empty input returns `0` or an empty list; single-item batches use the single-row path.
- There is no primary-key-gateway batch delete by surrogate ID, because that gateway has no `TRowID`.

## Implemented API

### `TableGateway<TEntity, TRowID>`

- `BuildBatchCreate(IReadOnlyList<TEntity>, IDatabaseContext? context = null)`
- `BatchCreateAsync(IReadOnlyList<TEntity>, IDatabaseContext? context = null, CancellationToken = default)`
- `BuildBatchUpdate(IReadOnlyList<TEntity>, IDatabaseContext? context = null)`
- `BatchUpdateAsync(IReadOnlyList<TEntity>, IDatabaseContext? context = null, CancellationToken = default)`
- `BuildBatchUpsert(IReadOnlyList<TEntity>, IDatabaseContext? context = null)`
- `BatchUpsertAsync(IReadOnlyList<TEntity>, IDatabaseContext? context = null, CancellationToken = default)`
- `BuildBatchDelete(IEnumerable<TRowID>, IDatabaseContext? context = null)`
- `BatchDeleteAsync(IEnumerable<TRowID>, IDatabaseContext? context = null, CancellationToken = default)`
- `BuildBatchDelete(IReadOnlyCollection<TEntity>, IDatabaseContext? context = null)`
- `BatchDeleteAsync(IReadOnlyCollection<TEntity>, IDatabaseContext? context = null, CancellationToken = default)`

Convenience overloads delegate to these batch methods:

- `CreateAsync(IReadOnlyList<TEntity>)`
- `UpdateAsync(IReadOnlyList<TEntity>)`
- `UpsertAsync(IReadOnlyList<TEntity>)`
- `DeleteAsync(IEnumerable<TRowID>)`
- `DeleteAsync(IReadOnlyCollection<TEntity>)`

### `PrimaryKeyTableGateway<TEntity>`

- `BuildBatchCreate(IReadOnlyList<TEntity>, IDatabaseContext? context = null)`
- `BatchCreateAsync(IReadOnlyList<TEntity>, IDatabaseContext? context = null, CancellationToken = default)`
- `BuildBatchUpdate(IReadOnlyList<TEntity>, IDatabaseContext? context = null)`
- `BatchUpdateAsync(IReadOnlyList<TEntity>, IDatabaseContext? context = null, CancellationToken = default)`
- `BuildBatchUpsert(IReadOnlyList<TEntity>, IDatabaseContext? context = null)`
- `BatchUpsertAsync(IReadOnlyList<TEntity>, IDatabaseContext? context = null, CancellationToken = default)`
- `BuildBatchDelete(IReadOnlyCollection<TEntity>, IDatabaseContext? context = null)`
- `BatchDeleteAsync(IReadOnlyCollection<TEntity>, IDatabaseContext? context = null, CancellationToken = default)`

## Runtime Behavior

- Empty input returns `0` for execute methods and an empty list for build methods.
- Single-item execute calls take a fast path through the single-row method.
- Batch methods execute chunk-by-chunk and return the total affected-row count.
- Build methods return `IReadOnlyList<ISqlContainer>` so callers can inspect or execute the generated statements themselves.
- Chunking is driven by the current dialect's parameter limits and maximum rows per batch.
- Audit values are resolved once per batch, not once per entity.
- Version columns are prepared during batch create, and batch update/upsert uses the same version-aware SQL generation rules as the single-row paths.

## Architecture

### Entry Points

The implementation is split across:

- `pengdows.crud/TableGateway.Batch.cs`
- `pengdows.crud/TableGateway.Core.cs`
- `pengdows.crud/PrimaryKeyTableGateway.Delete.cs`
- `pengdows.crud/PrimaryKeyTableGateway.Update.cs`
- `pengdows.crud/PrimaryKeyTableGateway.Upsert.cs`

There is no separate batch coordinator type. The gateway itself owns validation, chunking, SQL generation, and execution.

### Execution Flow

1. Validate input and short-circuit empty collections.
2. Resolve the effective `IDatabaseContext`.
3. Detect the dialect and capability flags from the context.
4. Prepare entities for the batch:
   - assign writable IDs when needed
   - resolve audit values once for the batch
   - populate audit fields
   - initialize version values for create paths
5. Split the input into chunks based on parameter limits and dialect row caps.
6. Build one `ISqlContainer` per chunk.
7. Execute containers sequentially and sum the affected-row count.

### Chunking Model

Chunking is based on:

- columns/parameters consumed per row
- `IDatabaseContext.MaxParameterLimit`
- dialect-specific `MaxRowsPerBatch`

This keeps generated statements within provider limits without exposing a user-configurable batching API.

### Dialect Strategy / Fallback Rules

- Batch insert uses a multi-row statement only when the dialect advertises `SupportsBatchInsert`; otherwise the gateway falls back to one container per entity.
- Batch update uses dialect-specific SQL only when the dialect advertises `SupportsBatchUpdate`; otherwise it falls back to one update statement per entity. As of this writing, `SupportsBatchUpdate` is `true` for PostgreSQL/CockroachDB/YugabyteDB (`UPDATE ... FROM (VALUES ...)`), SQL Server (`MERGE ... USING (VALUES ...)`), Snowflake (`UPDATE ... FROM (VALUES ...)`, no target alias), and Oracle (`MERGE ... USING (SELECT ... FROM DUAL UNION ALL ...)` — Oracle has no `VALUES(...)` row-constructor table literal, so the MERGE source is built the same row-per-`SELECT ... FROM DUAL` shape as Oracle's own batch INSERT). All other dialects fall back to one update per entity.
- Batch upsert uses multi-row `ON CONFLICT` or `ON DUPLICATE KEY` only when the connected product advertises those capabilities; otherwise it falls back to one `BuildUpsert(...)` container per entity.
- Batch delete by IDs uses chunked `WHERE ... IN (...)`.
- Batch delete by entity collection builds one delete container per entity because each delete is keyed by the mapped `[Id]` or `[PrimaryKey]` values from that entity.

### Important Constraints

- Batch APIs are SQL-container based, not data-loader based.
- Batch execution is sequential over the generated containers.
- There is no partial-success result object.
- There is no retry, compensation, or per-row error reporting layer.
- There is no diff-based `BuildUpdateAsync(original, updated)` batch path in the public API.

## Compatibility Matrix

| Operation | Uses dialect capability | Fallback when unsupported |
|---|---|---|
| Batch create | `SupportsBatchInsert` | one `BuildCreate(...)` container per entity |
| Batch update | `SupportsBatchUpdate` | one update container per entity |
| Batch upsert | `SupportsInsertOnConflict` / `SupportsOnDuplicateKey` | one `BuildUpsert(...)` container per entity |
| Batch delete by IDs | always available on `TableGateway<TEntity, TRowID>` | n/a |
| Batch delete by entities | always available | one delete container per entity |

### Upsert Shape Used Today

| Product family | Batch upsert shape |
|---|---|
| PostgreSQL-compatible products with `SupportsInsertOnConflict` | multi-row `INSERT ... ON CONFLICT ...` |
| MySQL-compatible products with `SupportsOnDuplicateKey` | multi-row `INSERT ... ON DUPLICATE KEY UPDATE ...` |
| Products without either flag | per-entity `BuildUpsert(...)` fallback |

That means SQL Server, Oracle, Firebird, and similar engines still support batch upsert through the batch API, but the batch is executed as a sequence of per-entity upsert containers rather than a single multi-row upsert statement.

## What Is Not Implemented

The current codebase does not expose:

- `CreateManyAsync`, `UpdateManyAsync`, or `UpsertManyAsync`
- `BatchResult`, `BulkResult`, or `ContinueOnError`
- progress callbacks
- resumable/checkpointed batches
- provider-native bulk loaders such as `COPY` or `SqlBulkCopy`
- provider-native bulk copy pipelines
- concurrent chunk execution
- partial-success reporting
- user-selectable batch strategies

Most of these are open ideas — see [`planning/future-work.md`](./planning/future-work.md), not the
current public API. **Provider-native bulk loaders/pipelines specifically were investigated and
rejected** (`FEAT-012`, see [`planning/bulk-loading-design.md`](./planning/bulk-loading-design.md)):
`SqlBulkCopy`/`COPY`/`MySqlBulkCopy`/DuckDB's `Appender` aren't semantically uniform across
providers, so a caller-facing API built on them would break this library's provider-independence
guarantee. Don't expect that one to ship without a new, explicit request.
