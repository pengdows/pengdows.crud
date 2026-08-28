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

---

## Null & DBNull Handling

- Database `DBNull.Value` is mapped to `null` for nullable value types (`int?`, `DateTime?`, `Guid?`) and reference types.
- Attempting to map `DBNull.Value` to a non-nullable value type throws an informative `InvalidCastException`.
