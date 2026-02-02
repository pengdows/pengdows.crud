# Connection Management and DbMode

**pengdows.crud** handles connections with a strong bias toward performance, predictability, and safe concurrency.
At the heart of this is **DbMode**, which defines how each DatabaseContext manages its connection lifecycle.

## Overview

The philosophy is simple:
* Open connections late — only when needed
* Close connections early — as soon as possible
* Respect database-specific quirks (see Connection Pooling for SQLite and LocalDB rules)

Advantages:
* Prevents exhausting your connection pool
* Avoids leaking resources or unclosed connections
* Reduces cost in cloud environments by minimizing active resource usage

## DbMode Enum

```csharp
[Flags]
public enum DbMode
{
    Standard = 0,       // Recommended for production
    KeepAlive = 1,      // Keeps one sentinel connection open
    SingleWriter = 2,   // Standard lifecycle + single-write-slot governor
    SingleConnection = 4, // All work goes through one pinned connection
    Best = 15
}
```

## Descriptions

Use the lowest number (closest to Standard) possible for best results. Best, will select the best operation mode for the connected DB.

### Standard
* Recommended for production.
* Each operation opens a new connection from the pool and closes it after use, unless inside a transaction.
* Fully supports parallelism and provider connection pooling.

### KeepAlive
* Keeps a single sentinel connection open (never used for work) to prevent unloads in some embedded/local DBs.
* Otherwise behaves like `Standard`.

### SingleWriter
* Uses the Standard lifecycle (no pinned connections) but adds a governor profile that enforces `MaxConcurrentWrites = 1` and `MaxConcurrentReads = N`.
* A writer-preference turnstile pauses new readers when a writer is queued, guaranteeing fairness for SQLite/DuckDB workloads.
* Triggered automatically for file-based SQLite/DuckDB and shared in-memory pools so writers serialize safely without blocking the entire process (see Connection Pooling).

### SingleConnection
* All work — reads and writes — is funneled through a single pinned connection.
* Used automatically for isolated in-memory SQLite/DuckDB where each `:memory:` connection would otherwise create its own databa
se (see Connection Pooling).

## Best Practices

* **Use Standard in production** for scalability and correctness.
* KeepAlive, SingleWriter, and SingleConnection are best suited for embedded/local DBs or dev/test.
* Each DatabaseContext can be safely used as a singleton (via DI or subclassing).

## Shared connection locking & timeouts

Persistent connections (KeepAlive's sentinel and the SingleConnection pin) rely on `RealAsyncLocker` instances that serialize operations through a shared `SemaphoreSlim`. The lock includes a default `ModeLockTimeout` of 30 seconds (`DatabaseContextConfiguration.ModeLockTimeout` / `IDatabaseContextConfiguration.ModeLockTimeout`); exhausting that window throws `ModeContentionException`, which includes a `ModeContentionSnapshot` describing the number of waiters and timeouts. Tune the timeout (or set it to `null`) to trade between waiting for transient contention and failing fast when the pool is saturated, and monitor `ModeContentionStats` through logs/metrics if you need to understand which operations are queuing.

## Pool governors & acquisition windows

The context also installs read and write `PoolGovernor` instances (enabled by `EnablePoolGovernor`/`DatabaseContextConfiguration.EnablePoolGovernor`) that gate access to each database provider’s connection pool. Each governor issues `PoolPermit` tokens with a default `PoolAcquireTimeout` of 5 seconds (`DatabaseContextConfiguration.PoolAcquireTimeout`) before opening a connection. Shared connections grab their permit during initialization so the pool accounts for the pinned connection (KeepAlive/SingleConnection). SingleWriter mode enforces `MaxConcurrentWrites = 1`, `MaxConcurrentReads = N`, and, when configured, a writer-preference turnstile so readers stop entering while a writer waits. If a governor cannot deliver a permit within the timeout, a `PoolSaturatedException` is raised along with statistics for the queue depth and permit usage so you can scale the pool or reduce concurrency. Override `MaxConcurrentReads`/`MaxConcurrentWrites` (legacy `ReadPoolSize`/`WritePoolSize`) to clamp the governors to your desired limits or disable the governor entirely for experiments.

## Benefits

* Avoids connection starvation and excessive licensing costs (per active connection).
* Plays well with provider-managed pooling (see Connection Pooling).
* Handles embedded/local DB quirks without manual intervention.

## Integration with Transactions

* Inside a TransactionContext, the pinned connection stays open for the life of the transaction.
* Outside transactions, connections are opened per-operation and closed immediately after.

## Observability

* Tracks current and max open connections with thread-safe `Interlocked` counters.
* Useful for tuning pool sizes and spotting load issues.

## Timeout Recommendations

* Set connection timeouts as **low as reasonable** to avoid hanging on transient failures.
* Because pengdows.crud reconnects for every call, long timeouts are unnecessary.

## Related Documentation

* [CONNECTION-MODES.md](../CONNECTION-MODES.md) - Technical invariants and implementation details
* [Connection Pooling](connection-pooling.md) - Database-specific pooling behavior
* [Transactions](transactions.md) - Transaction management patterns
* [Supported Databases](supported-databases.md) - Database provider support matrix
* [Primary Keys and Pseudokeys](primary-keys-pseudokeys.md) - Entity key patterns
