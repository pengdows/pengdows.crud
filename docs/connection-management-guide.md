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
    SingleWriter = 2,   // One pinned writer, concurrent ephemeral readers
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
* Holds one persistent write connection open.
* Acquires ephemeral read-only connections as needed.
* Used automatically for file-based SQLite/DuckDB and for named in-memory databases that use `Mode=Memory;Cache=Shared` so multiple connections share the same database (see Connection Pooling).

### SingleConnection
* All work — reads and writes — is funneled through a single pinned connection.
* Used automatically for isolated in-memory SQLite/DuckDB where each `:memory:` connection would otherwise create its own databa
se (see Connection Pooling).

## Best Practices

* **Use Standard in production** for scalability and correctness.
* KeepAlive, SingleWriter, and SingleConnection are best suited for embedded/local DBs or dev/test.
* Each DatabaseContext can be safely used as a singleton (via DI or subclassing).

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