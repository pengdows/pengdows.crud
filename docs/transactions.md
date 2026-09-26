# Transaction Management

## Starting a transaction

`TransactionContext` drives every explicit `BeginTransactionAsync` call. The factory invokes `context.GetConnection` with the resolved `ExecutionType` so the configured connection strategy (Standard/SingleWriter/SingleConnection) can pick the right physical connection, and the connection is opened before the transaction starts. CockroachDB always moves to `IsolationLevel.Serializable`, DuckDB begins with the provider default (its ADO.NET provider rejects explicit levels; the resolved level is still reported), and read-only contexts are prohibited from opening write transactions (`NotSupportedException` if the caller requests `ExecutionType.Write` while the context is read-only; requesting `ExecutionType.Read` on a write-only context throws `InvalidOperationException`). A dedicated `SemaphoreSlim` user lock (wrapped by `ReusableAsyncLocker`) serializes operations on the pinned connection, and a separate completion `SemaphoreSlim` serializes commit/rollback.

### Async signatures (all return ValueTask)

```csharp
// By IsolationProfile (portable, cross-database):
await using var tx = await context.BeginTransactionAsync(
    IsolationProfile.StrictConsistency,
    ExecutionType.Write,
    cancellationToken);

// By native IsolationLevel:
await using var tx = await context.BeginTransactionAsync(
    IsolationLevel.Serializable,
    ExecutionType.Write,
    cancellationToken);
```

`ExecutionType` defaults to `ExecutionType.Write` on every overload (and `IsolationLevel?` defaults to `null`): pass `ExecutionType.Read` explicitly for read-only transactions. In `SingleWriter` mode this determines whether the write-slot governor is acquired. Synchronous `BeginTransaction(...)` overloads with the same parameters (minus the token) also exist.

## ITransactionContext properties and methods

| Member | Description |
|--------|-------------|
| `WasCommitted` | `true` after a successful `CommitAsync` call. |
| `WasRolledBack` | `true` after rollback (explicit or disposal-triggered). |
| `IsCompleted` | `true` when the transaction is no longer open (committed, rolled back, or failed). After a commit/rollback failure, this is `true` because the connection has already been released; `Dispose` will not attempt a second rollback. |
| `IsolationLevel` | The `IsolationLevel` active for this transaction. |
| `CommitAsync(CancellationToken)` | Commits the transaction. Returns `ValueTask`. |
| `RollbackAsync(CancellationToken)` | Rolls back the transaction. Returns `ValueTask`. |
| `SavepointAsync(string name, CancellationToken)` | Creates a named savepoint. Throws `NotSupportedException` if the dialect does not support savepoints (`SupportsSavepoints == false`, i.e. `SavepointCapabilities` lacks `Create`). Returns `ValueTask`. |
| `RollbackToSavepointAsync(string name, CancellationToken)` | Rolls back to a named savepoint without ending the transaction. Throws `NotSupportedException` if the dialect does not support savepoints (`SupportsSavepoints == false`). Returns `ValueTask`. |
| `ReleaseSavepointAsync(string name, CancellationToken)` | Explicitly releases a savepoint before the transaction ends. Throws `NotSupportedException` if the dialect's `SavepointCapabilities` lacks `Release` (SQL Server, Sybase, Oracle — none have an explicit release statement). Returns `ValueTask`. |

## Error handling — TransactionException

`BeginTransaction`, `Commit`, and `Rollback` (sync and async) throw `TransactionException` if the driver-level operation fails. `TransactionException` inherits `DatabaseOperationException → DatabaseException` — a `catch (DatabaseException)` block will catch it.

**Critical behavior after failure:** `IsCompleted` is set to `true` because the underlying connection has already been released by the time the exception propagates. `Dispose` / `DisposeAsync` will not attempt a second rollback. This prevents "rollback on a dead connection" errors.

```csharp
try
{
    await tx.CommitAsync(ct);
}
catch (TransactionException ex)
{
    // ex.InnerException = original driver exception
    // tx.IsCompleted == true here — connection already released
    // No need to call tx.Rollback(); Dispose will skip it
    logger.LogError(ex, "Commit failed on {Database}", ex.Database);
    throw;
}
```

## Committing, rolling back, and savepoints

`CommitAsync`/`RollbackAsync` (and the sync `Commit`/`Rollback`, which block on them) route through `CompleteTransactionWithWaitAsync`, which serializes completion behind a semaphore (bounded by `ModeLockTimeout`; `null` waits indefinitely) so commits/rollbacks never overlap; a second completion throws `InvalidOperationException`. `SavepointAsync` and `RollbackToSavepointAsync` fail fast with `NotSupportedException` when the dialect's `SupportsSavepoints` is `false`, and `ReleaseSavepointAsync` does the same when `SavepointCapabilities` lacks `Release`, rather than silently doing nothing — a caller only discovers non-support once, at the first unsupported call, instead of after later destructive work it believed was protected. When the capability IS present, the dialect's SQL is executed on the same transaction so you can create, roll back to, or explicitly release a savepoint without leaving the context. Every completion closes the tracked connection and notifies the metrics collector (`TransactionCommitted` or `TransactionRolledBack`; a commit/rollback that throws is counted as neither) so telemetry stays accurate.

