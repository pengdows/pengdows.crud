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

pengdows.crud supports flexible enum parsing through `EnumParseFailureMode`:

### EnumParseFailureMode.Throw (Default)

- Throws an exception if enum parsing fails
- Recommended for strict validation scenarios
- Ensures data integrity by failing fast on invalid values

```csharp
var gateway = new TableGateway<User, int>(context, enumParseBehavior: EnumParseFailureMode.Throw);
```

### EnumParseFailureMode.SetNullAndLog

- Sets the property to null on parse failure and logs a warning
- Useful for nullable enum properties where missing values should be tolerated
- Requires the property to be nullable

```csharp
var gateway = new TableGateway<User, int>(context, enumParseBehavior: EnumParseFailureMode.SetNullAndLog);
```

### EnumParseFailureMode.SetDefaultValue

- Returns the enum's default value (typically 0) on parse failure
- Useful for data migration scenarios
- Logs warnings for failed conversions

```csharp
var gateway = new TableGateway<User, int>(context, enumParseBehavior: EnumParseFailureMode.SetDefaultValue);
```

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

## CorrelationToken

The `[CorrelationToken]` attribute marks a property used as a unique correlation token for generated-ID retrieval fallback. Used when the database doesn't support `RETURNING`/`OUTPUT` and session-scoped identity functions are unreliable.

```csharp
[CorrelationToken]
[Column("correlation_id", DbType.Guid)]
public Guid CorrelationId { get; set; }
```

TableGateway generates a unique value, inserts it alongside the row, then immediately queries back using this token to retrieve the database-generated identity. The `CorrelationId` property is separate from the `[Id]` column.

## JSON Support

Complex objects can be stored as JSON in supported databases:

```csharp
[Json]
[Column("settings", DbType.String)]
public UserSettings Settings { get; set; }
```

- Automatically serializes/deserializes complex types
- Uses `System.Text.Json` by default
- Supports nullable reference types
- Works across all supported databases

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
- Use `EnumParseFailureMode.SetNullAndLog` for nullable enum properties where missing values are tolerated
- Use `EnumParseFailureMode.SetDefaultValue` for non-nullable enum properties in data migration scenarios
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
