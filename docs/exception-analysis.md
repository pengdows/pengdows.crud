# Portable Database-Error Analysis

Every dialect can classify a raw provider exception into a portable, provider-neutral shape —
useful for control flow (retry? map to a 409? log at what severity?) without `catch`ing each
provider's own exception type or parsing its message text yourself.

```csharp
try
{
    await container.ExecuteNonQueryAsync();
}
catch (Exception ex)
{
    var info = context.Dialect.AnalyzeException(ex);

    if (info.IsRetryable) { /* retry with backoff */ }
    if (info.ConstraintKind == DbConstraintKind.ForeignKey) { /* map to HTTP 409 */ }
    if (info.ConstraintKind == DbConstraintKind.Unique) { /* map to HTTP 409, different message */ }

    throw;
}
```

## `DbExceptionInfo`

`ISqlDialect.AnalyzeException(Exception)` (`pengdows.crud/dialects/SqlDialect.cs`) returns:

```csharp
public readonly record struct DbExceptionInfo(
    DbErrorCategory Category,
    DbConstraintKind ConstraintKind,
    bool IsTransient,
    bool IsRetryable,
    int? ProviderErrorCode,
    string? SqlState);
```

| Field | Meaning |
|---|---|
| `Category` | `None`, `Deadlock`, `SerializationFailure`, `ConstraintViolation`, `Timeout`, `ReadOnlyViolation`, or `Unknown`. |
| `ConstraintKind` | Only meaningful when `Category == ConstraintViolation`: `Unique`, `ForeignKey`, `NotNull`, `Check`, or `Unknown` (a real constraint violation whose specific kind the provider's error text didn't let the dialect determine). `None` otherwise. |
| `IsTransient` | `true` for `Deadlock`, `SerializationFailure`, `Timeout` — categories where retrying the same operation might succeed because nothing about the operation itself was wrong. |
| `IsRetryable` | Currently identical to `IsTransient` in every shipped dialect (`AnalyzeException`'s base implementation sets both from the same category check) — kept as a separate field because a future dialect-specific override could in principle diverge them (e.g. a transient condition that's still not safe to blindly retry). Don't assume they'll always be equal; check the one that matches your intent. |
| `ProviderErrorCode` | The raw provider error number, when the provider exposes one as an integer (`SqlException.Number`, MySQL error codes, etc.) — `null` for providers that only expose SQLSTATE. |
| `SqlState` | The ANSI SQLSTATE code, when the provider exposes one — `null` for providers that only expose a numeric code (SQL Server). |

**Cancellation is exempt, not classified as an error.** `AnalyzeException` special-cases
`OperationCanceledException` first and returns an all-empty/`false` `DbExceptionInfo`
(`Category = None`, both flags `false`) — matching the project-wide rule that cancellation is
never wrapped into a `DatabaseException` (see CLAUDE.md's Exception Hierarchy section).

## Building blocks: `ClassifyException` and the `Is*Violation` checks

`AnalyzeException` is built from two lower-level, independently callable pieces on `ISqlDialect`:

- **`ClassifyException(Exception)`** → `DbErrorCategory` alone — use this if you only need the
  coarse category and don't need constraint-kind detail.
- **`IsUniqueViolation`/`IsForeignKeyViolation`/`IsNotNullViolation`/`IsCheckConstraintViolation`**
  (each `DbException → bool`) — the same checks `AnalyzeException` composes from internally, safe
  to call directly if you only need one specific check and already know the category is a
  constraint violation.

## How classification actually works, per provider family

`ClassifyException` matches on the connected `DatabaseType` against the provider's raw error code
or SQLSTATE (`SqlDialect.TryClassifyProviderException`, a single method with one `switch` per
database family — not per-dialect virtual overrides). Concrete examples from the current mapping:

| Database family | Deadlock | Serialization failure | Timeout | Constraint violation |
|---|---|---|---|---|
| SQL Server | error `1205` | error `3960` | error `-2` | errors `515`, `547`, `2601`, `2627` |
| PostgreSQL / CockroachDB / YugabyteDB / Aurora PostgreSQL | SQLSTATE `40P01` | SQLSTATE `40001` | SQLSTATE `55P03` or `57014` | any SQLSTATE class `23xxx` |
| MySQL / MariaDB / TiDB / Aurora MySQL | error `1213` | SQLSTATE `40001` | error `1205` | errors `1048`, `1062`, `1169`, `1216`, `1451`, `1452`, `3819`, `4025` |

Every dialect not in this table (Oracle, SQLite, Firebird, DuckDB, Db2, Snowflake) has its own
entries in the same `switch` — check `SqlDialect.cs`'s `TryClassifyProviderException` directly for
the exact codes if you're targeting one of those specifically; the shape (numeric code vs.
SQLSTATE, per family) follows the same pattern.