**Open readers block nothing — they make the next call fail fast.** A reader opened on the transaction (`ExecuteReaderAsync`, `LoadStreamAsync`, `RetrieveStreamAsync`) holds the transaction's user lock until it is disposed. While it is open, another command, `Commit`/`Rollback` (sync or async), or any savepoint call on the same transaction throws `InvalidOperationException` ("…while a reader opened on it is still active…") instead of waiting — waiting would deadlock whenever the caller is the code that must dispose the reader. A `Commit`/`Rollback` that fails this way leaves the transaction uncompleted (`IsCompleted` stays `false`), so dispose the reader and call it again.

## Disposal and cleanup

`TransactionContext` guards against forgotten commits. `Dispose`/`DisposeAsync` try to grab the completion lock without waiting, roll back the transaction if it is still open, and log an error (skipping the explicit rollback) if another thread currently holds the lock. Every path calls `CompleteTransactionMetrics` to ensure the metrics delta is recorded even when the transaction rolls back automatically. The provider transaction is disposed by whichever call completes it (commit, rollback or the automatic rollback), exactly once. The user-lock semaphore is disposed unless a reader still holds it, and the completion semaphore is disposed unless another thread still holds it. If a reader opened on the transaction is still open, `Dispose` does not throw but cannot roll back either (the reader owns the connection), so dispose readers before the transaction.

## Usage patterns

```csharp
// Recommended: await using for automatic async disposal
await using var tx = await context.BeginTransactionAsync(
    IsolationProfile.StrictConsistency, ExecutionType.Write, ct);
try
{
    var order = await gateway.RetrieveOneAsync(orderId, tx);
    order.Status = OrderStatus.Cancelled;
    await gateway.UpdateAsync(order, tx);
    await tx.CommitAsync(ct);
}
catch
{
    if (!tx.IsCompleted) // after a failed commit the transaction is already completed
    {
        await tx.RollbackAsync(ct);
    }
    throw;
}

// Savepoints
await tx.SavepointAsync("checkpoint1", ct);
// ... some work ...
await tx.RollbackToSavepointAsync("checkpoint1", ct);
// ...or, if the work succeeded and the checkpoint is no longer needed (dialect permitting —
// check ctx.Dialect.SavepointCapabilities for Release; SQL Server/Sybase/Oracle don't have it):
await tx.ReleaseSavepointAsync("checkpoint1", ct);
```

**CRITICAL:** Do not use `TransactionScope`. It is incompatible with pengdows.crud's open-late/close-early connection management and will cause MSDTC promotion or broken transactional guarantees.

## Isolation profiles (portable)

`IsolationProfile` maps to a per-database `IsolationLevel` (see `pengdows.crud/isolation/IsolationResolver.cs`):

| Profile | Intent |
|---------|--------|
| `SafeNonBlockingReads` | MVCC/snapshot-style reads without dirty reads (Snapshot, RepeatableRead, or ReadCommitted depending on the database) |
| `StrictConsistency` | Serializable |
| `FastWithRisks` | ReadUncommitted (dirty reads) where supported — almost never recommended |

Isolation **fails up, never down**:

- **Explicit `IsolationLevel`:** used as-is if the database supports it; otherwise the weakest supported level that is at least as strong is used (ReadUncommitted < ReadCommitted < RepeatableRead < Serializable; Snapshot sits above ReadCommitted and is satisfied only by Snapshot or Serializable; RepeatableRead is satisfied only by Serializable). For example, `ReadCommitted` on CockroachDB or DuckDB runs as `Serializable`. If nothing at or above the requested level exists (e.g. `Serializable` on TiDB or Snowflake), `BeginTransaction` throws `InvalidOperationException`.
- **`IsolationProfile`:** throws `TransactionModeNotSupportedException` (a `NotSupportedException`) rather than run below the profile's guarantee — e.g. `StrictConsistency` on TiDB, Snowflake, Access, or FlatFile; `SafeNonBlockingReads` on SQL Server without snapshot isolation enabled. On PostgreSQL/YugabyteDB `SafeNonBlockingReads` runs as `RepeatableRead` (an MVCC snapshot: non-blocking, no non-repeatable reads).
- **Read-only `BeginTransaction` with no level and no profile** (`ExecutionType.Read`, `isolationLevel: null`): uses the `SafeNonBlockingReads` mapping and logs a warning if that mapping is degraded — it never throws for that.
- **Write `BeginTransaction` with no level:** `ReadCommitted` if supported, else `Serializable`.

## Connection sharing inside transactions

In `DbMode.SingleConnection` the transaction's connection is the context's only connection. Until
the transaction completes, a read through the context (not the transaction) throws
`InvalidOperationException` — on that connection it would otherwise run outside the transaction or
be silently enlisted in it, depending on the provider — and a write through the context waits for
the transaction to finish.

All commands issued with the same `ITransactionContext` share the single physical connection pinned when the transaction started, regardless of `DbMode`. Reads and writes inside the transaction are not split across read/write pools.
