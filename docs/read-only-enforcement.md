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

A write attempt is checked against **two separate flags, in sequence**, each throwing a different exception type — a catch block that only handles one will miss the other:

1. **Context-level configuration**: if `context.ReadWriteMode == ReadWriteMode.ReadOnly` (the whole context was configured read-only), throws `NotSupportedException("Write operations are not supported in read-only mode.")`.
2. **Connection/transaction-scoped intent**: checked second, only if the first check passes. `context.IsReadOnlyConnection` is a *different* flag — set per connection/transaction, e.g. a transaction opened with `ExecutionType.Read` on an otherwise-writable context. If set, throws `InvalidOperationException("Transaction is read-only.")` (`InternalConnectionAccessAssertions.AssertIsWriteConnection`).

These are real, production-active guards (not `Debug.Assert`), checked on every write execution.
Both library-generated exceptions implement the public `IReadOnlyViolation` marker interface
(`pengdows.crud.exceptions`). Their existing base types and messages are preserved, so existing
code catching `NotSupportedException` or `InvalidOperationException` continues to work. New
code can catch `IReadOnlyViolation` without inspecting exception messages.

## `ReadOnlyViolationException`

For SQLite and DuckDB specifically (`SqliteExceptionTranslator`, `DuckDbExceptionTranslator`),
a raw provider error indicating a write attempted against a read-only database file is translated
into `ReadOnlyViolationException` (`pengdows.crud.exceptions`, extends `DatabaseOperationException`
→ `DatabaseException` — a `catch (DatabaseException)` block catches it). Other dialects that reject
write intent at transaction-creation time do so via a plain `NotSupportedException` instead (see
`docs/transactions.md`) — the two mechanisms are not unified into one exception type.
