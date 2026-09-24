# Ownership and Shutdown Contract

This doc states, precisely, what each object in the library owns, what happens when it's
disposed, and what exception a caller gets for using something after it's gone. It complements
`docs/connection-pooling.md` (the admission-control mechanics) and
`docs/architecture.md` (the broader lifecycle/locking model) rather than replacing them.

## What each object owns

| Object | Owns | Does not own |
|---|---|---|
| `IDatabaseContext` (`DatabaseContext`) | Its writer/reader `PoolGovernor` instances; its writer/reader `DbDataSource` (if internally created — a caller-supplied one is never disposed, see below); its persistent connection (the `SingleConnection` pinned connection or the `PreventDatabaseUnload` sentinel); its `MetricsCollector` | The provider's own connection pool (ADO.NET-managed — see `docs/architecture.md`'s "Provider Connection Pooling" section); anything a `TransactionContext`, reader, or gateway is separately holding |
| `ITransactionContext` (`TransactionContext`) | One pinned physical connection for its entire lifetime; the connection's user lock for the transaction's duration | The context that created it (a `TransactionContext` is operation-scoped — see CLAUDE.md's "Transactions" section — never store it as a field) |
| `ITrackedReader` | The `DbDataReader`, the `DbCommand` that produced it, the connection lease (and its governor permit, when the connection is ephemeral), and every lock layer acquired to open it | Nothing beyond its own lease — a reader never owns the context or transaction it was opened from |
| `ITableGateway<TEntity,TRowID>` / `IPrimaryKeyTableGateway<TEntity>` | Its compiled reader-plan cache and `TypeMapRegistry`-derived entity metadata (gateway-lifetime, not tied to any one context — see `docs/cache-and-context-contract.md`) | No connection, transaction, or context — every call takes the operation context as a parameter (or defaults to the constructor context); gateways are stateless with respect to database resources |
| `PreventDatabaseUnload` sentinel | Nothing beyond its own connection and the governor permit it holds for the context's lifetime | Never runs application work — see `docs/connection-pooling.md` and CLAUDE.md's `PreventDatabaseUnload` section |
| `ITenantContextRegistry` (`TenantContextRegistry`) | Every `IDatabaseContext` it has created, for as long as it's cached | Nothing beyond that — see `docs/connection/multitenancy.md` for the full tenant lifecycle contract |

**There is no public raw `DbConnection` accessor — but there is one raw `DbDataSource`
accessor.** `IDatabaseContext` (`pengdows.crud.abstractions/IDatabaseContext.cs`) exposes no member
that returns an open `DbConnection`; connection acquisition (`GetConnection`/`GetConnectionAsync`)
is `internal`, so the ordinary way to run a command is through
`CreateSqlContainer`/`BeginTransaction`, both of which route through admission control
(`PoolGovernor`) and the tracked-connection/reader-lease machinery. `IDatabaseContext` does,
however, expose `DbDataSource? DataSource { get; }` — the writer-side data source the context was
built with (a caller-supplied one such as `NpgsqlDataSource`, or one the context created itself;
`null` when there is none), and `ITransactionContext` forwards its parent context's value. A
connection created from it via `DataSource.CreateConnection()` is outside the governed system
entirely: no admission limits, no session settings, no metrics attribution, and no disposal
tracking. Treat it as a provider-level escape hatch for interop only, never as an execution path.

## `PoolGovernor` admission lifecycle

Each governor (one per read/write pool, absent in `DbMode.SingleConnection`) has an admission
state independent of `SafeAsyncDisposableBase.IsDisposed`:

- **Open** (default): `Acquire`/`TryAcquire`/`AcquireAsync`/`TryAcquireAsync` all work normally.
- **`Close()`** (idempotent): every subsequent acquire call throws `ObjectDisposedException`
  immediately, before touching the underlying semaphore. `Close()` does **not** wait for existing
  holders to release — it only stops new admission. This is what makes the next step a real
  guarantee rather than best-effort: once closed, in-use permits can only decrease, never increase.
- **`WaitForDrainAsync(timeout)`**: waits until in-use permits reach zero, or throws
  `TimeoutException` if the timeout elapses first. Only meaningful after `Close()` — calling it on
  a still-open governor can never reach a stable zero if new work keeps being admitted.
- **`Dispose()`**: calls `Close()` (if not already closed) and disposes the owned semaphore/
  turnstile. Disposing without first draining is safe from a memory-safety standpoint but can
  throw `ObjectDisposedException` from a still-in-flight holder's own `Release()` call — which is
  exactly why context disposal drains before disposing, and skips disposal of a governor that
  failed to drain (see below).

## `DatabaseContext` shutdown sequence

`SafeAsyncDisposableBase` flips the context's `IsDisposed` flag first, then runs
`DisposeManaged()`/`DisposeManagedAsync()` (sync/async disposal share this structure):

1. Unsubscribe from the metrics collector's own change event.
2. Dispose the persistent connection, if any (`SingleConnection` pinned connection /
   `PreventDatabaseUnload` sentinel).
3. Dispose the connection-open coordination primitives (`_connectionOpenLocker`/`_connectionOpenGate`).
4. **Drain each governor (writer first, then reader) with `PoolAcquireTimeout` as the drain
   timeout, then dispose it** — a *different* use from ordinary slot acquisition, but the same
   configured value (`DatabaseContextConfiguration.PoolAcquireTimeout`, default 5s). There is no
   separate "shutdown timeout" setting. Admission is not closed before the drain wait begins; new
   work is kept out by the context's own disposed flag instead (see the next section), and the
   governor's `Dispose()` closes admission once the drain completes.
5. **If a governor times out (or is cancelled) draining**, a warning is logged and that governor
   is deliberately left undisposed — a lease may still be genuinely outstanding, and disposing
   its semaphore out from under the holder would make that holder's `Release()` throw.
6. Dispose the owned `DbDataSource`(s) — this runs whether or not the drains succeeded. A
   caller-supplied `DbDataSource` (via the `DatabaseContext(configuration, dataSource, factory,
   loggerFactory)` overload) is never disposed by the context; only internally created ones are.
   Exceptions from data-source disposal are swallowed.
7. Base-class disposal completes (lifetime listeners, `DisposeUnmanaged`).

The context itself is fully, terminally disposed regardless of whether step 5 applied.

**Sync vs. async disposal** run the same sequence. `DisposeManagedAsync` uses async-native
implementations for each phase (`DisposeAsync` on the persistent connection,
`DisposePoolGovernorsAsync`, `DisposeOwnedDataSourcesAsync`); the synchronous `Dispose()` path
blocks the calling thread on the governor drain wait (`WaitForDrainAsync(...).GetAwaiter().GetResult()`),
so prefer `DisposeAsync()` when a drain could take a while.

**Constructor-failure cleanup** (a failed `DatabaseContext` construction, not a normal disposal)
is much narrower than the sequence above: if connection initialization fails, the initialization
connection is disposed (any exception from that is ignored), the failure is logged, and the
original construction exception is rethrown unchanged. The full disposal sequence does not run
for a context whose constructor threw.

## The exception you get after disposal

**`ObjectDisposedException`**, for every operation that would touch a connection — but note
the one entry point that does *not* reject on its own:

- `CreateSqlContainer(...)` on a disposed `DatabaseContext` does **not** throw — it still returns
  a container (it only builds SQL text and parameters). The rejection happens when that container
  is executed (next bullet). (`TransactionContext.CreateSqlContainer` is different: it throws
  `InvalidOperationException` once the transaction has completed — which includes a disposed
  transaction, since `Dispose()` rolls back an uncompleted one.)
- `BeginTransaction(...)`/`BeginTransactionAsync(...)` — rejected with `ObjectDisposedException`.
- A container executed against a disposed context — whether it was created before or after
  disposal (`ExecuteNonQueryAsync`, `ExecuteReaderAsync`, `ExecuteScalarOrNullAsync`, ...) —
  throws `ObjectDisposedException`, with **zero admission side effects**:
  `PoolStatisticsSnapshot.TotalAcquired` for both pools stays exactly where it was before the
  attempt, proving the disposal check fires before any connection/governor interaction, not
  after a wasted acquisition attempt (`pengdows.crud.Tests/DatabaseContextTerminalStateTests.cs`).
- The internal connection-acquisition paths (`GetConnection(...)`/`GetConnectionAsync(...)`,
  not public API) check the disposed flag the same way.
- A caller racing a concurrent `DisposeAsync()` is rejected by that same disposed-flag check if it
  reaches the execution path after disposal began. An operation that had already passed the check
  before disposal began may still acquire a governor permit during the drain wait — which is
  exactly what the drain waits for (up to `PoolAcquireTimeout`).

In practice this is a single, predictable exception type at execution time — never a
`NullReferenceException` from a nulled-out field, never a provider-level exception from a
torn-down connection.

## `ITransactionContext` shutdown

A `TransactionContext` pins one physical connection for its entire lifetime. `Commit()`/
`Rollback()` (and `Dispose()`'s auto-rollback) all route through the same completion path,
serialized by an internal completion lock (a `SemaphoreSlim` bounded by the context's
`ModeLockTimeout`, throwing `InvalidOperationException` if it can't be acquired in time). That
lock is deliberately separate from the transaction's user/command lock — completion does not wait
for, or block against, an in-flight command or an open reader on the transaction, so dispose
readers before committing. `IsCompleted` flips to `true` exactly once, atomically, at the *start*
of the first completion attempt (before the provider's commit/rollback runs) and stays `true` even
if that commit/rollback then fails (surfaced as `TransactionException`); a second attempt observes
it and throws `InvalidOperationException("Transaction already completed.")` rather than racing
the connection. Whatever the outcome, the completion path releases the pinned connection back to
whatever pool/mode it came from, and a later `Dispose()` does not attempt a second rollback.

## `ITrackedReader` shutdown

`Dispose()`/`DisposeAsync()` release, in order: the underlying `DbDataReader` (after recording
the reader's metrics once), the `DbCommand`, the connection (and its governor permit, when the
connection is ephemeral), every lock layer acquired to open the reader, and the lifetime-listener
notification. The steps run sequentially — an exception from an earlier step (other than a known
MySql.Data `NullReferenceException` quirk during reader/command disposal, which is suppressed)
propagates and the remaining steps are skipped, so a provider that throws while disposing its
reader can leave the rest of the lease unreleased.

`Close()` is **not** equivalent to `Dispose()`: it only closes the underlying `DbDataReader`. It
does not dispose the command, release the connection/permit, or release the locks — always
dispose the reader (or read to end-of-results) rather than relying on `Close()`.

Reaching end-of-results (`Read()`/`ReadAsync()` returning `false`) triggers the same full disposal
automatically — confirmed directly in `TrackedReader.ReadAsync(CancellationToken)`
(`pengdows.crud/wrappers/TrackedReader.cs`): it awaits the underlying reader's `ReadAsync`, and the
moment that returns `false` it calls `DisposeAsync()` on itself before returning — see
`docs/architecture.md`'s "Reader-as-Lease Model" for the auto-disposal rationale.

### Streaming and cancellation

`LoadStreamAsync`/`RetrieveStreamAsync` (`BaseTableGateway.Core.cs`, `TableGateway.Core.cs`) open
their `ITrackedReader` with `await using` *inside* the `IAsyncEnumerable<T>` iterator method
itself — not in the caller. This is what makes disposal unconditional regardless of how the
consumer's `await foreach` exits:

- **Normal completion (EOF)** — the reader already self-disposed via the EOF path above; the
  iterator's own `await using` disposal is a no-op on an already-disposed object.
- **Early `break` out of `await foreach`** — the compiler-generated iterator `DisposeAsync()`
  unwinds the method's `await using` scope, disposing the still-open reader even though EOF was
  never reached.
- **A cancelled `CancellationToken`** — the token flows into `ReadAsync(cancellationToken)` (via
  `[EnumeratorCancellation]`, so `await foreach (... WithCancellation(ct))` threads it through
  correctly). A mid-read cancellation throws `OperationCanceledException` *before* the EOF
  self-dispose path runs, but the same `await using` unwinding disposes the reader as the exception
  propagates out of the iterator. Either way — EOF, break, or cancellation — the reader, its
  command, its connection/permit, and its locks are released exactly once.
- `RetrieveStreamAsync` nests one level deeper (`await using var container` wrapping a
  `LoadStreamAsync` call it forwards the same token into), so both layers unwind together on any
  of the three exit paths above.
