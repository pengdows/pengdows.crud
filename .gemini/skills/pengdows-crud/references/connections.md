# Connection Management and DbMode

`pengdows.crud` handles connections with a strict focus on high throughput, resource predictability, and coordinated execution safety.

---

## The Coordinated Execution Architecture

`DatabaseContext` is the **single execution authority** that coordinates connection lifecycle, `PoolGovernor` admission slots, transaction connection pinning, and dialect session configuration.

### Core Philosophy: "Open Late, Close Early"
- Connections are acquired from the provider pool only when execution begins.
- Disposed/returned to the pool immediately upon command completion (unless pinned by an active `TransactionContext` or `ITrackedReader` lease).
- Prevents connection starvation and scales across thousands of concurrent operations.

---

## DbMode Enum & Semantics

```csharp
public enum DbMode
{
    Standard = 0,         // Production default for client/server databases
    KeepAlive = 1,        // Standard + idle sentinel connection preventing engine unload
    SingleWriter = 2,     // Standard lifecycle + PoolGovernor serialized writer admission
    SingleConnection = 4, // 1 persistent connection serialized with RealAsyncLocker
    Best = 15             // Automatic heuristic selection
}
```

### Detailed Mode Contracts

#### 1. Standard (`0`)
- **Recommended for production servers** (PostgreSQL, SQL Server, MySQL, MariaDB, Oracle).
- Every operation acquires an ephemeral pooled connection and closes it immediately after completion.
- Full parallel execution gated only by provider pool limits and `PoolGovernor` quotas.

#### 2. KeepAlive (`1`)
- Extends `Standard` by holding **one idle persistent sentinel connection** to prevent the database engine from unloading between operations.
- **The sentinel connection is NEVER used for queries or commands.** All actual operations use ephemeral connections identical to `Standard`.
- **Target Use Cases**: SQL Server LocalDB, SQLite WAL mode keeping shared memory mapped, or embedded engines with expensive initialization overhead.
- *Clarification*: NOT for AWS RDS Proxy or server-side connection keep-alives.

#### 3. SingleWriter (`2`)
- **For file-based databases** (SQLite, DuckDB) where concurrent writers cause disk locking errors (`SQLITE_BUSY`).
- Connections are still **ephemeral** (opened late, closed early).
- Write tasks are serialized by `PoolGovernor` with `MaxConcurrentWrites = 1` and a writer-preference turnstile.
- Readers remain concurrent and ephemeral.

#### 4. SingleConnection (`4`)
- All operations (reads and writes) share a **single persistent connection**.
- Serialized via `RealAsyncLocker` (`SemaphoreSlim`).
- **Target Use Case**: In-memory databases (`Data Source=:memory:`) that disappear if the initial connection closes.

#### 5. Best (`15`)
- Heuristic auto-selection at context initialization:
  - `:memory:` SQLite / DuckDB $\to$ `SingleConnection`
  - File-based SQLite / DuckDB $\to$ `SingleWriter`
  - SQL Server LocalDB $\to$ `KeepAlive`
  - Server databases $\to$ `Standard`

---

## Dependency Injection Registration

```csharp
// Standard mode (default for production server databases)
services.AddSingleton<IDatabaseContext>(sp =>
    new DatabaseContext(connectionString, SqlClientFactory.Instance));

// Explicit mode via configuration
services.AddSingleton<IDatabaseContext>(sp =>
    new DatabaseContext(new DatabaseContextConfiguration
    {
        ConnectionString = connectionString,
        DbMode = DbMode.SingleWriter
    }, SqliteFactory.Instance));

// Dual read/write connection string support
services.AddSingleton<IDatabaseContext>(sp =>
    new DatabaseContext(new DatabaseContextConfiguration
    {
        ConnectionString = writeConnectionString,
        ReadOnlyConnectionString = readOnlyConnectionString,
        DbMode = DbMode.Standard
    }, NpgsqlFactory.Instance));
```

---

## IsolationProfile

Portable transaction isolation profiles mapped to the optimal native engine level:

```csharp
public enum IsolationProfile
{
    SafeNonBlockingReads,  // MVCC snapshot / RCSI where supported — avoids blocking readers
    StrictConsistency,     // Serializable / highest consistency
    FastWithRisks          // Read uncommitted / lowest overhead
}
```

Usage:
```csharp
await using var tx = await context.BeginTransactionAsync(IsolationProfile.SafeNonBlockingReads);
await gateway.CreateAsync(order, tx);
await tx.CommitAsync();
```

---

## Connection Poisoning Immunity & Self-Healing

`pengdows.crud` is completely immune to connection poisoning in all modes except `SingleConnection`:

- **`Standard` & `KeepAlive`**: Ephemeral pooled connections are used for every operation. A broken or dead connection is discarded by the ADO.NET pool; the next request acquires a clean connection.
- **`SingleWriter`**: Write serialization is governed by **in-memory admission tokens (`PoolGovernor`)**, NOT a pinned persistent connection. If a write fails or breaks a connection, it is disposed, the slot permit is freed, and the next write gets a clean connection from the pool. Zero connection resurrection code required.
- **`SingleConnection`**: The sole exception. In `:memory:` SQLite, the connection *is* the database; if it closes, memory is cleared.

---

## Critical Invariants & Rules

1. **`DatabaseContext` is a SINGLETON**: Register one singleton per connection string.
2. **NEVER use `TransactionScope`**: Incompatible with the open-late/close-early lifecycle; causes distributed transaction escalation (MSDTC). Use `context.BeginTransactionAsync()`.
3. **`ITrackedReader` is a lease**: Holds a connection lock and slot until disposed. Always dispose readers promptly.
4. **Context Lock is NoOp**: `DatabaseContext.GetLock()` returns `NoOpAsyncLocker.Instance` to prevent false global serialization. Connection-level and transaction-level locks handle all necessary synchronization.
5. **Supported Database Roster (15 Engines/Flavors)**: PostgreSQL, SQL Server, Oracle, IBM DB2, Firebird, SQLite, DuckDB, MySQL, MariaDB, CockroachDB, YugabyteDB, TiDB, Snowflake, AWS Aurora MySQL, AWS Aurora PostgreSQL.

