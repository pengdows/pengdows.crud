# Entity Attributes Reference

All attributes for entity mapping in pengdows.crud.

## Core Mapping Attributes

### TableAttribute

Specifies the database table name for an entity.

```csharp
[Table("table_name")]
[Table("schema.table_name")]  // With schema
public class MyEntity { }
```

**Parameters:**
- `name` (string) — Table name, optionally schema-qualified

**Notes:**
- Required on all entities used with TableGateway
- Supports schema qualification (e.g., "dbo.users", "public.accounts")
- Schema portion is automatically quoted per database requirements

### ColumnAttribute

Maps a property to a database column with type information.

```csharp
[Column("column_name", DbType.String)]
[Column("column_name", DbType.Int32, 50)]  // With size
public string Name { get; set; }
```

**Parameters:**
- `name` (string) — Database column name
- `type` (DbType) — ADO.NET database type
- `size` (int, optional) — Column size for variable-length types

**Supported DbTypes:**
- `DbType.String` — Text/varchar columns
- `DbType.Int32`, `Int64`, `Int16` — Integer columns
- `DbType.Boolean` — Boolean/bit columns
- `DbType.DateTime` — Date/time columns (stored as UTC)
- `DbType.Decimal` — Decimal/numeric columns
- `DbType.Guid` — UUID/uniqueidentifier columns
- `DbType.Binary` — Binary/blob columns

## Key Attributes

### IdAttribute

Marks the pseudokey (row ID) column for the entity.

```csharp
[Id]
[Column("id", DbType.Int64)]
public long Id { get; set; }

[Id(writable: false)]  // For identity/auto-increment columns
[Column("id", DbType.Int32)]
public int Id { get; set; }
```

**Parameters:**
- `writable` (bool, default: true) — Whether the ID can be set on insert

**Rules:**
- Exactly one `[Id]` property required per entity
- Must be a primitive type: `int`, `long`, `Guid`, or `string` (nullable allowed)
- Non-writable IDs are excluded from INSERT statements (for identity columns)
- Used for single-row lookups and as the `TRowID` generic parameter

### PrimaryKeyAttribute

Defines business/natural primary key columns (separate from pseudokey).

```csharp
[PrimaryKey(1)]
[Column("tenant_id", DbType.String)]
public string TenantId { get; set; }

[PrimaryKey(2)]
[Column("email", DbType.String)]
public string Email { get; set; }
```

**Parameters:**
- `order` (int) — Order of this column in composite key (1-based)

**Rules:**
- Can have multiple PrimaryKey columns with different orders
- Used for composite key lookups and WHERE clause generation
- Cannot overlap with `[Id]`, audit fields, or `[Version]`
- Supports multi-column business keys (tenant_id + email, etc.)

### VersionAttribute

Enables optimistic concurrency control with version fields.

```csharp
[Version]
[Column("row_version", DbType.Int32)]
public int Version { get; set; }
```

**Behavior:**
- Automatically incremented on each UPDATE
- Added to WHERE clause during updates to detect concurrent changes
- Protects against lost update problems
- Only one version field allowed per entity

## Audit Attributes

### CreatedByAttribute

Automatically populated with user ID on INSERT operations.

```csharp
[CreatedBy]
[Column("created_by", DbType.String)]
public string CreatedBy { get; set; }
```

**Behavior:**
- Set once during INSERT, never updated
- Populated from registered `IAuditValueResolver`
- Excluded from UPDATE statements to preserve audit trail

### CreatedOnAttribute

Automatically populated with UTC timestamp on INSERT operations.

```csharp
[CreatedOn]
[Column("created_at", DbType.DateTime)]
public DateTime CreatedAt { get; set; }
```

**Behavior:**
- Set once during INSERT with UTC timestamp
- Never updated to preserve creation time
- Excluded from UPDATE statements

### LastUpdatedByAttribute

Automatically updated with user ID on INSERT and UPDATE operations.

```csharp
[LastUpdatedBy]
[Column("updated_by", DbType.String)]
public string? UpdatedBy { get; set; }
```

**Behavior:**
- Set during both INSERT and UPDATE
- Populated from registered `IAuditValueResolver`
- Tracks who last modified the record

### LastUpdatedOnAttribute

Automatically updated with UTC timestamp on INSERT and UPDATE operations.

```csharp
[LastUpdatedOn]
[Column("updated_at", DbType.DateTime)]
public DateTime? UpdatedAt { get; set; }
```

**Behavior:**
- Set during both INSERT and UPDATE with current UTC time
- Nullable to distinguish never-updated records (NULL) from updated ones
- Automatically maintained by the framework

## Behavior Modifier Attributes

### NonInsertableAttribute

Excludes property from INSERT statements.

```csharp
[NonInsertable]
[Column("computed_field", DbType.String)]
public string ComputedValue { get; set; }
```

**Use Cases:**
- Database computed columns
- Fields populated by triggers
- Read-only columns that shouldn't be inserted

### NonUpdateableAttribute

Excludes property from UPDATE statements.

```csharp
[NonUpdateable]
[Column("created_at", DbType.DateTime)]
public DateTime CreatedAt { get; set; }
```

**Use Cases:**
- Immutable fields (creation timestamps, etc.)
- Fields that should only be set once
- Computed columns that change based on other fields

