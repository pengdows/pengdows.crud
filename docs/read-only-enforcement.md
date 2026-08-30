# Read-Only Enforcement

`pengdows.crud` exposes read intent through `ReadWriteMode` on the context and `ExecutionType.Read` on command and transaction entry points.

## What The Public API Looks Like

```csharp
var config = new DatabaseContextConfiguration
{
    ConnectionString = "...",
    ReadWriteMode = ReadWriteMode.ReadOnly
};

var context = new DatabaseContext(config, factory);

await using var tx = await context.BeginTransactionAsync(
    IsolationProfile.SafeNonBlockingReads,
    ExecutionType.Read,
    cancellationToken);
```

There is no public `readOnly: true` transaction argument. Read intent is expressed through `ReadWriteMode` and `ExecutionType`.

## Enforcement Model

The exact session SQL varies by dialect, but the framework can enforce read intent through:

- connection-string shaping when the provider supports it
- dialect-specific session settings when the provider requires it
- transaction creation rules that reject write intent on a read-only context

The details live in the dialect and connection-lifecycle code, not in a separate read-only subsystem with its own public API.

## Read-only violation exceptions

A write attempt can be rejected by **three distinct exception types**, thrown from three different
layers. A catch block that only handles one will miss the other two — this is why the library also
ships a common marker interface (below) rather than expecting callers to enumerate all three.

| # | Exception | Base type | `DatabaseException`? | Layer / trigger |
|---|---|---|---|---|
| 1 | `ReadOnlyContextException` | `NotSupportedException` | No | `SqlContainer.ExecuteNonQueryAsync`/`ExecuteScalarCore` pre-flight check, before any connection work: `context.ReadWriteMode == ReadWriteMode.ReadOnly` — the **whole context** was configured read-only. |
| 2 | `ReadOnlyAccessException` | `InvalidOperationException` | No | `InternalConnectionAccessAssertions.AssertIsWriteConnection` / `TransactionContext.AssertIsWriteConnection` pre-flight check, checked only if (1) passes: `context.IsReadOnlyConnection` (or a transaction's own `_isReadOnly` flag) is set — this **specific connection or transaction** was opened read-only, e.g. a transaction started with `ExecutionType.Read` on an otherwise-writable context. |
| 3 | `ReadOnlyViolationException` | `DatabaseOperationException` → `DatabaseException` | **Yes** | `SqliteExceptionTranslator`/`DuckDbExceptionTranslator` (via `DbExceptionTranslationSupport.CreateReadOnlyViolation`), translating a raw provider error returned only after a write actually reached the database — see the section below. |

Exceptions 1 and 2 are pure local pengdows.crud logic: no command is ever sent to the provider, and
the check either passes or throws before a connection is touched. Exception 3 is the opposite — it
only fires after a write round-trips to a genuinely read-only database file and the provider itself
refuses it (e.g. the underlying `.db` file has read-only OS permissions, independent of anything
`ReadWriteMode`/`IsReadOnlyConnection` knew about in advance).

These are real, production-active guards (not `Debug.Assert`), checked on every write execution.
All three existing base types and messages are preserved, so existing code catching
`NotSupportedException`, `InvalidOperationException`, or `DatabaseException` continues to work
unchanged.

### The `IReadOnlyViolation` catch-all

All three implement the public marker interface `IReadOnlyViolation` (`pengdows.crud.exceptions`,
no members). New code that only cares "was this write rejected because of read-only state,
regardless of which layer caught it" can catch that one interface instead of three separate types:

```csharp
try
{
    await container.ExecuteNonQueryAsync();
}
catch (IReadOnlyViolation)
{
    // Handles ReadOnlyContextException, ReadOnlyAccessException, and
    // ReadOnlyViolationException uniformly — no need to enumerate all three,
    // and no need to inspect the exception message.
    return Results.Problem("This operation requires a writable connection.", statusCode: 409);
}
```

Because `ReadOnlyViolationException` is also a `DatabaseException`, a broader
`catch (DatabaseException)` block still catches case 3 but **not** cases 1 or 2 — if you want
uniform read-only handling alongside other `DatabaseException` handling, order the
`catch (IReadOnlyViolation)` clause before (or as a sibling of) `catch (DatabaseException)`.

### Retry guidance

**Retrying never helps for any of the three.** `ReadOnlyViolationException` is always constructed
with `IsTransient = false` (see CLAUDE.md's Exception Hierarchy and `docs/exception-analysis.md`'s
`DbErrorCategory.ReadOnlyViolation` mapping) — it is deliberately excluded from the
`IsTransient`/`IsRetryable` categories (`Deadlock`, `SerializationFailure`, `Timeout`) that a retry
wrapper should act on. `ReadOnlyContextException` and `ReadOnlyAccessException` aren't
`DatabaseException`s at all, so they carry no `IsTransient`/`IsRetryable` property to check — but
the same reasoning applies: a context's `ReadWriteMode`, a connection's read-only intent, and a
database file's read-only permissions don't change between one attempt and the next of the *same*
operation, so nothing about retrying could turn a rejected write into a successful one.

## `ReadOnlyViolationException` in detail

For SQLite and DuckDB specifically (`SqliteExceptionTranslator`, `DuckDbExceptionTranslator`),
a raw provider error indicating a write attempted against a read-only database file is translated
into `ReadOnlyViolationException` (`pengdows.crud.exceptions`, extends `DatabaseOperationException`
→ `DatabaseException` — a `catch (DatabaseException)` block catches it). Other dialects that reject
write intent at transaction-creation time do so via a plain `NotSupportedException` instead (see
`docs/transactions.md`) — the two mechanisms are not unified into one exception type beyond the
shared `IReadOnlyViolation` marker described above.

## `PoolForbiddenException`: a related but distinct concept

`PoolForbiddenException` (`: InvalidOperationException`, `pengdows.crud.exceptions`) is thrown by
`PoolGovernor.Acquire`/`AcquireAsync` when a pool configured with `MaxConcurrentWrites=0` (or
`MaxPoolSize=0`) is accessed at all — most commonly the write pool on a context that was promoted
to `ReadOnly` (see CLAUDE.md's `ExecutionType` section: "Setting `MaxConcurrentWrites=0` promotes
the context to `ReadOnly`"). It carries `PoolLabel` (which pool — `Write` or `Read`) and
`PoolKeyHash` (for correlating with pool metrics/logs) properties.

**It deliberately does not implement `IReadOnlyViolation`.** The distinction is where in the
pipeline the rejection happens: the three exceptions above all reject a write that has an eligible
connection but is disallowed by read-only state; `PoolForbiddenException` rejects the request
before a connection slot is even considered, because the pool itself has zero capacity
(admission control), not because a write reached a read-only-tagged connection or database. A
caller that wants to catch "no way to do this write at all, for any reason" needs to catch both
`IReadOnlyViolation` and `PoolForbiddenException` separately — they are adjacent concepts, not the
same one under two names.
