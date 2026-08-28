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

## Usage pattern

No special attribute is needed to use these types. Declare the property with the value-object
type and a `[Column]` attribute giving the storage `DbType`; the coercion pipeline does the rest
based on the CLR type:

```csharp
[Table("hosts")]
public class Host
{
    [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }

    [Column("address", DbType.String)] public Inet Address { get; set; }
    [Column("subnet", DbType.String)] public Cidr Subnet { get; set; }
    [Column("mac", DbType.String)] public MacAddress Mac { get; set; }
    [Column("tags", DbType.String)] public HStore Tags { get; set; }
    [Column("uptime", DbType.String)] public PostgreSqlInterval Uptime { get; set; }
    [Column("metadata", DbType.String)] public JsonValue Metadata { get; set; }

    [Version]
    [Column("row_version", DbType.Binary)]
    public RowVersion RowVersion { get; set; }
}
```

`RowVersion` follows the same `[Version]` contract as a plain `byte[]` rowversion column
(`docs/primary-keys-pseudokeys.md` / `CLAUDE.md`'s Version Column section): it's excluded from
the SET clause and used only in the optimistic-concurrency WHERE match. It is **not**
incremented by the library — SQL Server generates the new value server-side, so (like a plain
`byte[]` rowversion) the caller's in-memory value goes stale after a successful write unless
reloaded; there is no free write-back for this column shape (tracked in
`docs/planning/future-work.md`).

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
| `Geometry` / `Geography` (`Geometry.cs`, `Geography.cs`, both extend `SpatialValue`) | Planar vs. geodetic spatial data; WKB/WKT/GeoJSON-backed | SQL Server (UDT), PostgreSQL/PostGIS (WKB via `Binary`) |
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
type mapping. If you need one, the only option today is defining `[Column(..., DbType.X)]` on a
primitive-backed property and doing the conversion yourself in the entity, or filing a request to
expose a public registration surface.

## Declared but not wired in

`types/attributes/WeirdTypeAttributes.cs` defines 12 attributes intended to configure this system
further: `DbEnumAttribute`, `JsonContractAttribute`, `ConcurrencyTokenAttribute`,
`RangeTypeAttribute`, `ComputedAttribute`, `CaseInsensitiveAttribute`, `AsStringAttribute`,
`MaxLengthForInlineAttribute`, `CaseFoldOnReadAttribute`, `SpatialTypeAttribute`, and
`CurrencyAttribute`. As of this writing **none of them are read anywhere outside their own unit
test** (`pengdows.crud.Tests/WeirdTypeAttributesTests.cs`, which only exercises each attribute's
constructor/property getters-setters) — they have no effect on SQL generation, type coercion, or
`TypeMapRegistry`. Applying `[Currency("USD")]` or `[ConcurrencyToken]` to a property today is
inert.

Two of these also shadow the name of an already-wired, unrelated attribute in a different
namespace, which is worth knowing before reaching for either by name alone:
- `DbEnumAttribute` (`pengdows.crud.types.attributes`, inert) vs. `EnumColumnAttribute`
  (`pengdows.crud.attributes`, real — see `docs/overview.md`) for configuring enum storage.
- `JsonContractAttribute` (`pengdows.crud.types.attributes`, inert) vs. `[Json]`
  (`pengdows.crud.attributes.JsonAttribute`, real) for JSON column mapping.

This gap (wire these attributes into the mapping pipeline, or remove them) isn't tracked in
`docs/planning/future-work.md` yet — worth adding there before treating any of the 12 as usable.
