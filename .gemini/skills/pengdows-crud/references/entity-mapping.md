# Entity Mapping & Attributes

`pengdows.crud` uses attributes to map C# POCOs to database tables.

## Table & Column Mapping

- `[Table("name", Schema = "schema")]`: Maps a class to a database table.
- `[Column("name", DbType.Int32, n)]`: Maps a property to a database column.
- `[NonInsertable]`: Excludes column from INSERT statements.
- `[NonUpdateable]`: Excludes column from UPDATE statements.

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
- **IMPORTANT:** Both `CreatedBy/On` AND `LastUpdatedBy/On` are set on CREATE.
- `[Version]`: Optimistic concurrency column (e.g., `int` or `byte[]`).

## Type Conversions

- `[IsEnum]`: Auto-converts enums to `string` or `int` in the database.
- `[IsJsonType]`: Auto-serializes objects to JSON using `System.Text.Json`.
- `Uuid7Optimized`: High-performance, time-sortable sortable IDs (RFC 9562).
