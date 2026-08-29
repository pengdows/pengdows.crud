# Ownership and Shutdown Contract

This doc states, precisely, what each object in the library owns, what happens when it's
disposed, and what exception a caller gets for using something after it's gone. It complements
`docs/connection/connection-pooling.md` (the admission-control mechanics) and
`docs/architecture.md` (the broader lifecycle/locking model) rather than replacing them.

## What each object owns

| Object | Owns | Does not own |
|---|---|---|
| `IDatabaseContext` (`DatabaseContext`) | Its writer/reader `PoolGovernor` instances; its writer/reader `DbDataSource` (if internally created — see below); any `PreventDatabaseUnload` sentinel connections; its `MetricsCollector`; its unique-connection-string claim/warning registration | The provider's own connection pool (ADO.NET-managed — see `docs/architecture.md`'s "Provider Connection Pooling" section); anything a `TransactionContext`, reader, or gateway is separately holding |
| `ITransactionContext` (`TransactionContext`) | One pinned physical connection for its entire lifetime; the connection's user lock for the transaction's duration | The context that created it (a `TransactionContext` is operation-scoped — see CLAUDE.md's "Transactions" section — never store it as a field) |
| `ITrackedReader` | The `DbDataReader`, the `DbCommand` that produced it, the connection lease (and its governor permit, when the connection is ephemeral), and every lock layer acquired to open it | Nothing beyond its own lease — a reader never owns the context or transaction it was opened from |
| `ITableGateway<TEntity,TRowID>` / `IPrimaryKeyTableGateway<TEntity>` | Its compiled reader-plan cache and `TypeMapRegistry`-derived entity metadata (gateway-lifetime, not tied to any one context — see `docs/planning/future-work.md`'s CORE-019 resolution) | No connection, transaction, or context — every call takes the operation context as a parameter (or defaults to the constructor context); gateways are stateless with respect to database resources |
| `PreventDatabaseUnload` sentinel | Nothing beyond its own connection and the governor permit it holds for the context's lifetime | Never runs application work — see `docs/connection/connection-pooling.md` and CLAUDE.md's `PreventDatabaseUnload` section |
| `ITenantContextRegistry` (`TenantContextRegistry`) | Every `IDatabaseContext` it has created, for as long as it's cached | Nothing beyond that — see `docs/connection/multitenancy.md` for the full tenant lifecycle contract |

**There is no public raw `DbDataSource`/`DbConnection` escape hatch.** `IDatabaseContext`
(`pengdows.crud.abstractions/IDatabaseContext.cs`) exposes no property that returns the underlying
`DbDataSource` or an open `DbConnection` — confirmed by inspection, zero such members exist. The
only way to run a command is through `CreateSqlContainer`/`BeginTransaction`, both of which route
through admission control (`PoolGovernor`) and the tracked-connection/reader-lease machinery. This
is deliberate: an escape hatch would let application code bypass admission limits, session-setting
guarantees, and metrics attribution entirely.

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
  exactly why context disposal always closes and drains before disposing (see below).

## `DatabaseContext` shutdown sequence

`DisposeManaged()`/`DisposeManagedAsync()` (sync/async disposal share this structure):

1. Unsubscribe from the metrics collector's own change event.
2. Dispose any persistent connection(s) (`SingleConnection`/`PreventDatabaseUnload` sentinels).
3. Dispose the connection-open coordination primitives (`_connectionOpenLocker`/`_connectionOpenGate`).
4. **Close both governors first, then drain each with `PoolAcquireTimeout` as the drain
   timeout** — a *different* timeout from the one used for ordinary slot acquisition, but the
   same configured value (`DatabaseContextConfiguration.PoolAcquireTimeout`, default 5s). There is
   no separate "shutdown timeout" setting.
5. **If both governors drained cleanly**, dispose the owned `DbDataSource`(s), release the
   unique-connection-string claim, and unregister the duplicate-connection warning registration.
   **If either governor timed out draining**, none of that cleanup runs — a lease may still be
   genuinely outstanding, so the data source and uniqueness claim are deliberately leaked rather
   than torn down out from under a caller that hasn't finished yet. A warning is logged either way.
   The context itself is still fully, terminally disposed regardless of which branch ran.
6. Base-class disposal (flips `IsDisposed`, notifies lifetime listeners).

**Sync vs. async disposal** run the identical sequence — `DisposeManagedAsync` is not a thin
wrapper delegating to the sync path or vice versa; each phase has its own async-native
implementation (`DisposePersistentConnectionsAsync`, `DisposeOwnedDataSourcesAsync`, etc.) so
neither path blocks a thread on the other's I/O.

**Constructor-failure cleanup** (a failed `DatabaseContext` construction, not a normal disposal)
runs a similar but not identical sequence — see `docs/planning/future-work.md`'s CORE-025/026/027/028
and TEST-017 entries for the exact phase-by-phase behavior, including that a caller-supplied
`DbDataSource` (via the `DatabaseContext(configuration, dataSource, factory, loggerFactory)`
overload) is never disposed on construction failure, only internally-created ones are, and that a
cleanup failure never replaces or hides the original construction exception.

## The exception you get after disposal

**`ObjectDisposedException`, uniformly**, for every entry point, whether the container/transaction
was created before or after disposal:

- `CreateSqlContainer(...)` — rejected immediately (`ValidateCanCreateContainer` calls
  `ThrowIfDisposed()` before doing anything else).
- `GetConnection(...)`/`GetConnectionAsync(...)` — rejected immediately, before any governor or
  provider interaction.
- `BeginTransaction(...)` — rejected immediately.
- A container created **before** disposal, executed **after** (`ExecuteNonQueryAsync`,
  `ExecuteReaderAsync`, `ExecuteScalarOrNullAsync`, ...) — also throws `ObjectDisposedException`,
  with **zero admission side effects**: `PoolStatisticsSnapshot.TotalAcquired` for both pools stays
  exactly where it was before the attempt, proving the disposal check fires before any
  connection/governor interaction, not after a wasted acquisition attempt.
- A caller racing a concurrent `DisposeAsync()` that is mid-drain (waiting on an outstanding
  lease) gets rejected the same way — `PoolGovernor.Close()` runs synchronously before the drain
  wait begins, so no "post-close" lease is ever granted to a racing acquisition attempt.

This is a single, predictable exception type across every operation — never a `NullReferenceException`
from a nulled-out field, never a provider-level exception from a torn-down connection.

## `ITransactionContext` shutdown

A `TransactionContext` pins one physical connection for its entire lifetime. `Commit()`/
`Rollback()`/`Dispose()` all route through the same completion path, guarded by a reader-aware
lock (`ReusableAsyncLocker`) so completion cannot run concurrently with an active command or
open reader on the transaction — see `docs/planning/future-work.md`'s CORE-023 entry for the exact
race matrix this closes. `IsCompleted` becomes `true` exactly once, atomically, the moment either
`Commit()` or `Rollback()` succeeds (or `Dispose()`'s own auto-rollback path runs); a concurrent
second attempt observes `IsCompleted` already `true` and throws `InvalidOperationException` rather
than racing the connection. After completion, `Dispose()` releases the pinned connection back to
whatever pool/mode it came from and does not attempt a second rollback.

## `ITrackedReader` shutdown

`Close()` and `Dispose()`/`DisposeAsync()` are fully equivalent — `Close()` simply calls
`Dispose()`. Both release, in order: the underlying `DbDataReader`, the `DbCommand`, the
connection (and its governor permit, when ephemeral), every lock layer acquired to open the
reader, and the lifetime-listener notification. If any individual phase throws during cleanup,
every other phase is still attempted (continue-on-failure, not stop-on-first-exception) and the
*first* exception encountered is the one that propagates — a failure disposing the command, say,
never prevents the connection from being released.

Reaching end-of-results (`Read()`/`ReadAsync()` returning `false`) triggers the same full disposal
automatically — see `docs/architecture.md`'s "Reader-as-Lease Model" for the auto-disposal
rationale.
