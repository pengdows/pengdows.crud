# Transaction Management

## Starting a transaction

`TransactionContext` drives every explicit `BeginTransactionAsync` call. The factory invokes `context.GetConnection` with the resolved `ExecutionType` so the configured connection strategy (Standard/SingleWriter/SingleConnection) can pick the right physical connection, and the connection is opened before the transaction starts. CockroachDB always moves to `IsolationLevel.Serializable`, DuckDB prefers the provider default, and read-only contexts are prohibited from opening write transactions (`NotSupportedException` if the caller requests `ExecutionType.Write` while the context is read-only). A dedicated `SemaphoreSlim`, wrapped by `ReusableAsyncLocker`, guards the logical user lock so the caller can still buffer async work inside the transaction without racing commit/rollback.

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
| `FastWithRisks` | Read uncommitted / dirty reads (almost never recommended) |

## Connection sharing inside transactions

All commands issued with the same `ITransactionContext` share the single physical connection pinned when the transaction started, regardless of `DbMode`. Reads and writes inside the transaction are not split across read/write pools. `GetConnection` always returns that one pinned connection — the `ExecutionType` argument passed to it is accepted for interface-compatibility but has no effect on which connection comes back.

## Concurrency contract

`TransactionContext` is **single-flow by design, not built for concurrent use from multiple threads/tasks against the same instance.** Every operation — an ordinary command, `CommitAsync`, `RollbackAsync`, `SavepointAsync`, and `RollbackToSavepointAsync` — serializes through the same internal lock before touching the pinned connection. This section states exactly what happens when that design assumption is violated, since the enforced behavior (fail fast in some cases, block in others, one clean winner in a genuine race) needs to match what's documented here rather than be discovered by trial and error.

### Ordinary concurrent commands: serialized, not parallel

Two commands issued concurrently against the same `TransactionContext` do not corrupt anything and do not run in parallel — the second one blocks on the internal lock until the first finishes, then proceeds. There is no timeout on this wait and no `SemaphoreFullException`-style rejection; it's ordinary mutual exclusion. If you need real concurrency, use separate transactions (or no transaction) against separate connections, not one `TransactionContext` shared across concurrent workers.

### An open reader blocks everything else — and fails fast instead of deadlocking

`ExecuteReaderAsync` on a transaction holds that same lock for as long as the returned reader stays open (until it reaches EOF or is disposed) — not just for the duration of executing the command. While a reader is open:

- **Any other operation on the same transaction — a command, `CommitAsync`, `RollbackAsync`, `SavepointAsync`, `RollbackToSavepointAsync`, or `Dispose`/`DisposeAsync` — throws `InvalidOperationException` immediately** ("Cannot execute another command, or commit/roll back this transaction, while a reader opened on it is still active. Dispose the reader (or finish consuming it) first.") rather than blocking. This applies identically whether the second attempt comes from the same logical flow (a bug — nested reentrant use) or a genuinely different thread sharing the same `TransactionContext` reference — the lock has no way to distinguish the two, and blocking in either case would either hang forever or eventually fail at the provider anyway (most providers reject a second command while a reader is open on the same connection). Proven for both cases: `TransactionReaderLockLifetimeTests.ExecuteReaderAsync_InTransaction_NestedOperationOnSameFlow_ThrowsImmediately_WhileReaderOpen` and `..._AnotherThread_AlsoThrowsImmediately_WhileReaderOpen`; the completion/savepoint side is covered by `TransactionCompletionReaderGuardTests.cs` (`Commit`/`CommitAsync`/`Rollback`/`RollbackAsync`/`Dispose`/`SavepointAsync`/`RollbackToSavepointAsync` all `_WhileReaderOpen_Throws...`).
- Dispose the reader (or finish consuming it) before issuing another operation on that transaction. This is the same rule that applies to a plain `IDatabaseContext`'s reader leases — a `TransactionContext` doesn't relax it, and the fail-fast behavior here is specifically there to surface that mistake immediately instead of hanging.
- A failed attempt (rejected because a reader is open) leaves the transaction otherwise unaffected and retryable — it does not mark the transaction completed, and does not touch `_committed`/`_rolledBack`.

### Commit vs. Rollback race: exactly one wins

