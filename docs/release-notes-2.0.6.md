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
- A string enum column holding a number that isn't a defined member ("999") fails like any other
  undefined value (`ArgumentException`, as before for unknown names). `[Flags]` enums accept any
  combination of defined flags on both string and numeric columns; 2.0.5 rejected numeric
  combinations (COR-005).
- A fractional number read into an integer property (or an enum stored as a number) must be whole:
  `2.7` fails with `DataMappingException` naming the column (`DataReaderMapper`: when `Strict`,
  otherwise logged and left at the default; scalar reads: `InvalidCastException`). 2.0.5 truncated
  it on the gateway (`2`) and rounded it to even in `DataReaderMapper` and scalar reads (`3`)
  (COR-009).

## Reads that now work

- An unsigned column read into a wider signed property (MySQL `INT UNSIGNED` into a `long`) is
  converted; 2.0.5 failed the read with `DataMappingException` (an `InvalidCastException` inside)
  (COR-008).
- A `decimal` column (Oracle `NUMBER`) read into an enum property works; 2.0.5 failed building the
  mapper with `InvalidOperationException` (COR-013).

## Metrics

- `AvgConnectionOpenMs` and `AvgConnectionCloseMs` include sub-millisecond opens and closes (a
  pooled open). 2.0.5 rounded them down to 0 ms and left them out, so the averages counted only
  slow opens (COR-010).

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

## TiDB

- Upserts of `UInt64` columns (`BIT(64)`, `BIGINT UNSIGNED`) no longer go through `VALUES(col)`,
  which TiDB returns byte-reversed for `BIT(64)`: a single-row upsert sets the column from its own
  parameter, and a batch upsert of such an entity runs one statement per row (DEC-012).

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
