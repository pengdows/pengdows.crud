# Primary Keys And Pseudokeys

## `[Id]` means row identifier

`[Id]` marks the surrogate row ID used by row-id operations on `TableGateway<TEntity, TRowID>`.

- exactly one `[Id]` property per entity
- `RetrieveOneAsync(TRowID)` uses it
- `DeleteAsync(TRowID)` and batch delete by IDs use it
- `IdAttribute.Writable` decides whether inserts send the value or expect the database to generate it

## `[PrimaryKey]` means business key

`[PrimaryKey]` marks one or more business-key columns.

- composite keys are supported through ordered `[PrimaryKey(n)]` declarations
- `RetrieveOneAsync(TEntity)` uses `[PrimaryKey]`
- upsert key selection prefers `[PrimaryKey]`
- `PrimaryKeyTableGateway<TEntity>` requires at least one `[PrimaryKey]`

## The two attributes are different

- `[Id]` and `[PrimaryKey]` may exist on different properties in the same entity
- they must not be placed on the same property
- use `TableGateway<TEntity, TRowID>` when an `[Id]` exists
- use `PrimaryKeyTableGateway<TEntity>` when the entity has no `[Id]` at all
