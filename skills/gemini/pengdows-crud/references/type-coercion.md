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
| `DateOnly` | DATE | DATE | DATE | DATE | TEXT |
| `TimeOnly` | TIME | TIME | INTERVAL DAY(0) TO SECOND | TIME | TEXT |

## DateTime Handling

- All timestamps are normalized to **UTC**
- Database-specific timezone handling is abstracted away
- Audit timestamps (`[CreatedOn]`, `[LastUpdatedOn]`) are always UTC
- Local time conversion is handled at the application layer

## CorrelationToken

The `[CorrelationToken]` attribute marks a property used as a unique correlation token for generated-ID retrieval fallback. Needed only where the dialect's plan is `CorrelationToken` (Snowflake): no `RETURNING`/`OUTPUT`, sequence prefetch or session last-id function. Without it `CreateAsync` throws `NotSupportedException` there before writing (DEC-007). See `docs/generated-keys.md`.

```csharp
[CorrelationToken]
[Column("correlation_id", DbType.Guid)]
public Guid CorrelationId { get; set; }
```

TableGateway generates a unique value, inserts it alongside the row, then immediately queries back using this token to retrieve the database-generated identity. The `CorrelationId` property is separate from the `[Id]` column.

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
- A `Guid` declared `DbType.Binary` is stored as 16 bytes in the database's Guid byte order: RFC 4122 big-endian by
  default (MySQL family's `UUID_TO_BIN`, PostgreSQL, SQLite, Firebird, ...), .NET `ToByteArray()` order on SQL Server,
  Oracle and Sybase ASE; 16-byte columns read into a `Guid` decode the same way (changed in 2.0.6, TYPE-002).
- SQLite: a `decimal` binds as exact invariant text (declare the column `TEXT` to keep every digit; `REAL`/`NUMERIC`
  convert by affinity); a DuckDB `LIST` maps to `T[]`/`List<T>` through `DataReaderMapper` as through the gateway.
- PostgreSQL family: `Geometry`/`Geography` bind EWKB (WKT encoded; GeoJSON-only refused) and PostGIS/`vector` columns
  read without the NetTopologySuite/pgvector plugins (binary value via `GetBytes`); a C# enum into a PG `ENUM` column is
  sent untyped on PostgreSQL/YugabyteDB (CockroachDB accepts text) (string properties, PG 15+ MERGE upserts and custom `WHERE` still need `CAST`); `DateTimeOffset` with
  `DbType.Time` is `timetz`; CockroachDB runs multi-statement commands unprepared (TYPE-002).
- Oracle: `double`/`float` bind as `BINARY_DOUBLE`/`BINARY_FLOAT`; NUMBER beyond `decimal` reads into `double`; LONG data
  is fetched with the row; `DbType.DateTime2`/`DbType.Xml` are remapped. Firebird: `TIME WITH TIME ZONE` from a
  `DateTimeOffset` with `DbType.Time`; `BINARY`/`VARBINARY` read as `byte[]` (TYPE-002).
- SingleStore: spatial as WKT (`GEOGRAPHYPOINT` ~1e-7°), `VECTOR` written as JSON text / read from packed float32.
  Spanner: Guid sent untyped, uuid read from bytes, arrays read with nullable elements. Known gaps: Informix `TEXT`/`BSON`
  and `BOOLEAN` in `WHERE` (TYPE-020), Informix time-of-day fractions and ASE `BIGDATETIME`/`BIGTIME` (driver, TYPE-022).
- Snowflake: `VARIANT`/`OBJECT`/`ARRAY` written as `PARSE_JSON(:p)`, with inserts/MERGE source/batch update values from a
  `SELECT` (Snowflake refuses it in `VALUES`); spatial as EWKT both ways; `TIMESTAMP_LTZ`/`TZ` read as the exact
  `DateTimeOffset`. `VECTOR` needs the column's type (TYPE-020).
- Snowflake scale-0 `NUMBER` beyond `long` (Snowflake.Data reports it as `Int64` and overflows) → `ulong`, `decimal`,
  `double`, `BigInteger` or `Int128`/`UInt128`: read from its text as `BigInteger`, then checked casts; a `long`
  property that can't hold it throws `DataMappingException`.
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
