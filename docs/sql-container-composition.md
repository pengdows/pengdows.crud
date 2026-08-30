# SQL Container Composition Guide

`ISqlContainer` is the low-level building block for custom SQL: a query-text builder bound to a
dialect, plus its parameters. This guide covers the fluent composition helpers
(`SqlContainerExtensions`, `pengdows.crud/SqlContainerExtensions.cs`) and the execution-overload
selection rules for running the container once it's built. For the two other things you can do
with a container — reuse it as a template (`Clone()`/`Clone(context)`) or call a stored procedure
(`WrapForStoredProc`) — see [`sql-container-templates.md`](./sql-container-templates.md) and
[`stored-procedures.md`](./stored-procedures.md) respectively; this guide doesn't repeat either.

## Identifier and parameter placeholder helpers

All of these write directly into `container.Query` and return the container for chaining. Every
one delegates to the container's own dialect so the output is always correctly quoted/formatted
for whichever database the container is bound to — never hand-write quote characters or parameter
prefixes yourself.

```csharp
using var sc = context.CreateSqlContainer();
sc.AppendQuery("SELECT ")
  .AppendName("p", "post_date")   // → "p"."post_date"  (dialect-quoted, dot-split)
  .AppendQuery(" FROM ")
  .AppendName("posts")            // → "posts"
  .AppendQuery(" p")
  .AppendWhere()                  // → " WHERE "
  .AppendName("p", "status")
  .AppendEquals()                 // → " = "
  .AppendParam("status");         // → the dialect's placeholder for parameter "status"

sc.AddParameterWithValue("status", DbType.String, "published");
```

| Helper | Appends |
|---|---|
| `AppendQuery(string)` | The literal string as-is — the deliberate escape hatch. Values must still go through `AddParameterWithValue`/`AppendParam`, never string-interpolated into this. |
| `AppendName(string)` | A dialect-quoted identifier. Splits on `.` internally (`"p.col"` → two quoted, separator-joined tokens) so a dotted name still quotes correctly. |
| `AppendName(alias, name)` | Equivalent to `AppendName("alias.name")`, written as two arguments for clarity. |
| `AppendParam(DbParameter)` | The dialect-formatted placeholder for an *already-created* parameter (from `CreateDbParameter`/`AddParameterWithValue`'s return value) — does not add the parameter to the container itself. |
| `AppendParam(string name)` | The dialect-formatted placeholder for a parameter by name — same non-adding behavior. |
| `AppendWhere()` / `AppendAnd()` / `AppendEquals()` / `AppendIn()` / `AppendComma()` / `AppendCloseParen()` | Common SQL fragments (` WHERE `, ` AND `, ` = `, ` IN (`, `, `, `)`) — small helpers so call sites don't hand-write spacing around these tokens inconsistently. |

`AppendParam` never adds a parameter to the container's parameter list — you still need to call
`AddParameterWithValue`/`CreateDbParameter`/`AddParameter` yourself. `AppendParam` only writes the
*placeholder text* into the query for a parameter that either already exists on the container or
that you're about to add with the same name.

## Choosing an execution overload

Every `ISqlContainer` execution method comes in extension-method overloads (added via
`SqlContainerExtensions` without breaking the interface) that add `ExecutionType`,
`CommandType`, and `CancellationToken` selection:

```csharp
// Explicit execution intent (Read/Write) + cancellation — the fully-specified form:
var count = await sc.ExecuteScalarRequiredAsync<int>(
    ExecutionType.Write, CommandType.Text, cancellationToken);

var reader = await sc.ExecuteReaderAsync(
    ExecutionType.Read, CommandType.Text, cancellationToken);

// CommandType + cancellation, no explicit ExecutionType (uses the container's own default):
await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken);
```

- **`ExecutionType`** — pass `ExecutionType.Write` for any statement that mutates data or must land
  on the writable connection/pool (this includes `INSERT ... RETURNING`/`OUTPUT` scalar reads —
  see the `ExecuteScalarRequiredAsync` remarks in code: using the wrong intent here is exactly the
  class of bug CORE-016/TEST-010 found and fixed this session for generated-ID retrieval). Pass
  `ExecutionType.Read` for ordinary queries. Omitting it entirely uses whichever default the
  specific method documents — check the method's own doc comment rather than assuming, since
  `ExecuteScalarOrNullAsync()`'s parameterless overload hardcodes `ExecutionType.Read` (a real,
  previously-fixed footgun — see CORE-016's history in `docs/planning/future-work.md`).
- **`CommandType`** — `Text` (default) for ordinary SQL, `StoredProcedure` to invoke
  `WrapForStoredProc` automatically before execution (see `docs/stored-procedures.md`).
- **`ExecuteReaderSingleRowAsync`** — requests `CommandBehavior.SingleRow` where the concrete
  `SqlContainer` type supports it (falls back to an ordinary reader for other `ISqlContainer`
  implementations, e.g. test doubles). Use it when you know the query returns at most one row —
  some providers can apply a real optimization for this hint.

## Gateway and context diagnostics

A few useful, easy-to-miss members that live on concrete types rather than the three-tier
Build/Load/convenience API surface:

- **`BaseTableGateway<TEntity>.BuildWhereByPrimaryKey(...)`** adds a parameterized natural-key
  predicate to an existing container — useful when composing custom SQL that still needs to filter
  by an entity's `[PrimaryKey]` columns.
- **`ClearCaches()`** clears a gateway's compiled reader plans, column lists, query templates, and
  cached WHERE parameter names. This is gateway-lifetime state (see `docs/entity-mapping.md` and
  this session's CORE-019 resolution in `docs/planning/future-work.md` for why these caches are
  intentionally not tied to any one operation context) — call this only after a schema or mapping
  change you know invalidates the cached shapes, not as routine housekeeping.
- **`IDatabaseContext.GetSupportedIsolationLevels()`**, **`GetBaseSessionSettings()`**, and
  **`GetReadOnlySessionSettings()`** expose the dialect's raw isolation/session-setting data for
  diagnostics and provider-specific integration checks — see
  [`capability-discovery.md`](./capability-discovery.md) for the broader capability-inspection
  story these fit into.
- **`GetPoolStatisticsSnapshot(PoolLabel)`** reports slot usage, queue depth, turnstile waits,
  timeouts, cancellations, and whether a pool is disabled or forbidden — see
  [`connection/connection-pooling.md`](./connection/connection-pooling.md) and
  [`connection/ownership-and-shutdown.md`](./connection/ownership-and-shutdown.md) for the
  admission-control model this reports on.
- **`MaxQueuedReads`/`MaxQueuedWrites`** independently bound admission queues (`0` disables
  queueing for that role; `null` uses the governor default). **`EnforceUniqueConnectionString`**
  upgrades duplicate live contexts sharing a connection string from a warning into a
  construction-time exception.

## A related but separate mapper: `DataReaderMapper`

`IDataReaderMapper`/`DataReaderMapper` (`DataReaderMapper.Instance`) are public and usable — see
[`data-reader-mapper.md`](./data-reader-mapper.md) — but they are **not** part of the gateway
hydration path documented above. Gateway hydration uses a separate, attribute-driven compiled-plan
path (`GetOrBuildRecordsetPlan`/`MapReaderToObjectWithPlan`); `DataReaderMapper` is for mapping an
arbitrary SQL result (e.g. a stored procedure with no corresponding entity) to any POCO by
column-name matching instead. `TypeCoercionOptions.TimePolicy` is read internally but not
externally configurable through the normal gateway/context path.