If `Commit`/`CommitAsync` and `Rollback`/`RollbackAsync` are called concurrently on the same transaction, exactly one succeeds and the other throws `InvalidOperationException` ("Transaction already completed.") — never both, never neither, and the underlying connection is released exactly once regardless of which one won. Proven by `TransactionContextTests.CommitAndRollback_RaceOnlyOneSucceeds` (asserts exactly one of the two calls threw, the other didn't, and the connection-release count is exactly 1).

### Commit/Rollback vs. Dispose race

`Dispose`/`DisposeAsync` never tears down a transaction that another thread is actively completing. If `Dispose` can't immediately acquire the completion lock (because a concurrent `Commit`/`Rollback` already holds it), it logs and returns without attempting its own rollback — the in-flight `Commit`/`Rollback` call owns disposing the transaction and connection when it finishes, exactly once, regardless of which caller "started" the disposal. Proven by `TransactionContextDisposeRaceTests.cs`: `Commit_ConcurrentDispose_TransactionNotDisposedUntilCommitCompletes`, `Rollback_ConcurrentDispose_...`, their `Async` counterparts, and `Commit_ThenDispose_DisposesTransactionExactlyOnce`.

### Cancellation leaves the transaction fully untouched

A `CancellationToken` passed to `CommitAsync`/`RollbackAsync` that is already cancelled (or becomes cancelled before the internal completion lock is acquired) causes `OperationCanceledException` to propagate **unwrapped** — matching this project's general exception-hierarchy rule that cancellation is never translated into a `DatabaseException` — and leaves the transaction in exactly its pre-call state: `IsCompleted`/`WasCommitted`/`WasRolledBack` all remain `false`, and a subsequent call with a fresh, non-cancelled token succeeds normally. There is no half-completed state to clean up after a cancelled attempt. Proven by `TransactionContextTests.CommitAsync_WithAlreadyCancelledToken_LeavesTransactionFullyUntouched_ThenCommitSucceeds` and its `Rollback` counterpart.

### Savepoints share the same lock as ordinary commands

`SavepointAsync`/`RollbackToSavepointAsync` acquire the identical internal lock an ordinary command does before running their SQL — they are not a separate, lighter-weight operation. This means they queue behind a concurrent command exactly like any other operation, and fail fast with the same `InvalidOperationException` if a reader is currently open on the transaction.

### Mode locks: `DbMode.SingleConnection`'s transaction gate

Under `DbMode.SingleConnection` — where one physical connection is shared by the entire `DatabaseContext`, transactional and non-transactional work alike — beginning a transaction acquires a dedicated gate (bounded by `ModeLockTimeout`) for the transaction's **entire lifetime**, not just around individual commands. This is a separate lock from the one described above: it serializes the transaction against *other, non-transactional* callers of the same shared connection (an ordinary write issued while a transaction is open elsewhere correctly queues behind it, rather than being silently absorbed into or corrupting the open transaction), while the reader/reusable lock above serializes operations *within* the transaction itself. This gate is a no-op under every other `DbMode` — `Standard`, `SingleWriter`, and `PreventDatabaseUnload` transactions each get an ordinary pooled connection that isn't shared with concurrent non-transactional work in the first place, so there's nothing for this second gate to arbitrate.

### Known, accepted limitation (not closed)

A narrow TOCTOU remains between a command's internal "is this transaction already completed" check and the moment it actually acquires the lock — closing it fully would require a single atomic gate distinguishing "begin an ordinary operation" from "begin completion," a larger redesign than the current per-operation locking. Every realistic, materialized interleaving described above (command vs. commit, reader vs. commit, command vs. rollback, reader vs. dispose, savepoint vs. command) is covered; this residual gap is a race between two checks that would need to land in an implausibly narrow window to matter in practice, and is documented here as a deliberate, accepted limitation rather than an oversight.

## Isolation resolution and degradation

Resolving an `IsolationProfile` to a concrete `IsolationLevel` is not always exact. `IIsolationResolver.ResolveWithDetail` classifies the resolved level against the profile's canonical guarantee and returns an `IsolationResolution` with a `Degraded` flag and an `IsolationResolutionKind` (`Exact`, `Higher`, or `Lower` — `Lower` always implies `Degraded`).

Both `BeginTransaction`/`BeginTransactionAsync` overloads that accept an `IsolationProfile` also accept an optional `IsolationResolutionPolicy` (default `AllowHigher`):

```csharp
await using var tx = await context.BeginTransactionAsync(
    IsolationProfile.StrictConsistency,
    ExecutionType.Write,
    cancellationToken,
    IsolationResolutionPolicy.AllowHigher); // default — never silently weakens isolation
```

| Policy | Meaning |
|--------|---------|
| `ExactOnly` | No substitution permitted; throws `NotSupportedException` if the dialect doesn't offer the exact ideal level for the profile. |
| `AllowHigher` (default) | A strictly-stronger available level may substitute for an unavailable ideal. Never opts into a weaker guarantee. |
| `AllowLower` | A strictly-weaker level may substitute; any resolution using this is reported as `Degraded`. |
| `AllowAny` | `AllowHigher \| AllowLower`; prefers exact, then higher, then lower. |

Degradation can still happen under the default `AllowHigher` policy without the caller opting into anything: some engines simply have no level that satisfies a profile's canonical guarantee at all (e.g. TiDB and Snowflake don't offer a true `Serializable`, so `IsolationProfile.StrictConsistency` resolves to their strongest available level, which is weaker than the profile's ideal). `DatabaseContext` logs a warning in this case rather than throwing or failing silently — the caller isn't asking for a downgrade, but the engine can't fully honor the request either way.
