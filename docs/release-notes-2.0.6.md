# pengdows.crud 2.0.6 release notes (in progress)

2.0.6 is binary compatible with 2.0.5 (package validation against 2.0.5 for `pengdows.crud`,
`pengdows.crud.abstractions` and `pengdows.crud.fakeDb`). This file collects behavior changes a
2.0.5 user can notice; the release (REL-008) completes it.

## Reads that now fail instead of returning a wrong value

- A `[Json]` column holding text that isn't valid JSON for the property type fails the read with
  `DataMappingException` naming the column. 2.0.5 returned `null`/default silently (DEC-008).

- Blank or whitespace text read into a number, Guid, date or bool (nullable or not) fails with
  `DataMappingException` naming the column (scalar reads: `FormatException`). 2.0.5 returned
  0/`Guid.Empty`/default/false (COR-002).
- A binary column read as a `Guid` must hold exactly 16 bytes; a longer value was silently cut to
  its first 16 (COR-003).
- A provider failure while reading a binary column into a `Guid` surfaces as the provider's own
  exception; 2.0.5 replaced it with an `InvalidValueException` that kept no inner exception (COR-004).
- A string enum column holding a number that isn't a defined member ("999") fails like any other
  undefined value. `[Flags]` enums accept any
  combination of defined flags on both string and numeric columns; 2.0.5 rejected numeric
  combinations (COR-005).
- A gateway read of an enum value that is no member (an unknown name, `"999"`, an undefined number)
  under `EnumParseFailureMode.Throw` fails with `DataMappingException` naming the column, its inner
  exception an `ArgumentException`; 2.0.5 let a bare `ArgumentException` escape. Scalar reads still
  throw an `ArgumentException` (REV-071).
- A fractional number read into an integer property (or an enum stored as a number) must be whole:
  `2.7` fails with `DataMappingException` naming the column (`DataReaderMapper`: when `Strict`,
  otherwise logged and left at the default; scalar reads: `InvalidCastException`). 2.0.5 truncated
  it on the gateway (`2`) and rounded it to even in `DataReaderMapper` and scalar reads (`3`)
  (COR-009).

## Reads that now work

- An unsigned column read into a wider signed property (MySQL `INT UNSIGNED` into a `long`) is
  converted, and so is an unsigned or `sbyte` column into an enum property stored as a number; 2.0.5
  failed the read with `DataMappingException` (an `InvalidCastException` inside) (COR-008, REV-072).
- A `decimal` column (Oracle `NUMBER`) read into an enum property works; 2.0.5 failed building the
  mapper with `InvalidOperationException` (COR-013).

## Oracle spatial

