# Primary Keys vs. Pseudokeys

## Surrogate IDs (`[Id]`)
`[Id]` marks the pseudo key that `TableGateway` uses for create/update/delete operations. `IdAttribute` exposes a `Writable` flag (true by default) so you can mark columns as client-supplied (`[Id]` or `[Id(true)]`) or database-generated (`[Id(false)]`). Update helpers and `Upsert` need this surrogate ID to exist, and inserts will populate the column from the database when `Writable` is false while still allowing you to pass an explicit value when it is true.【F:pengdows.crud/attributes/IdAttribute.cs†L1-L15】【F:pengdows.crud/TableGateway.Update.cs†L1-L92】【F:pengdows.crud/TableGateway.Upsert.cs†L66-L79】

## Business keys (`[PrimaryKey]`)
`PrimaryKeyAttribute` can be placed on one or more properties to declare the natural/business key, with an optional `Order` parameter to establish the sequence for composite keys. These attributes are the source of truth for `RetrieveOneAsync`, which generates a `WHERE` clause using every `[PrimaryKey]` column and throws if none exist, so every retrievable entity must define a business key explicitly.【F:pengdows.crud/attributes/PrimaryKeyAttribute.cs†L1-L18】【F:pengdows.crud/TableGateway.Retrieve.cs†L102-L185】

## Mapping rules enforced in code
`TypeMapRegistry` hooks enforce the contract: every entity must declare either `[Id]` or at least one `[PrimaryKey]`, `[Id]` columns may not also carry `[PrimaryKey]`, and explicit `Order` values on composite keys must form a contiguous sequence starting at 1 (unchecked orders are assigned sequentially). These checks ensure the helper APIs can always tell which columns to use for surrogates, uniqueness checks, and upserts.【F:pengdows.crud/TypeMapRegistry.cs†L266-L345】

## CRUD and upsert behavior
`TableGateway` prefers the `[Id]` column for updates and deletes and falls back to `[PrimaryKey]` columns for `Upsert` conflict detection only when the surrogate ID is not writable. That means `[PrimaryKey]` attributes are the go-to choice for composite business keys, while `[Id]` (with `Writable=false`) is the safest choice when the database autogenerates the row ID yet you still need a stable handle for `Update`/`Delete`. Never mix the two annotations on the same property—`TypeMapRegistry` will throw, keeping the contract unambiguous.【F:pengdows.crud/TypeMapRegistry.cs†L266-L345】【F:pengdows.crud/TableGateway.Update.cs†L1-L92】【F:pengdows.crud/TableGateway.Upsert.cs†L66-L79】
