# Type Coercion

pengdows.crud provides robust type coercion between .NET types and database types through the `TypeCoercionHelper` class. This enables seamless mapping between POCOs and SQL data across different database providers.

## Overview

Type coercion handles the conversion between:

- Database values → .NET object properties
- .NET object properties → database parameters
- Cross-database type compatibility (e.g., Oracle NUMBER to .NET int)
- String-to-enum conversions with configurable failure modes

## TypeCoercionHelper

The core coercion logic provides:

- Safe conversion with null handling
- Enum parsing from strings or numeric values
- DateTime UTC conversion and normalization
- GUID string parsing and formatting
- JSON serialization/deserialization for complex types
- Provider-specific type mapping

## Enum Parsing

pengdows.crud supports flexible enum parsing through `EnumParseFailureMode`. Set it on the gateway via the `EnumParseBehavior` property:

```csharp
gateway.EnumParseBehavior = EnumParseFailureMode.SetNullAndLog;
```

`EnumParseFailureMode` has three values:

### EnumParseFailureMode.Throw (Default)

- Throws an exception if enum parsing fails
- Recommended for strict validation scenarios
- Ensures data integrity by failing fast on invalid values

### EnumParseFailureMode.SetNullAndLog

- Sets the property to `null` (requires a nullable enum type) on parse failure
- Logs a warning so the failure is visible without crashing
- Useful when bad data is expected and the application can tolerate a null

### EnumParseFailureMode.SetDefaultValue

- Sets the property to the enum's default value (value `0`) on parse failure
- Useful for data migration or legacy-data scenarios where the default is a safe sentinel

## Enum Column Attributes

Three attributes control how enum columns are mapped:

| Attribute | Purpose |
|-----------|---------|
| `[EnumColumn(Type enumType)]` | Explicitly declares the enum type for a column |
| `[EnumLiteral(string literal)]` | Overrides the database string for a specific enum member |
| `[Json]` | Serializes the value as JSON (works for complex types and enums stored as JSON objects) |

## Cross-Database Type Mapping

pengdows.crud normalizes types across database providers:

| .NET Type | SQL Server | PostgreSQL | Oracle | MySQL | SQLite |
|-----------|------------|------------|--------|-------|--------|
| `int` | INT | INTEGER | NUMBER(10,0) | INT | INTEGER |
| `long` | BIGINT | BIGINT | NUMBER(19,0) | BIGINT | INTEGER |
| `decimal` | DECIMAL | NUMERIC | NUMBER | DECIMAL | REAL |
| `string` | NVARCHAR | TEXT | VARCHAR2 | VARCHAR | TEXT |
| `DateTime` | DATETIME2 | TIMESTAMP | DATE | DATETIME | TEXT |
| `bool` | BIT | BOOLEAN | NUMBER(1,0) | TINYINT(1) | INTEGER |
| `Guid` | UNIQUEIDENTIFIER | UUID | RAW(16) | BINARY(16) | TEXT |

## DateTime Handling

- All timestamps are normalized to **UTC**
- Database-specific timezone handling is abstracted away
- Audit timestamps (`[CreatedOn]`, `[LastUpdatedOn]`) are always UTC
- Local time conversion is handled at the application layer

## JSON Support

Complex objects can be stored as JSON in supported databases:

```csharp
[Json]
[Column("settings", DbType.String)]
public UserSettings Settings { get; set; }
```

- Automatically serializes/deserializes complex types
- Uses `System.Text.Json` by default
- The `[Json]` attribute exposes a `SerializerOptions` property for supplying a custom `JsonSerializerOptions` instance
- Supports nullable reference types
- Works across all supported databases

## Special-Purpose Coercion Attributes

### [CorrelationToken]

Marks a column used as a fallback correlation identifier on databases that do not support `RETURNING` / `OUTPUT` clauses. When the database cannot return the generated row ID inline, pengdows.crud uses this column to locate the newly inserted row.

### [Version]

Enables optimistic concurrency control:

```csharp
[Version]
[Column("version")]
public int Version { get; set; }
```

| Operation | Behavior |
|-----------|----------|
| **Create** | Version is automatically set to `1` if null or `0` |
| **Update** | Version is incremented by 1 in the `SET` clause; `WHERE version = @currentVersion` is appended |

`UpdateAsync` automatically throws `ConcurrencyConflictException` when a `[Version]` column is present and the UPDATE affects 0 rows (version mismatch — optimistic concurrency conflict).

## Null Handling

- Nullable reference types are properly supported
- `DBNull.Value` is correctly mapped to `null`
- Value types use nullable variants (`int?`, `DateTime?`, etc.)
- Required fields throw exceptions on null values

## Custom Type Converters

pengdows.crud allows custom type conversion logic:

```csharp
var registry = new AdvancedTypeRegistry();
registry.RegisterConverter(new CustomTypeConverter());

// Register provider-specific mapping when needed
registry.RegisterMapping<CustomType>(
    SupportedDatabase.PostgreSql,
    new ProviderTypeMapping(DbType.String));
```

## Best Practices

- Use `EnumParseFailureMode.Throw` in production for data integrity
- Store complex types as JSON for cross-database portability
- Always use UTC for timestamp fields
- Leverage nullable reference types for proper null handling
- Test type coercion with your specific database providers

## Error Handling

Type coercion failures are logged and can throw exceptions:

- Invalid enum values (when using Throw mode)
- Incompatible type conversions
- Malformed JSON for complex types
- Out-of-range numeric conversions
