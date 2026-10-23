# pengdows.crud 2.0.6 release notes

2.0.6 is binary compatible with 2.0.5 (package validation against 2.0.5 for `pengdows.crud`,
`pengdows.crud.abstractions` and `pengdows.crud.fakeDb`). This file collects what is new and the behavior
changes a 2.0.5 user can notice.

## New databases

2.0.0 to 2.0.5 supported the same fourteen databases. 2.0.6 adds nine, each with a `SupportedDatabase` value of its
own (23 in all), its own detection, and an integration suite that passes on net8.0 and net10.0
([integration-status.md](integration-status.md)). A new value is additive: nothing that compiled against 2.0.5
changes.

| Database | How it is supported | Integration suite |
|---|---|---|
| IBM Db2 for Linux/Unix/Windows (LUW) | its own dialect; Db2 for z/OS and Db2 for i are not supported | always on |
| FlatFile: [pengdows.flatfile](https://github.com/pengdows/pengdows.flatfile) (CSV, delimited, fixed-width, NDJSON files) | its own dialect | always on |
| SingleStore | detected at runtime (it reports itself as plain MySQL); MySQL dialect | always on |
| SAP (Sybase) ASE | its own dialect | always on |
| Google Cloud Spanner (PostgreSQL interface) | its own dialect, derived from the PostgreSQL dialect | always on |
| IBM Informix | its own dialect | always on |
| SAP HANA | its own dialect | opt-in: `INCLUDE_SAPHANA=true` (the image needs 16 to 32 GB of RAM) |
| InterBase | its own dialect | opt-in: `INCLUDE_INTERBASE=true` (node-locked license) |
| Microsoft Access (Jet/ACE) | its own dialect | runs when the OS is Windows (no env var) |

Four more products are supported as subsidiaries of their parent, with **no code change and no new
`SupportedDatabase` value**: Percona Server for MySQL (under MySQL), and Citus, TimescaleDB and Fujitsu Enterprise
Postgres (under PostgreSQL). Each reports itself as MySQL or PostgreSQL, so detection resolves it to the parent's
dialect (`Product` reports the parent), and each passed the same integration suite as the vanilla engine with the same
number of checks. EDB Postgres Extended is not claimed (no public image to verify against).

Aurora PostgreSQL and Aurora MySQL are detected at runtime and use the PostgreSQL and MySQL dialects; they are opt-in in
the integration run (`INCLUDE_AURORA=true`). Per-database requirements and version floors:
[supported-databases.md](supported-databases.md).

## Reads that now fail instead of returning a wrong value

- A `[Json]` column holding text that isn't valid JSON for the property type fails the read with
  `DataMappingException` naming the column. 2.0.5 returned `null`/default silently (DEC-008).
  Blank text reads as the JSON literal `null` on every path (gateway, `DataReaderMapper`,
  `TypeCoercionHelper`, the `JsonDocument` converter): null for a reference or nullable type, as in
  2.0.5, but a non-nullable value type such as `int` now fails instead of reading `default`, and a
  `JsonValue` reads as `null` JSON rather than as C# `null` or empty text (COR-007).

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

## New API

- `ColumnName(string propertyName)` on `ITableGateway`/`IPrimaryKeyTableGateway`, and
  `ColumnName<TEntity>(string propertyName)` as an `IDatabaseContext` extension, return a property's
  mapped `[Column]` name for custom SQL: `sc.WrapObjectName("o." + ColumnName(nameof(Order.CustomerId)))`
  instead of a hard-coded `"o.customer_id"`. Both interface members have default implementations, so
  implementations compiled against 2.0.5 still load. See [entity-mapping.md](entity-mapping.md).

- `AuditCreationPolicy` (enum in `pengdows.crud.abstractions`) and an init-only `AuditCreationPolicy` property on
  `ITableGateway`, `IPrimaryKeyTableGateway` and `BaseTableGateway`. `PreserveExplicitValues` (the default, and the
  2.0.5 behavior) keeps a non-default `CreatedBy`/`CreatedOn` already on the entity; `Authoritative` always
  overwrites them with the resolver's values, so request-model binding cannot spoof them. The interface members
  have default implementations, so implementations compiled against 2.0.5 still load. See
  [audit-fields.md](audit-fields.md).

- `ClampPoolsToServerConnectionLimit` and `ResourceConnectionHeadroom` on `DatabaseContextConfiguration`
  (off and `0` by default) read the database server's own connection limit while a context initializes and
  size each role's pool so the reader and writer pools together never exceed it, leaving optional headroom
  for other clients. They are supported for PostgreSQL and YugabyteDB, MySQL and MariaDB, and SQL Server;
  other engines are left unchanged. Both are default interface members on `IDatabaseContextConfiguration`,
  so implementations compiled against 2.0.5 still load. Because the clamp can shrink a pool that exceeded
  the server's limit, it is opt-in on 2.0.x. See [connection-pooling.md](connection-pooling.md).

- `services.AddDbProviderLoading(configuration)` (`pengdows.crud.configuration`) loads the
  `DatabaseProviders` section of an `IConfiguration` (by assembly path, assembly name, or the legacy
  `DbProviderFactories` registry) and registers each `DbProviderFactory` as a keyed singleton, so a host can
  wire providers in one call instead of constructing a `DbProviderLoader` by hand. It adds a static class and
  no member on any existing type.

## Writes that now work

- A C# enum stored by name in a PostgreSQL-family user-defined `ENUM` column now works in a MERGE
  upsert (PostgreSQL 15+) and a batch update on PostgreSQL, CockroachDB and YugabyteDB. 2.0.5 failed
  with "column is of type ... but expression is of type text". Those statements now take their rows
  from a source typed by the table itself, only when a row holds such an enum (TYPE-020, WRT-007).
- A `string` property bound to a PostgreSQL-family user-defined `ENUM` (or other non-text) column
  now writes through every gateway path, and a C# enum value used as a parameter in your own SQL is
  sent as its name. 2.0.5 failed both ("column is of type ... but expression is of type text";
  "Writing values of '...' is not supported"). To do this, the first async gateway call on a context
  runs one zero-row metadata query per table, `SELECT <columns> FROM <table> WHERE 1 = 0`, for the
  dialects and columns that need a declared type. It is described in
  [advanced-types.md](advanced-types.md#declared-column-types) (TYPE-020).
- Informix upserts of `INTERVAL`, `LIST`/`SET`/`MULTISET` and `BOOLEAN` columns work. 2.0.5 threw
  `NotSupportedException` ("does not support a Object column in the source row") or failed with
  "Value does not match the type of column". The MERGE now carries only the key columns in its
  source and binds every other value directly in `UPDATE SET` and `INSERT VALUES`, where the column
  types it (WRT-004, WRT-005).
- Informix `BLOB`, `CLOB`, `TEXT`, `BYTE`, `BSON` and `DATETIME HOUR TO FRACTION(n)` columns now
  round-trip through every gateway path. 2.0.5 failed `BLOB`/`CLOB` updates and upserts and small
  `BLOB`/`CLOB` inserts ("Illegal attempt to use Text/Byte host variable"), `TEXT` writes, `BYTE`/`TEXT`
  upserts and `BSON` writes, and truncated a time of day to whole seconds. With the column's declared type
  (above), `TEXT` is bound as `IfxType.Text`, `BSON` goes through `::JSON::BSON` and is read as `::JSON`, a
  time of day is written as text cast to the column's type, and `BLOB`/`CLOB` values (and `BYTE`/`TEXT`
  values in a MERGE) are staged in a session temp table, `pengdows_lob_stage`, and read back by key
  (WRT-006, TYPE-020, TYPE-022).
- A NULL in a MySQL-family parameter of `DbType.Object` (every nullable spatial column, and any such parameter in
  your own SQL) now writes on MySQL with MySqlConnector 2.5 or later. MySqlConnector 2.5 maps `DbType.Object` to
  `MySqlDbType.Vector`, and MySQL 8.4 refuses that NULL in a prepared statement ("Incorrect arguments to
  mysqld_stmt_execute"; confirmed live, MySQL 8.4.11, MySqlConnector 2.6.2). Just before execution the parameter's
  type is reset, so the driver types it from the value.

## Reads that now work

- An integer beyond `long` (Snowflake `NUMBER`) read into a `double` through the gateway rounds to
  the nearest double, as `DataReaderMapper` and `TypeCoercionHelper` do; it was truncated
  (DRY-009).

- Sybase ASE `BIGDATETIME` keeps its microseconds through the gateways (2.0.5 wrote milliseconds and
  read values a few microseconds off), and `BIGTIME` can be read at all (2.0.5: "Unsupported data
  type 188"). `DbType.DateTime2` and `DbType.DateTimeOffset` parameters are now sent as microsecond
  text, also in your own SQL; a `DATETIME` column stores the same value as before. Gateway reads of
  these columns select `CONVERT(VARCHAR, col, 140/137)` (TYPE-022).
- Snowflake `VECTOR(FLOAT, n)` and `VECTOR(INT, n)` columns map to `float[]`/`double[]` and `int[]`.
  2.0.5 failed to write them (Snowflake.Data can't bind a vector). They are written as
  `PARSE_JSON(:p)::VECTOR(..., n)`, with `n` taken from each value when the command runs (TYPE-020).
- SAP HANA `ARRAY` columns map to `int[]`, `long[]`, `short[]`, `double[]`, `float[]` and `string[]`
  (nullable elements allowed). 2.0.5 failed ("The parameter data type of Int32[] is invalid") and read
  the column as raw bytes. Arrays are bound as JSON text and built with `ARRAY(SELECT V FROM
  JSON_TABLE(?, ...) ORDER BY O)`, and reads decode the driver's bytes; see
  [advanced-types.md](advanced-types.md) (TYPE-020).
- SAP HANA `TIMESTAMP` keeps all 7 fractional digits through the gateways; Sap.Data.Hana.Net cut them
  to 6 on write and read. `DbType.DateTime`/`DateTime2` parameters are now sent as 7-digit text, also
  in your own SQL (`SECONDDATE` and `DATE` store the same value as before), and gateway reads select
  `TO_VARCHAR(col, '... FF7')` (TYPE-022).
- SAP HANA spatial values keep their SRID. 2.0.5 sent plain WKB, which has none: a column declared
  with a spatial reference (`ST_GEOMETRY(4326)`) refused every upsert and update ("Spatial value is
  incompatible with column"), and every `Geometry`/`Geography` read back with SRID 0. Values are now
  written as EWKB through `ST_GeomFromEWKB(?)` and gateway reads select `col.ST_AsEWKB()` (DRY-023).
- A negative DuckDB `INTERVAL` read into a `TimeSpan` works. DuckDB.NET 1.5.6 throws for every
  negative interval; the value is now read from its stored parts. An interval with months fails with
  `DataMappingException` instead of reading as zero months (TYPE-022).

- An unsigned column read into a wider signed property (MySQL `INT UNSIGNED` into a `long`) is
  converted, and so is an unsigned or `sbyte` column into an enum property stored as a number; 2.0.5
  failed the read with `DataMappingException` (an `InvalidCastException` inside) (COR-008, REV-072).
- A `decimal` column (Oracle `NUMBER`) read into an enum property works; 2.0.5 failed building the
  mapper with `InvalidOperationException` (COR-013).

## Guids in 16-byte binary columns

- A `Guid` property declared `DbType.Binary` (`BINARY(16)`, `RAW(16)`, `BLOB`, ...) is written and read
  in the database's own Guid byte order through the gateway and `DataReaderMapper`: RFC 4122 order
  (the order `UUID_TO_BIN()` and every uuid tool print) everywhere except SQL Server, Oracle and
  Sybase ASE, which use .NET's `ToByteArray()` order. 2.0.5 refused the declaration and always
  decoded 16 bytes in .NET order.
- **Behavior change:** on the RFC 4122 databases, bytes your application wrote itself with
  `guid.ToByteArray()` now read as a different Guid. Rewrite them with
  `ToByteArray(bigEndian: true)`, or read the column into `byte[]` and call `new Guid(bytes)`. A `Guid`
  declared `DbType.Guid` is unchanged. See
  [advanced-types.md](advanced-types.md#guids-in-16-byte-binary-columns).
- `GuidRoundTripTests` round-trips a recognisable Guid through every database's Guid columns and
  checks the stored bytes.

## Parameter creation

- Creating a command parameter allocates no more than in 2.0.5 on every dialect: the 2.0.6 per-parameter
  closure and extra boxing introduced while adding the type checks were removed.

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

## Errors

- On Oracle, a connect refused because the server is out of process slots (ODP.NET's ORA-50201
  wrapping the listener's ORA-12516, ORA-12519 or ORA-12520) is now a `TooManyConnectionsException`
  (transient), like ORA-00018/00020; it was a plain `ConnectionException`. Found under load in the full
  integration run.
- A connect refused at the server's connection limit now surfaces as `TooManyConnectionsException` from
  `BeginTransaction` / `BeginTransactionAsync` too, as it already did from commands. Opening the transaction's
  connection used to throw the raw provider exception, and a refused begin was wrapped in a generic
  `TransactionException`. A failure to begin that is not a connection problem is still a `TransactionException`.
  Code that caught the raw provider exception around `BeginTransaction`, or `TransactionException` for a refused
  connect, should catch `TooManyConnectionsException` / `ConnectionException`.

## Metrics

- `NumberOfOpenConnections` and the connection metrics come back down when a provider's `Close` throws.
  2.0.5 already disposed such a connection and released its pool slot, but the close was never reported,
  so the count stayed one too high for good after each failure. See
  [connection/release-guarantees.md](connection/release-guarantees.md) for every failure path and its test.

- `RowsAffectedTotal` counts each write's rows once; 2.0.5 counted them twice, so the total halves
  for the same workload (COR-001).

- `AvgConnectionOpenMs` and `AvgConnectionCloseMs` include sub-millisecond opens and closes (a
  pooled open). 2.0.5 rounded them down to 0 ms and left them out, so the averages counted only
  slow opens (COR-010).

- A pool's `TotalWaitTicks` and `AverageWaitMs` count only time actually spent waiting for a slot. An
  acquire that finds a slot free now records a wait of exactly 0; 2.0.5 recorded the few hundred
  nanoseconds between two clock reads, so an uncontended pool showed a small non-zero average wait and
  now shows 0. A pool whose callers did queue (`TotalWaits` above 0) reports its waits as before.

## How commands run

- SQLite and DuckDB commands are no longer prepared by default (`CommandPrepareMode.Auto`): both
  drivers keep a prepared statement only on its command, and every execution makes a new command, so
  preparing bought nothing and cost about 1 µs per operation. Prepare metrics read 0 for these
  databases; `CommandPrepareMode.Always` still prepares (PERF-027).
- On SQL Server, gateway hydration (`LoadSingleAsync`, `LoadListAsync`, `LoadStreamAsync` and the
  methods built on them) opens its reader with `CommandBehavior.SequentialAccess`, reading each
  column once, in order. A reader you open yourself with `ExecuteReaderAsync` is unchanged (DEC-013).
- Firebird DDL (`CREATE`/`DROP`/`ALTER`/`TRUNCATE`) outside a transaction now waits for the table's
  lock, up to the command timeout, in a READ COMMITTED WAIT transaction. After writes to a table,
  the engine's garbage collector holds it, and 2.0.5 ran the DDL NO WAIT (FirebirdClient's default),
  so it failed with "lock conflict on no wait transaction ... object TABLE is in use" until the
  collector let go, which took over 90 s in tests (WRT-001).
- `CreateAsync` with a database-generated id returned by the INSERT (`RETURNING`/`OUTPUT`) no longer
  pins a connection up front for the rare fallback id query: the INSERT keeps its own connection and
  hands it to the fallback only when the id doesn't come back, so the fallback still runs on the
  INSERT's connection. That path now allocates exactly what the INSERT itself does; the id and
  `[Version]` write-backs use a compiled setter instead of reflection (DEC-014, PERF-016).
- Less per-operation overhead: the cached retrieve-by-id and delete-by-id statements render their
  SQL once instead of on every call, a container's parameter list sizes itself to the statement
  instead of reserving room for eight, and each context makes its SQL container logger once
  instead of per container. On in-memory SQLite, `RetrieveOneAsync` went from 26.5 to 25.5 µs and
  3,711 to 3,391 bytes; creates and updates allocate 120 bytes less (PERF-031).

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

## One type system

Every type is now read and written by one path, and a support matrix generated by a test lists, for
every CLR type and every database, whether the value round-trips and why not where it doesn't
([type-support-matrix.md](type-support-matrix.md); DRY-010). Behavior a 2.0.5 user can notice:

- **A value declared `DbType.DateTimeOffset` is bound as its instant.** Db2, Informix and Access
  bound a `DateTime` or timestamp text declared `DbType.DateTimeOffset` as **NULL**; SQL Server,
  Oracle, DuckDB and Snowflake passed an Unspecified `DateTime` on for the driver to give the host's
  local offset; other databases passed text verbatim. A `DateTime` (Unspecified is UTC) or timestamp
  text declared `DbType.DateTimeOffset` now binds exactly as the `DateTimeOffset` of the same instant,
  and no value is bound as NULL. InterBase also remaps a NULL `DateTimeOffset` (DRY-021).
- **A `DateTimeOffset` written to a column with no offset stores its UTC instant.** On SQL Server,
  Firebird, Db2, DuckDB, FlatFile, Sybase ASE, Informix, InterBase and Access, a `DateTimeOffset`
  declared `DbType.DateTime`/`DateTime2` was handed to the driver as-is, which stored its local wall
  time: `13:45+02:00` was stored as `13:45` and read back two hours off. It is now bound as its UTC
  `DateTime` on every database, as PostgreSQL and MySQL already did. Rows already written this way
  hold the wall time and aren't corrected by upgrading (DRY-025).
- **Oracle and Snowflake keep a `DateTimeOffset`'s offset.** A `DateTimeOffset` declared
  `DbType.DateTimeOffset` was sent as its UTC instant, so Oracle `TIMESTAMP WITH TIME ZONE` and
  Snowflake `TIMESTAMP_TZ` columns read it back at `+00:00`. The gateways now learn each such column's
  declared type (the one-time probe) and send the value with its offset to a column that keeps one;
  every other column, and every parameter in your own SQL, still gets the UTC instant, so nothing that
  worked before changes. Snowflake gets ISO 8601 text with the offset there (Snowflake refuses its
  driver's `TIMESTAMP_TZ`-typed bind into `TIMESTAMP_LTZ` and `TIMESTAMP_NTZ` columns). With ODP.NET 21
  (3.21), whose `GetValue` returns only the wall time of a `TIMESTAMP WITH TIME ZONE`, the gateway,
  `DataReaderMapper` and `ExecuteScalar*<DateTimeOffset>` read it with the driver's `GetDateTimeOffset`.
  An Oracle batch insert (array binding) also truncated only its first row's fractional seconds to the
  column (DRY-028); every row is now prepared alike (DRY-029).
- **PostgreSQL-family `DateTime` kinds.** On PostgreSQL, CockroachDB, YugabyteDB and Spanner a
  `DateTime` with `Kind=Unspecified` declared `DbType.DateTime` (`timestamptz`) was refused by
  Npgsql, and a `Kind=Local` one declared `DateTime2` was sent as its local wall time. Both now go as
  the UTC instant (DRY-026).
- **Oracle NULL intervals bind.** A NULL `IntervalDaySecond`, `IntervalYearMonth` or `TimeSpan`
  declared `DbType.Object` failed with ORA-50028; it now binds typed (DRY-027).
- **Fractional seconds are truncated, never rounded, on SQL Server, Oracle and Sybase ASE.** SQL
  Server `DATETIME`/`SMALLDATETIME` (and `DATETIME2`/`TIME`/`DATETIMEOFFSET` below scale 7), Oracle
  `DATE`/`TIMESTAMP(n)`/`INTERVAL DAY TO SECOND(n)` and Sybase `SMALLDATETIME` round the digits they
  can't hold, so a value one tick before midnight was stored as the next day. The gateways now learn
  each temporal column's declared type and scale once per table (one zero-row query, on the first
  async operation) and truncate the value before binding it (DRY-028). fakeDb's `fakeDbColumn` gains
  `NumericPrecision`/`NumericScale`, and `fakeDbDataReader.GetSchemaTable()` reports declared columns.
- **Oracle: a disposed context no longer keeps server sessions open.** Each ODP.NET
  `OracleDataSource` owns its own pool, and disposing it leaves that pool's idle connections open
  until the process exits, so every `DatabaseContext` created and disposed (a tenant registry
  dropping a tenant, a context per job) kept up to two Oracle sessions. A context now clears the
  pools of the data sources it created when it is disposed; one you pass in is left alone
  (HARN-016).
- **Streams and readers bind as declared.** A `Stream` property declared `DbType.Binary` or a
  `TextReader` declared `DbType.String`/`AnsiString` made the gateway fail to build its templates
  ("CLR type 'MemoryStream' is not compatible with DbType.Binary"); only `DbType.Object` worked. Any
  subclass is now accepted, gets the dialect's LOB mapping (SQL Server's MAX size, Oracle's
  BLOB/CLOB) and is sent as its bytes or text (DRY-022).
- SQLite writes `decimal.MaxValue`/`MinValue` (as exact text); 2.0.5 threw `OverflowException`.
  SingleStore writes a spatial value built from WKB (any value read from another database) as its
  WKT; 2.0.5 threw `NotSupportedException` (DRY-022).
- Interval text is parsed strictly, to the tick: Oracle's `+0001-02` (which pengdows itself writes)
  read as 0 months, `P1DT2H3M4S` as 3 months, `P1Y2M` as 0 days and lower-case ISO as 1 day; every
  form a database returns now reads exactly and anything else fails. ISO day-second text is written
  with all 7 fraction digits (DRY-012).
- Range text has one grammar: `Range<T>.Parse("(,5]")` is unbounded below (2.0.5: a lower bound of
  0); PostgreSQL's quoted bounds (every timestamp range) are read; blank or bracket-less text is no
  value rather than an empty range; `DateTime` bounds are written as ISO to the tick (2.0.5:
  `MM/dd/yyyy HH:mm:ss`, dropping the fraction). `Range<T>.Parse` no longer echoes the text in its
  exception (DRY-013).
- A `Geography` read from PostGIS EWKB is plain WKB with its SRID; 2.0.5 left the SRID flag and
  bytes in the WKB (DRY-011). A spatial value or `HierarchyId` read through an unresolved column no
  longer carries trailing zeros when the provider returns fewer bytes than it reported (DRY-014).
- UTF-8 bytes read into a `JsonValue` work as they do for `JsonDocument`/`JsonElement`, and an empty
  byte segment or memory reads as JSON null instead of a serialized struct (DRY-015).
- A converter type reads every input its old coercion did: a `RowVersion` from a `ulong`, an
  `IntervalYearMonth` from a month count, provider network types (such as Npgsql 8's `NpgsqlCidr`)
  by shape (DRY-010).
- Text holding only a time of day (`"13:45:30"`) is no timestamp: it read into a `DateOnly` or
  `DateTimeOffset` as today's date, a value that depended on the day it was read, and now fails like
  any other non-date text. A `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan` or
  `Guid` read into a `string` property is its invariant canonical text (ISO 8601, `"c"`, `"D"`) on
  every database; 2.0.5 used the culture's `ToString` (and failed a `Guid` with
  `InvalidCastException`). The update dirty check reads timestamp text the same way.
- **Upserts handle audit fields as creates do.** Without an `IAuditValueResolver`, `UpsertAsync`
  and `BatchUpsertAsync` (both gateways) left `[CreatedOn]`/`[LastUpdatedOn]` at `default`
  (writing `0001-01-01`, which SQL Server's `DATETIME` rejects) and wrote or dropped
  `[CreatedBy]`/`[LastUpdatedBy]` silently. They now stamp the timestamps and, for an entity with a
  user audit field, throw the `InvalidOperationException` `CreateAsync` and `UpdateAsync` throw;
  `BatchUpdateAsync` throws it too instead of writing `[LastUpdatedBy]` as NULL (DRY-016).
- The single-row ON CONFLICT upsert of an entity with an opaque (`byte[]`/`RowVersion`) version no
  longer guards on it: the guard compared the stored version with the inserted one, so an existing
  row was never updated. Creating such an entity with a null version no longer throws
  `InvalidCastException` (DRY-016).
- `CommandsTimedOut` counts a provider timeout wrapped in an outer exception (Npgsql's client-side
  timeout), which was already thrown as `CommandTimeoutException`; `TotalConnectionFailures` and
  `TotalConnectionTimeoutFailures` count failed connection opens (they were never incremented)
  (DRY-017). Classifying a Db2/Informix error whose code is `int.MinValue` no longer throws
  `OverflowException` (DRY-018).
- Constraint-violation exceptions from DuckDB, Firebird, FlatFile, SQLite and Access carry the
  provider's `SqlState` and `ConstraintName` (where it reports them), as every other database's do;
  they carried only `ErrorCode` (DRY-018).
- Firebird `NONE`-charset text with non-Latin characters reads back correctly when another driver in
  the process has registered .NET code pages (DRY-020).

## Isolation profiles

- SQL Server: `IsolationProfile.SafeNonBlockingReads` now also resolves, as ReadCommitted, when `READ_COMMITTED_SNAPSHOT`
  (RCSI) is on and snapshot isolation is off; 2.0.5 threw `TransactionModeNotSupportedException`. With RCSI, readers read row
  versions, so they never block on writers and never see dirty data: the same statement-level MVCC the profile already
  accepts as ReadCommitted on Oracle and MySQL. It is not a transaction-wide snapshot. Snapshot isolation on still gives
  Snapshot; with neither on, the profile still throws. Verified on a live SQL Server (`SqlServerRcsiProfileTests`): with RCSI on,
  a reader does not block on an uncommitted writer and reads the old row; with neither on, the profile is refused and a plain
  ReadCommitted reader does block.
- One refusal contract: an `IsolationProfile` that cannot be met (no mapping, a mapped level the database lacks, or a mapping
  weaker than the profile guarantees) always throws `TransactionModeNotSupportedException`, which derives from
  `NotSupportedException`, so a handler written for the earlier bare `NotSupportedException` still catches it. An explicit
  native level that nothing at or above satisfies still throws `InvalidOperationException`.

## SQLite

- **A connection a caller changed no longer carries the change to the next operation.** Microsoft.Data.Sqlite's pool
  returns a connection to the next checkout exactly as the last caller left it, and SQLite has no connection-string
  keyword for most settings. pengdows.crud put back `foreign_keys` on every checkout; it now also puts back
  `query_only` (always OFF: SQLite refuses `BEGIN IMMEDIATE` on a `query_only` connection and Microsoft.Data.Sqlite
  begins every transaction that way, so a poisoned connection broke the next write, or a read transaction at BEGIN).
  They travel in the same single command, so a checkout costs no extra round trip.
- **`synchronous = NORMAL` when the database is in WAL mode (behavior change).** pengdows.crud already put every
  write-capable SQLite file into WAL mode; in WAL, NORMAL keeps every committed transaction through an application
  crash and risks only the newest commits in a power failure (the database is never corrupted), and it saves the sync
  FULL does on every commit. It is applied only when the database really went into WAL, so a context that cannot get WAL
  (a memory database, some network file systems) keeps SQLite's own default. It is put back on every checkout. There
  is no setting to opt out in 2.0.6 (see `docs/FUTURE_WORK.md`, SQLITE-006).
- **`PRAGMA optimize` once at startup.** SQLite's advice for a long-lived process, run as its own best-effort command
  that waits at most two seconds for a lock, so a file another process is writing never stalls or fails startup.

## Aurora PostgreSQL

- An Aurora PostgreSQL cluster is now detected as `AuroraPostgreSql` (its `Product`), where it was reported as
  plain `PostgreSql`. The detector looked for `aurora_version()` in `pg_proc`, but on a live Aurora PostgreSQL 17.9
  cluster the function answers when called and is not a `pg_proc` row. Detection now also checks for Aurora-only
  settings in `pg_settings`. Code that branches on `Product` and treated Aurora as `PostgreSql` by accident should
  handle both values (the dialect, types and error handling are the same). Aurora MySQL detection was already correct.

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
  statement per row (DEC-012, REV-083). The async gateways learn those columns' declared types once
  per table, so only a `BIT` column (or one whose type can't be read) takes that path; `BIGINT` and
  `VARBINARY` columns keep the single multi-row statement (PERF-029).
- TiDB sends no read-only statement. It refuses `SET SESSION transaction_read_only = 1` ("function READ ONLY has only
  noop implementation in tidb now"; live, v8.5.7) and cannot enforce read-only even with `tidb_enable_noop_functions`.
  A read-intent transaction on TiDB now begins (with a failed read-only entry now throwing, it would have failed), and a
  read connection's session settings no longer fail on every checkout, which logged an error each time and, under
  `SessionInitializationFailureMode.FailClosed`, refused the connection. pengdows.crud's own read-intent guard still
  applies.

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
| `fakeDbParameter.ResetDbType()` | set `DbType.Object` | restores a new parameter's `DbType` | back to the type inferred from the value (MySqlConnector 2.6.2, checked 2026-10-23) |
| `fakeDbFactory.CreateDataSource` | .NET's default data source | .NET's default data source; `SupportsNativeDataSource = true` returns a `FakeDbDataSource` | providers without their own data source return .NET's default |
| A column holding a `fakeDbInterval` (new) | — | reports `TimeSpan` and data type `Interval`; `GetValue` converts as DuckDB.NET 1.5.6 does (throws for negatives and months ≥ 1, drops negative months); `GetProviderSpecificValue` returns the stored parts | DuckDB.NET 1.5.6 on `INTERVAL` |
| `OffsetDroppedByGetValueColumns` (new) | — | the column reports `DateTime` and data type `TimeStampTZ`; `GetValue` returns the wall time with no offset, `GetDateTimeOffset` the value | ODP.NET 3.21 on `TIMESTAMP WITH TIME ZONE` |
| `GetProviderSpecificValue` / `GetProviderSpecificFieldType` | .NET's default (`GetValue`) | the stored value for a provider-specific type (`fakeDbInterval`), otherwise unchanged | each provider's own value type |
| A new parameter's `Direction` and `Value` | `0` (no defined direction) and `DBNull.Value` | `Input` and `null` | the documented `DbParameter` defaults |
| A new command's `CommandType` | `0` | `Text` | the documented `DbCommand` default |
| `CommandText = null` | read back `null` | reads back `""` | SqlClient, Npgsql, Microsoft.Data.Sqlite |
| Execute or `Prepare` with no connection, or a closed one | ran | throws `InvalidOperationException` | every provider |
| A command whose reader is still open | could change its text or connection and run again | throws `InvalidOperationException` | every provider |
| A command whose `Transaction` belongs to another connection | ran | throws `InvalidOperationException` | every provider |
| `CommandBehavior.CloseConnection` | ignored | closing the reader closes the connection | documented |
| Async execute with a canceled token, or one that fails | threw from the call | returns a canceled or faulted task | .NET's `DbCommand` |
| `ExecuteNonQuery` of a SELECT | 1 (or the factory's default non-query result) | -1; scripted results still come first | documented |
| Setting `ConnectionString` on an open connection | accepted | throws `InvalidOperationException` | SqlClient, Npgsql, Microsoft.Data.Sqlite |
| `DbProviderFactories.GetFactory(connection)` | `null` | the factory that created it (`fakeDbFactory.Instance` for one constructed directly) | each provider's factory |
| `GetSchema()` | returned `DataSourceInformation` | returns `MetaDataCollections`; `GetSchema("DataSourceInformation")` is unchanged; an unknown collection throws `ArgumentException` | documented |
| A committed, rolled back or disposed transaction | kept its `Connection` and could complete again | `Connection` is `null`; another `Commit`/`Rollback` throws `InvalidOperationException` | every provider |
| `Release`/`ReleaseAsync` of a savepoint | did nothing | throws `NotSupportedException`, as `Save` and `Rollback(name)` already did (`SupportsSavepoints` is false) | `DbTransaction.SupportsSavepoints` |

New in fakeDb since the table above (each is pinned by tests in `pengdows.crud.Tests`):

- Factory-level canned results: `EnqueueNonQueryResult`, `EnqueueScalarResult`, `SetNonQueryResultForCommand`,
  `SetScalarResultForCommand` and `SetDefaultNonQueryResult` reach every connection the factory creates; a connection's own
  queue is consulted first. A context opens its own connections, so this is how a test scripts them.
- A unique-key model: `fakeDbFactory.DataStore.DefineUniqueKey("table", "col")` makes a duplicate INSERT raise the violation
  the emulated product's provider raises, so a dialect's classification, the `UniqueConstraintViolationException`
  translation and the upsert retry run end to end. `INSERT ... ON CONFLICT` and `ON DUPLICATE KEY UPDATE` update the
  existing row (atomically, as the real statements do). `MERGE` is not modelled: the MERGE-versus-concurrent-insert race is
  shown only by `ConcurrentUpsertTests` against real databases.
- `fakeDbFactory.CreateConnection` is thread-safe. The 200-worker, pool-of-40 boundary now runs on fakeDb with execute
  gates (`FakeDbPoolBoundaryStressTests`): 40 commands held in flight, every other caller refused with
  `PoolSaturatedException`, peak connections never above 40, recovery when the gates open. A real server's own
  connection limit, process kill and network loss remain integration-only.
- An ordered `Operations` log on connection, command and transaction, `SchemaException`, `ChangeDatabase` (allowed in
  fakeDb; pengdows.crud itself refuses it), `OverstatedLengthColumns` (a provider that reports more bytes than it
  delivers), `SetFailOnDispose` (a connection whose cleanup throws), `fakeDbDataReader.FailOnClose` (a reader whose close throws), `ConnectionInitializer` (scripts every connection the factory creates, however many a context opens), and positional `?` markers in the data store.
- With `EnableDataPersistence`, the data store answers SELECTs that read no table the way a database does: literals,
  `NULL` and bound parameters, `UNION`, `ORDER BY` a column number, comments, and one result set per statement in a
  batch. A parameter value no database type can hold throws `NotSupportedException`. In `StrictMode`, text that is not
  SQL at all raises the emulated product's syntax error (a `DbException`); SQL the store does not model still throws
  `NotSupportedException`.
- fakeDb passes the ADO.NET conformance suite (`pengdows.AdoNetApiTest.conformance` 3.0.0-beta.1), run by
  `pengdows.crud.fakeDb.Conformance.Tests`: every Required, Optional and Common test, on net8.0 (207) and net10.0 (221).
- Conformant is the default; `fakeDbDriverQuirks` reproduces where real drivers depart from the contract, so code that
  must survive a driver's defect can be tested against it. Each departure is a switch (`PrepareWithoutOpenConnection`,
  `CompletedTransactionKeepsConnection`, `SavepointReleaseIsNoOp`, `NonQueryResultForSelect`, ...), set on
  `fakeDbFactory.DriverQuirks` or one connection's `DriverQuirks`. Presets start from a driver version as the conformance
  suite recorded it on 2026-10-23: `SqlClient()` (Microsoft.Data.SqlClient 6.0.2), `Npgsql()` (9.0.3),
  `MySqlConnector()` (2.6.2), `MySqlData()` (9.7.0), `DuckDbNet()` (1.5.6.1), `OracleManagedDataAccess()` (23.8),
  `SystemDataSqlite()` (1.0.119), `AseClient()` (0.19.2), `Db2()` (8.0.0.500). Run through the conformance suite, a
  preset fails no test its driver passes; it does not yet reproduce every test its driver fails.

## Performance

Measured 2026-10-18 on one machine (AMD Ryzen 9 5950X, .NET 8.0.31, Testcontainers databases), after the audit fixes. Cells swing
a few percent from run to run, so read these as "no measurable difference", not as exact figures.

**Single-row upsert** (`PostgreSqlUpsertBenchmarks`, `SqlServerUpsertBenchmarks`; each arm opens its own pooled connection per
call; a setup check requires the arms to produce the same row):

| Database | Arm | Update (existing key) | Insert (new key) | Allocated |
|---|---|---|---|---|
| PostgreSQL 16 | pengdows `UpsertAsync` (`INSERT ... ON CONFLICT`) | 312.4 us | 307.0 us | 5.38 KB |
| PostgreSQL 16 | hand-written `ON CONFLICT` | 312.2 us | 295.6 us | 3.65 KB |
| PostgreSQL 16 | hand-written 2.0.5 `MERGE` | 314.1 us | 301.2 us | 3.89 KB |
| SQL Server 2022 | pengdows `UpsertAsync` (`MERGE ... WITH (HOLDLOCK)`) | 460.8 us | 466.1 us | 8.2 KB |
| SQL Server 2022 | hand-written `MERGE` with `HOLDLOCK` | 443.4 us | 444.6 us | 5.64 KB |
| SQL Server 2022 | hand-written 2.0.5 `MERGE`, no hint | 445.3 us | 452.0 us | 5.64 KB |

The statement-shape change that makes a single-row upsert atomic costs nothing measurable when there is no contention: on
PostgreSQL `ON CONFLICT` and the old `MERGE` are within about 1 to 2% of each other, and on SQL Server `HOLDLOCK` is within 0.5% (update)
and 1.6% (insert) of the unhinted `MERGE`. pengdows is 0 to 4% over the hand-written statement, with the extra allocation being
the gateway building the statement per call.

**CRUD against Dapper and EF** (`PostgreSqlEqualFootingBenchmarks`, `SqlServerEqualFootingBenchmarks`, 1, 10 and 100 records):

| Database | pengdows / Dapper (mean) | EF / pengdows |
|---|---|---|
| PostgreSQL 16 (2026-10-19) | 0.940 to 1.067 | 1.20 to 1.70 |
| SQL Server 2022 | 0.984 to 1.060 | 1.21 to 1.69 |

pengdows is at parity with Dapper on both databases, within run-to-run noise. EF is 20 to 70% slower than pengdows everywhere.
The PostgreSQL row is the 2026-10-19 rerun: until then that benchmark's Dapper arms left two of the six columns unmapped (Dapper's
`MatchNamesWithUnderscores` was never set), so earlier PostgreSQL read ratios flattered Dapper. With every column mapped, ReadList at
100 rows is 0.996 (1.061 before).

**Large result sets** (`PostgreSqlRowScalingBenchmarks`, `SqlServerRowScalingBenchmarks`, 1 to 10,000 rows; full tables in
`benchmarks/CrudBenchmarks/results/row-scaling-2026-10-19.md`): up to 1,000 rows pengdows and Dapper are level (within about 4%). From
1,000 to 10,000 rows each extra row costs Dapper 135 to 145 ns more, and pengdows allocates about 213 bytes per row against Dapper's 284 to
309 and EF's 404 to 531. A 10,000-row query takes pengdows 0.77 to 0.86 of Dapper's time and 25 to 31% less memory, with up to half
the Gen2 collections.

**A connection storm with reads and writes** (`PostgreSqlMixedGovernanceBenchmarks`, PostgreSQL with 22 usable connections, 1,500 calls
of 300 ms from 50 or 1,000 callers; `benchmarks/CrudBenchmarks/results/postgres-mixed-storm-run-2026-10-19.md`): Dapper and EF on the
provider's default pool lost 95 to 97% of calls to server refusals, at 50 callers as at 1,000. Behind StormGate (20 permits) they lost
none, and so did pengdows with `ClampPoolsToServerConnectionLimit`; pengdows with 20 reader and 20 writer slots and no clamp (40
connections possible against 22) lost 6 to 50%. The clamp's static split finished a half-read mix 7 to 8% sooner than StormGate and a 90%-read
mix 64% later (P99 27.4 s against 15.1 s at 1,000 callers), the cost described in [connection-pooling.md](connection-pooling.md).

**Synchronous callers parked behind an asynchronous slot holder** (real PostgreSQL, `PostgreSqlSyncWaiterStarvationTests`): the
holder's release is delayed by about 0.9 to 1.0 s once synchronous callers are parked, regardless of how many (64 to 1,000); see
[connection-pooling.md](connection-pooling.md).

## Tests, review policy and provenance

- ADO.NET provider behaviour is never mocked: connections, commands, readers, transactions, parameters, factories, data
  sources and their failures belong to fakeDb. Mocking the library's own interfaces is fine (Moq). `NoMockedProviderTypesTests`
  enforces it. See `REVIEW_POLICY.md` for the amendments: capability skips only, a concurrent same-key test for any upsert or
  merge change, a stated reason for every empty or comment-only catch, braced control flow.
- `docs/facts.json` records, for each database and framework, the last run's counts, time, commit and whether the tree was
  clean, next to `docs/integration-status.md`.
- Considered and not done in 2.0.6: virtual time for pool acquire timeouts (the waiter queue waits on OS timers; real, short
  timeouts pin the behaviour) and one registry for every coercion rule (the Guid and date helpers are thin and have different
  failure semantics; 3.0). Both are recorded in `docs/FUTURE_WORK.md`.
