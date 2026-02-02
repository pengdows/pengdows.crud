# Connection Management and DbMode

pengdows.crud handles connections with a strong bias toward performance, predictability, and safe concurrency. At the heart of this is **DbMode**, which defines how each DatabaseContext manages its connection lifecycle.

## Overview

The philosophy is simple:

- **Open connections late** — only when needed
- **Close connections early** — as soon as possible
- **Respect database-specific quirks** — see Connection Pooling for SQLite and LocalDB rules

**Advantages:**
- Prevents exhausting your connection pool
- Avoids leaking resources or unclosed connections
- Reduces cost in cloud environments by minimizing active resource usage

## DbMode Enum

```csharp
[Flags]
public enum DbMode
{
    Standard = 0,         // Recommended for production
    KeepAlive = 1,        // Keeps one sentinel connection open
    SingleWriter = 2,     // Governor-enforced single writer, concurrent ephemeral readers
    SingleConnection = 4, // All work goes through one pinned connection
    Best = 15             // Auto-select best mode for the database
}
```

## Mode Descriptions

**Use the lowest number (closest to Standard) possible for best results.** `Best` will select the optimal mode for the connected DB.

### Standard

- **Recommended for production**
- Each operation opens a new connection from the pool and closes it after use, unless inside a transaction
- Fully supports parallelism and provider connection pooling

### KeepAlive

- Keeps a single sentinel connection open (never used for work) to prevent unloads in some embedded/local DBs
- Otherwise behaves like `Standard`

### SingleWriter

- Uses the Standard lifecycle but enforces `MaxConcurrentWrites = 1` with a writer-preference gate, keeping readers ephemeral while writers serialize.
- Ideal for file-based SQLite and shared in-memory databases where writes must serialize without pinning a dedicated connection.

### SingleConnection

- All work — reads and writes — is funneled through a single pinned connection
- Used automatically for in-memory SQLite (see Connection Pooling)

## DI Registration

```csharp
// Standard mode (default, recommended for production)
services.AddSingleton<IDatabaseContext>(sp =>
    new DatabaseContext(connectionString, SqlClientFactory.Instance));

// Specific mode
services.AddSingleton<IDatabaseContext>(sp =>
    new DatabaseContext(connectionString, factory, null, DbMode.SingleWriter));

// Auto-select best mode
services.AddSingleton<IDatabaseContext>(sp =>
    new DatabaseContext(connectionString, factory, null, DbMode.Best));
```

## Best Practices

- **Use Standard in production** for scalability and correctness
- KeepAlive, SingleWriter, and SingleConnection are best suited for embedded/local DBs or dev/test
- Each DatabaseContext can be safely used as a singleton (via DI or subclassing)

## Benefits

- Avoids connection starvation and excessive licensing costs (per active connection)
- Plays well with provider-managed pooling (see Connection Pooling)
- Handles embedded/local DB quirks without manual intervention

## Integration with Transactions

- Inside a `TransactionContext`, the pinned connection stays open for the life of the transaction
- Outside transactions, connections are opened per-operation and closed immediately after

## Observability

- Tracks current and max open connections with thread-safe `Interlocked` counters
- Useful for tuning pool sizes and spotting load issues

```csharp
var openConns = context.NumberOfOpenConnections;  // Current count
var maxConns = context.PeakOpenConnections;    // Peak observed
var dbProduct = context.Product;                   // Detected database
var mode = context.ConnectionMode;                 // Current DbMode
```

## Timeout Recommendations

- Set connection timeouts as **low as reasonable** to avoid hanging on transient failures
- Because pengdows.crud reconnects for every call, long timeouts are unnecessary

```csharp
// Good: Short timeout
"Server=localhost;Database=MyDb;Connection Timeout=5;..."

// Avoid: Long timeout
"Server=localhost;Database=MyDb;Connection Timeout=300;..."
```

## Default Pool Sizes by Database

| Database | Provider Default | Recommended |
|----------|-----------------|-------------|
| SQL Server | 100 | 50-200 |
| PostgreSQL | 100 | 20-100 (use PgBouncer if more needed) |
| MySQL/MariaDB | 100 | 50-200 |
| Oracle | 100 | 50-200 |
| SQLite | Unlimited | 1-20 |
| DuckDB | Unlimited | 1-8 |

## Related Pages

- Connection Pooling — Database-specific pooling behavior
- Transactions — Transaction management patterns
- Supported Databases — Database provider support matrix
