# Batch Operations

`pengdows.crud` 2.0 exposes batch APIs on both gateway types. The implemented surface is smaller and more concrete than some older design notes in this repo.

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

There is no primary-key-gateway batch delete by surrogate ID, because that gateway has no `TRowID`.

## Runtime Behavior

- Empty input returns `0` for execute methods and an empty list for build methods.
- Single-item execute calls take a fast path through the single-row method.
- Batch methods execute chunk-by-chunk and return the total affected-row count.
- Build methods return `IReadOnlyList<ISqlContainer>` so callers can inspect or execute the generated statements themselves.
- Chunking is driven by the current dialect's parameter limits and maximum rows per batch.
- Audit values are resolved once per batch, not once per entity.
- Version columns are prepared during batch create, and batch update/upsert uses the same version-aware SQL generation rules as the single-row paths.

## Dialect Strategy

- Batch insert uses a multi-row statement only when the dialect advertises `SupportsBatchInsert`; otherwise the gateway falls back to one container per entity.
- Batch update uses dialect-specific SQL only when the dialect advertises `SupportsBatchUpdate`; otherwise it falls back to one update statement per entity.
- Batch upsert uses multi-row `ON CONFLICT` or `ON DUPLICATE KEY` only when the connected product advertises those capabilities; otherwise it falls back to one `BuildUpsert(...)` container per entity.
- Batch delete by IDs uses chunked `WHERE ... IN (...)`.
- Batch delete by entity collection builds one delete container per entity because each delete is keyed by the mapped `[Id]` or `[PrimaryKey]` values from that entity.

## What Is Not Implemented

The current codebase does not expose:

- `CreateManyAsync`, `UpdateManyAsync`, or `UpsertManyAsync`
- `BatchResult`, `BulkResult`, or `ContinueOnError`
- progress callbacks
- resumable/checkpointed batches
- provider-native bulk loaders such as `COPY` or `SqlBulkCopy`

Those ideas belong in [FUTURE_WORK.md](./FUTURE_WORK.md), not in the current public API.
