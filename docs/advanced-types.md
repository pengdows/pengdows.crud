# Advanced Value Types

`pengdows.crud/types/` implements a set of immutable value objects for database types that
don't map onto a primitive .NET type — PostgreSQL network/range/interval types, spatial data,
`HSTORE`, and SQL Server `rowversion`. This doc covers what's actually wired into the
mapping/coercion pipeline today, since the wiki's `v2-Type-System` page undersells this surface
and (separately) documents a registration API that isn't public — see "Not a public extension
point" below.

Both directions use the same built-in coercion model: reads resolve through `CoercionRegistry`,
and ordinary dialect parameter creation resolves legacy mappings first, then the coercion
registry, then cross-provider binding rules. Consequently, a supported value type is handled
through the normal CRUD path; callers do not need to invoke a coercion helper directly.

## Conversion contract

For every advanced type exposed by pengdows.crud, the library owns the conversion boundary in
both directions:

* provider value → CLR value when materializing a result;
* CLR value → provider parameter when inserting, updating, or filtering.

The provider may expose a native type, a binary representation, text, or another ADO.NET value.
The application model remains the same, so developers do not need a provider-specific type
handler for each database or custom conversion code in every entity. This guarantee covers the
provider/type combinations listed below and exercised by the unit and provider integration tests.
It does not claim that an arbitrary third-party ADO.NET extension type can be converted without a
registered implementation.

For PostgreSQL-family spatial values (PostGIS `geometry`/`geography`, CockroachDB's built-in spatial
types), the gateway binds EWKB (WKB with the SRID); a value built from WKT is encoded to WKB first,
and a GeoJSON-only value can't be written (`NotSupportedException`). Reads need no NetTopologySuite
plugin: Npgsql has no handler for these types without it, so the dialect reads the column's EWKB
through `GetBytes` and splits off the SRID. `WellKnownBinary` is always plain WKB and the SRID is
`Srid` (2.0.5 kept the EWKB SRID flag in `WellKnownBinary`). Verified live on PostGIS 3.5
(`PostGisRoundTripTests`) and CockroachDB 25.1 (TYPE-002).

## Usage pattern

No special attribute is needed to use these types. Declare the property with the value-object
type and a `[Column]` attribute with `DbType.Object`; the coercion pipeline does the rest based on
the CLR type. (`DbType.String` and the other string `DbType`s only accept `string`, `char`,
`char[]` and `Guid` properties, so a value-object property declared with them fails when the
gateway builds its SQL templates.)

```csharp
[Table("hosts")]
public class Host
{
    [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }

    [Column("address", DbType.Object)] public Inet Address { get; set; }
    [Column("subnet", DbType.Object)] public Cidr Subnet { get; set; }
    [Column("mac", DbType.Object)] public MacAddress Mac { get; set; }
    [Column("tags", DbType.Object)] public HStore Tags { get; set; }
    [Column("uptime", DbType.Object)] public PostgreSqlInterval Uptime { get; set; }
    [Column("metadata", DbType.Object)] public JsonValue Metadata { get; set; }
}
```

**`RowVersion` and `[Version]`:** a `RowVersion`- or `byte[]`-typed `[Version]` column is a
server-generated version token. It is excluded from the SET clause and used only in the
optimistic-concurrency WHERE match — it is **not** incremented by the library; SQL Server generates
the new value server-side, so the caller's in-memory value goes stale after a successful write
unless reloaded. A stale value makes `UpdateAsync` throw `ConcurrencyConflictException`. Mark the
property `[NonInsertable]` and `[NonUpdateable]` as well, since the database assigns it:

```csharp
[Version, NonInsertable, NonUpdateable]
[Column("rv", DbType.Binary)]
public RowVersion Rv { get; set; }
```

## Type reference

