# Bulk and High-Volume Write Paths — Design (FEAT-005, FEAT-012, FEAT-013)

**Status: designed, zero implementation.** Nothing described in this document exists in code yet.
This covers three related gaps in one place because they're really one question answered three
ways per-provider: how does pengdows.crud move *many* rows to the server faster than the existing
per-entity/multi-row-`VALUES` batch path (`docs/batch-operations.md`)?

| ID | Gap | Tracking status before this document |
|---|---|---|
| `FEAT-005` | Oracle array binding | Already tracked in `future-work.md`, one line, "not started — design work needed." Expanded here. |
| `FEAT-012` | Provider-native bulk loading (`SqlBulkCopy`/`COPY`/`MySqlBulkCopy`/DuckDB Appender) | **Not tracked at all before this document.** `docs/batch-operations.md`'s own "What Is Not Implemented" section already names this explicitly, but no `future-work.md` row existed. New. |
| `FEAT-013` | Batch upsert via a single multi-row `MERGE` (SQL Server/Oracle/Firebird) | **Not tracked at all before this document.** Mentioned only as an aside in `FEAT-006`'s closing note ("batch *upsert* via MERGE still doesn't exist for any dialect... a separate, wider gap"). New. |

Every technical claim about a third-party provider API below was verified by loading the exact
package version this repo already depends on and reflecting over its public surface — not assumed
from memory or documentation. Each claim names the version checked.

## Part 1: Provider-native bulk loading (FEAT-012)

### The gap

`docs/batch-operations.md`'s batch API caps out at one multi-row `INSERT`/`MERGE` statement (or a
sequence of per-entity containers where multi-row isn't supported). For genuinely large loads
(tens of thousands of rows and up), every mainstream provider ships a purpose-built bulk-loading
mechanism that bypasses per-row SQL text and parameter binding entirely. None of them is reachable
through pengdows.crud today.

### Per-provider mechanics (verified against the exact packages this repo depends on)

| Provider family | Mechanism | Verified against | Shape |
|---|---|---|---|
| SQL Server | `Microsoft.Data.SqlClient.SqlBulkCopy` | `Microsoft.Data.SqlClient` 6.0.2 (already pinned in `pengdows.crud.Tests`/`testbed`) | `new SqlBulkCopy(SqlConnection, SqlBulkCopyOptions, SqlTransaction)` — **accepts an existing open transaction**, so it can participate in an already-open `ITransactionContext`. `WriteToServerAsync(DbDataReader)` is the natural fit for streaming entities without materializing a `DataTable`. |
| PostgreSQL / CockroachDB / YugabyteDB (Npgsql-based) | `Npgsql.NpgsqlBinaryImporter`, obtained via `connection.BeginBinaryImport("COPY table (cols) FROM STDIN (FORMAT BINARY)")` | `Npgsql` 10.0.3 | Genuinely row-at-a-time streaming: `StartRow()`, `Write<T>(value)` per column, `Complete()`/`CompleteAsync()`. No `DataTable` needed at all — the best fit for an `IAsyncEnumerable<TEntity>`-shaped API. |
| MySQL / MariaDB / TiDB (MySqlConnector-based) | `MySqlConnector.MySqlBulkCopy` | `MySqlConnector` 2.6.2 (already pinned in `testbed`) | `new MySqlBulkCopy(MySqlConnection, MySqlTransaction)` — also accepts an existing transaction. `WriteToServerAsync(IDataReader, ...)` mirrors `SqlBulkCopy`'s shape closely. |
| Oracle | **No `BulkCopy`-shaped type exists.** ODP.NET's efficient multi-row mechanism is array-bound parameters (`OracleCommand.ArrayBindCount` + array-valued `OracleParameter.Value`), not a streaming-importer object. | N/A — this is `FEAT-005`; see Part 2. Oracle's "bulk loading" answer and its "array binding" answer are the same mechanism, not two separate features. | |
| DuckDB | `DuckDB.NET.Data.DuckDBAppender`, obtained via `connection.CreateAppender(tableName)` | `DuckDB.NET.Data.Full` 1.5.5 (repo currently pins 1.3.2; 1.5.x confirmed to carry the same `Appender` shape) | `CreateRow()` returns an `IDuckDBAppenderRow` with a fluent `AppendValue(...)` chain per column, `EndRow()`; or the callback form `AppendRow(Action<IDuckDBAppenderRow>)`. Also genuinely row-streaming, closer to Npgsql's shape than SqlClient's. |
| SQLite | No native bulk-copy client API. | — | A single transaction wrapping many prepared-statement `INSERT`s is already close to optimal for SQLite specifically — this is the same insight `DbMode.SingleWriter`'s turnstile is already built around. **Recommendation: don't build a special path here** — dispatch to the existing batch-insert path unchanged. |
| Firebird | No native bulk-copy client API either. | — | Same recommendation as SQLite: dispatch to the existing batch/per-entity path. |

