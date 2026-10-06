# Why UTC everywhere

pengdows.crud stores and reads every timestamp as a UTC instant: entity values, audit fields,
parameters in your own SQL, on every database. This is a correctness choice, not a convention. A
timestamp's truth is the instant it names. UTC keeps that truth intact even when the database
underneath has no idea what a time zone is.

## The problem UTC solves

Most databases store a timestamp as a bare wall-clock value: `2026-10-05 13:45:30`, with no zone and
no offset (MySQL `DATETIME`, SQL Server `datetime2`, Db2/Informix/Firebird/SQLite/Sybase timestamps,
and plain `TIMESTAMP` almost everywhere). The column cannot say which 13:45 it means. Something
outside the column has to decide, and if that something is "the server's time zone", "the client
machine's time zone" or "the session's time zone", the same row means different instants depending
on where and when it is read:

- an application server in Chicago and one in Frankfurt write the same moment as two different
  wall-clock values;
- a server whose time zone or daylight-saving rule changes silently shifts every stored value;
- an hour repeats every autumn: a local wall-clock value in that hour names two instants.

Writing UTC removes the choice. A UTC wall-clock value names exactly one instant, on every server,
in every season, whether or not the database knows anything about zones. Ordering, comparison,
"what happened first" and "how long between" all stay correct.

## What the library does

| Value | Written as | Read back as |
|---|---|---|
| `DateTime` with `DateTimeKind.Utc` | that instant | `DateTimeKind.Utc` |
| `DateTime` with `DateTimeKind.Local` | converted to UTC | `DateTimeKind.Utc` |
| `DateTime` with `DateTimeKind.Unspecified` | treated as UTC (never as the host's local time) | `DateTimeKind.Utc` |
| `DateTimeOffset` | its instant, as UTC, where the column can't hold an offset; as-is where it can | see below |
| Timestamp text with an offset (`...+02:00`, `...Z`) | its instant | |
| Timestamp text without an offset | UTC | |
| Audit fields (`[CreatedOn]`, `[LastUpdatedOn]`) | always UTC: `DateTime.UtcNow`, or the resolver's value, which must be UTC | |

**Unspecified means UTC, on every path.** Drivers return `DateTimeKind.Unspecified` for zone-less
columns (SQL Server `datetime2`, MySQL, Oracle, Snowflake `TIMESTAMP_NTZ`, ...). If that value were
taken as local time, calling `.ToUniversalTime()` on it would shift it by the host's offset: a six-hour
error on a server in UTC-6. The gateway, `DataReaderMapper`, scalar reads and the update dirty check
all read an unspecified value as UTC, and write one the same way.

**Text without an offset is UTC too.** SQLite and FlatFile store timestamps as text. Text with an
offset keeps its instant; text without one is read as UTC, as an unspecified `DateTime` is
elsewhere. Earlier releases read it as the host's local time, so the same row read differently on
differently configured machines. Text holding only a time of day (`"13:45:30"`) is not a timestamp,
and reading it into a date type fails rather than borrowing today's date.

## Audit timestamps are UTC by design

Audit fields exist to answer "when did this happen", across every server that ever touched the row.
A local-time audit trail can't be ordered reliably across machines or across a daylight-saving
change, and can't be compared with logs or other systems. So audit timestamps are always UTC:

- without a resolver, `[CreatedOn]`/`[LastUpdatedOn]` are `DateTime.UtcNow`;
- with a resolver, `IAuditValues.UtcNow` is UTC by contract, and a `TimestampOffset` whose offset
  isn't exactly zero is refused with `InvalidOperationException`: a resolver can't slip a local time
  into the trail;
- every write path (create, update, upsert, single or batch) stamps them the same way.

See [audit-fields.md](audit-fields.md).

## `DateTimeOffset`: the instant is kept everywhere; the offset only where the column holds one

A `DateTimeOffset` carries two things: an instant, and the offset it was written with.

- **The instant is kept on every database**, exactly (Sybase ASE keeps microseconds; see the matrix).
  On a database or column with no offset-aware type, the value is stored as its UTC wall-clock time:
  `2026-10-05 13:45:30+02:00` is stored as `11:45:30` and reads back as `2026-10-05 11:45:30+00:00`,
  the same instant.
- **The original offset is kept only where the column type can hold one.** SQL Server's
  `DATETIMEOFFSET` does. PostgreSQL's `timestamptz` does not, despite its name: it stores the UTC
  instant and displays it in the session's zone. Oracle (`TIMESTAMP WITH TIME ZONE`) and Snowflake
  (`TIMESTAMP_TZ`) could, but are sent the UTC instant: a parameter can't tell which column it is
  going to, and a value sent with its offset into a plain `TIMESTAMP` column stores its local wall
  time there (confirmed live on Oracle), so the instant is the only safe thing to send. Once a value
  is in a column without an offset, the offset is gone and nothing can recover it.

**Declare the column you have.** Declare `DbType.DateTimeOffset` only on a column that holds an
offset or an instant (`DATETIMEOFFSET`, `timestamptz`, `TIMESTAMP WITH TIME ZONE`, ...). For a
`DateTimeOffset` property on a plain timestamp column, declare `DbType.DateTime2` (or `DateTime`):
the value is then stored as its UTC wall time on every database. SQL Server takes the declaration at
its word: a `DateTimeOffset` declared `DbType.DateTimeOffset` but stored in a `DATETIME2` column keeps
its local wall time, so `13:45+02:00` lands as `13:45`. Before 2.0.6, a `DateTimeOffset` declared
`DateTime`/`DateTime2` was stored that way too on SQL Server, Firebird, Db2, DuckDB, FlatFile,
Sybase ASE, Informix, InterBase and Access; rows written then hold the wall time.

The same applies to a `DateTime` or timestamp text bound as `DbType.DateTimeOffset`: it is stored as
the `DateTimeOffset` of its instant would be, never as NULL and never with the server's local offset.

[type-support-matrix.md](type-support-matrix.md) lists, per database, which types round-trip
exactly and which keep only the instant.

### If you need the offset or the zone

Store it yourself, explicitly:

- **The offset**: a second column (`offset_minutes SMALLINT`), or the value as ISO 8601 text with its
  offset in a string column.
- **The time zone**: an offset is not a zone. `+02:00` doesn't say whether it was Paris in summer or
  Cairo, so it can't recreate local time under a later daylight-saving rule. If local time matters
  (a meeting "at 9:00 in Paris"), store the IANA zone name (`Europe/Paris`) alongside the UTC instant
  and convert when displaying.

The instant stays UTC either way: that's the part every query, index and comparison relies on.

## Fractional seconds are truncated, never rounded

When a column holds fewer fractional digits than .NET's seven, the extra digits are dropped, not
rounded. Rounding up can move a value across a second, a day or a year boundary (23:59:59.9999999
rounded to `TIME(0)` becomes 24:00:00, which isn't a valid time). MySQL 8.0.8+ sessions set
`TIME_TRUNCATE_FRACTIONAL` for this; MariaDB, PostgreSQL, Firebird, Db2, DuckDB, Informix and
SQL Server's `DATETIME2`/`TIME` truncate on their own (checked live with a value one tick before
midnight). Still rounding today: SQL Server `DATETIME`/`SMALLDATETIME`, Sybase ASE `SMALLDATETIME`,
Oracle `TIMESTAMP WITH LOCAL TIME ZONE` and a `TimeSpan` into an Oracle `INTERVAL DAY TO SECOND(6)`
(DRY-028); TiDB (and MySQL before 8.0.8) can't truncate either. Send values at the column's
precision there.