- `Geometry` and `Geography` properties now work with Oracle `SDO_GEOMETRY` columns through every
  gateway path, with no ODP.NET UDT class (TYPE-021). In 2.0.5, writing one threw
  `InvalidOperationException` ("use WithProviderValue") and reading one failed. Values are written as
  EWKT, which the server converts with `SDO_GEOMETRY(...)`, and read through
  `SDO_UTIL.TO_WKTGEOMETRY`; see [advanced-types.md](advanced-types.md#oracle-sdo_geometry). Custom
  SQL must select that same expression. Oracle keeps 15 significant digits per ordinate, and the
  database needs Spatial/Locator installed.
- Oracle's array-bound batch insert and batch update now apply a column's conversion as a single-row
  write does; spatial is the first Oracle column type that needs one.

## Metrics

- `RowsAffectedTotal` counts each write's rows once; 2.0.5 counted them twice, so the total halves
  for the same workload (COR-001).

- `AvgConnectionOpenMs` and `AvgConnectionCloseMs` include sub-millisecond opens and closes (a
  pooled open). 2.0.5 rounded them down to 0 ms and left them out, so the averages counted only
  slow opens (COR-010).

## How commands run

- SQLite and DuckDB commands are no longer prepared by default (`CommandPrepareMode.Auto`): both
  drivers keep a prepared statement only on its command, and every execution makes a new command, so
  preparing bought nothing and cost about 1 µs per operation. Prepare metrics read 0 for these
  databases; `CommandPrepareMode.Always` still prepares (PERF-027).
- On SQL Server, gateway hydration (`LoadSingleAsync`, `LoadListAsync`, `LoadStreamAsync` and the
  methods built on them) opens its reader with `CommandBehavior.SequentialAccess`, reading each
  column once, in order. A reader you open yourself with `ExecuteReaderAsync` is unchanged (DEC-013).

## Every read path converts the same way

The gateway, `DataReaderMapper` and scalar reads (`ExecuteScalar*`) now give the same result for the
same stored value, checked by one table-driven test (DRY-003). Where 2.0.5 differed:

- `DataReaderMapper` returns a `DateTime` column as UTC (`DateTimeKind.Utc`), as the gateway does;
  2.0.5 left it `Unspecified`.
- A number read into a `bool` is true when non-zero and fails when NaN. 2.0.5 read NaN as `true` in
  `DataReaderMapper` and `false` elsewhere, read the smallest non-zero `double` as `false`, and failed
  a `ulong` above `long.MaxValue` in scalar reads.
- An enum read from a number accepts any combination of `[Flags]` members in `DataReaderMapper` and
  scalar reads (2.0.5: only on the gateway). Text holding a number that is no member (`"99"`) fails
  there too, as it does on the gateway.
- `EnumParseFailureMode.SetNullAndLog` gives a non-nullable enum property its default in
  `DataReaderMapper` and `Coerce` (2.0.5 returned null, which failed to unbox: a
  `DataMappingException` under `Strict`), and now logs on the gateway, which gave the default
  without logging (DRY-007).

## SingleStore

- SingleStore runs every transaction as READ COMMITTED whatever level is requested (it accepts and
  reports the others; confirmed live on 9.1.1). pengdows now reports `ReadCommitted` as its only
  supported level: `RepeatableRead`, `Snapshot` or `Serializable` throws `InvalidOperationException`
  and `IsolationProfile.StrictConsistency` throws `TransactionModeNotSupportedException`, where 2.0.5
  began the transaction and silently ran it at READ COMMITTED (REV-087).

## TiDB

- Upserts of `UInt64`, `Int64` and `Binary` columns no longer go through `VALUES(col)`, which TiDB
  returns byte-reversed for a `BIT(64)` column however it is bound: a single-row upsert sets the
  column from its own parameter, and a batch upsert of an entity with such a non-key column runs one
  statement per row (DEC-012, REV-083).

## pengdows.crud.fakeDb

fakeDb behaves like the provider it emulates. Each change below was checked against the real
driver (Microsoft.Data.Sqlite, DuckDB.NET, Npgsql, SqlClient) on 2026-10-04 (DEC-009). A test that
passed on 2.0.5 and fails now was relying on behavior no real provider has.

| Behavior | 2.0.5 | 2.0.6 | Real providers |
|---|---|---|---|
| `Open()` on an open connection | returned silently | throws `InvalidOperationException`; a no-op when emulating SQLite | SqlClient, Npgsql, DuckDB.NET throw; Microsoft.Data.Sqlite returns |
| `GetOrdinal` of a missing column | returned -1 | throws `IndexOutOfRangeException` | every provider tested throws (25 of 30 targets exactly `IndexOutOfRangeException`) |
| `ConnectionTimeout` default | 0 | 15, or the connection string's value | 15 on Microsoft.Data.Sqlite, Npgsql, SqlClient |
| `Database` | the emulated product's name (e.g. `"Sqlite"`) | the connection string's `Database`/`Initial Catalog`, else `""`; `"main"` when emulating SQLite, the data source when emulating DuckDB | what each provider reports |
| Minimum pool size above maximum | accepted | rejected with `ArgumentException` | Npgsql and SqlClient reject |
| `fakeDbFactory.CreateDataSource` | .NET's default data source | .NET's default data source; `SupportsNativeDataSource = true` returns a `FakeDbDataSource` | providers without their own data source return .NET's default |
