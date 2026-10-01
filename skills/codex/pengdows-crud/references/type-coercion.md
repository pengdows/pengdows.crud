# Type Coercion & Coercion Registry

`pengdows.crud` provides a high-performance type coercion pipeline between .NET CLR types and ADO.NET provider types via `TypeCoercionHelper` and `CoercionRegistry`.

---

## Core Capabilities

- **Bidirectional automatic conversion**: Handles provider values → CLR values and CLR values → provider parameters across supported engines. Applications do not need Dapper-style per-type handlers or entity-specific conversion code for the supported matrix.
- **Advanced provider types**: Built-in coercions cover network, ranges, intervals, JSON, spatial values, large objects, and rowversion values through the normal CRUD materialization and parameter paths.
- **UTC Timestamp Normalization**: All timestamps (`DateTime`, `DateTimeOffset`, `TimestampOffset`) are normalized to UTC.
- **Unified GUID Handling**: Native `UUID` (PostgreSQL), `uniqueidentifier` (SQL Server), `RAW(16)` (Oracle), `BINARY(16)` (Firebird/MySQL), or `TEXT` (SQLite).
- **JSON Serialization/Deserialization**: Seamless mapping for complex types via `System.Text.Json`. Auto-detected for `JsonDocument`, `JsonElement`, `JsonNode`, `JsonValue`.

The guarantee applies to the provider/type combinations covered by the built-in registry and
provider integration tests. An arbitrary third-party ADO.NET extension type is not automatically
supported; missing types belong in the library's coercion/converter matrix rather than in each
application's entities.

---

## Enum Storage & Parsing

### Storage Format
Enum storage is controlled by `DbType` in the `[Column]` attribute:
- `DbType.String`: Stored as the enum member name (`"Active"`).
- `DbType.Int32` (or other numeric): Stored as the underlying integer value (`1`).
- *Note*: Throws an exception at mapping compilation if `DbType` is neither string nor numeric.

### EnumParseFailureMode
Controls behavior when a database value cannot be parsed into the target enum:

```csharp
public enum EnumParseFailureMode
{
    Throw,           // Throws exception (default, recommended for production data integrity)
    SetNullAndLog,   // Sets property to null and logs warning (requires nullable enum)
    SetDefaultValue  // Sets property to default enum value (0) and logs warning
}
```

Usage:
```csharp
var gateway = new TableGateway<User, long>(
    context, 
    enumParseBehavior: EnumParseFailureMode.SetDefaultValue);
```

---

## Cross-Database Type Mapping Matrix

| .NET CLR Type | SQL Server | PostgreSQL | Oracle | MySQL / MariaDB | SQLite |
|---|---|---|---|---|---|
| `int` | `INT` | `INTEGER` | `NUMBER(10,0)` | `INT` | `INTEGER` |
| `long` | `BIGINT` | `BIGINT` | `NUMBER(19,0)` | `BIGINT` | `INTEGER` |
| `decimal` | `DECIMAL` | `NUMERIC` | `NUMBER` | `DECIMAL` | `REAL` |
| `string` | `NVARCHAR` | `TEXT` / `VARCHAR` | `VARCHAR2` | `VARCHAR` | `TEXT` |
| `DateTime` | `DATETIME2` | `TIMESTAMP WITH TIME ZONE` | `TIMESTAMP` | `DATETIME` | `TEXT` |
| `bool` | `BIT` | `BOOLEAN` | `NUMBER(1,0)` | `TINYINT(1)` | `INTEGER` |
| `Guid` | `UNIQUEIDENTIFIER` | `UUID` | `RAW(16)` | `BINARY(16)` | `TEXT` |
| `byte[]` | `VARBINARY(MAX)` | `BYTEA` | `BLOB` | `LONGBLOB` | `BLOB` |
| `DateOnly` | `DATE` | `DATE` | `DATE` | `DATE` | `TEXT` |
| `TimeOnly` | `TIME` | `TIME` | `INTERVAL DAY(0) TO SECOND` | `TIME` | `TEXT` |

---

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
- SQL Server `hierarchyid` → `HierarchyId` (or `string`) with no `Microsoft.SqlServer.Types`: written as its text
  (`/1/2.5/`, converted implicitly by SQL Server; text on other databases), read from the stored encoding (TYPE-016).
- A stored value with no .NET representation throws `DataMappingException` naming the column, never a raw provider
  exception or a default: MySQL/MariaDB zero dates, PostgreSQL `numeric` NaN into `decimal`, SQL Server
  `geometry`/`geography`/other CLR types without `Microsoft.SqlServer.Types` (select `col.ToString()` instead).

## Null & DBNull Handling

- Database `DBNull.Value` is mapped to `null` for nullable value types (`int?`, `DateTime?`, `Guid?`) and reference types.
- Attempting to map `DBNull.Value` to a non-nullable value type throws an informative `InvalidCastException`.
