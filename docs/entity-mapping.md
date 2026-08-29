# Entity Mapping Attribute Reference

Every attribute that controls how an entity class maps to a table, column, or gateway behavior.
All fifteen live in `pengdows.crud/attributes/`; all are `[AttributeUsage]`-restricted to a single
target (property, except `[Table]` on the class and `[EnumLiteral]` on an enum field), and none are
`Inherited` unless noted. Validation happens once, at `TypeMapRegistry.BuildTableInfo` (first access
per `Type`, cached forever — see CLAUDE.md's TypeMapRegistry section) — a bad combination throws
`SqlGenerationException` (or, for the three "too many" cases, `TooManyColumns`/
`PrimaryKeyOnRowIdColumn`) at that point, not at query time.

## Summary matrix

| Attribute | Target | Read | Write (INSERT) | Write (UPDATE) | Requires resolver | Composite | Notes |
|---|---|---|---|---|---|---|---|
| `[Table(name, schema?)]` | class | — | — | — | no | — | Required on every mapped entity |
| `[Column(name, type, ordinal?)]` | property | yes | yes* | yes* | no | — | Required on every mapped property; unmapped properties are ignored entirely |
| `[Id]` / `[Id(true)]` | property | yes | yes | no (implicit `NonUpdateable`) | no | no — exactly one per entity | Client-provided row ID |
| `[Id(false)]` | property | yes | no (implicit `NonInsertable`) | no | no | no | DB-generated row ID (IDENTITY/SERIAL) |
| `[PrimaryKey]` / `[PrimaryKey(order)]` | property | yes | yes* | yes* | no | yes, via `order` | Business key; mutually exclusive with `[Id]` on the same property |
| `[Version]` | property | yes | yes (defaulted) | yes (server-incremented) | no | no — at most one per entity | Optimistic concurrency |
| `[CreatedBy]` | property | yes | yes | no | **yes** | — | Set once, on CREATE only |
| `[CreatedOn]` | property | yes | yes | no | no | — | Set once, on CREATE only, `UtcNow` |
| `[LastUpdatedBy]` | property | yes | yes | yes | **yes** | — | Set on both CREATE and UPDATE |
| `[LastUpdatedOn]` | property | yes | yes | yes | no | — | Set on both CREATE and UPDATE, `UtcNow` |
| `[CorrelationToken]` | property | yes | yes (generated) | yes* | no | no — at most one per entity | `TableGateway<T,TId>` only, see below |
| `[Json]` | property | yes | yes* | yes* | no | — | Serializes to/from a string column |
| `[EnumColumn(type)]` | property | yes | yes* | yes* | no | — | Only needed when the property type doesn't already reveal the enum |
| `[EnumLiteral(literal)]` | enum field | yes | yes | yes | no | — | Overrides the string stored for one enum member |
| `[NonInsertable]` | property | yes | no | yes* | no | — | Computed columns, DB defaults, triggers |
| `[NonUpdateable]` | property | yes | yes* | no | no | — | Immutable-after-create fields |

`*` = subject to the other attributes also present on the same property (e.g. `[PrimaryKey]` +
`[NonUpdateable]` together still excludes it from UPDATE).

## `[Table]`

```csharp
[Table("orders", "dbo")]
public class Order { ... }
```

Required on every entity class used with any gateway. `name` is required and cannot be empty
(`SqlGenerationException` if it is); `schema` is optional (`null` = provider default schema).
`Inherited = false` — an entity that derives from another mapped class still needs its own
`[Table]`.

## `[Column]`

```csharp
[Column("email", DbType.String)]
public string Email { get; set; }

[Column("legacy_flag", DbType.Int32, ordinal: 5)]
public int LegacyFlag { get; set; }
```

Required on every property that should be persisted — **properties without `[Column]` are
silently ignored** by every gateway, not an error. `name` cannot be empty. `type` is the
`System.Data.DbType` used for parameter creation and drives enum storage format (`DbType.String`
→ enum stored as its name; a numeric `DbType` → stored as its underlying numeric value; anything
else on an enum-typed column throws `SqlGenerationException`). `ordinal` (default `0`) controls
column/parameter ordering — see "Ordinal ordering" below.

**Duplicate column names** (two properties mapping to the same `[Column("x", ...)]` name) throw
`SqlGenerationException` at registration.

### Ordinal ordering

`ordinal` is opt-in per property. `TypeMapRegistry.AssignOrdinals` validates the *whole set* at
once, not per-property: a negative ordinal throws immediately; two properties sharing the same
non-zero ordinal throws `SqlGenerationException` ("Duplicate ColumnAttribute.Ordinal"). A
property left at the default `0` participates in ordering by discovery order (declaration order),
interleaved after any explicitly-ordinaled columns — leave `ordinal` unset unless you specifically
need to override the default column order (e.g. matching a legacy schema's physical column order).

## `[Id]` vs `[PrimaryKey]`

See CLAUDE.md's "CRITICAL: Pseudo Key (Row ID) vs Primary Key (Business Key)" section for the
conceptual distinction — this section covers only the attribute mechanics.

```csharp
[Id(false)]                      // DB-generated
[Column("id", DbType.Int64)]
public long Id { get; set; }

[PrimaryKey(1)]                  // Composite business key, part 1
[Column("order_id", DbType.Int32)]
public int OrderId { get; set; }

[PrimaryKey(2)]                  // Composite business key, part 2
[Column("product_id", DbType.Int32)]
public int ProductId { get; set; }
```

- `[Id]` and `[Id(true)]` are equivalent — `Writable` defaults to `true`. `[Id(false)]` means the
  database generates the value; that column is automatically excluded from INSERT
  (`IsNonInsertable = true` internally) and is **always** excluded from UPDATE regardless of
  writability — `[Id]` implies `NonUpdateable` unconditionally, not just the `Writable=false` case.
  This is a real behavior detail the `[NonUpdateable]` attribute's own doc comment doesn't
  mention (it only calls out `[CreatedBy]`/`[CreatedOn]` as "implicit") — verified directly against
  `TypeMapRegistry`'s `ColumnInfo` construction (`IsNonUpdateable = nonUpd != null || isId`).
- **Exactly one `[Id]` per entity** — a second throws `TooManyColumns`. `[Id]` can never be
  composite; if your natural key needs multiple columns, that's `[PrimaryKey]`'s job.
- **`[Id]` and `[PrimaryKey]` are mutually exclusive on the same property** — putting `[PrimaryKey]`
  on the `[Id]` column throws `PrimaryKeyOnRowIdColumn`. They can coexist on *different* columns of
  the same entity.
- **An entity needs at least one of `[Id]` or `[PrimaryKey]`** — neither present throws
  `SqlGenerationException` ("must define either [Id] or [PrimaryKey]").
- **Composite `[PrimaryKey]` ordering is all-or-nothing.** Every `[PrimaryKey]` on the entity must
  either all specify an explicit `order` or all omit it (bare `[PrimaryKey]`, `Order = 0`) —
  mixing throws `SqlGenerationException`. When orders are specified, they must form a contiguous
  sequence starting at 1 with no duplicates and no gaps (`1, 2, 3`, not `1, 3` or `1, 1`) —
  verified directly against `TypeMapRegistry.ValidatePrimaryKeys`.
- **Gateway selection** follows directly from which attribute is present: `[Id]` present →
  `TableGateway<TEntity, TRowID>` (row-ID operations: `UpdateAsync`, `DeleteAsync(TRowID)`,
  `RetrieveOneAsync(TRowID)`). `[PrimaryKey]`-only (no `[Id]`) → `PrimaryKeyTableGateway<TEntity>`
  — constructing it against an entity with no `[PrimaryKey]` columns throws
  `InvalidOperationException` at gateway-construction time, not at first use.

## `[Version]`

```csharp
[Version]
[Column("version", DbType.Int32)]
public int Version { get; set; }
```

At most one `[Version]` per entity (`TooManyColumns` on a second). The property type must be an
integral type (`byte`/`sbyte`/`short`/`ushort`/`int`/`uint`/`long`/`ulong`), `byte[]`, or the
library's `RowVersion` value type — anything else throws `SqlGenerationException`. `byte[]`/
`RowVersion` model an **opaque, database-generated version token** (e.g. SQL Server `rowversion`)
rather than a numeric counter the library increments itself.

- **CREATE:** if the numeric version is null/zero, the library sets it to `1` before INSERT.
  (Opaque `byte[]`/`RowVersion` versions are simply whatever the database assigns — there's nothing
  for the library to default.)
- **UPDATE:** for numeric versions, the SET clause increments it by 1 and the WHERE clause adds
  `version = @currentVersion`. `UpdateAsync` throws `ConcurrencyConflictException` automatically
  when this predicate matches zero rows (stale version or the row was deleted). Supported on
  **both** `TableGateway<T,TId>` and `PrimaryKeyTableGateway<T>`.
- A `[Version]` column also enables `loadOriginal`-by-default change-aware updates (see
  `docs/batch-operations.md` and `BuildUpdateAsync`'s `loadOriginal` parameter) — versioned
  entities re-read the current row before building the UPDATE so only genuinely changed columns
  are included, alongside the version predicate.
- **Mutually exclusive with `[Json]`** on the same property — if both are present, JSON handling
  is silently dropped in favor of version handling (`ci.IsJsonType = false` when `ci.IsVersion` is
  true). Don't rely on this; use `[Version]` alone.

## `[CreatedBy]` / `[CreatedOn]` / `[LastUpdatedBy]` / `[LastUpdatedOn]`

```csharp
[CreatedBy]    [Column("created_by", DbType.String)]   public string? CreatedBy { get; set; }
[CreatedOn]    [Column("created_on", DbType.DateTime)] public DateTime CreatedOn { get; set; }
[LastUpdatedBy][Column("updated_by", DbType.String)]   public string? UpdatedBy { get; set; }
[LastUpdatedOn][Column("updated_on", DbType.DateTime)] public DateTime UpdatedOn { get; set; }
```

**Both `CreatedBy`/`CreatedOn` AND `LastUpdatedBy`/`LastUpdatedOn` are set on CREATE** — this is
intentional (see CLAUDE.md's "CRITICAL: Audit Field Behavior"), so "last modified" queries work
without checking whether a row was ever updated. On UPDATE, only the `LastUpdated*` pair changes;
`CreatedBy`/`CreatedOn` are excluded from every UPDATE's SET clause by dedicated filtering in the
gateway SQL builders (`TableGateway.Sql.cs`/`.Batch.cs`), independent of the generic
`IsNonUpdateable` flag.

- **`[CreatedBy]`/`[LastUpdatedBy]` require an `IAuditValueResolver`.** If either attribute is
  present on the entity and no resolver was supplied to the gateway, the exact exception is
  `InvalidOperationException("AuditValues resolver is required for user-based audit fields.")`,
  thrown when audit values are actually applied (`CreateAsync`/`UpdateAsync`), not at
  `TypeMapRegistry` registration time. The property type must be `string`, `Guid`, or a numeric
  type — anything else throws `SqlGenerationException` at registration.
- **`[CreatedOn]`/`[LastUpdatedOn]` need no resolver** — they use `DateTime.UtcNow` (or
  `DateTimeOffset`/`TimestampOffset`, all UTC) directly. Property type must be `DateTime` or
  `DateTimeOffset` (nullable allowed) — anything else throws `SqlGenerationException`.
- **`AuditCreationPolicy`** (a settable property on every `ITableGateway`/`IPrimaryKeyTableGateway`,
  default `PreserveExplicitValues`) controls whether a caller-supplied `CreatedBy`/`CreatedOn` on
  the entity survives `CreateAsync` instead of being overwritten by the resolver — see CLAUDE.md's
  "CRITICAL: Audit Field Behavior" section for the security implication of the default when an
  entity is populated from untrusted input. Set `Authoritative` to always trust the resolver.

## `[CorrelationToken]`

```csharp
[CorrelationToken]
[Column("insert_token", DbType.String)]
public string? InsertToken { get; set; }
```

At most one per entity (`TooManyColumns` on a second). **Only meaningful on `TableGateway<T,TId>`**
— it is the `GeneratedKeyPlan.CorrelationToken` fallback for retrieving a database-generated
`[Id(false)]` value on engines with no `RETURNING`/`OUTPUT` clause and no safe session-scoped
last-insert-id function. `PrimaryKeyTableGateway<T>` never reads `CorrelationColumn` — adding this
attribute to a `[PrimaryKey]`-only entity has no effect. See `docs/generated-keys.md` for the full
`GeneratedKeyPlan` strategy hierarchy this fits into.

## `[Json]`

```csharp
[Json]
[Column("metadata", DbType.String)]
public Dictionary<string, object>? Metadata { get; set; }

[Json(SerializerOptions = myOptions)]
[Column("config", DbType.String)]
public MyConfig? Config { get; set; }
```

Serializes the property to a JSON string on write, deserializes on read. `SerializerOptions`
defaults to `JsonSerializerOptions.Default`. **`[Json]` is not required for every JSON-shaped
type** — `TypeMapRegistry` auto-infers JSON handling for `System.Text.Json.JsonDocument`,
`JsonElement`, any `JsonNode`-derived type, and the library's own `JsonValue` value object, without
the attribute. Everything else (POCOs, `Dictionary<,>`, `List<>`, etc. — including the
`Dictionary<string, object>` example above) needs the explicit attribute; there is no general
"any complex type is JSON" inference.

## `[EnumColumn]`

```csharp
[EnumColumn(typeof(StatusEnum))]
[Column("status", DbType.String)]
public object Status { get; set; }   // property type doesn't reveal the enum
```

Only needed when the property's declared type doesn't already indicate which enum to use (e.g. an
`object`-typed or otherwise generic property). When the property is directly typed as the enum
(`public StatusEnum Status { get; set; }`), the enum type is inferred automatically and
`[EnumColumn]` is redundant. Throws `ArgumentException` at attribute-construction time if the
given `Type` isn't actually an enum.

Storage format (name vs. numeric value) is controlled by `[Column]`'s `DbType`, not by this
attribute — see CLAUDE.md's "Enum Storage" table. An enum-typed column with a `DbType` that is
neither string-family nor numeric-family throws `SqlGenerationException` at registration.
Read-side failure policy (what happens when a stored value doesn't parse back to a valid enum
member) is a *mapper* concern, not an attribute — `IMapperOptions.EnumMode`
(`EnumParseFailureMode`: `Throw`, `SetNullAndLog`, or `SetDefaultValue`), passed to
`DataReaderMapper`/gateway hydration, not declared per-property.

## `[EnumLiteral]`

```csharp
public enum Status
{
    [EnumLiteral("Active")]
    Active,

    [EnumLiteral("On-Hold")]  // stored/read as "On-Hold", not "OnHold"
    OnHold
}
```

Applied to an enum **field**, not a property. Overrides the string persisted for that one member
when the column's `DbType` is string-family — use it when the desired database literal isn't a
valid C# identifier (hyphens, spaces) or needs to differ from the member name for any other
reason. `Inherited = false`, `AllowMultiple = false`.

## `[NonInsertable]` / `[NonUpdateable]`

```csharp
[NonInsertable]                 // computed column / DB default / trigger-populated
[Column("full_name", DbType.String)]
public string FullName { get; set; }

[NonUpdateable]                 // immutable after creation
[Column("original_price", DbType.Decimal)]
public decimal OriginalPrice { get; set; }
```

Independent, stackable exclusion flags — a column can be `[NonInsertable]`, `[NonUpdateable]`,
both, or neither, in addition to whatever other attributes it carries. `[Id(false)]` already
implies `NonInsertable` and every `[Id]` already implies `NonUpdateable` — don't add these
attributes redundantly to an `[Id]` column. Prefer `[Id(false)]` over `[NonInsertable]` for actual
identity/auto-increment columns; `[NonInsertable]` is for the general case (computed columns,
server-side defaults, trigger-populated values) that isn't specifically the row identifier.

## Validation summary (`SqlGenerationException`, `TooManyColumns`, `PrimaryKeyOnRowIdColumn`)

All thrown once, at `TypeMapRegistry.BuildTableInfo` (first access per `Type`) — never during query
execution:

| Condition | Exception |
|---|---|
| Missing `[Table]`, or `[Table]`'s name is empty | `SqlGenerationException` |
| `[Column]`'s name is empty | `SqlGenerationException` |
| Two properties map to the same `[Column]` name | `SqlGenerationException` |
| Enum column's `DbType` is neither string- nor numeric-family | `SqlGenerationException` |
| Negative or duplicate non-zero `[Column]` ordinal | `SqlGenerationException` |
| No `[Id]` and no `[PrimaryKey]` anywhere on the entity | `SqlGenerationException` |
| Some but not all `[PrimaryKey]`s specify an explicit `order` | `SqlGenerationException` |
| `[PrimaryKey]` orders aren't a contiguous 1..N sequence | `SqlGenerationException` |
| `[Version]`/`[CreatedOn]`/`[LastUpdatedOn]` property type is invalid for that attribute | `SqlGenerationException` |
| `[CreatedBy]`/`[LastUpdatedBy]` property type isn't `string`/`Guid`/numeric | `SqlGenerationException` |
| A second `[Id]` on the same entity | `TooManyColumns` |
| A second `[Version]` on the same entity | `TooManyColumns` |
| A second `[CorrelationToken]` on the same entity | `TooManyColumns` |
| `[PrimaryKey]` applied to the same property as `[Id]` | `PrimaryKeyOnRowIdColumn` |

Audit-resolver absence (`InvalidOperationException("AuditValues resolver is required for
user-based audit fields.")`) is a separate, runtime (not registration-time) check — see
`[CreatedBy]`/`[LastUpdatedBy]` above.
