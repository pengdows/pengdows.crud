# Entity Attributes & Mapping Reference

All mapping attributes and operational rules for entity modeling in `pengdows.crud`.

---

## Core Mapping Attributes

### `[Table]`
Specifies the database table name, optionally schema-qualified.

```csharp
[Table("orders")]
[Table("sales.orders")] // Schema-qualified
public class Order { }
```

### `[Column]`
Maps a property to a database column with explicit type and optional size.

```csharp
[Column("order_number", DbType.String, 50)]
public string OrderNumber { get; set; } = string.Empty;

[Column("total_amount", DbType.Decimal)]
public decimal TotalAmount { get; set; }
```

---

## Key Attributes: `[Id]` vs. `[PrimaryKey]`

> [!IMPORTANT]
> **CRITICAL RULE**: `[Id]` and `[PrimaryKey]` are **MUTUALLY EXCLUSIVE** on a single property. Never place both on the same property.

| Attribute | Meaning | Gateway Used | Composite? | Purpose |
|---|---|---|---|---|
| **`[Id]`** | Surrogate Pseudokey / Row ID | `TableGateway<T, TRowID>` | Single only | Row-id operations, easy lookups, FK targets. |
| **`[PrimaryKey(n)]`** | Natural / Business Primary Key | `PrimaryKeyTableGateway<T>` (or compound lookups in `TableGateway`) | Yes (1-based order) | Domain uniqueness, natural key lookup, upsert conflict key. |

### `[Id]` (Pseudokey)
```csharp
[Id]           // Writable: client provides value (included in INSERT)
[Column("id", DbType.Int64)]
public long Id { get; set; }

[Id(false)]    // Non-writable: DB generates value via autoincrement/identity (omitted from INSERT)
[Column("id", DbType.Int64)]
public long Id { get; set; }
```

### `[PrimaryKey]` (Natural Business Key)
```csharp
[PrimaryKey(1)]
[Column("tenant_id", DbType.String, 50)]
public string TenantId { get; set; } = string.Empty;

[PrimaryKey(2)]
[Column("order_number", DbType.String, 50)]
public string OrderNumber { get; set; } = string.Empty;
```

---

## Optimistic Concurrency: `[Version]`

Enables optimistic locking for concurrent modifications.

```csharp
[Version]
[Column("row_version", DbType.Int32)]
public int Version { get; set; }
```

- **Create**: If version is `0` or `null`, automatically initialized to `1`.
- **Update**: Automatically increments `SET row_version = row_version + 1` and appends `WHERE row_version = @currentVersion`.
- **Conflict Detection**: Throws `ConcurrencyConflictException` if 0 rows are affected during update.

---

## Audit Attributes: `[CreatedBy]`, `[CreatedOn]`, `[LastUpdatedBy]`, `[LastUpdatedOn]`

```csharp
[CreatedBy]
[Column("created_by", DbType.String, 100)]
public string CreatedBy { get; set; } = string.Empty;

[CreatedOn]
[Column("created_at", DbType.DateTime)]
public DateTime CreatedAt { get; set; }

[LastUpdatedBy]
[Column("updated_by", DbType.String, 100)]
public string UpdatedBy { get; set; } = string.Empty;

[LastUpdatedOn]
[Column("updated_at", DbType.DateTime)]
public DateTime UpdatedAt { get; set; }
```

### Critical Audit Invariants
1. **BOTH `CreatedBy/On` AND `LastUpdatedBy/On` are set on CREATE.** This allows unified "last modified" queries without conditional branching.
2. **Update Operations**: Only `LastUpdatedBy` and `LastUpdatedOn` are updated; `CreatedBy/On` remain untouched.
3. **Audit Resolver Requirement**: Entities with `[CreatedBy]` or `[LastUpdatedBy]` REQUIRE a registered `IAuditValueResolver`. Time-only audit fields (`[CreatedOn]`, `[LastUpdatedOn]`) default to `DateTime.UtcNow`.
4. **All Timestamps Normalized to UTC**: DateTime, DateTimeOffset, and TimestampOffset are always normalized to UTC.
5. **In-Memory Audit Restoration on Failure**: In gateway convenience methods, audit properties are snapshot before execution. If a write fails or is rejected before DB acceptance (e.g. version conflict), the previous in-memory values are automatically restored.
6. **`AuditCreationPolicy`**:
   - `AuditCreationPolicy.PreserveExplicitValues` (default): Honors existing non-default values on create.
   - `AuditCreationPolicy.OverwriteExplicitValues`: Always overwrites with resolver values.

---

## Behavioral Attributes

### `[NonInsertable]` & `[NonUpdateable]`
- `[NonInsertable]`: Omitted from generated `INSERT` statements (e.g., computed columns, server defaults).
- `[NonUpdateable]`: Omitted from generated `UPDATE` statements.

### `[Json]`
Explicitly marks a column for JSON serialization/deserialization.
- Automatically detected for `System.Text.Json` CLR types (`JsonDocument`, `JsonElement`, `JsonNode`, `JsonValue`).
- Works across all supported database engines.
