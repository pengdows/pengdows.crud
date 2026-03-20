# Batch Operations Quick Reference

## `TableGateway<TEntity, TRowID>`

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

## `PrimaryKeyTableGateway<TEntity>`

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

## Notes

- Execute methods return `ValueTask<int>`.
- Build methods return `IReadOnlyList<ISqlContainer>`.
- Empty input returns `0` or an empty list.
- Single-item batches use the single-row path.