### The public-API-vs-governance tension, and how it's already solved

Every one of the native mechanisms above needs the **real, provider-specific connection object**
(`SqlConnection`, `NpgsqlConnection`, `MySqlConnection`, the DuckDB connection) — not an abstract
`DbConnection`, and definitely not pengdows.crud's own `ITrackedConnection` wrapper. This looks like
it conflicts with a real, deliberate architectural decision:
`docs/positioning/implementation-evidence.md`'s "DataSource removal history" section documents that
`IDatabaseContext.DataSource` was **removed entirely** (not just made `internal`) specifically
because any public path to a raw provider connection lets a caller bypass governor accounting,
session settings, and disposal tracking.

The resolution doesn't require reopening that decision. `TrackedConnection` (verified:
`pengdows.crud/wrappers/TrackedConnection.cs`) already exposes the real underlying `DbConnection`
through `internal interface IInternalConnectionWrapper { DbConnection UnderlyingConnection { get; } }`
— an *internal* seam, already used by other dialect-specific mechanisms in this codebase (the same
reflection-based-provider-hook pattern `OracleDialect`'s `StatementCacheSize` and `FirebirdDialect`'s
`FbConnection.ClearPool` calls already use). A bulk-loader implementation reuses this exact
mechanism: acquire a connection the normal governed way (`BeginTransactionAsync`/`GetConnection`,
which already goes through `PoolGovernor` admission), reach its real provider connection via the
existing internal seam, and hand that to `SqlBulkCopy`/`MySqlBulkCopy`/etc. — all inside
`pengdows.crud`'s own code, never exposed to the caller. No new public escape hatch needed.

### Proposed shape

Bulk-loading APIs are fundamentally row-*streaming push* operations (`WriteToServerAsync`,
`StartRow`/`Write`/`Complete`, `CreateRow`/`AppendValue`/`EndRow`) — none of them produce or consume
an `ISqlContainer`. They don't fit the existing Build → Load → Convenience three-tier model, and
shouldn't be forced into it. Proposed as a clearly separate, fourth surface:

```csharp
IBulkLoader<TEntity> loader = gateway.CreateBulkLoader(context); // or an ambient transaction, see below
await loader.WriteAsync(entities, cancellationToken); // IAsyncEnumerable<TEntity> or IEnumerable<TEntity>
var result = await loader.CompleteAsync(cancellationToken);
```

Open design points this needs to settle, not yet decided:

- **Transaction participation.** If an `ITransactionContext` is active, should `CreateBulkLoader`
  join it automatically (reusing the same underlying provider transaction via the same internal
  seam), or always run in its own governed connection? Joining the ambient transaction is more
  useful (all-or-nothing alongside other work in the same unit) but couples the bulk-loader's
  lifetime to the transaction's — needs a decided answer before implementation, not left implicit.
- **Pool admission.** The loader's underlying connection acquisition must go through
  `PoolGovernor` the same way every other write path does — a bulk load that bypassed admission
  control would undermine the whole governance thesis this library is built on. This should be a
  requirement stated explicitly in the eventual implementation's tests, not just assumed.
- **Audit fields, `[Version]`, and generated keys are NOT populated automatically.** Bulk-loading
  mechanisms write rows directly with no per-row hook — that's *why* they're fast. This is a sharp,
  real departure from `CreateAsync`'s contract (which always sets `CreatedOn`/`CreatedBy` per
  `docs/audit-fields.md`) and must be documented loudly wherever this ships, not left for a caller
  to discover the hard way. The caller is responsible for populating anything a per-row hook would
  normally set.
