# Transaction Management

## Starting a transaction

`TransactionContext` drives every explicit `BeginTransactionAsync` call. The factory invokes `context.GetConnection` with the resolved `ExecutionType` so the configured connection strategy (Standard/SingleWriter/SingleConnection) can pick the right physical connection, and the connection is opened before the transaction starts. CockroachDB always moves to `IsolationLevel.Serializable`, DuckDB prefers the provider default, and read-only contexts are prohibited from opening write transactions (`NotSupportedException` if the caller requests `ExecutionType.Write` while the context is read-only). A dedicated `SemaphoreSlim` (`RealAsyncLocker`) guards the logical user lock so the caller can still buffer async work inside the transaction without racing commit/rollback.

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

`ExecutionType` is required: pass `ExecutionType.Write` for mutating transactions and `ExecutionType.Read` for read-only transactions. In `SingleWriter` mode this determines whether the write-slot governor is acquired.

## ITransactionContext properties and methods

| Member | Description |
|--------|-------------|
| `WasCommitted` | `true` after a successful `CommitAsync` call. |
| `WasRolledBack` | `true` after rollback (explicit or disposal-triggered). |
| `IsCompleted` | `true` when the transaction is no longer open (committed, rolled back, or failed). After a commit/rollback failure, this is `true` because the connection has already been released; `Dispose` will not attempt a second rollback. |
| `IsolationLevel` | The `IsolationLevel` active for this transaction. |
| `CommitAsync(CancellationToken)` | Commits the transaction. Returns `ValueTask`. |
| `RollbackAsync(CancellationToken)` | Rolls back the transaction. Returns `ValueTask`. |
| `SavepointAsync(string name, CancellationToken)` | Creates a named savepoint (dialect must support savepoints). Returns `ValueTask`. |
| `RollbackToSavepointAsync(string name, CancellationToken)` | Rolls back to a named savepoint without ending the transaction. Returns `ValueTask`. |

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

`CommitAsync`/`RollbackAsync` route through `CompleteTransactionWithWaitAsync`, which serializes completion behind a semaphore so commits/rollbacks never overlap. Savepoints and rollbacks-to-savepoint run as long as the dialect advertises support, and the dialect's SQL is executed on the same transaction so you can roll back a subset of work without leaving the context. Every completion closes the tracked connection and notifies the metrics collector (`TransactionCompleted`) so telemetry stays accurate.

## Disposal and cleanup

`TransactionContext` guards against forgotten commits. `DisposeAsync` attempts to grab the completion lock, roll back the transaction if it is still open, and log errors if it cannot acquire the lock within a brief window. Every path calls `CompleteTransactionMetrics` to ensure the metrics delta is recorded even when the transaction rolls back automatically. The transaction object and both semaphores are disposed once the work finishes.

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
    await tx.RollbackAsync(ct);
    throw;
}

// Savepoints
await tx.SavepointAsync("checkpoint1", ct);
// ... some work ...
await tx.RollbackToSavepointAsync("checkpoint1", ct);
```

**CRITICAL:** Do not use `TransactionScope`. It is incompatible with pengdows.crud's open-late/close-early connection management and will cause MSDTC promotion or broken transactional guarantees.

## Isolation profiles (portable)

`IsolationProfile` maps to the safest available `IsolationLevel` for each database:

| Profile | Intent |
|---------|--------|
| `SafeNonBlockingReads` | Snapshot / repeatable-read equivalent where possible |
| `StrictConsistency` | Serializable |
| `ReadCommitted` | Read committed |

## Connection sharing inside transactions

All commands issued with the same `ITransactionContext` share the single physical connection pinned when the transaction started, regardless of `DbMode`. Reads and writes inside the transaction are not split across read/write pools.
