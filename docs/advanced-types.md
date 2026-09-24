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

**`RowVersion` and `[Version]`:** `TypeMapRegistry` accepts a `RowVersion`-typed `[Version]`
property, but on this release the gateway UPDATE/upsert builders only recognize `byte[]` as a
server-generated version token. A `byte[]` `[Version]` column is excluded from the SET clause and
used only in the optimistic-concurrency WHERE match — it is **not** incremented by the library;
SQL Server generates the new value server-side, so the caller's in-memory value goes stale after a
successful write unless reloaded. A `RowVersion`-typed `[Version]` property is instead treated like
a numeric counter (`SET row_version = row_version + 1`), which SQL Server rejects for a
`rowversion` column. **Use `byte[]` for a `[Version]` rowversion column**; `RowVersion` is fine as
the CLR type of an ordinary (read-only, non-`[Version]`) mapped rowversion column.

## Type reference

| Type | Represents | Wired dialects (`AdvancedTypeRegistry.RegisterDefaultMappings`) |
|---|---|---|
| `Inet` (`types/valueobjects/Inet.cs`) | IP address, optional CIDR prefix | PostgreSQL, CockroachDB, YugabyteDB → `inet` |
| `Cidr` (`Cidr.cs`) | Network subnet, prefix required, host bits canonicalized to 0 | PostgreSQL, CockroachDB, YugabyteDB → `cidr` |
| `MacAddress` (`MacAddress.cs`) | Hardware address, wraps `PhysicalAddress` | PostgreSQL, CockroachDB, YugabyteDB → `macaddr` |
| `Range<T>` (`Range.cs`, `T : struct`) | Bounded range with inclusive/exclusive brackets | PostgreSQL/CockroachDB/YugabyteDB `int4range` (`Range<int>`), `tsrange` (`Range<DateTime>`) |
| `PostgreSqlInterval` (`PostgreSqlInterval.cs`) | months/days/microseconds, matches PG's internal storage | PostgreSQL, CockroachDB, YugabyteDB → `interval` |
| `IntervalYearMonth` (`IntervalYearMonth.cs`) | Oracle `INTERVAL YEAR TO MONTH` | Oracle only |
| `IntervalDaySecond` (`IntervalDaySecond.cs`) | Oracle `INTERVAL DAY TO SECOND` | Oracle only |
| `HStore` (`HStore.cs`) | PostgreSQL key/value column | Built-in `coercion/` pipeline (`ProviderParameterFactory`/`BasicCoercions`), provider-agnostic at the CLR boundary |
| `JsonValue` (`JsonValue.cs`) | Lazy string/`JsonDocument`/`JsonElement` JSON wrapper | Same built-in `coercion/` pipeline as `HStore`, provider-agnostic at the CLR boundary |
| `Geometry` / `Geography` (`Geometry.cs`, `Geography.cs`, both extend `SpatialValue`) | Planar vs. geodetic spatial data; WKB/WKT/GeoJSON-backed | SQL Server (UDT) for both; PostgreSQL/PostGIS for `Geometry` only (WKB via `Binary`) |
| `RowVersion` (`RowVersion.cs`) | 8-byte optimistic-concurrency token | SQL Server → `rowversion`/`timestamp` |

`JsonDocument` (the BCL type, not `JsonValue`) is also directly mapped in
`AdvancedTypeRegistry.RegisterJsonMappings` for PostgreSQL/CockroachDB/YugabyteDB (`jsonb`),
MySQL/TiDB (`JSON`), and SQL Server (`NVARCHAR(MAX)`). `JsonValue` is a separate, provider-agnostic
wrapper maintained in the newer coercion system — prefer it for new code since it works
uniformly across dialects without a per-provider registration; `JsonDocument` remains supported
for existing code and BCL interop. Don't confuse either with the entity-level `[Json]` attribute
(`pengdows.crud.attributes.JsonAttribute`) documented in `CLAUDE.md`/the wiki, which serializes an
arbitrary POCO property to a JSON column — that's a different, higher-level mechanism than these
value-object types.

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