- **All-or-nothing, not partial-success.** `SqlBulkCopy`/`MySqlBulkCopy` fail the whole batch on a
  constraint violation by default; a `COPY ... FROM STDIN` likewise aborts the whole import on
  error. This matches `docs/batch-operations.md`'s existing "What Is Not Implemented" list
  (`BatchResult`, `ContinueOnError`, partial-success reporting) — the recommendation is to keep that
  boundary, not attempt partial-success semantics no provider's native mechanism actually offers.
- **Streaming vs. materializing.** `NpgsqlBinaryImporter` and `DuckDBAppender` are genuinely
  row-at-a-time streaming and map cleanly onto `IAsyncEnumerable<TEntity>`. `SqlBulkCopy`/
  `MySqlBulkCopy` want a `DataTable` or `IDataReader` — an `IAsyncEnumerable<TEntity>`-to-`DbDataReader`
  adapter would be needed for those two specifically (non-trivial, but a well-understood pattern —
  effectively the inverse of what `DataReaderMapper` already does in the other direction).
- **`ISqlDialect.SupportsNativeBulkLoad`** (new capability flag, default `false`, overridden `true`
  for SQL Server/PostgreSQL-family/MySQL-family/DuckDB) lets the fallback path (SQLite, Firebird,
  Oracle-via-array-binding) dispatch cleanly without a provider-specific `if` in application code —
  consistent with `docs/capability-discovery.md`'s existing pattern.

### Shortcomings / open questions

