# Entity Mapping & Attributes

`pengdows.crud` uses attributes to map C# POCOs to database tables.

## Table & Column Mapping

- `[Table("name", Schema = "schema")]`: Maps a class to a database table.
- `[Column("name", DbType.Int32, n)]`: Maps a property to a database column.
- `[NonInsertable]`: Excludes column from INSERT statements (e.g., generated columns, some audit fields).
- `[NonUpdateable]`: Excludes column from UPDATE statements (e.g., `[CreatedBy]`, `[CreatedOn]`).
- `[Id]` and `[PrimaryKey]` are also excluded from UPDATE SET clauses.

## Primary Keys vs Pseudo Keys

| Concept | Attribute | Purpose |
|---------|-----------|---------|
| **Pseudo Key / Row ID** | `[Id]` | Surrogate identifier for TableGateway operations, FKs, easy lookup. |
| **Primary Key / Business Key** | `[PrimaryKey(n)]` | Natural/business key — why the row exists (can be composite). |

**Rules:**
1. `[Id]` and `[PrimaryKey]` are MUTUALLY EXCLUSIVE on a column.
2. `[Id(false)]` (default): DB-generated (autoincrement/identity). Column omitted from INSERT.
3. `[Id(true)]`: Client-provided. Column included in INSERT.
4. `RetrieveOneAsync(TRowID)` uses `[Id]`.
5. `RetrieveOneAsync(TEntity)` uses `[PrimaryKey]` columns.
6. Upsert conflict key: `[PrimaryKey]` preferred; fallback to writable `[Id]`.

## Audit & Concurrency

- `[CreatedBy]`, `[CreatedOn]`, `[LastUpdatedBy]`, `[LastUpdatedOn]`: Auto-populated audit fields.
- **IMPORTANT:** Both `CreatedBy/On` AND `LastUpdatedBy/On` are set on **CREATE**.
- **Update Behavior:** Only `LastUpdatedBy/On` are updated during an **UPDATE** operation.
- `[Version]`: Optimistic concurrency column (e.g., `int` or `long`). Incremented by 1 on each UPDATE.

## Update Strategy

- `UpdateAsync(entity)`: Generates an UPDATE for all updatable columns.
- `UpdateAsync(entity, loadOriginal: true)`: Reloads the original row from the DB to detect changes and perform a concurrency check using the `[Version]` column. If no changes are detected or if a version mismatch occurs, it returns 0.

## Type Conversions

- Enum properties typed directly as an enum are auto-detected (no attribute needed). Use `[EnumColumn(typeof(T))]` only when the property type is `object` and the enum type can't be inferred.
- `[EnumLiteral("string")]`: Applied to **enum fields** (not properties) to map enum values to custom string literals in the database.
- `[Json]`: Serializes/deserializes complex property types to/from JSON using `System.Text.Json`. Pair with `DbType.String`.
- `[CorrelationToken]`: Marks a property used as a unique correlation token for generated-ID retrieval fallback (populated on INSERT, then queried back to get the DB-generated identity).
- `Uuid7Optimized`: High-performance, time-sortable IDs (RFC 9562).