**Two independent classification systems exist in this codebase and can disagree** — this
`DbErrorCategory`/`DbExceptionInfo` system (for application control flow) and the separate
`IDbExceptionTranslator`/`DbExceptionTranslatorRegistry` system that produces the typed
`DatabaseException` subclass pengdows.crud actually throws (`UniqueConstraintViolationException`,
`DeadlockException`, etc. — see CLAUDE.md's Exception Hierarchy). They are maintained separately;
adding a new database requires updating both (see CLAUDE.md's "Adding a New Database" checklist,
item 11).

## Relationship to thrown `DatabaseException` subclasses

| `DbErrorCategory` / `DbConstraintKind` | Corresponding typed exception pengdows.crud throws |
|---|---|
| `Deadlock` | `DeadlockException` (`TransientWriteConflictException`, `IsTransient = true`) |
| `SerializationFailure` | `SerializationConflictException` (`TransientWriteConflictException`, `IsTransient = true`) |
| `Timeout` | `CommandTimeoutException` (`IsTransient = true`) |
| `ConstraintViolation` + `Unique` | `UniqueConstraintViolationException` |
| `ConstraintViolation` + `ForeignKey` | `ForeignKeyViolationException` |
| `ConstraintViolation` + `NotNull` | `NotNullViolationException` |
| `ConstraintViolation` + `Check` | `CheckConstraintViolationException` |
| `ReadOnlyViolation` | `ReadOnlyViolationException` (implements `IReadOnlyViolation`) |

These two systems agree by design on the well-known cases, but `AnalyzeException`/`ClassifyException`
is the one meant for you to call directly in a `catch` block for control-flow branching —
`IDbExceptionTranslator` runs internally to decide which typed exception pengdows.crud itself
throws in the first place, before your code ever sees it.

## Retry-policy boundary: this does not retry for you

`AnalyzeException`/`IsRetryable` tells you whether retrying *might* help — it does not retry
anything itself. There is no built-in retry loop, backoff, or connection-pool-aware retry
coordinator in the current library. A `RetryContext` subsystem with exactly that shape (governor-
aware backoff, no connection held during sleep, transient-exception-only retry) is designed but
**not implemented** — see [`docs/planning/retry-context-design.md`](./planning/retry-context-design.md)
for the full design, its concrete shortcomings, and how it compares to Polly/EF Core/other DALs
(the original design prose lives in `docs/planning/future-work.md`'s "RetryContext Subsystem"
section, tracked as `FEAT-001`) if you want to build your own retry wrapper around
`IsRetryable`/`IsTransient` today; don't assume it already exists.

```csharp
// Minimal example of what an application-level retry wrapper looks like today —
// pengdows.crud does not provide this for you.
for (var attempt = 0; ; attempt++)
{
    try
    {
        await container.ExecuteNonQueryAsync();
        break;
    }
    catch (Exception ex) when (context.Dialect.AnalyzeException(ex).IsRetryable && attempt < 3)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(50 * (attempt + 1)));
    }
}
```