- Should SQLite/Firebird even implement `IBulkLoader<TEntity>`, given their existing batch path is
  already close to optimal? Recommendation: yes, as a thin wrapper over the existing path, purely
  for API consistency (a caller shouldn't need to know which providers have a "real" bulk path) —
  not because it's meaningfully faster there.
- Needs its own testbed coverage: one new `TestProvider` virtual hook (mirroring `FEAT-006`'s
  `TestBatchUpdate()` precedent — added to the shared base so every engine gets it, skipped only
  where genuinely inapplicable) that loads N rows and reads them back, run against the live
  12-engine/30-target matrix.
- Interaction with `EnforceUniqueConnectionString` and multi-tenancy is unaddressed — presumably
  "no different from any other write," but that should be confirmed with a test, not assumed.

### Comparison to other DALs

No mainstream .NET DAL abstracts this across providers today. `SqlBulkCopy`, `NpgsqlBinaryImporter`,
and `MySqlBulkCopy` are each provider-specific ADO.NET features, unwrapped by Dapper, EF Core
(without the third-party `EFCore.BulkExtensions` package), or any DAL surveyed in
`docs/positioning/dal-taxonomy-and-comparison.md`. A unified `IBulkLoader<TEntity>` dispatching to
the right native mechanism per dialect would be more abstracted than what exists in any peer
ecosystem's first-party tooling — a genuine differentiator if built, not a catch-up feature.

## Part 2: Oracle array binding (FEAT-005) — Oracle's version of Part 1

Oracle has no `BulkCopy`-shaped streaming-importer type. Its efficient multi-row mechanism is array
binding: set `OracleCommand.ArrayBindCount = N`, bind each `OracleParameter`'s `Value` to an *array*
of N values instead of a scalar, and one `ExecuteNonQuery` inserts all N rows — more efficient than
the `INSERT ALL ... SELECT ... FROM DUAL` multi-row-literal trick `FEAT-006` already uses for
Oracle's batch UPDATE-via-MERGE, and the natural fit for large row counts specifically.

This is not a separate feature from Part 1 conceptually — it's Oracle's `IBulkLoader<TEntity>`
implementation, using array-bound parameters instead of a streaming-importer object because that's
what ODP.NET actually offers. It should be exposed through the *same* `IBulkLoader<TEntity>`
interface proposed in Part 1, with `OracleDialect` providing the array-binding implementation
underneath, rather than a second, Oracle-only API surface a caller has to know to reach for.

Requires ODP.NET (Managed or Unmanaged) specifically — `ArrayBindCount` is not reachable via the
generic `DbProviderFactory`/`DbCommand`/`DbParameter` abstraction, so this needs the same
reflection-based provider-specific hook pattern already used elsewhere in `OracleDialect.cs`
(`StatementCacheSize`) rather than a hard package reference from `pengdows.crud` to
`Oracle.ManagedDataAccess.Core`.

## Part 3: Batch upsert via a single multi-row `MERGE` (FEAT-013)

### Current state (verified: `docs/batch-operations.md`'s "Upsert Shape Used Today" table)

| Product family | Batch upsert shape today |
|---|---|
| PostgreSQL-compatible (`SupportsInsertOnConflict`) | Real multi-row `INSERT ... ON CONFLICT ...` |
| MySQL-compatible (`SupportsOnDuplicateKey`) | Real multi-row `INSERT ... ON DUPLICATE KEY UPDATE ...` |
| SQL Server, Oracle, Firebird, and anything else with neither flag | **Falls back to N separate per-entity `BuildUpsert(...)` containers** — not a single statement |

### Why this is a smaller, well-precedented lift

`FEAT-006` (already shipped, see `future-work.md`) proved the exact SQL-generation technique this
needs, for the closely-related batch-*UPDATE*-via-`MERGE` case: SQL Server/PostgreSQL/Snowflake use
`MERGE ... USING (VALUES ...) AS s ... WHEN MATCHED THEN UPDATE`; Oracle uses the `DUAL`-chained
`USING (SELECT ... FROM DUAL UNION ALL SELECT ... FROM DUAL ...)` variant, since Oracle's `MERGE`
rejects a bare `VALUES(...)` row-constructor in `USING`. Batch *upsert* is the same statement shape
with one addition: a `WHEN NOT MATCHED THEN INSERT` clause alongside the existing
`WHEN MATCHED THEN UPDATE` clause. This reuses `FEAT-006`'s already-built and already-tested
multi-row source construction almost directly, rather than starting from nothing.

### Known landmine to avoid repeating

`FEAT-006`'s own closing notes record a real, live-database-only bug it hit and fixed: the
version-increment expression (`version = version + 1`) was unqualified, which real SQL Server/
PostgreSQL reject as ambiguous once the batch source alias also projects a same-named `version`
column, and Oracle separately forbids referencing an `ON`-clause column in `MERGE`'s `UPDATE SET`.
Batch upsert's `WHEN NOT MATCHED THEN INSERT` clause will hit the identical ambiguous-column class
of bug for any column name shared between the target table and the `USING` source alias — the
qualified-reference fix `FEAT-006` already applied must be carried over, not rediscovered from
scratch via another live-only failure.

### Open question specific to this part

Firebird already supports pure-key upsert for single-row operations (`SupportsPureKeyUpsert`, see
CLAUDE.md's Upsert Behavior section) via `MERGE`. Whether Firebird's `MERGE` accepts a multi-row
`USING (...)` source the same way SQL Server's does needs live verification against a real Firebird
container before committing to this shape for that dialect specifically — don't assume symmetry
with SQL Server's syntax without checking, consistent with this project's own standing rule for
every other cross-dialect SQL claim in this file.

## Sequencing recommendation

These three are independently shippable — none blocks the others:

1. **FEAT-013 (batch upsert via `MERGE`) first** — smallest scope, directly reuses `FEAT-006`'s
   proven technique, no new public API surface, no governance/transaction-participation design
   questions to resolve first.
2. **FEAT-005 (Oracle array binding) second** — single-provider, but needs the `IBulkLoader<TEntity>`
   interface shape from Part 1 decided first if it's to share that surface rather than ship as a
   one-off Oracle-only API.
3. **FEAT-012 (general bulk loading) last** — the largest scope, with the most open design
   questions (transaction participation, streaming adapter for `SqlBulkCopy`/`MySqlBulkCopy`,
   testbed coverage across every engine) — treat as its own TDD-first effort per CLAUDE.md's
   mandatory-TDD rule, the same way `docs/planning/retry-context-design.md` recommends for
   `RetryContext`.
