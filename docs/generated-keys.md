# Generated Key Retrieval

"How does `CreateAsync` get my auto-generated `[Id]` value back after INSERT?" has a different answer per database. `GeneratedKeyPlan` (`pengdows.crud.abstractions/enums/GeneratedKeyPlan.cs`) is the enum that names each strategy; `SqlDialect.GetGeneratedKeyPlan()` picks one per dialect. Neither has been documented anywhere before this — this doc exists so that question has one place to be answered instead of requiring a read of `TableGateway.Core.cs`.

## The strategies, in preference order

| Plan | How it works | Round trips | Used when |
|---|---|---|---|
| `Returning` | Inline `INSERT ... RETURNING id` | 1, atomic | PostgreSQL, Firebird, DuckDB, SQLite 3.35+, Db2 (`FROM FINAL TABLE`) |
| `OutputInserted` | Inline `INSERT ... OUTPUT INSERTED.id` | 1, atomic | SQL Server |
| `SessionScopedFunction` | `SELECT LAST_INSERT_ID()` / `last_insert_rowid()` / `SCOPE_IDENTITY()` as a separate statement, same connection | 2 | Safe only when guaranteed to run on the exact same physical connection immediately after the INSERT |
| `PrefetchSequence` | `SELECT seq.NEXTVAL` before the INSERT, then insert the already-known value | 2, but ID is known before the write | Not currently returned by any shipped dialect (see the Oracle note below) — `TableGateway.Core.cs` has two live branches for it, so the plumbing is real, just currently unreachable |
| `CorrelationToken` | Add a unique token value to the INSERT, then `SELECT` the row back by that token | 2 | Universal fallback — works on any database with a uniqueness guarantee on the token column |
| `NaturalKeyLookup` | Look up the just-inserted row by its natural key columns within the same transaction | 2 | Last resort; requires unique constraints on the lookup columns and explicit opt-in due to race-condition risk |
| `CompoundStatement` | `INSERT ...; SELECT LAST_INSERT_ID()` as one multi-statement batch | 1 (batched) | Fixes `SessionScopedFunction`'s two-lease hazard — see below. Requires multi-statement support enabled on the connection. |
| `ReaderInsertedId` | Execute the INSERT as a reader and read the generated key off a provider-specific `DbDataReader` property (e.g. `MySqlDataReader.LastInsertedId`), populated from the database's own OK packet | 1 | MySqlConnector, which deliberately does not support `AllowMultipleStatements` |
| `None` | No retrieval strategy | — | Database doesn't support auto-generated keys, or the dialect hasn't configured one |

## Why `SessionScopedFunction` needs a fix at all

`SessionScopedFunction` is only safe when the INSERT and the follow-up `SELECT LAST_INSERT_ID()`-style call land on the *same physical connection*. With connection pooling in play, issuing them as two separate commands risks the pool handing back a different physical connection for the second call — silently returning the wrong (or no) generated ID. `CompoundStatement` and `ReaderInsertedId` both exist specifically to close this hazard for MySQL/MariaDB/pre-3.35 SQLite, by keeping the INSERT and the ID retrieval in a single round trip instead of two.

## Per-dialect assignment

`SqlDialect.GetGeneratedKeyPlan()`'s base (virtual) logic: if `DatabaseType == Oracle`, return `PrefetchSequence`; otherwise, if the dialect supports inline RETURNING/OUTPUT, use `OutputInserted` for SQL Server and `Returning` for everything else; otherwise fall back to `SessionScopedFunction` if a safe session-scoped function exists; otherwise `CorrelationToken`. Each dialect below either uses that base logic as-is or overrides it explicitly — note that `OracleDialect` itself overrides the method and returns `Returning`, so the base class's Oracle branch above is currently dead for the shipped dialect (see the Oracle row below):

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

Databases not listed fall through to the base logic (RETURNING if supported, else session-scoped function, else correlation token).

## Related docs
- `docs/architecture.md` — connection lifecycle and lease model this strategy selection sits on top of.
- The wiki's Database-Specific Gotchas page for the driver-choice-changes-strategy MySQL/MariaDB detail in context.

This document covers single-entity `CreateAsync`/`BuildCreate`. Batch create (`docs/batch-operations.md`) does not use `GeneratedKeyPlan` for per-row ID retrieval the same way — that document doesn't currently describe batch-specific generated-key handling either, which is a separate gap from this one.