| Type | Represents | Wired dialects (each dialect's `DatabaseTraits.RegisterTypeMappings`, collected by `AdvancedTypeRegistry`) |
|---|---|---|
| `Inet` (`types/valueobjects/Inet.cs`) | IP address, optional CIDR prefix | PostgreSQL, CockroachDB, YugabyteDB → `inet` |
| `Cidr` (`Cidr.cs`) | Network subnet, prefix required, host bits canonicalized to 0 | PostgreSQL, CockroachDB, YugabyteDB → `cidr` |
| `MacAddress` (`MacAddress.cs`) | Hardware address, wraps `PhysicalAddress` | PostgreSQL, CockroachDB, YugabyteDB → `macaddr` (6-byte EUI-48) or `macaddr8` (8-byte EUI-64), dispatched on the address's actual byte length |
| `Range<T>` (`Range.cs`, `T : struct`) | Bounded range with inclusive/exclusive brackets; `Range<T>.Empty` is PostgreSQL's `empty` (distinct from the unbounded `(,)`, which is `default`; `IsEmpty` is true for both, `IsEmptyRange` only for `Empty`) | PostgreSQL/CockroachDB/YugabyteDB `int4range` (`Range<int>`), `int8range` (`Range<long>`), `tsrange` (`Range<DateTime>`); sent as `NpgsqlRange<T>` |
| `PostgreSqlInterval` (`PostgreSqlInterval.cs`) | months/days/microseconds, matches PG's internal storage | PostgreSQL, CockroachDB, YugabyteDB → `interval` |
| `IntervalYearMonth` (`IntervalYearMonth.cs`) | Oracle `INTERVAL YEAR TO MONTH` | Oracle only |
| `IntervalDaySecond` (`IntervalDaySecond.cs`) | Oracle `INTERVAL DAY TO SECOND` | Oracle only |
| `HStore` (`HStore.cs`) | PostgreSQL key/value column | Built-in `coercion/` pipeline (`ProviderParameterFactory`/`BasicCoercions`), provider-agnostic at the CLR boundary |
| `JsonValue` (`JsonValue.cs`) | Lazy string/`JsonDocument`/`JsonElement` JSON wrapper | Same built-in `coercion/` pipeline as `HStore`, provider-agnostic at the CLR boundary |
| `Geometry` / `Geography` (`Geometry.cs`, `Geography.cs`, both extend `SpatialValue`) | Planar vs. geodetic spatial data; WKB/WKT/GeoJSON-backed | SQL Server `geometry`/`geography` without `Microsoft.SqlServer.Types` (below); PostgreSQL/CockroachDB/YugabyteDB for both as EWKB (WKT encoded to WKB, read without NetTopologySuite; a GeoJSON-only value can't be written); MySQL/MariaDB `GEOMETRY` in the server's internal format (4-byte SRID + WKB, WKT encoded to WKB; a GeoJSON-only value can't be written there); Oracle `SDO_GEOMETRY` as EWKT (below) |
| `RowVersion` (`RowVersion.cs`) | 8-byte optimistic-concurrency token | SQL Server → `rowversion`/`timestamp` |
| `HierarchyId` (`HierarchyId.cs`) | Node path such as `/1/2.5/` with SQL Server `hierarchyid` semantics (`Level`, `GetAncestor`, `IsDescendantOf`, SQL Server's depth-first ordering) | SQL Server `hierarchyid` without `Microsoft.SqlServer.Types`; text on any other database (TYPE-016, below) |

`JsonDocument` (the BCL type, not `JsonValue`) is also directly mapped in
the dialects' type mappings for PostgreSQL/CockroachDB/YugabyteDB (`jsonb`),
MySQL/TiDB (`JSON`), and SQL Server (`NVARCHAR(MAX)`). `JsonValue` is a separate, provider-agnostic
wrapper maintained in the newer coercion system — prefer it for new code since it works
uniformly across dialects without a per-provider registration; `JsonDocument` remains supported
for existing code and BCL interop. Don't confuse either with the entity-level `[Json]` attribute
(`pengdows.crud.attributes.JsonAttribute`) documented in `CLAUDE.md`/the wiki, which serializes an
arbitrary POCO property to a JSON column — that's a different, higher-level mechanism than these
value-object types.

### `HierarchyId`

A SQL Server `hierarchyid` column maps to `HierarchyId` (or `string`, its text form) with no
`Microsoft.SqlServer.Types` reference:

```csharp
[Column("node", DbType.String)] public HierarchyId? Node { get; set; }
```

- **Write:** sent as its text form (`/1/2.5/`), which SQL Server converts to `hierarchyid`
  implicitly, in INSERT, UPDATE and WHERE alike. Declared `DbType.Binary`, it is sent as SQL
  Server's stored encoding instead (also converted implicitly). On other databases the text is
  stored as is (`VARCHAR`), so the same entity works everywhere.
- **Read:** without `Microsoft.SqlServer.Types`, SqlClient reports no field type for the column
  and can't return it from `GetValue`; the SQL Server dialect reads its stored encoding
  (`GetBytes`) and decodes it. With the assembly loaded, its `SqlHierarchyId` is converted. Text
  and `byte[]` are accepted as well. Both the gateway and `DataReaderMapper` read it.
- `CompareTo`/`<` order nodes as SQL Server's `ORDER BY` does; `HierarchyId.Root` (also
  `default`) is `/`. `ToSqlServerBytes()`/`FromSqlServerBytes()` expose the encoding.
- Verified live on SQL Server 2025 (every range of the encoding, dotted and negative components,
  WHERE and ORDER BY).

### Guids in 16-byte binary columns

Declare a `Guid` property `[Column(..., DbType.Binary)]` to store it as 16 bytes (`BINARY(16)`,
`RAW(16)`, `BLOB`, ...). The bytes are in the database's own Guid byte order, and a 16-byte column read
into a `Guid` is decoded the same way, through the gateway and `DataReaderMapper` alike:

- **RFC 4122 big-endian** (the default): MySQL, MariaDB, TiDB and SingleStore (the order of
  `UUID_TO_BIN`/`BIN_TO_UUID` and MySqlConnector's `GuidFormat=Binary16`), Firebird, PostgreSQL,
  SQLite, DuckDB and every other database.
- **.NET `Guid.ToByteArray()` order** (mixed-endian): SQL Server (`uniqueidentifier`'s own byte
  layout), Oracle (ODP.NET's `RAW(16)` Guids) and Sybase ASE (AseClient's `BINARY(16)` Guids).

**Changed in 2.0.6:** earlier releases refused a `Guid` declared `DbType.Binary` and always decoded
16 bytes in .NET order. On the RFC 4122 databases, bytes your application wrote itself with
`guid.ToByteArray()` now read as a different Guid. Rewrite them with `ToByteArray(bigEndian: true)`,
or read the column into `byte[]` and call `new Guid(bytes)`. Data written by `UUID_TO_BIN()` or by
MySqlConnector's `Binary16` format now reads correctly. A `Guid` declared `DbType.Guid` is unchanged
(each dialect's native UUID type or text).

### SQL Server `geometry` / `geography`

`Geometry` and `Geography` properties map to SQL Server's spatial columns with no
`Microsoft.SqlServer.Types` reference. That package validates every shape but a point in native
code that only ships for Windows (`PlatformNotSupportedException` elsewhere).

```csharp
[Column("shape", DbType.Object)] public Geometry? Shape { get; set; }
[Column("location", DbType.Object)] public Geography? Location { get; set; }
```

- **Write:** the parameter is the SRID (4 bytes, big-endian) followed by the value's WKB (WKT is
  encoded to WKB; a GeoJSON-only value can't be written). The gateways write the column's value as
  `CASE WHEN @p IS NULL THEN NULL ELSE geometry::STGeomFromWKB(SUBSTRING(CAST(@p AS varbinary(max)), 5,
  DATALENGTH(@p)), CAST(SUBSTRING(CAST(@p AS varbinary(max)), 1, 4) AS int)) END` (`geography::` for a
  `Geography`), in single-row and batch INSERT, UPDATE and MERGE alike. SQL Server builds the
  instance itself, so the SRID is kept and validity is the server's verdict: an invalid polygon is
  stored and reports `STIsValid() = 0`. SQL Server's stored encoding is never sent instead,
  because SQL Server trusts its validity flag. An invalid polygon sent as valid reported area 0;
  a valid one sent as invalid refused `STArea()`. In your own SQL, use the same expression around
  a spatial parameter.
- **Read:** without the package SqlClient's `GetValue` throws, so the dialect reads the stored
  encoding (`GetBytes`) and decodes it to WKB and SRID. Geography stores latitude first; the WKB is
  longitude (x) first. Points, line strings, polygons, their multi forms and collections, and
  Z/M coordinates (as ISO WKB) are read. Curves (`CIRCULARSTRING`, `COMPOUNDCURVE`, `CURVEPOLYGON`)
  and `FULLGLOBE` have no WKB form and throw `DataMappingException`. With the package loaded, its
  `SqlGeometry`/`SqlGeography` is converted. Both the gateway and `DataReaderMapper` read it.
- Verified live on SQL Server 2025: every gateway write path, SRIDs other than 0/4326, an invalid
  polygon, NULLs, and reads (TYPE-002).

### Oracle `SDO_GEOMETRY`

`Geometry` and `Geography` properties map to `SDO_GEOMETRY` columns with no ODP.NET UDT class.
`SDO_GEOMETRY` is an object type, which ODP.NET reads only through a custom type mapping, so
the server converts it both ways.

```csharp
[Column("shape", DbType.Object)] public Geometry? Shape { get; set; }
[Column("location", DbType.Object)] public Geography? Location { get; set; }
```

- **Write:** the parameter is EWKT text (`SRID=4326;POINT (1 2)`), bound as a CLOB so a long
  geometry fits. WKB is decoded to WKT exactly. A GeoJSON-only value throws `NotSupportedException`.
  The gateways write the column's value as
  `(SELECT CASE WHEN x IS NULL THEN NULL ELSE SDO_GEOMETRY(SUBSTR(x, INSTR(x, ';') + 1), NULLIF(TO_NUMBER(SUBSTR(x, 6, INSTR(x, ';') - 6)), 0)) END FROM (SELECT TO_CLOB(:p) x FROM DUAL))`.
  This applies to single-row and batch INSERT, UPDATE and MERGE alike, including the array-bound
  batch insert. ODP.NET binds by position, so the marker appears only once. SRID 0 is stored as a
  NULL `SDO_SRID`.
- **Read:** gateway SELECT lists read the column as
  `CASE WHEN col IS NULL THEN NULL ELSE 'SRID=' || NVL(JSON_VALUE(SDO_UTIL.TO_JSON(col), '$.srid'), '0') || ';' || SDO_UTIL.TO_WKTGEOMETRY(col) END AS col`,
  and that EWKT is parsed back into the value with its SRID. In your own SQL, select that
  expression too. ODP.NET can't read a bare `SDO_GEOMETRY` column, and `DataReaderMapper` maps the
  EWKT like the gateway does.
- **Precision:** Oracle stores ordinates to 15 significant digits whether they arrive as WKT or WKB,
  so a double that needs 16 or 17 digits reads back rounded.
- **Requirements:** Oracle Spatial/Locator must be installed. The `gvenzl` *slim* images leave it
  out (`ORA-00902: invalid datatype`); the full images and every standard install include it.
- 2D only, as elsewhere: Z/M values throw `NotSupportedException`. `WithProviderValue` is not used
  for Oracle; the value's WKT or WKB is written.
- Verified live on Oracle Free 23ai (TYPE-021): every gateway write path, NULLs, SRID 0, a
  41,000-character line string, and reads through the gateway and `DataReaderMapper`.

## Provider-specific column types mapped to .NET types

These need no value object: the library converts between the provider's representation and the
plain .NET type (verified live, TYPE-005).

| Column type | Property type | Notes |
|---|---|---|
| DuckDB `HUGEINT` / `UHUGEINT`, Firebird `INT128` | `Int128` / `UInt128` (or `BigInteger`, `long`, `decimal` when the value fits) | Read as `BigInteger` by the provider and converted with checked casts; a value outside the property's range throws `DataMappingException`. Written as `BigInteger` (Firebird, PostgreSQL) or as exact text (DuckDB, whose driver can't bind `Int128.MinValue` or `UInt128` above `Int128.MaxValue`). |
| DuckDB `LIST` (`INTEGER[]`, `VARCHAR[]`, ...), PostgreSQL arrays | `T[]` or `List<T>` | Elements are coerced one by one. |
| DuckDB `INTERVAL` | `TimeSpan` (`DbType.Object`) | Written as DuckDB interval text (microseconds). Read from the stored months, days and microseconds: DuckDB.NET 1.5.6 throws for every negative interval and drops negative months, so the dialect converts the parts itself, exactly. An interval with months throws `DataMappingException`, because a month has no fixed length. |
| DuckDB `MAP` / `STRUCT` | `Dictionary<TKey, TValue>` / `Dictionary<string, object>` | Passed through as the provider returns them. |
| Firebird `DECFLOAT` | `decimal` (or `double`) with `[Column(..., DbType.VarNumeric)]` | FirebirdClient binds a `DECFLOAT` parameter only from its own `FbDecFloat` and never binds a `NUMERIC` from it, so `DbType.VarNumeric` marks the column. Reads are exact: `NaN`/infinity, or a 34-digit value `decimal` would round, throw `DataMappingException` into a `decimal` (a `double` gets `NaN`/infinity). |
| Informix `INTERVAL DAY TO SECOND` | `TimeSpan` | |
| Snowflake `NUMBER(p,0)` beyond `long` (e.g. `NUMBER(20,0)` holding `ulong.MaxValue`, `NUMBER(38,0)`) | `ulong`, `decimal`, `double`, `BigInteger`, `Int128`/`UInt128` | Snowflake.Data reports every scale-0 `NUMBER` as `Int64` and its `GetValue`/`GetInt64` overflow beyond it; such a value is read from its text as a `BigInteger` and converted with checked casts, so a property that can't hold it (e.g. `long`) throws `DataMappingException`. `ITrackedReader.GetValue` returns the `BigInteger`. |
| SQLite column holding a `decimal` (declare it `TEXT` to keep every digit) | `decimal` with `DbType.Decimal` | Bound as a `double` when one holds the value exactly, else as exact invariant text (2.0.5 always bound a `double`, losing digits beyond 15 significant). Text only where needed, because SQLite never matches text against an arithmetic or aggregate result (`price * qty > @p`). A `REAL`/`NUMERIC` column converts either by affinity; decimal text with an exponent (as 2.0.5 stored) reads back. |
| Oracle `BINARY_DOUBLE`/`BINARY_FLOAT` and every `double`/`float` | `double`/`float` | Bound as `BINARY_DOUBLE`/`BINARY_FLOAT`: ODP.NET's default NUMBER binding overflowed beyond 1e126 and silently kept only 15 significant digits (0.30000000000000004 stored as 0.3; confirmed live). NUMBER/FLOAT columns still store and compare them, but Oracle compares a NUMBER column with a `BINARY_DOUBLE` value by converting the column, which can stop an index on it being used: map a NUMBER column you filter or join on to `decimal`, which binds as an exact NUMBER. A NUMBER/`FLOAT(126)` value beyond `decimal`'s range reads into a `double` property through `GetDouble`. |
| Oracle `LONG` / `LONG RAW` | `string` / `byte[]` | Commands fetch LONG data with the row (`InitialLONGFetchSize = -1`); ODP.NET otherwise reads them as empty when the select list lacks the row's key. |
| Oracle `TIMESTAMP(n)`, `XMLTYPE` | `DateTime` with `DbType.DateTime2`, `string` with `DbType.Xml` | ODP.NET rejects both DbTypes; they bind as `TIMESTAMP` (all 7 fraction digits) and text, which `XMLTYPE` converts. |
| Firebird `TIME WITH TIME ZONE` | `DateTimeOffset` with `DbType.Time` | Sent as the UTC time (FirebirdClient takes named zones only); reads back on 0001-01-01 at UTC. |
| Firebird `BINARY(n)` / `VARBINARY(n)` | `byte[]` | FirebirdClient reports them as `string` but returns the bytes, which are read as such. |
| SingleStore `GEOGRAPHY` / `GEOGRAPHYPOINT` | `Geography` / `Geometry` | Written as WKT text (a WKB-only value can't be written there); read back from WKT. `GEOGRAPHYPOINT` stores coordinates to about 1e-7 degrees, so they read back approximately. `GEOGRAPHY` columns need a rowstore table. |
| SingleStore `VECTOR(n)` | `float[]` | Written as JSON array text, read from its packed little-endian float32 bytes. |
| Spanner (PostgreSQL) `uuid`, arrays | `Guid`, `T[]` | A Guid is sent untyped (a uuid column refuses text, and Npgsql can't type it as uuid) and read from its 16 bytes; arrays Npgsql refuses as non-nullable elements are read with nullable ones. |
| Informix `TEXT`, `BYTE`, `BLOB`, `CLOB` | `string` / `byte[]` | The gateways learn the column's declared type ([below](#declared-column-types)). `TEXT` is bound as `IfxType.Text`. A `BLOB`/`CLOB` value (no host variable works on `UPDATE`, or on `INSERT` below about 9,000 bytes, and the driver's locator API fails) and a `BYTE`/`TEXT` value in a MERGE upsert are staged first in a session temp table, `pengdows_lob_stage` (created on first use, `WITH NO LOG`), then read back by key in the same statement on the same connection (WRT-006, TYPE-020, live). In your own SQL, bind `TEXT` with `IfxType.Text`, and stage a `BLOB`/`CLOB` the same way or use `LVARCHAR`. |
| Informix `BSON` | `string` (JSON text) | The gateways write `?::JSON::BSON` and read `col::JSON` (TYPE-020). Use the same casts in your own SQL. |
| Informix `BOOLEAN` in `WHERE` | `bool` | Writing and reading work. A `bool` parameter binds as `SMALLINT`, which a `BOOLEAN` column compares only with `'t'`/`'f'` text, so compare with text in your own SQL. |
| Informix `DATETIME HOUR TO FRACTION(n)` | `TimeOnly` / `TimeSpan` | The gateways write the value as text cast to the column's type, keeping `n` digits (truncated, never rounded). In your own SQL Informix.Net.Core truncates a `TimeSpan` parameter to whole seconds, so compare with `CAST({P}p AS DATETIME HOUR TO FRACTION(n))` bound as `'hh:mm:ss.fffff'` (TYPE-022). `DATETIME YEAR TO FRACTION` with a `DateTime` keeps its fraction. |
| Sybase ASE `BIGDATETIME`, `BIGTIME` | `DateTime` / `DateTimeOffset` with `DbType.DateTime2` / `DbType.DateTimeOffset`; `TimeSpan` / `TimeOnly` with `DbType.Time` | AdoNetCore.AseClient 0.19 truncates `BIGDATETIME` fractions to milliseconds on write, decodes them a few microseconds off on read, and can't read `BIGTIME` at all ("Unsupported data type 188"). So `DateTime2`/`DateTimeOffset` values are bound as microsecond text (truncated), which ASE converts into `BIGDATETIME` exactly and into `DATETIME` as before, in your own SQL too; the gateways write a `DbType.Time` column as `CONVERT(BIGTIME, text)` and read these columns through `CONVERT(VARCHAR, col, 140)` (dates) and `137` (times), so both round-trip to the microsecond (TYPE-022, live). In your own SQL, read them with the same `CONVERT`s, and compare a `BIGTIME` with `CONVERT(BIGTIME, {P}p)` bound as `'hh:mm:ss.ffffff'`: a `TimeSpan` parameter is truncated to milliseconds and `BIGTIME` refuses plain text. |
| DuckDB `BIT` | `BitArray` | DuckDB.NET reads it as its bit string ("10110") and binds a `BitArray` as its `ToString()`; a `BitArray` is written as its bit string and read back from it. |
| SQL Server `sql_variant` | `object` | Whatever the stored value is (an `int` stays an `int`). |
| InterBase `ARRAY` (one dimension) | `T[]` | Bound with `IBDbType.Array` (the driver rejects an array otherwise); read back with its declared bounds (`Int32[*]` for `[1:5]`) and copied into a zero-based array. |
| InterBase text with the database charset `NONE` | `string` | The driver resolves `NONE` once, to the system code page if .NET code pages are registered first (SqlClient registers them), storing other characters as `?`; pengdows pins it to UTF-8, so any text round-trips. `NCHAR` (ISO8859_1) still takes only Latin-1. With `Charset=UTF8` in the connection string, `UTF8` columns and text BLOBs round-trip any text, but the server can't return non-ASCII text from a `NONE` column (the fetch fails, "string truncation"), so keep the default charset for `NONE` columns. A `CHAR(n)` value longer than `n` bytes fails with the driver's `ArgumentException` rather than a truncation error (TYPE-022, live). |
| Informix `LIST` / `SET` / `MULTISET` | `T[]` | Written as a `LIST{...}` literal (Informix converts it to a `SET` or `MULTISET` too) and read by parsing the literal text the driver returns. `ROW` maps to `string` only: Informix.Net.Core reads it as literal text with numbers padded (`ROW(5          ,'q',NULL)`) and takes a literal as a text parameter (cast it, `?::ROW(a INT, ...)`, when a field is a `DATE`). It reports the column's type as plain `ROW`, with no fields, so pengdows can't map one to a structure. Select the fields (`col.a`, `col.b`) to read them typed (TYPE-020, live). |
| HANA `ARRAY`, Sybase ASE `XML` | — | HANA: not supported yet (the driver refuses array parameters, HANA converts no text or binary into an `ARRAY`, and reads return an undocumented encoding; TYPE-020). ASE has no `XML` column type: XML Services store it in text columns. |
| SAP HANA `DECIMAL` / `SMALLDECIMAL` | `decimal` | Sap.Data.Hana.Net's `GetValue` returns its own `HanaDecimal`; the value is read with `GetDecimal`. A decimal with trailing zeros is sent without them: the driver cut `12345678901234567.00m` (Precision 18, Scale 0) to `2345678901234567` with no error. |
| SAP HANA `ST_GEOMETRY` / `ST_POINT` | `Geometry` / `Geography` | Written as WKB (WKT is encoded to it; HANA refuses WKT text), stored with the column's SRID, read back from WKB. |
| SAP HANA `ARRAY` | `int[]`, `long[]`, `short[]`, `double[]`, `float[]`, `string[]` (value-type elements may be nullable: `long?[]`) | Sap.Data.Hana.Net refuses array parameters and HANA converts no text or binary into an `ARRAY`, so an array is bound as its JSON text and the gateways write `ARRAY(SELECT V FROM JSON_TABLE(?, '$[*]' COLUMNS (O FOR ORDINALITY, V INT PATH '$')) ORDER BY O)` (`BIGINT`, `DOUBLE` or `NVARCHAR(5000)` for the other element types; a null array is written as `NULL`). Reads decode the bytes the driver returns, HANA's wire encoding. The driver reports the column only as `varbinary`, so the property's element type must match the column's: `int[]` for `INTEGER ARRAY`, `long[]` for `BIGINT`, `short[]` for `SMALLINT`, `double[]` for `DOUBLE`, `float[]` for `REAL`, `string[]` for `(N)VARCHAR`. A mismatch fails as `DataMappingException` rather than misreading. HANA can't compare an `ARRAY` with `=`. In your own SQL, an array parameter is sent as JSON text; build the `ARRAY` with the same subquery (TYPE-020, live). |
| SAP HANA `TIMESTAMP` | `DateTime` (`DbType.DateTime` / `DateTime2`) | HANA stores 7 fraction digits (a tick) but Sap.Data.Hana.Net 2.29 truncates to microseconds on write and read. `DateTime` parameters are therefore bound as 7-digit text, which HANA converts into `TIMESTAMP` exactly and into `SECONDDATE`/`DATE` as before, in your own SQL too; gateway reads select `TO_VARCHAR(col, 'YYYY-MM-DD HH24:MI:SS.FF7')`, so values round-trip to the tick (TYPE-022, live). Select that expression in your own SQL to read all 7 digits. |
| SAP HANA `BINTEXT` | `string` | The driver reads it back as text. |
| Snowflake `VARIANT` / `OBJECT` / `ARRAY` | `JsonValue` (or a `[Json]` property) | Snowflake.Data can't bind them, so the JSON is sent as text and written as `PARSE_JSON(:p)`. Snowflake refuses that inside `INSERT ... VALUES`, so inserts, batch inserts, the MERGE upsert source and batch updates of such an entity take their values from a `SELECT` (`UNION ALL` for several rows). |
| Snowflake `GEOGRAPHY` / `GEOMETRY` | `Geography` / `Geometry` | Written as EWKT text (or EWKB hex / GeoJSON for a value without WKT); the session returns them as EWKT, so the SRID reads back. |
| Snowflake `TIMESTAMP_LTZ` / `TIMESTAMP_TZ` | `DateTimeOffset` | Snowflake.Data reports them as `DateTime`, whose getter gives local wall time (LTZ) or throws (TZ); the exact `DateTimeOffset` value is read instead. |
| Timestamp text (SQLite `TEXT`, FlatFile, any string column) | `DateTime` / `DateTimeOffset` | Text with an offset keeps its instant; text without one is read as UTC, like an unspecified `DateTime` everywhere else (2.0.5 read it as the host's local time, so the same row differed between machines). |
| Snowflake `VECTOR(FLOAT, n)` / `VECTOR(INT, n)` | `float[]` / `double[]` (FLOAT), `int[]` (INT) | Snowflake.Data can't bind a vector and Snowflake takes one only as `PARSE_JSON(:p)::VECTOR(FLOAT, n)` with its exact dimension. The driver doesn't report the dimension, so the gateways write that cast with `n` set to each value's length when the command runs; a value of another length fails with Snowflake's dimension error. A null vector is written as `NULL`. Reads come back as JSON array text and fill the array. In your own SQL, an array parameter is sent as JSON text; write the same cast (TYPE-020, live). |
| SQL Server `sql_variant` | `object` | The stored value's own type. |
| PostgreSQL `BIT(n)` | `BitArray` | |
| SQL Server 2025 `VECTOR(n)`, Oracle 23ai `VECTOR` | `float[]` (or `double[]`, `List<float>`) with `[Column(..., DbType.Object)]` | Written as exact text (`[1.5,2,-3]`, each element in shortest round-trip form), which both servers convert implicitly; SqlClient and ODP.NET reject a raw `float[]`. Read from ODP.NET's `float[]`, SqlClient 6.1+'s `SqlVector<float>` (exact), or SqlClient 6.0's text. **SqlClient 6.0 prints 8 significant digits**, so about 2% of arbitrary `float` values come back one bit off (and `-0` as `0`); use SqlClient 6.1+ for exact reads. A `float[]` parameter is a `VECTOR_DISTANCE` argument: `CAST({P}p AS VECTOR(n))` on SQL Server, `TO_VECTOR({P}p)` on Oracle. (TYPE-015) |
| pgvector `vector(n)` | `float[]` | Npgsql binds a `float[]` as `real[]`, which pgvector's assignment cast stores. Npgsql has no handler for `vector` without the `Pgvector.Npgsql` plugin, so the dialect reads the column's binary value through `GetBytes` (no plugin or `::real[]` cast needed; the plugin's `Pgvector.Vector` also converts). Same on CockroachDB's `VECTOR`. Comparisons need `CAST({P}p AS vector)`. (TYPE-015, TYPE-002) |
| PostgreSQL user-defined `ENUM` | a C# enum or a `string` with `DbType.String` | A `string` property is written like a C# enum once the gateway has learned that the column's declared type isn't text ([declared column types](#declared-column-types)). A C# enum value in your own SQL is sent as its name, untyped, too (Npgsql can't write a CLR enum at all). Sent untyped on PostgreSQL and YugabyteDB, so the server applies the enum type (a text parameter is refused: "column is of type mood but expression is of type text"). CockroachDB accepts text into an ENUM column (and refuses untyped values in a `VALUES` list), so it keeps text. A MERGE upsert (PostgreSQL 15+) and a batch update would type a `VALUES` source as text, so for rows with such an enum their source is an empty `SELECT` of the table's own columns followed by one `SELECT` per row, `UNION ALL`, which gives each value its column's type (TYPE-020/WRT-007, all three live). Not covered: a plain `string` parameter in your own SQL (no column to learn from); pass the C# enum, or use `CAST({P}p AS mood)`. (TYPE-002, TYPE-020) |
| PostgreSQL `timetz` | `DateTimeOffset` with `DbType.Time` | Sent as `time with time zone` with its offset kept (as `DbType.DateTimeOffset` it would be `timestamptz`, which `WHERE` can't compare to a `timetz`). |
| PostgreSQL `pg_lsn` | `NpgsqlTypes.NpgsqlLogSequenceNumber` | Npgsql can't infer the type from the value; the dialect names it. Npgsql's own geometric types (`NpgsqlPoint`, `NpgsqlBox`, ...) and `NpgsqlTsVector`/`NpgsqlTsQuery` pass through as is. |

A stored value with no .NET representation throws `DataMappingException` naming the column, never
a raw provider exception or a default value: a MySQL/MariaDB zero date (`'0000-00-00'`), a
PostgreSQL `numeric` `NaN` read into `decimal`, a SQL Server curve or `FULLGLOBE`, and a SQL Server
user CLR type when its assembly isn't loaded (select `col.ToString()` instead). `hierarchyid`,
`geometry` and `geography` are read without `Microsoft.SqlServer.Types` (above).

A fractional number read into an integer property, or into an enum stored as a number, must be
whole: `2.0` reads as `2`, `2.7` fails (gateway: `DataMappingException` naming the column;
`DataReaderMapper`: the same when `Strict`, otherwise logged with the property left at its default;
scalar reads: `InvalidCastException`), as a value beyond the property's range does. 2.0.5 truncated
it on the gateway and rounded it to even elsewhere (COR-009). A `decimal` column (Oracle `NUMBER`)
reads into an enum property; 2.0.5 failed to build the mapper (COR-013).

## Declared column types

A few columns can only be written correctly if pengdows knows the column's declared database type,
which the entity doesn't say: a `string` property bound to a PostgreSQL user-defined `ENUM` column,
an Informix `BLOB`/`CLOB`/`TEXT`/`BYTE`/`BSON` column, an Informix `DATETIME HOUR TO FRACTION(n)` column.
For those dialects, the first async operation through a gateway on a context learns the types of the
columns that matter, once per table, with one zero-row query of those columns:

```sql
SELECT "mood", "body" FROM "notes" WHERE 1 = 0
```

It reads only the provider's result metadata (`GetDataTypeName`). It runs through the same context, so
inside a transaction it uses the transaction's connection. A read-only user can run it, and if it
fails (permissions, a write-only context) nothing changes: the column is written as before. `Build*`
methods never query; they use the types once a gateway has learned them on that context. Dialects that
need no declared types never probe. PostgreSQL needs them only for writes, so its reads don't probe.

## Not a public extension point

`AdvancedTypeRegistry`, `CoercionRegistry`, and `ProviderTypeMapping` are all `internal`. The
wiki's `v2-Type-System` page currently shows an example calling
`AdvancedTypeRegistry.Shared.RegisterMapping<EmailAddress>(...)` from application code — **this
does not compile against the public API**; it was written against an earlier/aspirational shape
of this system. There is currently no supported way for a consumer to register a custom advanced
type mapping. That is deliberate: the supported built-in matrix is the product contract. If a
provider's supported type is missing from the matrix, it should be implemented in the built-in
coercion/converter layer and covered by provider integration tests; applications should not have
to reimplement that conversion in each entity.
