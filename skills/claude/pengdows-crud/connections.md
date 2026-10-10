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

- Keeps a single sentinel connection open (never used for work) to prevent idle disconnection
- Otherwise behaves like `Standard`
- Modern use cases: long-running Lambda, Aurora Serverless cold-start prevention, RDS Proxy idle disconnection, LocalDB unload prevention

### SingleWriter

- Uses the Standard lifecycle but enforces `MaxConcurrentWrites = 1` with a writer-preference gate, keeping readers ephemeral while writers serialize.
- Ideal for the file-based embedded engines (SQLite, DuckDB, Microsoft Access, pengdows.flatfile) and shared in-memory databases where writes must serialize without pinning a dedicated connection. It is a write-admission policy, not a SQLite workaround.

### SingleConnection

- All work — reads and writes — is funneled through a single pinned connection
- Used automatically for in-memory SQLite (see Connection Pooling)

## Pool Size Priority

- Each pool (reads, writes) is sized on its own from the first source that applies: `MaxConcurrentReads`/`MaxConcurrentWrites` > the pool-size setting in that pool's connection string (`Max Pool Size`, `MaxPoolSize`, `Maximum Pool Size`; reads use `ReadOnlyConnectionString` when supplied) > the dialect's default (100). A disagreement is resolved in favour of configuration, with a warning
- The winning size is used for both the governor and the provider's own pool (it is written into the connection string), so they always agree
- Then, in order: the absolute ceiling of 512; mode rules (`SingleWriter` = exactly one writer; `PreventDatabaseUnload` raises a pool below 2 to 2); the optional server ceiling
- `MaxConcurrentReads = 0` / `MaxConcurrentWrites = 0` forbids a pool (a write size of 0 makes the context read-only); a `0` in a connection string is treated as unset; a negative size throws
- SQLite and DuckDB run in-process, have no provider pool and ignore a connection-string pool size

## Server Connection Ceiling (opt-in)

- `ClampPoolsToServerConnectionLimit = true` (default `false`) reads the server's own connection limit while the context initializes and never sizes a pool above it; `ResourceConnectionHeadroom` (default `0`) leaves that many server connections free for other clients
- Supported for PostgreSQL/YugabyteDB (`max_connections` minus the reserved connections), MySQL/MariaDB (`@@max_connections`, or a lower per-user limit) and SQL Server (`user connections`; `0` = unlimited = unknown); other engines are left unchanged
- Reader and writer provider pools are separate and hold connections open, so when both target one server and together ask for more than it allows, its limit is divided between them in proportion to what each asked for; each role's governor and provider pool are sized to its share
- A read replica on another host is probed and budgeted on its own; a pool sized `0` stays forbidden
- The budget belongs to one `DatabaseContext` — keep one context per connection string (see `EnforceUniqueConnectionString`)

## Constructor Overloads

```csharp
// (1) String-based provider — supports DbMode and ReadWriteMode directly
new DatabaseContext(connectionString, "Microsoft.Data.SqlClient",
    mode: DbMode.Best, readWriteMode: ReadWriteMode.ReadWrite,
    loggerFactory: null, readOnlyConnectionString: null);

// (2) Factory object — always DbMode.Best; use IDatabaseContextConfiguration for full control
new DatabaseContext(connectionString, SqlClientFactory.Instance);
new DatabaseContext(connectionString, SqlClientFactory.Instance, readOnlyConnectionString: "...");

// (3) Full configuration object
new DatabaseContext(new DatabaseContextConfiguration
{
    ConnectionString = connectionString,
    DbMode = DbMode.SingleWriter,
    ReadWriteMode = ReadWriteMode.ReadWrite
}, SqlClientFactory.Instance, loggerFactory);

// (4) Native DbDataSource (e.g. NpgsqlDataSource) — best performance for PostgreSQL
new DatabaseContext(configuration, npgsqlDataSource, NpgsqlFactory.Instance, loggerFactory);
```

Every constructor has an asynchronous counterpart that returns the same initialized context
without blocking the calling thread (async open, detection probes and session setup; cancellation
propagates as `OperationCanceledException` and releases what initialization opened):

```csharp
var context = await DatabaseContext.CreateAsync(configuration, NpgsqlFactory.Instance, loggerFactory, ct);
var context2 = await DatabaseContext.CreateAsync(connectionString, SqlClientFactory.Instance);
```

## DI Registration

```csharp
// Standard mode (default, recommended for production)
services.AddSingleton<IDatabaseContext>(sp =>
    new DatabaseContext(connectionString, SqlClientFactory.Instance));

// Specific mode — use IDatabaseContextConfiguration
services.AddSingleton<IDatabaseContext>(sp =>
    new DatabaseContext(
        new DatabaseContextConfiguration { ConnectionString = connectionString, DbMode = DbMode.SingleWriter },
        SqlClientFactory.Instance));

// Auto-select best mode (factory overload always uses DbMode.Best)
services.AddSingleton<IDatabaseContext>(sp =>
    new DatabaseContext(connectionString, SqlClientFactory.Instance));
```

## Best Practices

- **Use Standard in production** for scalability and correctness
- KeepAlive, SingleWriter, and SingleConnection are best suited for embedded/local DBs or dev/test
- Each DatabaseContext can be safely used as a singleton (via DI or subclassing)

## Benefits

- Avoids connection starvation and excessive licensing costs (per active connection)
- Plays well with provider-managed pooling (see Connection Pooling)
- Handles embedded/local DB quirks without manual intervention

## IsolationProfile

Portable transaction isolation profiles that map to the optimal native level for the connected database:

```csharp
public enum IsolationProfile
{
    SafeNonBlockingReads,  // MVCC snapshot (where supported) — avoids blocking reads
    StrictConsistency,     // Serializable / full isolation
    FastWithRisks          // Lowest isolation; maximum throughput, accepts dirty/non-repeatable reads
}
```

Usage:
```csharp
await using var txn = await context.BeginTransactionAsync(IsolationProfile.SafeNonBlockingReads, ExecutionType.Write, ct);
try
{
    // perform operations using txn
    await txn.CommitAsync(ct);
}
catch
{
    if (!txn.IsCompleted) // a failed CommitAsync has already completed the transaction
    {
        await txn.RollbackAsync(ct);
    }
    throw;
}
```

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

## ReadWriteMode

`ReadWriteMode` controls whether the context enforces read-only or read-write access:

```csharp
public enum ReadWriteMode
{
    ReadOnly  = 1,   // Blocks write operations; read-only session settings applied
    WriteOnly = 2,   // Reserved for write-only pools
    ReadWrite = 3,   // Default — full read/write access
}
```

Combined with a `readOnlyConnectionString`, pengdows.crud creates separate read and write data sources, routing `ExecutionType.Read` operations to the read-only pool automatically.

## Default Pool Sizes by Database

| Database | Provider Default | Recommended |
|----------|-----------------|-------------|
| SQL Server | 100 | 50-200 |
| PostgreSQL | 100 | 20-100 (use PgBouncer if more needed) |
| MySQL/MariaDB | 100 | 50-200 |
| Oracle | 100 | 50-200 |
| SQLite | Unlimited | 1-20 |
| DuckDB | Unlimited | 1-8 |
| CockroachDB | 100 (Npgsql) | 20-100 |
| YugabyteDB | 100 (Npgsql) | 20-100 |
| TiDB | 100 (MySQL) | 50-200 |
| Firebird | 50 | 10-50 |
| Snowflake | provider-managed | provider default |

## Related Pages

- Connection Pooling — Database-specific pooling behavior
- Transactions — Transaction management patterns
- Supported Databases — Database provider support matrix
