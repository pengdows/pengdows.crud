# Generated Key Retrieval

"How does `CreateAsync` get my auto-generated `[Id]` value back after INSERT?" has a different answer per database. `GeneratedKeyPlan` (`pengdows.crud.abstractions/enums/GeneratedKeyPlan.cs`) is the enum that names each strategy; `SqlDialect.GetGeneratedKeyPlan()` picks one per dialect. Neither has been documented anywhere before this — this doc exists so that question has one place to be answered instead of requiring a read of `TableGateway.Core.cs`.

## The strategies, in preference order

| Plan | How it works | Round trips | Used when |
|---|---|---|---|
| `Returning` | Inline `INSERT ... RETURNING id` | 1, atomic | PostgreSQL, Firebird, DuckDB, SQLite 3.35+, Db2 (`FROM FINAL TABLE`) |
| `OutputInserted` | Inline `INSERT ... OUTPUT INSERTED.id` | 1, atomic | SQL Server |
| `SessionScopedFunction` | The INSERT, then a session-scoped last-id query as a separate statement on the same pinned connection | 2 | Informix (`DBINFO`), SAP HANA (`CURRENT_IDENTITY_VALUE()`), Access (`@@IDENTITY`): drivers that reject a compound statement and expose no id |
| `PrefetchSequence` | `SELECT seq.NEXTVAL` before the INSERT, then insert the already-known value | 2, but ID is known before the write | InterBase (`CREATE GENERATOR` + `GEN_ID(name, 1)`), FlatFile (`VALUES (NEXT VALUE FOR "<table>_seq")`); see the Oracle note below for why Oracle itself doesn't. The prefetch runs on a write connection: advancing a sequence is a write |
| `CorrelationToken` | Add a unique token value to the INSERT, then `SELECT` the row back by that token | 2 | Universal fallback — works on any database with a uniqueness guarantee on the token column |
| `NaturalKeyLookup` | Look up the just-inserted row by its natural key columns within the same transaction | 2 | Last resort; requires unique constraints on the lookup columns and explicit opt-in due to race-condition risk |
| `CompoundStatement` | `INSERT ...; SELECT LAST_INSERT_ID()` as one multi-statement batch | 1 (batched) | MySql.Data, SQLite before 3.35, Sybase ASE. Saves `SessionScopedFunction`'s second round trip; requires multi-statement support on the connection. |
| `ReaderInsertedId` | Execute the INSERT as a reader and read the generated key off a provider-specific `DbDataReader` property (e.g. `MySqlDataReader.LastInsertedId`), populated from the database's own OK packet | 1 | MySqlConnector, which deliberately does not support `AllowMultipleStatements` |
| `None` | No retrieval strategy | — | Database doesn't support auto-generated keys, or the dialect hasn't configured one |

## The id query always runs on the INSERT's connection

A session-scoped last-id function (`DBINFO`, `CURRENT_IDENTITY_VALUE()`, `@@IDENTITY`,
`LAST_INSERT_ID()`, ...) reports the id only on the connection that ran the INSERT. pengdows
normally returns a connection to the pool after each command, so running the id query as a second
command could land on another connection and read 0, a pooled connection's stale value, or another
session's id. Since 2.0.6 (GEN-001, CORE-016) the gateway holds one connection for both statements:

- Outside a transaction, `CreateAsync` pins one write connection (with its pool slot, and the write
  permit under `SingleWriter`) for the INSERT and the id query, and releases it exactly once, also
  when the INSERT throws. No transaction is opened.
- Inside a caller's transaction, the transaction's connection is used as-is.
- Under `SingleConnection`, the shared connection is used and stays open.

The same pin covers the fallback id query of `Returning`/`OutputInserted`, `CompoundStatement` and
`ReaderInsertedId`, which runs when the one-round-trip read yields no id.
`CompoundStatement` and `ReaderInsertedId` still exist because they save that second round trip.

## A two-round-trip plan's ID-retrieval failure does not falsify the write

`CorrelationToken` (and any other 2-round-trip plan) makes an INSERT and its ID lookup two distinct operations, which means they can fail independently — and `TableGateway.Core.cs`'s `CreateAsync` deliberately tracks this: it sets a local `writeSucceeded` flag to `true` only once the INSERT itself is confirmed to have affected exactly one row, *before* attempting the second round trip. If that second round trip (the token-keyed `SELECT`) then throws — a transient failure, a dropped connection, whatever — the exception propagates without calling `RestoreAuditFields`. This is intentional, not a missed catch: the row genuinely did get created, so restoring the entity's audit fields (`CreatedBy`/`CreatedOn`/`LastUpdatedBy`/`LastUpdatedOn`) as though the operation failed would misrepresent a row that actually exists in the database. Compare this to every other failure path in `CreateAsync`, which *does* restore audit fields via `RestoreAuditFieldsIfFailed` — those paths cover failures that happen before or during the write itself, where "the write didn't happen" is the correct conclusion. A caller catching an exception from `CreateAsync` against a `CorrelationToken`-plan dialect should not assume the row wasn't created; the entity's `[Id]` may simply be unpopulated even though the insert succeeded.

## Per-dialect assignment

