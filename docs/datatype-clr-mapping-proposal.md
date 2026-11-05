# Proposed CLR mapping for the semantic types

Status: **decided 2026-11-03, not implemented.** The choices below are made. Nothing in the code
changes until the characterization tests are written and red. The mapping is the shared default for every database; a database gets its own entry
only where it genuinely differs (see the last column and `docs/datatype-live-crossref.md`).

Inputs:

- The concept sheet's CLR / DbType column (`pengdows_datatype_crossref.xlsx`) for the default CLR.
- The value objects that already exist in `pengdows.crud/types/valueobjects/` for the types that
  have no CLR equivalent.
- The live probe for which types exist on which database. Its CLR column is zero-row metadata, so
  it is evidence that a type reads back as something, not a guarantee of the value.

Each row is a decision for you to confirm. **Decision** marks a choice where two reasonable
answers exist.

| Semantic type | Proposed CLR | DbType | Notes | Decision |
|---|---|---|---|---|
| signed 8-bit | `sbyte` | `SByte` | Native signed `TINYINT` only on the MySQL family, DuckDB and SingleStore (reads as `SByte`). SQL Server and Sybase `TINYINT` are **unsigned** (`Byte`). PostgreSQL-family and Firebird have no 8-bit type, so the nearest is `SMALLINT` (`Int16`). | **Decision:** `sbyte` as the default, with the unsigned `byte` on SQL Server and Sybase |
| signed 16-bit | `short` | `Int16` | | |
| signed 32-bit | `int` | `Int32` | | |
| signed 64-bit | `long` | `Int64` | | |
| signed 128-bit | `Int128` | `Object` | Native .NET 7+ type. Databases without 128-bit storage use `decimal` (`NUMERIC(38,0)`). | **Decided:** `Int128`. Native from .NET 7 and both targets (net8, net10) have it. Databases with no 128-bit storage use `decimal`. |
| unsigned 8-bit | `byte` | `Byte` | | |
| unsigned 16-bit | `ushort` | `UInt16` | | |
| unsigned 32-bit | `uint` | `UInt32` | | |
| unsigned 64-bit | `ulong` | `UInt64` | | |
| unsigned 128-bit | `UInt128` | `Object` | Only DuckDB (`UHUGEINT`) stores it natively. | |
| arbitrary-size integer | `BigInteger` | `Object` | `NUMBER`, `NUMERIC` with large precision. | |
| auto-increment integer | (no type) | (none) | A column generation clause, not a storage type. Reads as the integer it is built on: `int`, `long` or `ulong` depending on the database. | |
| fixed decimal | `decimal` | `Decimal` | | |
| decimal floating point | `decimal` | `Decimal` | Loses precision above 28 digits; document the limit. | |
| binary float32 | `float` | `Single` | | |
| binary float64 | `double` | `Double` | | |
| money / currency | `decimal` | `Decimal` | | |
| bit / bitstring | `BitArray` | `Object` | Several databases report `ulong` or `bool` for a single bit. | **Decided:** `BitArray`. It keeps the bit length, which `ulong` drops. |
| boolean | `bool` | `Boolean` | Some databases store `0/1` integers (`TINYINT(1)`) and read back as integers; the converter handles it. | |
| fixed character | `string` | `String` | | |
| varying character | `string` | `String` | | |
| unbounded text | `string` | `String` | | |
| national / Unicode character | `string` | `String` | | |
| long text LOB | `string` | `String` | Read as `string` by default; `TextReader` stream for large values. | |
| enumeration | `string` (enum name) or the enum | `String` | Enum storage follows the `DbType` of the `[Column]` (CLAUDE.md, Enum Storage). | |
| set-valued enum | `string` (comma list) | `String` | | |
| fixed binary | `byte[]` | `Binary` | | |
| varying binary | `byte[]` | `Binary` | | |
| binary LOB | `byte[]` | `Binary` | `Stream` for large values. | |
| legacy long binary | `byte[]` | `Binary` | | |
| rowversion / timestamp token | `byte[]` | `Binary` | Opaque; not a time. Write-back only for numeric `[Version]` (CLAUDE.md). | |
| calendar date | `DateOnly` | `Date` | The probe shows `DateTime` on most databases. | **Decided:** `DateOnly`, which is exact. Reads that return `DateTime` today stay supported through coercion. |
| time without zone | `TimeOnly` | `Time` | Probe shows `TimeSpan` on most databases. | **Decided:** `TimeOnly`, which is exact. `TimeSpan` reads stay supported through coercion. |
| time with zone | `DateTimeOffset` | `DateTimeOffset` | No CLR time-with-zone type. Firebird returns `FbZonedTime`, which is provider-specific and needs a converter. | |
| timestamp without zone | `DateTime` (`Kind` unspecified, treated as UTC) | `DateTime2` | Consistent with the UTC rule (docs/utc-and-time.md). | |
| timestamp with zone | `DateTimeOffset` | `DateTimeOffset` | Firebird returns `FbZonedDateTime`. | |
| local-zone timestamp | `DateTime` (UTC) | `DateTime` | Oracle `TIMESTAMP WITH LOCAL TIME ZONE` normalises to the session zone; the instant is what matters. | |
| second-precision datetime | `DateTime` | `DateTime` | | |
| year-only | `short` | `Int16` | Concept sheet default. | |
| year-month interval | `IntervalYearMonth` | (provider) | Value object exists (`types/valueobjects/IntervalYearMonth.cs`). Probe shows `TimeSpan` on some databases, which loses the month/day split. | **Decided:** `IntervalYearMonth`, which is exact. `TimeSpan` cannot hold the month split. |
| day-time interval | `TimeSpan` | `Time` | `IntervalDaySecond` value object exists for exact day/second parts. | |
| generic interval | `PostgreSqlInterval` (PostgreSQL) | (provider) | Value object exists. Other databases fall back to the day-time mapping. | |
| UUID / GUID | `Guid` | `Guid` | Probe shows `string` or `byte[]` on some databases; the converter handles the storage format (see `docs/type-support-matrix.md`). | |
| row identifier / ROWID | `string` | `String` | Opaque; read-only in practice. | |
| object identifier / OID | `uint` | `UInt32` | PostgreSQL `oid`. | |
| transaction ID | `ulong` | `UInt64` | PostgreSQL `xid8`; `xid` is 32-bit and reads as `uint`. | |
| WAL / log position | `string` | `String` | Provider-specific type (`NpgsqlLogSequenceNumber`) on PostgreSQL-compatible databases. | |
| JSON text / JSON value | `string` | `String` | `JsonDocument` is an opt-in converter (`JsonDocumentConverter`). | |
| native binary JSON | `string` | `String` | Same as JSON text. The binary form is not exposed separately. | |
| XML | `string` | `String` | `SqlXml` and `XmlReader` are provider-specific. | |
| BSON | `byte[]` | `Binary` | | |
| variant / dynamic value | `object` | `Object` | Read as the provider's own value. | |
| IP address | `IPAddress` | `Object` | `Inet` value object exists. Spanner has no IP type and stores a string. | **Decided:** `IPAddress`. Spanner has no IP type, so it reads as `string` and the converter parses it. |
| IP network / CIDR | `Cidr` | `Object` | Value object exists (`types/valueobjects/Cidr.cs`). | |
| MAC address | `PhysicalAddress` or `MacAddress` | `Object` | `MacAddress` value object exists. | **Decided:** `MacAddress`. It already exists and follows the `Inet` and `Cidr` pattern. |
| point geometry | `Geometry` | `Object` | `Geometry` value object exists. | |
| line / linestring geometry | `Geometry` | `Object` | | |
| polygon geometry | `Geometry` | `Object` | | |
| general geometry | `Geometry` | `Object` | | |
| geography / geodetic | `Geography` | `Object` | Value object exists. | |
| SQL array | `T[]` | `Object` | Typed element. SAP HANA returns the array's wire encoding (HanaArrayCoercion). | |
| fixed-length array | `T[]` | `Object` | Spanner reports an unbounded array, so the length is not enforced there. | |
| list / multiset / set collection | `List<T>` | `Object` | | |
| map / dictionary | `Dictionary<K,V>` | `Object` | `HStore` value object exists for PostgreSQL `hstore`. | |
| struct / composite / row | (custom mapping) | `Object` | No default CLR type. | **Decided:** `object`. A user who wants a typed value maps it through their own entity. |
| tagged union / variant | `object` | `Object` | DuckDB `UNION` creates but cannot be read back. Not supported until the reader handles it. | |
| range | `Range<T>` | `Object` | Value object exists (`Range.cs`); PostgreSQL uses `NpgsqlRange<T>`. | |
| multirange | `Range<T>[]` | `Object` | | |
| user-defined enum / domain | underlying type | (underlying) | A domain reads as its base type. | |
| user-defined scalar / object | `object` | `Object` | Provider-specific. | |
| full-text search vector / query | `string` | `String` | PostgreSQL `tsvector` and `tsquery` are provider-specific. | |
| vector embedding | `float[]` | `Object` | Probe shows `string` or `byte[]` on some databases. | |
| hierarchical path | `HierarchyId` | `Object` | Value object exists. | |
| opaque / special database type | `object` | `Object` | | |
| external-file reference | `string` | `String` | Path only; the file is not read. | |
| spatial raster / other extensions | `object` | `Object` | Provider-specific. | |
| cursor / table variable | (no column type) | (none) | Not a stored column; not supported as a column. | |
| specialized security label | `string` | `String` | | |

