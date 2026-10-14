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
    PreventDatabaseUnload = 1, // Keeps one sentinel connection open ([Obsolete] alias: KeepAlive)
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

### PreventDatabaseUnload

- Keeps a single sentinel connection open (never used for work) so an embedded/local database does not unload when idle
- Otherwise behaves like `Standard`
- `Best` selects it for SQL Server LocalDB, Firebird (embedded and client-server) and Db2 LUW; an explicit `Standard` is always honored. It is a knob for other engines (e.g. SQL Server with `AUTO_CLOSE`, proxy/serverless idle disconnection)
- `KeepAlive` is only an `[Obsolete]` alias for the same value (`1`); new code uses `PreventDatabaseUnload`
- The sentinel needs a second pooled connection: a pool below 2 is raised to 2, and a `Best` that would pick it falls back to `Standard` when the configured maximum is below 2

### SingleWriter

- Uses the Standard lifecycle but enforces `MaxConcurrentWrites = 1` with a writer-preference gate, keeping readers ephemeral while writers serialize.
- Ideal for the file-based embedded engines (SQLite, DuckDB, Microsoft Access, pengdows.flatfile) and shared in-memory databases where writes must serialize without pinning a dedicated connection. It is a write-admission policy, not a SQLite workaround.

### SingleConnection

- All work — reads and writes — is funneled through a single pinned connection
- Used automatically (`Best`) for in-memory `:memory:` SQLite/DuckDB only. Never selected for Firebird (`Best` gives it `PreventDatabaseUnload`)
- A `:memory:` database lives and dies with its connection: this mode is required for correctness there, not a persistence or recovery strategy

### Best

| Database | `Best` resolves to |
|----------|--------------------|
| SQLite / DuckDB file, Access, FlatFile | `SingleWriter` (they coerce to it; FlatFile has no in-memory mode) |
| SQLite / DuckDB `:memory:` | `SingleConnection` |
| SQL Server LocalDB, Firebird, Db2 LUW | `PreventDatabaseUnload` |
| every other client-server database (SQL Server, PostgreSQL family incl. CockroachDB/YugabyteDB/Spanner/Aurora, MySQL family incl. MariaDB/TiDB/SingleStore, Oracle, Snowflake, Informix, SAP HANA, InterBase, Sybase ASE, non-LUW Db2) | `Standard` |

`Pooling=false` in a connection string is rejected with `InvalidOperationException` (use `SingleConnection` for one persistent connection).

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
- The split is static (by what each role asked for, not what it uses): both roles at 20 against a server that allows 22 gives 11 and 11, so a context that only reads loses half its capacity. If the context never writes, set `MaxConcurrentWrites = 0`: the writer pool is forbidden, nothing is divided and the reader keeps its full request
- Failures under a spike are callers that waited `PoolAcquireTimeout` (default 5 s) and got `PoolSaturatedException`, not server refusals; size the wait with the slots (about `2 x 17 x slots` calls per two 5 s windows)
- Planned (POOL-003, not in 2.0.6): a per-server ceiling shared by every context pointing at one server, each role keeping its configured size, engaged only when the pools together exceed the server's budget

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
- PreventDatabaseUnload, SingleWriter, and SingleConnection are best suited for embedded/local DBs or dev/test; let `Best` choose them
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

A profile never runs below its guarantee: `BeginTransaction(profile)` throws `TransactionModeNotSupportedException` when the database can't meet it
(`StrictConsistency` on TiDB/Snowflake/SingleStore/Access/FlatFile; `SafeNonBlockingReads` on SQL Server without snapshot isolation).
A write transaction with neither a level nor a profile defaults to `ReadCommitted`; only a read-only transaction uses the `SafeNonBlockingReads` mapping.

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

pengdows.crud's governor uses the explicit max-pool-size keyword in the connection string where the dialect registers one, otherwise the dialect default
(100; `int.MaxValue` for SQLite and DuckDB; 10 for Snowflake). Values below are what the dialects and docs/supported-databases.md record; "not verified" means no recorded provider default.

| Database | Provider default | Recommended |
|----------|-----------------|-------------|
| SQL Server | 100 | 50-200 |
| PostgreSQL | 100 | 20-100 (use PgBouncer if more needed) |
| MySQL / MariaDB / TiDB / SingleStore / Aurora MySQL | 100 | 50-200 |
| Oracle | 100 | 50-200 |
| SQLite | Unlimited | 1-20 |
| DuckDB | Unlimited | 1-8 |
| CockroachDB / YugabyteDB / Spanner / Aurora PostgreSQL | 100 (Npgsql) | 20-100 |
| Firebird | not verified (dialect default 100) | not verified |
| Snowflake | 10 (dialect default) | not verified |
| Db2 | not verified (dialect default 100) | not verified |
| Informix | 100 (builder default); the dialect reads no pool keyword, so it governs on its default 100 | not verified |
| Sybase ASE / SAP HANA / InterBase | not verified; no pool keyword registered, governed on the dialect default 100 | not verified |
| Access / FlatFile | not applicable (no external pooling; both run `SingleWriter`) | not applicable |

## Related Pages

- Connection Pooling — Database-specific pooling behavior
- Transactions — Transaction management patterns
- Supported Databases — Database provider support matrix
