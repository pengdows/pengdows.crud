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
- Fractional seconds a column can't hold are truncated, never rounded up: MySQL 8.0.8+ sessions set
  `sql_mode` `TIME_TRUNCATE_FRACTIONAL` for this. TiDB (and MySQL before 8.0.8) can't, and round:
  23:59:59.9999999 into `TIME(0)` becomes 24:00:00, which fails on read. Send values at the column's
  precision there.
- A `TimeSpan` outside 00:00:00 to 24:00:00 into `TIME` throws `ArgumentOutOfRangeException` on Sybase
  ASE, Informix and FlatFile, whose drivers would otherwise store a different value; MySQL's `TIME`
  holds ±838 h, and other databases reject it themselves.
- `sbyte`/`ushort`/`uint`/`ulong` bind as the smallest signed type that holds their range (Int16,
  Int32, Int64, Decimal) where the provider rejects the unsigned `DbType`s (PostgreSQL family, SQL
  Server, Oracle, Informix, SQLite); declare columns at least that wide (`ulong`: `DECIMAL(20,0)`, or
  `TEXT` on SQLite, where it is stored as exact text). A `char` bound as a string `DbType` is sent as a
  one-character string, and stored text that isn't exactly one character fails as `DataMappingException`.
- Spanner has no time-of-day column type (no `TIME`), so declare a `VARCHAR(16)`/`STRING(16)` column: a
  `TimeOnly` (or a `TimeSpan` declared `DbType.Time`) is stored as fixed-width `HH:mm:ss.fffffff` text,
  which sorts and compares in time order and reads back exactly.

## Wide and Provider-Specific Column Types

No value object or converter is needed for these (verified live, TYPE-005; see `docs/advanced-types.md`):

- DuckDB `HUGEINT`/`UHUGEINT` and Firebird `INT128` → `Int128`/`UInt128` (or `BigInteger`, `long`, `decimal`
  when the value fits). Converted with checked casts; out of range throws `DataMappingException`. Written as
  `BigInteger`, or as exact text on DuckDB (its driver can't bind the full range).
- DuckDB `LIST` and PostgreSQL arrays → `T[]` or `List<T>`, coerced element by element. DuckDB `MAP`/`STRUCT`
  → `Dictionary<,>`.
- Firebird `DECFLOAT` → `decimal` (or `double`); declare `[Column(..., DbType.VarNumeric)]` so the value binds as
  `FbDecFloat`. NaN/infinity or a value `decimal` would round throws `DataMappingException` into `decimal`.
- Vectors → `float[]` (`DbType.Object`): SQL Server 2025/Oracle 23ai `VECTOR` are written as exact text and read back
  (SqlClient 6.0's text carries only 8 digits; 6.1+ is exact); pgvector stores a `float[]` but Npgsql reads it only as
  `col::real[]` or via the `Pgvector.Npgsql` plugin (TYPE-015).
- SQL Server `hierarchyid` → `HierarchyId` (or `string`) with no `Microsoft.SqlServer.Types`: written as its text
  (`/1/2.5/`, converted implicitly by SQL Server; text on other databases), read from the stored encoding (TYPE-016).
- SQL Server `geometry`/`geography` → `Geometry`/`Geography` with no `Microsoft.SqlServer.Types` (its validation is
  Windows-only native code): written as a big-endian SRID + WKB that the gateway SQL turns into the instance with
  `STGeomFromWKB`, so the server validates it and keeps the SRID; read by decoding the stored encoding. Curves and
  `FULLGLOBE` have no WKB form and throw `DataMappingException` (TYPE-002).
- A stored value with no .NET representation throws `DataMappingException` naming the column, never a raw provider
  exception or a default: MySQL/MariaDB zero dates, PostgreSQL `numeric` NaN into `decimal`, SQL Server
  curves/`FULLGLOBE`, user CLR types without their assembly (select `col.ToString()` instead).

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
