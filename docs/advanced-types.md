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

For PostgreSQL-family spatial values, the CRUD coercion path writes binary values as EWKB when an
SRID is present and restores that SRID when reading the EWKB payload. WKT writes use EWKT. The
converter boundary also formats GeoJSON with a `crs` member, but GeoJSON is not a native write
format for the ordinary gateway coercion path. The current live regression uses PostgreSQL
`BYTEA` to verify the EWKB wire payload; a PostGIS-extension column test remains provider-image
specific and is not implied by the ordinary PostgreSQL test.

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

| Type | Represents | Wired dialects (`AdvancedTypeRegistry.RegisterDefaultMappings`) |
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
| `Geometry` / `Geography` (`Geometry.cs`, `Geography.cs`, both extend `SpatialValue`) | Planar vs. geodetic spatial data; WKB/WKT/GeoJSON-backed | SQL Server `geometry`/`geography` without `Microsoft.SqlServer.Types` (below); PostgreSQL/CockroachDB/YugabyteDB spatial paths for both (binary writes use EWKB when an SRID is present; WKT/GeoJSON retain SRID in EWKT/`crs`); MySQL/MariaDB `GEOMETRY` in the server's internal format (4-byte SRID + WKB, WKT encoded to WKB; a GeoJSON-only value can't be written there) |
| `RowVersion` (`RowVersion.cs`) | 8-byte optimistic-concurrency token | SQL Server → `rowversion`/`timestamp` |
| `HierarchyId` (`HierarchyId.cs`) | Node path such as `/1/2.5/` with SQL Server `hierarchyid` semantics (`Level`, `GetAncestor`, `IsDescendantOf`, SQL Server's depth-first ordering) | SQL Server `hierarchyid` without `Microsoft.SqlServer.Types`; text on any other database (TYPE-016, below) |

`JsonDocument` (the BCL type, not `JsonValue`) is also directly mapped in
`AdvancedTypeRegistry.RegisterJsonMappings` for PostgreSQL/CockroachDB/YugabyteDB (`jsonb`),
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

## Provider-specific column types mapped to .NET types

These need no value object: the library converts between the provider's representation and the
plain .NET type (verified live, TYPE-005).

| Column type | Property type | Notes |
|---|---|---|
| DuckDB `HUGEINT` / `UHUGEINT`, Firebird `INT128` | `Int128` / `UInt128` (or `BigInteger`, `long`, `decimal` when the value fits) | Read as `BigInteger` by the provider and converted with checked casts; a value outside the property's range throws `DataMappingException`. Written as `BigInteger` (Firebird, PostgreSQL) or as exact text (DuckDB, whose driver can't bind `Int128.MinValue` or `UInt128` above `Int128.MaxValue`). |
| DuckDB `LIST` (`INTEGER[]`, `VARCHAR[]`, ...), PostgreSQL arrays | `T[]` or `List<T>` | Elements are coerced one by one. |
| DuckDB `MAP` / `STRUCT` | `Dictionary<TKey, TValue>` / `Dictionary<string, object>` | Passed through as the provider returns them. |
| Firebird `DECFLOAT` | `decimal` (or `double`) with `[Column(..., DbType.VarNumeric)]` | FirebirdClient binds a `DECFLOAT` parameter only from its own `FbDecFloat` and never binds a `NUMERIC` from it, so `DbType.VarNumeric` marks the column. Reads are exact: `NaN`/infinity, or a 34-digit value `decimal` would round, throw `DataMappingException` into a `decimal` (a `double` gets `NaN`/infinity). |
| Informix `INTERVAL DAY TO SECOND` | `TimeSpan` | |
| Snowflake `NUMBER(p,0)` beyond `long` (e.g. `NUMBER(20,0)` holding `ulong.MaxValue`, `NUMBER(38,0)`) | `ulong`, `decimal`, `double`, `BigInteger`, `Int128`/`UInt128` | Snowflake.Data reports every scale-0 `NUMBER` as `Int64` and its `GetValue`/`GetInt64` overflow beyond it; such a value is read from its text as a `BigInteger` and converted with checked casts, so a property that can't hold it (e.g. `long`) throws `DataMappingException`. `ITrackedReader.GetValue` returns the `BigInteger`. |
| SQL Server `sql_variant` | `object` | The stored value's own type. |
| PostgreSQL `BIT(n)` | `BitArray` | |
| SQL Server 2025 `VECTOR(n)`, Oracle 23ai `VECTOR` | `float[]` (or `double[]`, `List<float>`) with `[Column(..., DbType.Object)]` | Written as exact text (`[1.5,2,-3]`, each element in shortest round-trip form), which both servers convert implicitly; SqlClient and ODP.NET reject a raw `float[]`. Read from ODP.NET's `float[]`, SqlClient 6.1+'s `SqlVector<float>` (exact), or SqlClient 6.0's text. **SqlClient 6.0 prints 8 significant digits**, so about 2% of arbitrary `float` values come back one bit off (and `-0` as `0`); use SqlClient 6.1+ for exact reads. A `float[]` parameter is a `VECTOR_DISTANCE` argument: `CAST({P}p AS VECTOR(n))` on SQL Server, `TO_VECTOR({P}p)` on Oracle. (TYPE-015) |
| pgvector `vector(n)` | `float[]` | Npgsql binds a `float[]` as `real[]`, which pgvector's assignment cast stores. Npgsql can't read `vector` itself without the `Pgvector.Npgsql` plugin: select `col::real[]`, or build the context from an `NpgsqlDataSource` with `UseVector()`, whose `Pgvector.Vector` then converts to `float[]`. Comparisons need `CAST({P}p AS vector)`. (TYPE-015) |

A stored value with no .NET representation throws `DataMappingException` naming the column, never
a raw provider exception or a default value: a MySQL/MariaDB zero date (`'0000-00-00'`), a
PostgreSQL `numeric` `NaN` read into `decimal`, a SQL Server curve or `FULLGLOBE`, and a SQL Server
user CLR type when its assembly isn't loaded (select `col.ToString()` instead). `hierarchyid`,
`geometry` and `geography` are read without `Microsoft.SqlServer.Types` (above).

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
