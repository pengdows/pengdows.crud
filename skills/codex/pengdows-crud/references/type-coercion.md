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

- Sets the property to null (for nullable types) and logs a warning on parse failure
- Useful when you want to tolerate stale/unknown enum values without crashing

```csharp
var gateway = new TableGateway<User, int>(context, enumParseBehavior: EnumParseFailureMode.SetNullAndLog);
```

### EnumParseFailureMode.SetDefaultValue

- Returns the enum's default value (typically 0) on parse failure
- Useful for data migration scenarios

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
| `DateOnly` | DATE | DATE | DATE | DATE | TEXT |
| `TimeOnly` | TIME | TIME | INTERVAL DAY(0) TO SECOND | TIME | TEXT |

## DateTime Handling

- All timestamps are normalized to **UTC**
- Database-specific timezone handling is abstracted away
- Audit timestamps (`[CreatedOn]`, `[LastUpdatedOn]`) are always UTC
- Local time conversion is handled at the application layer

## DateOnly and TimeOnly

`DateOnly` and `TimeOnly` (and their nullable forms) work as plain entity properties on every
database, with no converter: declare them `[Column("d", DbType.Date)]` and `[Column("t", DbType.Time)]`
(`DbType.DateTime`/`DateTime2` also accept `DateOnly`).

- Writes bind exactly as the equivalent midnight `DateTime` / `TimeSpan`, so each database's
  date/time handling applies unchanged. Where a driver needs something else the dialect handles it:
  Snowflake binds `TIME` from a `DateTime`, Oracle (no `TIME` type) as `INTERVAL DAY TO SECOND`,
  SingleStore as text, and FlatFile takes `DateOnly`/`TimeOnly` natively (a `DateTime`/`TimeSpan`
  declared `DbType.Date`/`DbType.Time` is converted for it).
- Reads accept every shape providers return: `DateTime`, `DateTimeOffset`, `TimeSpan`, ISO strings
  (SQLite, FlatFile) and native `DateOnly`/`TimeOnly`. A date is taken from the stored wall-clock
  value, never shifted through UTC.
- Spanner has no time-of-day column type, so `TimeOnly` has no native column there (tracked as TYPE-006).

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