## Special Type Attributes

### EnumColumnAttribute

Configures enum handling for columns.

```csharp
public enum UserStatus { Active, Inactive, Suspended }

[EnumColumn(typeof(UserStatus))]
[Column("status", DbType.String)]
public UserStatus Status { get; set; }
```

**Parameters:**
- `enumType` (Type) — The enum type for conversion

**Notes:**
- Enum parsing behavior is configured at the TableGateway level via `EnumParseBehavior` property
- Invalid enum values throw by default, or can be configured to return default/null
- Supports both string and integer storage in the database

### JsonAttribute

Enables JSON serialization/deserialization for complex types.

```csharp
public class UserPreferences
{
    public string Theme { get; set; }
    public bool Notifications { get; set; }
}

[Json]
[Column("preferences", DbType.String)]
public UserPreferences? Preferences { get; set; }

// Custom serializer options
[Json(SerializerOptions = customOptions)]
[Column("metadata", DbType.String)]
public Dictionary<string, object>? Metadata { get; set; }
```

**Properties:**
- `SerializerOptions` (JsonSerializerOptions, optional) — Custom serialization settings

**Behavior:**
- Automatically serializes/deserializes to/from JSON strings
- Supports nullable properties (NULL database values)
- Uses System.Text.Json by default

## Complete Examples

### Basic Entity

```csharp
[Table("products")]
public class Product
{
    [Id]
    [Column("id", DbType.Int64)]
    public long Id { get; set; }

    [Column("name", DbType.String, 100)]
    public string Name { get; set; } = string.Empty;

    [Column("price", DbType.Decimal)]
    public decimal Price { get; set; }

    [Column("is_active", DbType.Boolean)]
    public bool IsActive { get; set; } = true;

    [CreatedOn]
    [Column("created_at", DbType.DateTime)]
    public DateTime CreatedAt { get; set; }

    [LastUpdatedOn]
    [Column("updated_at", DbType.DateTime)]
    public DateTime? UpdatedAt { get; set; }
}
```

### Multi-tenant Entity

```csharp
[Table("tenant_users")]
public class TenantUser
{
    [Id]
    [Column("id", DbType.Guid)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [PrimaryKey(1)]
    [Column("tenant_id", DbType.String, 50)]
    public string TenantId { get; set; } = string.Empty;

    [PrimaryKey(2)]
    [Column("email", DbType.String, 255)]
    public string Email { get; set; } = string.Empty;

    [Column("name", DbType.String, 100)]
    public string Name { get; set; } = string.Empty;

    [EnumColumn(typeof(UserRole))]
    [Column("role", DbType.String, 20)]
    public UserRole Role { get; set; }

    [Version]
    [Column("version", DbType.Int32)]
    public int Version { get; set; }

    [CreatedBy]
    [Column("created_by", DbType.String, 50)]
    public string CreatedBy { get; set; } = string.Empty;

    [CreatedOn]
    [Column("created_at", DbType.DateTime)]
    public DateTime CreatedAt { get; set; }

    [LastUpdatedBy]
    [Column("updated_by", DbType.String, 50)]
    public string? UpdatedBy { get; set; }

    [LastUpdatedOn]
    [Column("updated_at", DbType.DateTime)]
    public DateTime? UpdatedAt { get; set; }
}
```

### Entity with JSON and Computed Fields

```csharp
public class UserSettings
{
    public string Theme { get; set; } = "light";
    public bool EmailNotifications { get; set; } = true;
    public string[] Tags { get; set; } = Array.Empty<string>();
}

[Table("user_profiles")]
public class UserProfile
{
    [Id(writable: false)]  // Identity column
    [Column("id", DbType.Int32)]
    public int Id { get; set; }

    [Column("user_id", DbType.Guid)]
    public Guid UserId { get; set; }

    [Column("display_name", DbType.String, 100)]
    public string DisplayName { get; set; } = string.Empty;

    [Json]
    [Column("settings", DbType.String)]
    public UserSettings? Settings { get; set; }

    [NonInsertable]
    [NonUpdateable]
    [Column("full_display_name", DbType.String)]
    public string FullDisplayName { get; set; } = string.Empty; // Computed column

    [NonUpdateable]
    [Column("created_at", DbType.DateTime)]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at", DbType.DateTime)]
    public DateTime? UpdatedAt { get; set; }
}
```

## Validation Rules

### Required Combinations

- Every entity must have `[Table]` and exactly one `[Id]` property
- `[Id]` properties must also have `[Column]` attributes
- All properties mapped to database must have `[Column]` attributes

### Attribute Conflicts

- `[Id]` cannot be combined with `[PrimaryKey]`, audit attributes, or `[Version]`
- `[CreatedBy]` and `[CreatedOn]` should not be combined with `[NonInsertable]`
- `[LastUpdatedBy]` and `[LastUpdatedOn]` should not be combined with `[NonUpdateable]`
- Only one `[Version]` attribute allowed per entity

### Type Compatibility

- `[Id]` properties: `int`, `long`, `Guid`, `string` (and nullable versions)
- `[Version]` properties: `int`, `long`, or other numeric types
- Audit timestamp properties: `DateTime` or `DateTime?`
- Audit user properties: `string`, `int`, `Guid`, or nullable versions
- `[Json]` properties: Any serializable type