`SqlDialect.GetGeneratedKeyPlan()`'s base (virtual) logic: if `DatabaseType == Oracle`, return `PrefetchSequence`; otherwise, if the dialect supports inline RETURNING/OUTPUT, use `OutputInserted` for SQL Server and `Returning` for everything else; otherwise fall back to `SessionScopedFunction` if a safe session-scoped function exists; otherwise `CorrelationToken`. Informix, SAP HANA and Access select `SessionScopedFunction` by explicit override. Each dialect below either uses that base logic as-is or overrides it explicitly — note that `OracleDialect` itself overrides the method and returns `Returning`, so the base class's Oracle branch above is currently dead for the shipped dialect (see the Oracle row below):

| Database | Plan | Notes |
|---|---|---|
| Oracle | `Returning` | `OracleDialect.GetGeneratedKeyPlan()` explicitly overrides the base class and returns `Returning`, not the `PrefetchSequence` the base `SqlDialect` special-cases for Oracle. A doc comment on the neighboring `RenderInsertReturningClause` method previously claimed the opposite; confirmed via git history (both the override and the wrong comment were added in the same commit, and a later commit built further capability on `Returning`) that `Returning` is the deliberate, working design, and corrected the comment accordingly. Because `RequiresOutputParameterForReturning => true`, Oracle's `RETURNING id INTO :1` binds through an ADO.NET OUT parameter rather than a result set, so the gateway uses `ExecuteNonQueryAsync` + `GetParameterValue` here instead of the `ExecuteScalarOrNullAsync` path other `Returning` dialects use. |
| SQL Server | `OutputInserted` | Base logic |
| PostgreSQL, CockroachDB, YugabyteDB, DuckDB (3.35+) | `Returning` | Base logic |
| Firebird | `Returning` | Explicit override (matches base logic) |
| Db2 | `Returning` | Explicit override — actually emitted as `SELECT ... FROM FINAL TABLE (INSERT ...)`, wrapping the whole INSERT rather than a trailing clause |
| SQLite (<3.35) | `CompoundStatement` | Explicit override when `SupportsInsertReturning` is false |
| MariaDB | `ReaderInsertedId` | Explicit override — always, regardless of driver |
| MySQL | `ReaderInsertedId` if using MySqlConnector, else `CompoundStatement` | Explicit override, driver-dependent — the only dialect where the ADO.NET driver in use, not just the database engine, changes the generated-key strategy |
| SAP HANA | `SessionScopedFunction` | Explicit override (2.0.6 and 3.0). `SELECT CURRENT_IDENTITY_VALUE() FROM DUMMY`, confirmed live to return the new id right after the INSERT on the same connection, now runs on the pinned connection. `CompoundStatement` is rejected live (no multi-statement commands). HANA is opt-in in the integration suite (16-32 GB image), so the pinned path is unit-tested and has not been run live |
| Informix | `SessionScopedFunction` | Explicit override (2.0.6 and 3.0). `SELECT CASE WHEN DBINFO('bigserial') <> 0 THEN DBINFO('bigserial') WHEN DBINFO('serial8') <> 0 THEN DBINFO('serial8') ELSE DBINFO('sqlca.sqlerrd1') END FROM systables WHERE tabid = 1` returns the id for SERIAL, SERIAL8 and BIGSERIAL (each `DBINFO` form reports only its own type). Compound statements are rejected by Informix.Net.Core. Verified live on the pinned connection |
| FlatFile | `PrefetchSequence` | Explicit override (2.0.6 and 3.0). FlatFile has no IDENTITY, RETURNING or last-id function but supports ISO sequences, so the id is fetched first with `VALUES (NEXT VALUE FOR "<table>_seq")` and sent in the INSERT. The sequence must exist, named `{tableName}_seq` like InterBase's generator. Verified live against pengdows.flatfile 0.2.1-preview.2 |
| Snowflake | `CorrelationToken` | Base logic. Snowflake has no last-id mechanism; without a `[CorrelationToken]` column a database-generated id is left unset (DEC-007 in `docs/FUTURE_WORK.md`). Use a correlation column or a client-generated id |
| InterBase | `PrefetchSequence` | Explicit override — `InterBaseDialect.GetGeneratedKeyPlan()`. InterBase supports none of IDENTITY columns, `CREATE SEQUENCE`, or `INSERT ... RETURNING` (all confirmed rejected live against a real InterBase 15 server), so the base logic's fall-through chain would otherwise land on `CorrelationToken`. Its real, working mechanism is the classic InterBase 6 `CREATE GENERATOR name` + `GEN_ID(name, 1)` pair — confirmed live to return the expected sequential value — consumed via `GetSequenceNextValQuery`. The generator must already exist, named `{tableName}_seq` per `TableGateway.Core.cs`'s fixed `GetSequenceName()` convention; the testbed's DDL is responsible for creating it. |
| Access | `SessionScopedFunction` | Explicit override (2.0.6 and 3.0). `SELECT @@IDENTITY` returns the new id right after the INSERT on the same connection (confirmed live earlier), and now runs on the pinned connection. `CompoundStatement` is rejected (OleDb: "Characters found after end of SQL statement."). Access is Windows-only and can't run in this project's Linux integration suite, so the pinned path is unit-tested only |

Databases not listed fall through to the base logic (RETURNING if supported, else session-scoped function, else correlation token).

## Related docs
- `docs/architecture.md` — connection lifecycle and lease model this strategy selection sits on top of.
- The wiki's Database-Specific Gotchas page for the driver-choice-changes-strategy MySQL/MariaDB detail in context.

This document covers single-entity `CreateAsync`/`BuildCreate`. Batch create (`docs/batch-operations.md`) does not use `GeneratedKeyPlan` for per-row ID retrieval the same way — that document doesn't currently describe batch-specific generated-key handling either, which is a separate gap from this one.