## Where the probe disagrees with the proposal

These are the cases the probe shows a database reporting a CLR type that the proposal doesn't
use as default. They are candidates for per-database entries, not reasons to change the default:

- **Calendar date, time, and year-month interval** report `DateTime`, `TimeSpan` or `Int64` on
  several databases. The default above is the exact type; the provider's type is what the
  converter must map from.
- **UUID** reads as `string` or `byte[]` on several databases, depending on the storage format
  (see `docs/type-support-matrix.md`).
- **Booleans** read as `ulong`, `SByte` or `string` on some databases.
- **Zoned time** on Firebird reads as a provider type (`FbZonedTime`, `FbZonedDateTime`) that needs
  a converter to `DateTimeOffset`.

## Decisions

1. **Signed 128-bit:** `Int128`.
2. **Bit strings:** `BitArray`.
3. **Calendar date and time without zone:** `DateOnly` and `TimeOnly`.
4. **Year-month interval:** `IntervalYearMonth`.
5. **IP address:** `IPAddress`, with Spanner's string handled by the converter.
6. **MAC address:** `MacAddress`.
7. **Struct and composite:** `object`.

**No new CLR types are needed.** Every choice above is an existing BCL type or an existing value
object. The one gap left is Firebird's zoned time and zoned timestamp (`FbZonedTime`,
`FbZonedDateTime`). A converter maps them to `DateTimeOffset`, so they don't need a new CLR type
either. If the characterization tests show a type that has no fit, that's the point to add one, and
it will be a public API addition needing its own review.

**Compatibility:** the defaults change what the library writes and what a new entity declares. They
don't change what an existing read returns, because the coercions for `DateTime`, `TimeSpan` and the
other current reads stay in place.
