# pengdows.crud Internal Architecture

This document explains the internal design of pengdows.crud version 2.0 for developers working on the library itself or AI assistants trying to understand how it works.

**Audience**: Library maintainers, contributors, and AI systems analyzing the codebase.

**Purpose**: Prevent misconceptions and hallucinations by documenting architectural decisions, internal contracts, and design trade-offs.

---

## Terminology

**CRITICAL**: `DatabaseContext` is not equivalent to Entity Framework's `DbContext`.

`DatabaseContext` is a **singleton execution coordinator** bound to a specific provider + connection string.

**Concurrent callers are supported:**
- **Standard**: parallel operations using ephemeral connections
- **PreventDatabaseUnload**: identical to Standard; additionally keeps one unused sentinel connection open to prevent the DB from unloading
- **SingleConnection**: operations serialize on shared connection lock (single persistent connection, RealAsyncLocker)
- **SingleWriter**: identical to Standard; governor fixes writable connections to 1 concurrent writer and 0 writers on read-only connections; writer-starvation-prevention turnstile enabled

**APIs returning `ITrackedReader` hold a connection lease** until the reader is disposed.

**See [Why This Is Not Entity Framework Core](#why-this-is-not-entity-framework-core) for complete comparison.**

---

## Table of Contents

1. [Core Design Principles](#core-design-principles)
2. [Threading and Concurrency Model](#threading-and-concurrency-model)
3. [Connection Lifecycle Management](#connection-lifecycle-management)
4. [Locking Strategy (Two-Level Locking)](#locking-strategy-two-level-locking)
5. [Dependency Injection and Lifetime](#dependency-injection-and-lifetime)
6. [Strategy Pattern Architecture](#strategy-pattern-architecture)
7. [Reader-as-Lease Model](#reader-as-lease-model)
8. [Common Misconceptions](#common-misconceptions)
9. [Internal Contracts](#internal-contracts)
10. [Performance Characteristics](#performance-characteristics)

---

## Core Design Principles

### 1. SQL-First Philosophy
- **No LINQ to SQL translation**: User writes SQL directly, library provides safe parameterization
- **Database-agnostic where possible**: SqlDialect abstraction handles vendor differences
- **Full vendor feature access**: No lowest-common-denominator limitations

### 2. Testability by Design
- **fakeDb provider**: Complete ADO.NET provider implementation for unit testing
- **Separation of concerns**: Business logic testable without real databases
- **Integration tests**: Testcontainers for actual database validation

### 3. Performance Over Magic
- **Compiled property setters**: Generated Expression trees instead of reflection
- **Plan caching**: Column ordinals, type extractors cached per schema
- **Minimal allocations**: StringBuilder pooling, cached delegates

### 4. Explicit Over Implicit
- **No change tracking**: User controls when to save
- **No lazy loading**: User controls when to query
- **Explicit connection modes**: User chooses Standard/PreventDatabaseUnload/SingleWriter/SingleConnection

### 5. One unified execution-environment abstraction, one convergence point

`IDatabaseContext` isn't just "the connection object" — it's an execution-environment abstraction with two concrete substitutions sharing one contract: `DatabaseContext` (ordinary ephemeral execution) and `TransactionContext` (a pinned, transaction-scoped environment — see "TransactionContext is the alternate execution scope" in `docs/positioning/product-thesis.md`, principle 2). `pengdows.crud/ContextBase.cs` is the shared base centralizing container creation, parameter creation, quoting, and dialect delegation so SQL-building code written against `IDatabaseContext` never needs to know which one it actually received. `TransactionContext` specifically retains the parent context's `RootId`, metrics, dialect, product, and connection mode rather than exposing a separate, unrelated "transaction API" — a transaction is a *substituted execution scope*, not a different kind of object.

`ISqlContainer` is where all of this actually converges at execution time: it owns SQL text, parameters, execution intent (`ExecutionType`), connection acquisition, the two-level locking strategy, transaction association, metrics recording, tracing, command preparation, and provider exception translation — all in one place (see `SqlContainer.cs`). This is *why* so many apparently-separate features (dialect capabilities, connection modes, tenancy, observability) compose without every caller needing adapter code: they're all reached through the one object that already knows how to talk to whichever `IDatabaseContext` substitution it was handed.

---

## Threading and Concurrency Model

### Thread Safety Guarantees

**DatabaseContext is designed as a singleton per connection string.** Multiple threads can safely call into the same DatabaseContext instance concurrently.

**Important Distinction**: "Thread-safe" does NOT mean "parallel execution inside a transaction." It means:
- Multiple threads may attempt to use the same context/transaction
- The implementation serializes them correctly where required
- State cannot be corrupted
- Provider API contracts are respected

**Per-Mode Behavior:**

| Mode | Concurrent Calls | Behavior |
|------|-----------------|----------|
| **Standard** | ✅ Fully concurrent | Each operation gets ephemeral connection from provider pool. No serialization. |
| **PreventDatabaseUnload** | ✅ Fully concurrent | Identical to Standard. One unused sentinel connection is kept open to prevent DB unload; it never performs operations. |
| **SingleWriter** | ⚠️ Writes serialize | Identical to Standard. Governor: writable connections capped at 1 concurrent writer; read-only connections allow 0 writers. Writer-starvation-prevention turnstile on. |
| **SingleConnection** | ⚠️ All operations serialize | All operations share one persistent connection. Serialized at connection lock. |
| **Transaction** | ⚠️ All operations serialize | TransactionContext always uses SingleConnection mode. Serialized at transaction user lock. |

**Key Insight**: Serialization happens at the **connection lock** (or transaction lock), not the context lock. See [Locking Strategy](#locking-strategy-two-level-locking).

Outside of **SingleConnection**, read operations never reuse the writer connection; in **SingleWriter** mode reads always use ephemeral connections.

### TransactionContext Concurrency Model

**TransactionContext uses two-level locking** for correctness:

**1. User lock (`_userLock` - SemaphoreSlim)**
- Purpose: Serialize user-initiated operations within the transaction
- Prevents: Overlapping commands, readers, savepoints on same transaction
- Acquired by: SqlContainer, TrackedReader (held for reader lifetime)
- Result: Operations execute sequentially, never concurrently

**2. Completion lock (`_completionLock` - SemaphoreSlim)**
- Purpose: Ensure exactly-once completion (commit/rollback/dispose)
- Prevents: Commit racing with rollback, double completion
- Never exposed to user code

**State synchronization** uses atomics:
```csharp
private int _completedState;  // Interlocked.Exchange ensures exactly-once
private int _committed;       // Interlocked.CompareExchange for state queries
private int _rolledBack;      // Atomic, no locks needed
```

**Example - concurrent access to transaction:**
```csharp
// Thread A
await using var reader = await container.ExecuteReaderAsync();
// Holds _userLock for reader lifetime

// Thread B (concurrent attempt), or Thread A itself before disposing the reader
await container.ExecuteNonQueryAsync();
// Throws InvalidOperationException ("...while a reader opened on it is still active...")
// Commit/Rollback and savepoints throw the same way until the reader is disposed
```

**Result**: No overlap, no provider misuse, no corruption, deterministic behavior. This is the only correct behavior for a single database transaction.

**Why context-level locking would be wrong:**
- Transaction already operates in SingleConnection mode
- Connection lock + user lock provide complete serialization
- Adding context lock = redundant third lock for same critical section
- Would serialize unrelated operations outside the transaction
- Performance degradation for no safety gain

**Design correctness**: Lock only the resource that matters (the connection/transaction), not the entire context.

### Interlocked Operations

All shared counters use `Interlocked` for thread safety:

```csharp
// pengdows.crud/DatabaseContext.cs
private long _connectionCount;
private long _totalConnectionsCreated;
private long _totalConnectionsReused;
private long _totalConnectionFailures;

public long NumberOfOpenConnections => Interlocked.Read(ref _connectionCount);

// Increments are atomic
Interlocked.Increment(ref _connectionCount);
Interlocked.Decrement(ref _connectionCount);
```

### Events and Re-Entrancy

**MetricsUpdated event** is fired **without holding locks**:

```csharp
// pengdows.crud/DatabaseContext.Metrics.cs:320 (inside a per-subscriber try/catch)
((EventHandler<DatabaseMetrics>)invocation).Invoke(this, metrics);  // No lock held during callback
```

**WARNING**: Do not call back into the same DatabaseContext from event handlers. This is documented but not enforced. Adding a lock would cause deadlocks.

**Why no lock?** Standard .NET event pattern. Subscribers are observers, not controllers. Lock during callback = guaranteed deadlock if subscriber tries to use the context.

---

## Connection Lifecycle Management

### Connection Ownership Rules

**Ephemeral Connections (Standard/PreventDatabaseUnload modes)**:
- Created per operation
- Owned by the operation
- Disposed when operation completes
- Come from provider's connection pool (ADO.NET managed)

**Persistent Connections (SingleConnection mode)**:
- Created during DatabaseContext initialization
- Owned by DatabaseContext instance
- Held open for lifetime of context
- Disposed when context is disposed

### Provider Connection Pooling

**CRITICAL MISCONCEPTION**: DatabaseContext does **not** manage connection pooling. The **ADO.NET provider** (SqlClient, Npgsql, etc.) manages pooling.

```csharp
// WRONG: "DatabaseContext manages a connection pool"
// RIGHT: "DatabaseContext relies on provider connection pooling (ADO.NET)"

// Standard mode:
var conn = _factory.CreateConnection();  // Gets from provider pool
conn.ConnectionString = _connectionString;
await conn.OpenAsync();  // Provider handles pooling
// ... use connection ...
await conn.DisposeAsync();  // Returns to provider pool
```

**What DatabaseContext DOES manage**:
- Whether to use ephemeral (pool) or persistent (pinned) connections
- Connection lifecycle (open/close timing)
- Connection locking for shared connections

**What the PROVIDER manages**:
- Physical connection pooling
- Pool sizing (Min Pool Size, Max Pool Size)
- Connection validation and reset

### Mode Selection and Coercion

**DbMode.Best** auto-selects optimal mode based on database type and connection string analysis:

| Database | Connection String | Auto-Selected Mode | Why |
|----------|------------------|-------------------|-----|
| SQLite | `Data Source=:memory:` (isolated) | `SingleConnection` | **REQUIRED** - Each `:memory:` = separate database |
| SQLite | File-based (`mydb.db`) | `SingleWriter` | **OPTIMAL** - Prevents lock contention, WAL allows many readers + one writer |
| PostgreSQL | Any | `Standard` | **OPTIMAL** - Full server, high concurrency, provider pooling |
| SQL Server | LocalDB | `PreventDatabaseUnload` | **REQUIRED** - Prevents instance unload |

**Coercion** (forced mode change):
- SQLite `:memory:` + Standard → **Coerced to SingleConnection** (correctness)
- SQLite file + Standard → **Coerced to SingleWriter** (safety, prevents SQLITE_BUSY)
- Firebird embedded → **Not coerced** — `FirebirdDialect` deliberately doesn't override `CoerceConnectionMode`, so `Best` resolves to `Standard` as for any client-server engine; `PreventDatabaseUnload` remains available as an explicit opt-in

**Mode Mismatch Warnings** (safe but suboptimal):
- PostgreSQL + SingleConnection → Logs warning (limits concurrency unnecessarily)
- SQL Server + SingleWriter → Logs warning (limits concurrency unnecessarily)

See `pengdows.crud/DatabaseContext.Initialization.cs` for full coercion logic.

---

## Locking Strategy (Two-Level Locking)

### The Two-Level Design

pengdows.crud uses **context-level + connection-level** locking:

```
┌─────────────────────────────────────┐
│ DatabaseContext                     │
│ GetLock() → NoOpAsyncLocker        │  ← Always NoOp
│                                     │
│  ┌──────────────────────────────┐  │
│  │ ITrackedConnection           │  │
│  │ GetLock() → ???              │  │  ← Real or NoOp depending on mode
│  │                              │  │
│  │ - RealAsyncLocker (shared)   │  │
│  │ - NoOpAsyncLocker (ephemeral)│  │
│  └──────────────────────────────┘  │
└─────────────────────────────────────┘
```

### Why Context Lock is NoOp

**DatabaseContext.GetLock()** always returns `NoOpAsyncLocker.Instance` because:

1. **Configuration is immutable after init**: `_dialect`, `_connectionStrategy`, `ProcWrappingStyle` don't change
2. **Metrics use Interlocked**: `_connectionCount`, `_totalConnectionsCreated`, etc. are thread-safe via Interlocked
3. **Persistent connection has its own lock**: `_connection?.GetLock()` provides synchronization where needed

**WRONG**: "Context has no shared mutable state"
**RIGHT**: "Context state is immutable after init OR synchronized via specialized mechanisms (Interlocked, connection lock)"

### Connection-Level Locking

**ITrackedConnection.GetLock()** returns:

- **RealAsyncLocker** (SemaphoreSlim-based) for **shared connections**:
  - SingleConnection mode: The one connection is shared
  - PreventDatabaseUnload mode: Sentinel connection is shared (but never used for work)

- **NoOpAsyncLocker** for **ephemeral connections**:
  - Standard mode: Each operation gets its own connection
  - SingleWriter mode: All connections are ephemeral (serialization via governor, not connection lock)

**Implementation** (pengdows.crud/wrappers/TrackedConnection.cs):

```csharp
public ILockerAsync GetLock()
{
    // Shared connections: Real lock (serialize access)
    // Ephemeral connections: NoOp lock (no contention)
    return _locker;
}
```

### Acquisition Pattern

**Standard call path** (pengdows.crud/SqlContainer.cs):

```csharp
using var container = context.CreateSqlContainer("SELECT 1");
var value = await container.ExecuteScalarRequiredAsync<int>();
```

**Notes:**
- Connection acquisition happens inside `SqlContainer` through internal connection providers.
- The public API does not expose `GetConnection`.

**Why two locks?**
- Context lock: Reserved for future use, no-op today avoids overhead
- Connection lock: Actual synchronization happens here for shared connections

**Performance benefit**: Standard mode has zero locking overhead (both locks are NoOp).

---

## Dependency Injection and Lifetime

### Singleton-Per-Connection-String Pattern (REQUIRED)

**CRITICAL**: DatabaseContext must be registered as **singleton per unique connection string**, not scoped or transient.

**Why?**

1. **SingleWriter/SingleConnection REQUIRE singleton**:
   - These modes maintain persistent connections
   - Multiple contexts → multiple persistent connections → violates single-writer guarantee
   - SQLite: SQLITE_BUSY errors
   - In-memory: Each `:memory:` connection = separate database

2. **Standard mode WORKS WELL with singleton**:
   - Provider manages connection pooling (not context)
   - Singleton avoids repeated context initialization overhead
   - Multiple contexts to same DB = unnecessary resource duplication

3. **Lifecycle simplicity**:
   - No per-request disposal needed
   - Context lives for application lifetime
   - Disposal happens at shutdown

### Correct DI Registration

**Single-tenant application**:
```csharp
services.AddSingleton<DatabaseContext>(sp =>
    new DatabaseContext(connectionString, NpgsqlFactory.Instance));
```

**Multi-tenant (separate databases)**:
```csharp
// Tenant 1 database
services.AddKeyedSingleton<DatabaseContext>("tenant1", sp =>
    new DatabaseContext(tenant1ConnectionString, SqliteFactory.Instance));

// Tenant 2 database
services.AddKeyedSingleton<DatabaseContext>("tenant2", sp =>
    new DatabaseContext(tenant2ConnectionString, SqliteFactory.Instance));
```

**Read/Write separation (same database, different credentials)**:
```csharp
// Read-only connection
services.AddKeyedSingleton<DatabaseContext>("readonly", sp =>
    new DatabaseContext(readOnlyConnectionString, NpgsqlFactory.Instance));

// Read-write connection
services.AddKeyedSingleton<DatabaseContext>("readwrite", sp =>
    new DatabaseContext(readWriteConnectionString, NpgsqlFactory.Instance));
```

### WRONG Registrations (DO NOT DO THIS)

**❌ Scoped (per-request)**:
```csharp
services.AddScoped<DatabaseContext>(sp => ...);  // WRONG!
```
**Problems**:
- SingleWriter: Multiple write connections to SQLite → SQLITE_BUSY
- SingleConnection: Each `:memory:` instance = separate database → tests fail
- Standard: Wasted initialization overhead per request

**❌ Transient (per-injection)**:
```csharp
services.AddTransient<DatabaseContext>(sp => ...);  // WRONG!
```
**Problems**: Same as scoped, but worse (multiple contexts per request)

### TransactionContext Lifetime

**TransactionContext is NOT registered in DI.** It's created per operation via `context.BeginTransaction()` and disposed when the transaction completes.

**Correct usage:**
```csharp
public class OrderService
{
    private readonly DatabaseContext _context;

    public OrderService(DatabaseContext context)  // Inject singleton
    {
        _context = context;
    }

    public async Task ProcessOrderAsync(Order order)
    {
        // Create transaction per operation
        using var tx = _context.BeginTransaction();

        // Pass tx to operations that need transaction
        await _orderHelper.CreateAsync(order, tx);

        tx.Commit();  // Dispose releases connection lock
    }
}
```

**Key characteristics:**
- **Lifetime**: Operation-scoped (from BeginTransaction to Dispose)
- **Lock behavior**: Holds connection lock for entire lifetime
- **Disposal**: MUST dispose promptly (use `using` or `await using`)
- **DI**: NOT registered - created on-demand per operation

**WRONG usage:**
```csharp
// ❌ DO NOT: Store transaction as field or inject it
public class OrderService
{
    private ITransactionContext _tx;  // WRONG - long-lived transaction

    public OrderService(ITransactionContext tx)  // WRONG - can't inject
    {
        _tx = tx;
    }
}

// ❌ DO NOT: Create transaction per request (in middleware/filter)
app.Use(async (context, next) =>
{
    using var tx = dbContext.BeginTransaction();  // WRONG - holds lock too long
    await next();
    tx.Commit();
});
```

**Why operation-scoped?**
- Transactions hold connection locks
- Long-lived transactions = connection starvation
- Transaction per request = lock held across entire HTTP request (bad for concurrency)

**Correct scope**: Begin transaction as late as possible, commit/dispose as early as possible.

---

## Explicit Capability Properties

`ISqlDialect` exposes concrete `Supports*` capability flags — `SupportsJoins`, `SupportsMerge`, `SupportsWindowFunctions`, `SupportsJsonTypes`, `SupportsTemporalData`, `SupportsPropertyGraphQueries`, and more — so callers read one boolean per capability rather than reasoning about a standard level themselves.

The base `SqlDialect` still contains the legacy SQL-standard heuristic (`MaxSupportedStandard` and
`SqlStandardLevel`) for 2.x binary compatibility, but those members are obsolete and rejected for
new application use by `PGC027`. Consumers must query the specific `Supports*` capabilities. The
legacy implementation maps feature flags to approximate SQL eras, while individual dialects may
override capabilities with version-aware logic; that history is retained here to explain existing
behavior, not as a capability contract for new code.

## Strategy Pattern Architecture

### IConnectionStrategy Implementations

**Standard mode** (`StandardConnectionStrategy`):
- Every operation: Create → Open → Use → Close → Dispose
- Connection from provider pool
- No persistent connection

**PreventDatabaseUnload mode** (`PreventDatabaseUnloadConnectionStrategy`):
- One sentinel connection kept open (never used)
- Prevents database unload (LocalDB, embedded SQLite)
- All work uses ephemeral connections (identical to Standard)

**SingleWriter mode** (`StandardConnectionStrategy` + governor policy):
- Per-operation connections for both reads and writes
- Governor: writable connections capped at 1 concurrent writer; read-only connections allow 0 writers
- Writer-starvation-prevention turnstile enabled
- Ideal for file-based SQLite/DuckDB when writes must serialize

**SingleConnection mode** (`SingleConnectionStrategy`):
- One persistent connection for **all** operations
- Required for SQLite `:memory:` (each connection = separate database)
- All operations serialize at connection lock

### Strategy Selection Logic

**Initialization** (DatabaseContext.Initialization.cs:579-591):
```csharp
ConnectionMode = CoerceMode(requestedMode, product, isLocalDb);
WarnOnModeMismatch(ConnectionMode, product, requestedMode != ConnectionMode);
```

**Strategy instantiation** (strategies/connection/ConnectionStrategyFactory.cs):
```csharp
internal static class ConnectionStrategyFactory
{
    public static IConnectionStrategy Create(DatabaseContext context, DbMode mode)
    {
        return mode switch
        {
            DbMode.Standard => new StandardConnectionStrategy(context),
            DbMode.PreventDatabaseUnload => new PreventDatabaseUnloadConnectionStrategy(context),
            DbMode.SingleConnection => new SingleConnectionStrategy(context),
            // SingleWriter uses Standard lifecycle + governor policy (WriteSlots=1 + turnstile)
            DbMode.SingleWriter => new StandardConnectionStrategy(context),
            _ => throw new NotSupportedException($"Unsupported database mode: {mode}")
        };
    }
}
```
Note: there is no silent fallback for an unrecognized mode — the default case throws `NotSupportedException`.

### IProcWrappingStrategy

**Purpose**: Handle database-specific stored procedure invocation syntax.

**Implementations**:
- `NoProcWrappingStrategy`: Direct call (`EXEC sp_name @p1, @p2`)
- `FunctionCallProcWrappingStrategy`: Function syntax (`SELECT sp_name(@p1, @p2)`)
- `ExecProcWrappingStrategy`: EXEC keyword required

**Selection**: Determined by `DataSourceInformation.ProcWrappingStyle` based on detected database type.

---

## Reader-as-Lease Model

### What is a "Lease"?

When you call `ExecuteReaderAsync()`, the returned `ITrackedReader` represents a **lease over resources**:

1. **Database connection** (pinned until reader disposed)
2. **Connection lock** (held until reader disposed)
3. **ADO.NET DbDataReader** (underlying provider reader)
4. **DbCommand** (command that created the reader)

**Lease lifetime**: From reader creation until reader disposal.

### Lock Held During Reader Lifetime

**CRITICAL**: The connection lock is held for the **entire lifetime** of the reader.

```csharp
// pengdows.crud/wrappers/TrackedReader.cs (simplified)
public class TrackedReader : ITrackedReader
{
    private readonly ITrackedConnection _connection;
    private readonly IDisposable _lockHandle;  // ← Lock acquired at creation

    public TrackedReader(DbDataReader reader, ITrackedConnection connection)
    {
        _reader = reader;
        _connection = connection;
        _lockHandle = await connection.GetLock().LockAsync();  // ← ACQUIRE
    }

    public async ValueTask DisposeAsync()
    {
        await _reader.DisposeAsync();
        await _connection.CloseAndDisposeAsync();
        _lockHandle?.Dispose();  // ← RELEASE
    }
}
```

### Why This Matters

**In Standard mode**: No impact (connection lock is NoOp)

**In SingleWriter/SingleConnection modes**:
- Reader holds the connection lock
- Other operations **block** waiting for the lock
- Long-lived readers = serialization bottleneck

**Best Practice**:
```csharp
// ✅ GOOD: Dispose reader promptly
await using var reader = await container.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    // Process row
}
// Auto-disposed here, lock released

// ❌ BAD: Reader outlives its usefulness
var reader = await container.ExecuteReaderAsync();
var firstRow = await reader.ReadAsync();
// ... do other work for 10 seconds ...
// Reader still holds lock!
await reader.DisposeAsync();
```

### Auto-Disposal on End-of-Results

**TrackedReader** auto-disposes when `Read()` or `ReadAsync()` returns `false`:

```csharp
// pengdows.crud/wrappers/TrackedReader.cs:336
public async ValueTask<bool> ReadAsync(CancellationToken cancellationToken)
{
    if (await _reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
        Interlocked.Increment(ref _rowsRead);
        return true;
    }

    await DisposeAsync().ConfigureAwait(false); // ← Auto-dispose on end-of-results
    return false;
}
```

**Why?** Prevents accidental resource leaks when consuming all rows in a loop.

**Implication**: You can omit explicit disposal if you read to completion, but explicit disposal is still recommended.

---

## Why This Is Not Entity Framework Core

**CRITICAL**: pengdows.crud uses a fundamentally different lifecycle model than Entity Framework. Applying EF patterns will break correctness and performance.

### Entity Framework Core Pattern (WRONG for pengdows.crud)

```csharp
// ❌ EF Core pattern - DO NOT USE
services.AddDbContext<MyDbContext>(options =>
    options.UseSqlServer(connectionString),
    ServiceLifetime.Scoped);  // Per-request lifetime

public class OrderService
{
    private readonly MyDbContext _context;

    public OrderService(MyDbContext context)  // Scoped DbContext injected
    {
        _context = context;  // NEW instance per HTTP request
    }

    public async Task ProcessOrderAsync(Order order)
    {
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();  // Implicit transaction
    }
}
```

**EF Core assumptions:**
- DbContext is cheap to create (scoped per request)
- Change tracking manages state across calls
- SaveChanges creates implicit transaction
- One context per "unit of work" (HTTP request)
- Connection pooling is invisible, provider-managed

---

### pengdows.crud Pattern (CORRECT)

```csharp
// ✅ pengdows.crud pattern
services.AddSingleton<DatabaseContext>(sp =>
    new DatabaseContext(connectionString, SqlClientFactory.Instance));

public class OrderService
{
    private readonly DatabaseContext _context;

    public OrderService(DatabaseContext context)  // Singleton DatabaseContext injected
    {
        _context = context;  // SAME instance for entire application lifetime
    }

    public async Task ProcessOrderAsync(Order order)
    {
        // Explicit transaction, created per operation
        using var tx = _context.BeginTransaction();

        await _orderHelper.CreateAsync(order, tx);

        tx.Commit();  // Explicit commit
    }
}
```

**pengdows.crud assumptions:**
- DatabaseContext is singleton per connection string
- No change tracking (stateless)
- Explicit transactions via BeginTransaction()
- One context per connection string, shared across all requests
- Connection modes determine pooling vs persistence

---

### Key Architectural Differences

| Aspect | Entity Framework Core | pengdows.crud |
|--------|----------------------|--------------|
| **Context Lifetime** | Scoped (per request) | Singleton (per connection string) |
| **Change Tracking** | Automatic | None (stateless) |
| **Transactions** | Implicit (SaveChanges) | Explicit (BeginTransaction) |
| **Connection Pooling** | Always provider-managed | Mode-dependent (Standard=pooled, PreventDatabaseUnload=pooled+sentinel, SingleWriter=governor over pool, SingleConnection=single pinned) |
| **Unit of Work** | DbContext | TransactionContext |
| **Concurrency Model** | One context per request (isolated) | One context for all requests (serialized at connection/transaction level) |
| **SQL Control** | LINQ to SQL (generated) | Raw SQL (full control) |
| **State Management** | Context tracks entities | No tracking |

---

### Why EF Patterns Break pengdows.crud

**1. Scoped DatabaseContext = Multiple Persistent Connections**

```csharp
// ❌ WRONG: Scoped context
services.AddScoped<DatabaseContext>(sp =>
    new DatabaseContext(sqliteConnectionString, SqliteFactory.Instance));

// Result: Each HTTP request creates a new DatabaseContext
// Problem: In SingleWriter mode, each context creates its own governor (WriteSlots=1)
// Outcome: No coordinated write serialization across contexts → SQLITE_BUSY errors
```

**2. Scoped Context = Broken :memory: Isolation**

```csharp
// ❌ WRONG: Scoped context with :memory:
services.AddScoped<DatabaseContext>(sp =>
    new DatabaseContext("Data Source=:memory:", SqliteFactory.Instance));

// Result: Request A gets connection 1 (database A)
//         Request B gets connection 2 (database B)
// Problem: Each :memory: connection = separate database
// Outcome: Data inserted by Request A is NOT visible to Request B
```

**3. Injecting TransactionContext = Long-Lived Transaction**

```csharp
// ❌ WRONG: Trying to inject TransactionContext
services.AddScoped<ITransactionContext>(sp =>
    sp.GetRequiredService<DatabaseContext>().BeginTransaction());

// Result: Transaction created when request starts
//         Transaction disposed when request ends
// Problem: Holds connection lock for entire HTTP request duration
// Outcome: Connection starvation, terrible concurrency
```

---

### Mental Model: EF vs pengdows.crud

**Entity Framework Core:**
- "I want a new context for each request so I don't have to think about state"
- Context = unit of work = HTTP request boundary
- SaveChanges = commit
- Scoped lifetime isolates requests from each other

**pengdows.crud:**
- "I want one context for the database, transactions for units of work"
- Context = database access point (singleton)
- TransactionContext = unit of work (operation-scoped)
- Explicit transactions = commit
- Singleton lifetime + connection locks serialize access where needed

---

### Migration Guide: EF Core → pengdows.crud

**Step 1**: Change context lifetime
```csharp
// Before (EF)
services.AddDbContext<MyDbContext>(..., ServiceLifetime.Scoped);

// After (pengdows.crud)
services.AddSingleton<DatabaseContext>(...);
```

**Step 2**: Replace change tracking with explicit operations
```csharp
// Before (EF)
_context.Orders.Add(order);
await _context.SaveChangesAsync();

// After (pengdows.crud)
await _orderHelper.CreateAsync(order, context);
```

**Step 3**: Make transactions explicit
```csharp
// Before (EF) - implicit transaction
await _context.SaveChangesAsync();

// After (pengdows.crud) - explicit transaction
using var tx = _context.BeginTransaction();
await _orderHelper.CreateAsync(order, tx);
tx.Commit();
```

**Step 4**: Pass context as parameter, not field
```csharp
// Before (EF) - context is field
private readonly MyDbContext _context;
public async Task ProcessAsync() { ... }

// After (pengdows.crud) - context is singleton, transaction is local
private readonly DatabaseContext _context;
public async Task ProcessAsync()
{
    using var tx = _context.BeginTransaction();  // Local variable
    await DoWorkAsync(tx);  // Pass as parameter
    tx.Commit();
}
```

---

### Bottom Line

**Do NOT apply Entity Framework patterns to pengdows.crud.**

The lifecycle models are fundamentally incompatible. EF uses scoped contexts with change tracking. pengdows.crud uses singleton contexts with explicit operations. Mixing these patterns will break correctness (SQLITE_BUSY, lost data) and performance (connection starvation).

**When in doubt**: Singleton DatabaseContext, operation-scoped TransactionContext, explicit everything.

---

## Common Misconceptions

This section addresses **frequent misunderstandings** by developers and AI systems.

### 1. "DatabaseContext should be scoped (one per request)"

**❌ WRONG**. DatabaseContext must be **singleton per connection string**.

**Why the confusion?**
- Entity Framework uses scoped DbContext
- Seems logical to dispose context per request

**Why it's wrong**:
- SingleWriter/SingleConnection modes **require** singleton (see [DI section](#dependency-injection-and-lifetime))
- Standard mode **works best** with singleton (avoids initialization overhead)
- Provider manages pooling, not context

**Correct**: One DatabaseContext instance per unique connection string, shared across all requests/threads.

### 2. "DatabaseContext manages a connection pool"

**❌ WRONG**. The **ADO.NET provider** (SqlClient, Npgsql, etc.) manages pooling.

**What DatabaseContext does**:
- Decides whether to use ephemeral (from pool) or persistent (pinned) connections
- Manages connection lifecycle (open/close timing)
- Provides connection locking for shared connections

**What the provider does**:
- Physical connection pooling
- Pool configuration (Min/Max Pool Size, Timeout)
- Connection validation and reset

**Correct**: DatabaseContext **uses** provider pooling in Standard/PreventDatabaseUnload modes.

### 3. "Context lock serializes all operations"

**❌ WRONG**. Context lock is **always NoOp**.

**Why the confusion?**
- Serialization DOES happen in SingleWriter/SingleConnection modes
- Looks like it would be at the context level

**Why it's wrong**:
- Serialization happens at the **connection lock**, not context lock
- Standard mode: Both locks are NoOp (fully concurrent)
- SingleConnection mode: Context lock is NoOp, connection lock is Real (serializes)

**Correct**: Two-level locking. Context lock = NoOp. Connection lock = Real or NoOp depending on mode. See [Locking Strategy](#locking-strategy-two-level-locking).

### 4. "ITrackedReader is just a wrapper around DbDataReader"

**❌ INCOMPLETE**. ITrackedReader is a **lease over resources**.

**Why the confusion?**
- It implements IDataReader
- Forwards most calls to underlying DbDataReader

**What's missing**:
- Reader **holds connection lock** for its entire lifetime
- Reader **pins the connection** until disposed
- Reader **auto-disposes** on end-of-results

**Correct**: Reader-as-lease. Must be disposed promptly. See [Reader-as-Lease Model](#reader-as-lease-model).

### 5. "fakeDb simulates database behavior"

**❌ WRONG**. fakeDb **wires up ADO.NET control flow** without executing SQL.

**What fakeDb does**:
- Implements DbProviderFactory, DbConnection, DbCommand, DbDataReader
- Returns empty/mocked result sets
- Simulates connection failures

**What fakeDb does NOT do**:
- No SQL execution (INSERT succeeds without writing data)
- No constraints (foreign keys, unique constraints ignored)
- No triggers or stored procedures
- No transaction isolation semantics

**Correct**: fakeDb tests **code paths** (SQL generation, error handling), not **database semantics**. Integration tests still required.

### 6. "SingleConnection mode is dangerous in production"

**❌ WRONG**. SingleConnection is **optimal for certain databases**.

**Why the confusion?**
- Serializes all operations (sounds slow)
- Often associated with testing

**When it's correct**:
- SQLite `:memory:` → **REQUIRED** (each connection = separate database)
- Firebird embedded → **NOT REQUIRED**; embedded Firebird behaves like a client-server database, so `Best` resolves to `Standard` and work can use separate attachments
- Small embedded databases → **OPTIMAL** (no connection overhead)

**When it's suboptimal**:
- PostgreSQL, SQL Server, MySQL → Use Standard instead (supports concurrency)

**Correct**: SingleConnection is the **right choice** for certain databases. It is a disposable-data lifetime requirement for SQLite/DuckDB `:memory:`; embedded Firebird is not coerced into it (Best resolves to Standard) and can use SingleConnection only as an explicit specialized deployment shape. It is not a general high-concurrency mode.

### 7. "Best mode always selects Standard"

**❌ WRONG**. Best mode is **database-specific**.

**Actual selection**:
- PostgreSQL/MySQL/Oracle → Standard (full concurrency)
- SQLite `:memory:` → SingleConnection (required for correctness)
- SQLite file → SingleWriter (optimal for WAL)
- SQL Server LocalDB → PreventDatabaseUnload (prevents unload)

**Correct**: Best = "most functional safe mode for this specific database".

### 8. "Re-entrancy in MetricsUpdated is prevented by locking"

**❌ WRONG**. Re-entrancy is **documented but not prevented**.

**Why no lock?**
- Lock during callback = guaranteed deadlock if subscriber uses context
- Standard .NET event pattern: fire without locks
- Subscribers expected to be observers, not controllers

**What's actually done**:
- Warning in XML docs (DatabaseContext.Metrics.cs:43-85); event invoked at DatabaseContext.Metrics.cs:320
- Event fired without holding locks
- User responsible for not re-entering

**Correct**: Re-entrancy is **discouraged** (via docs) but not **prevented** (no lock). Adding lock would cause deadlocks.

### 9. "TransactionContext should be injected via DI"

**❌ WRONG**. TransactionContext is **NOT registered in DI**.

**Why the confusion?**
- In some patterns, units of work are injected
- Entity Framework uses DbContext which can be scoped
- Seems logical to inject transaction context

**Why it's wrong**:
- TransactionContext holds connection lock for its lifetime
- Injecting = long-lived transaction = connection starvation
- Transaction lifetime should be operation-scoped, not request-scoped

**What's actually done**:
- Create via `context.BeginTransaction()` per operation
- Dispose promptly using `using` or `await using`
- Pass as parameter to methods that need transaction

**Correct**: TransactionContext is **created per operation** (via BeginTransaction), not injected. Singleton is DatabaseContext, not TransactionContext.

---

## Internal Contracts

This section documents **contracts between internal components** that aren't visible in public APIs.

### The internal-interface seam — and its failure mode for custom decorators

The public `IDatabaseContext`/`ISqlDialect` interfaces deliberately omit connection acquisition, dialect version-detection, and session-settings internals — those live on separate `internal` interfaces (`IInternalConnectionProvider`, `IInternalSqlDialect`, `ITypeMapAccessor`) that the concrete `DatabaseContext`/`SqlDialect` classes implement *in addition to* the public ones. A family of `internal static` extension classes (`InternalConnectionExtensions`, `InternalDialectProviderExtensions`, `InternalSessionSettingsExtensions`, `InternalSqlContainerExtensions`, `InternalSqlDialectExtensions`) form the seam: each casts its `IDatabaseContext`/`ISqlDialect` parameter to the matching internal interface and throws `InvalidOperationException` (e.g. `"IDatabaseContext must provide internal connection access."`) if the cast fails. This is what lets `SqlContainer`/the gateways call capabilities that don't exist anywhere on the public surface at all.

**Sharp edge:** any custom `IDatabaseContext`/`ISqlDialect` implementation *within the same solution* — most plausibly a test decorator wrapping the real implementation to override one behavior — compiles fine if it only implements the public interface, but throws `InvalidOperationException` at runtime the first time internal code tries to use it, since the decorator doesn't also implement the internal interface. If you're writing a decorator around either interface for testing, it needs to forward the relevant internal interface too, not just the public one.

### IConnectionStrategy ↔ DatabaseContext

**Contract**:
- Strategy **must** return ephemeral connections via internal GetConnection for Standard/PreventDatabaseUnload modes
- Strategy **must** return ephemeral connections for SingleWriter operations (both reads and writes; governor serializes via permits, not connection sharing)
- Strategy **must** return the persistent connection for SingleConnection all operations

**Enforcement**: Strategy implementations in `pengdows.crud/strategies/connection/`

### ITrackedConnection ↔ TrackedReader

**Contract**:
- Connection **must** remain open for reader's lifetime
- Connection **must not** be used by other operations while reader is active
- Connection lock **must** be held from reader creation until reader disposal
- Auto-disposal **must** release connection and lock

**Enforcement**: TrackedReader.cs:70-94 and 256-298 (sync/async disposal logic)

**On a transaction specifically, this fails fast — it does not block.** A reader opened on an `ITransactionContext` holds the transaction's user lock (`ReusableAsyncLocker` over `_userLock`) until the reader is disposed, and `ReusableAsyncLocker.MarkHeldByActiveReader()` marks that hold as owned by the reader. While it is marked, *any* further lock attempt on the transaction — another command, `Commit`/`Rollback` (sync or async), `SavepointAsync`/`RollbackToSavepointAsync`/`ReleaseSavepointAsync`, from the same logical flow or another thread — throws `InvalidOperationException("Cannot execute another command, or commit/roll back this transaction, while a reader opened on it is still active...")` immediately. Blocking would deadlock when the waiter is the flow that must dispose the reader (e.g. writing while still streaming a `LoadStreamAsync` result from the same transaction). A failed `Commit`/`Rollback` leaves the transaction uncompleted, so dispose the reader and retry. `Dispose()` of the transaction does not throw here, but skips the rollback and leaves the transaction and connection to the reader; dispose the reader first.

### RealAsyncLocker is not reentrant

`RealAsyncLocker.Lock()`/`LockAsync()`/`TryLockAsync()` all throw `InvalidOperationException("Lock already acquired.")` immediately if the same locker instance is locked a second time before the first lock is released — including by the same logical caller. This is deliberate: a `SemaphoreSlim(1,1)`-backed lock would otherwise deadlock on self-reentry rather than erroring, and the loud exception is preferred over a hang. If you see this exception, look for a call path that re-enters a locked scope (directly or via a callback) rather than assuming a threading bug in the locker itself.

### MetricsCollector ↔ DatabaseContext

**Contract**:
- MetricsCollector fires MetricsChanged event **without holding locks**
- DatabaseContext subscribes during construction (DatabaseContext.Initialization.cs:285)
- DatabaseContext unsubscribes in disposal (DatabaseContext.cs:474, :515)
- Event handler (OnMetricsCollectorUpdated) **must not** acquire locks

**Enforcement**: DatabaseContext.Metrics.cs:297-331 (`OnMetricsCollectorUpdated`)

### SqlDialect ↔ DataSourceInformation

**Contract**:
- DataSourceInformation detected once during initialization
- SqlDialect **must** remain immutable after construction
- Dialect selection based on SupportedDatabase enum
- Vendor-specific behaviors encapsulated in dialect implementation

**Enforcement**: DatabaseContext.Initialization.cs:290-311

### TransactionContext ↔ IsolationResolver

**Contract**:
- IsolationResolver maps portable IsolationProfile → native IsolationLevel
- Mapping is database-specific (SQL Server RCSI differs from PostgreSQL)
- TransactionContext **must** use resolved native level
- Read Committed Snapshot Isolation (RCSI) detection happens at init

**Enforcement**: IsolationResolver.cs, TransactionContext.cs

---

## Performance Characteristics

### Compiled Property Setters

**Benchmark**: `benchmarks/CrudBenchmarks/Internal/ReaderMappingBenchmark.cs`

**Results** (AMD Ryzen 9 5950X, .NET 8.0.22):
- **100 rows**: 161.6 µs vs 969.7 µs = **6.0x faster** than pure reflection
- **1,000 rows**: 1.76 ms vs 9.80 ms = **5.57x faster** than pure reflection
- **Per-row**: ~1,700ns vs ~9,700ns

**How it works**:
1. First query: Introspect reader schema, build plan
2. Generate Expression tree for property setter
3. Compile to delegate (cached)
4. Subsequent queries: Direct delegate invocation (no reflection)

**Code**: `pengdows.crud/DataReaderMapper.cs` — `CompileSetter<T>()` builds the compiled `Expression` setter delegate, cached in `_setterCache` per target type/property/field type.

### SQL Template Caching

**What's cached**:
- INSERT/UPDATE/DELETE SQL templates
- Column ordinals for SELECT
- Parameter names and DbTypes

**Cache key**: Entity type + operation type

**Benefit**: Avoid repeated string concatenation and reflection

### Reader Plan Caching

**What's cached** (per reader schema):
- Column name → ordinal mapping
- Type extractors (GetInt32, GetString, etc.)
- Property setters (compiled delegates)

**Cache key**: Schema signature (column names + types)

**Benefit**:
- No GetOrdinal() calls per row
- No reflection per row
- Pure delegate invocations

**Invalidation**: None (schemas rarely change at runtime)

### NULL handling in compiled mappers is deliberately loud

`CompiledMapperFactory` skips the `IsDBNull` guard entirely for non-nullable value-type columns (`int`, `DateTime`, etc. — not `int?`) — the compiled getter calls the typed reader accessor directly. If the database genuinely returns `NULL` for a column mapped to a non-nullable value-type property (a schema change that didn't get mirrored in the C# type, most commonly), the result is a hard exception on the first NULL row rather than a silently-defaulted value. This is intentional: silently leaving the property at `default(T)` would be a worse failure mode (wrong data, no error) than a loud crash pointing at the mismatched column. If you hit this, the fix is making the property type match the column's actual nullability (`int?`), not suppressing the exception.

### Compiled binders close over the dialect instance

`CompiledBinderFactory<TEntity>.CreateInsertBinder`/`CreateUpdateBinder` bake the `ISqlDialect` instance itself into the compiled delegate via `Expression.Constant(dialect)` — the generated code calls back into that exact dialect object for the delegate's entire lifetime. Caching these delegates keyed on anything looser than the dialect *instance* (e.g. the `SupportedDatabase` enum alone) can silently return one tenant's compiled binder to another tenant on the same database engine but a different server version — this was a real, since-fixed bug. The current binder caches (`_insertBinders`/`_upsertBinders`/`_updateBinders` in `TableGateway.Core.cs`) are keyed by dialect instance via `ConditionalWeakTable`; the mechanism is noted here so a future change to the caching layer doesn't reintroduce the same class of bug.

### Hot-path compilation fails loudly instead of silently falling back to reflection

`CompiledBinderFactory<TEntity>`'s `Expression.Lambda<...>.Compile()` calls have no surrounding try/catch — if building a compiled insert/update binder for an entity type fails (a metadata mismatch the earlier validation pass didn't catch), the exception propagates directly out of the first `CreateAsync`/`UpdateAsync` call for that type rather than being swallowed in favor of a slower, reflection-based fallback path. This is a deliberate companion to "NULL handling in compiled mappers is deliberately loud" above, applied one layer earlier (compile time, not read time): silently falling back to reflection would turn a real mapping bug into a permanent, invisible performance regression instead of a loud, fixable error at the point it's introduced. (This doesn't contradict `ColumnInfo`'s own comment about falling back to reflection for *ad-hoc* `ColumnInfo` instances — those were never part of the compiled-metadata pipeline in the first place, by design, not as a fallback after a failed compile.)

### Parameter cloning preserves provider-specific state

`SqlContainer`'s parameter-cloning path (`Clone()`, `Clone(IDatabaseContext)`) does more than copy `DbType`/`Value`/`Direction`/`Size`/`Precision`/`Scale`: after building the clone via `_dialect.CreateDbParameter`, `CopyProviderSpecificProperties` additionally copies provider-specific properties (`OracleDbType`, `NpgsqlDbType`, etc.) that `CreateDbParameter` may not re-derive if the CLR type isn't registered in `AdvancedTypeRegistry`. This runs only when the source and target parameters are the *same concrete provider type* (a mismatched clone target skips it entirely, since cross-provider property copying wouldn't be meaningful). The property-copying delegate itself is built once per concrete parameter `Type` via reflection (`BuildProviderSpecificCopier`, scanning `SqlDialect.ProviderSpecificPropertyNames`) and cached in a static `ConcurrentDictionary<Type, Action<DbParameter,DbParameter>>` (`ProviderSpecificCopiers`) — the reflection cost is paid once per provider parameter type, not per clone. Without this, a parameter that had an explicitly-set provider-specific type (e.g. a caller-set `NpgsqlDbType.Uuid` outside the normal coercion path) would silently lose that setting across a clone — exactly the kind of "works until you use a real type" portability failure that's easy to miss in `fakeDb`-only testing.

### Prepared statements are per-connection state, with a dialect-level exhaustion veto

Command preparation isn't a single global `Prepare=true`/`false` switch. `IConnectionLocalState` (per pooled connection) tracks which SQL shapes have already been prepared on *that specific connection* (`IsAlreadyPreparedForShape`/`MarkShapePrepared`, bounded at 32 shapes with first-in-first-out eviction) so the same shape isn't re-prepared every time a command executes against it, and resets when the connection is recycled (`Reset()`). If preparing a statement fails with a provider error the dialect recognizes as prepare-related (`ShouldDisablePrepareOn(ex)`, declared on the internal `IInternalSqlDialect` interface and reached through the internal-interface seam described above — not an `ISqlDialect` member), preparation is disabled for *that one connection* only (`DisablePrepare()`/`PrepareDisabled`) rather than for the whole process — a transient, connection-specific provider hiccup doesn't have to disable a working optimization everywhere. Separately, a dialect can assert a hard, unconditional veto (`ISqlDialect.IsPrepareExhausted`, e.g. MySQL's `max_prepared_stmt_count` being exhausted server-side) that overrides `CommandPrepareMode.Always`/`Auto` entirely — `SqlContainer.ComputeEffectivePrepareSettings` checks this first, before consulting either configuration or the dialect's normal `PrepareStatements` recommendation, because retrying preparation after server-side exhaustion only makes the underlying problem worse.

### Dialect-level connection-cleanup hooks for provider-specific write hazards

Two `internal`, off-by-default `SqlDialect` members (deliberately not part of `ISqlDialect`) exist purely as `SqlContainer` connection-cleanup implementation details, not caller-observable capabilities — found and fixed together while implementing Oracle's batch-UPDATE strategy:

- **`RequiresExplicitRollbackAfterFailedWrite`** (`false` by default, `true` for Firebird) — `FirebirdSql.Data.FirebirdClient` starts an implicit transaction per command and auto-commits it on success, but has no corresponding auto-rollback on failure, so a failed write left its transaction's lock dangling on the pooled connection indefinitely. `SqlContainer.ExecuteNonQueryAsync` (and the reader/scalar execution paths that also perform writes) issues an explicit bare `ROLLBACK` after any failed write when the dialect requires it, before the connection returns to the pool — skipped entirely inside an explicit `ITransactionContext`, since that connection's commit/rollback lifecycle already belongs to the transaction.
- **`RequiresConnectionPoolResetForDdl`** (`false` by default, `true` for Firebird) — Firebird's DDL commit requires no *other* pooled connection to still be referencing the table's prior metadata generation, even one holding only cleanly-committed transactions; enough prior round trips reusing pooled connections could make a later `CREATE`/`DROP`/`ALTER`/`TRUNCATE` fail with nothing actually uncommitted. `SqlContainer` calls the internal `SqlDialect.ResetConnectionPoolForDdl(connectionString)` before such a statement when the dialect overrides it — Firebird's override reflects into `FbConnection.ClearPool(string)` (no hard package reference from `pengdows.crud` to `FirebirdSql.Data.FirebirdClient`, the same reflection pattern `OracleDialect` uses for its `StatementCacheSize` hook), using the real, unredacted connection string so it matches the actual ADO.NET pool key.

### Provider-bug workarounds are narrow and stack-trace-verified, not blanket suppression

`TrackedReader` contains targeted workarounds for two real, encountered provider bugs, both scoped as tightly as possible rather than a general `catch { ignore; }`: Npgsql 9's `GetValue()` throwing for `timestamp without time zone` columns (worked around by calling the supported `GetFieldValue<DateTime>()` API instead for those columns), and `MySql.Data` occasionally throwing `NullReferenceException` while disposing a prepared `MySqlCommand`/closing a prepared statement asynchronously — suppressed only when the exception's own stack trace matches the specific known-buggy internal call sites (`MySql.Data.MySqlClient.PreparableStatement.CloseStatementAsync`, `Statement.get_Driver`), via `ShouldSuppressMySqlDataDisposeNullReference`, not merely "any `NullReferenceException` during dispose."

### `BoundedCache<TKey,TValue>`

A thread-safe LRU used for `DataReaderMapper`'s setter/plan/property-lookup caches and several gateway accessor caches. Builds are deduped under concurrent load via `Lazy<T>` with `ExecutionAndPublication`, and eviction is a linear scan by last-access timestamp — cheap and correct at the library's typical bound sizes (roughly 32–512 entries depending on the cache), but not designed for a much larger working set. An application generating many distinct, ad-hoc SQL/result-set shapes at runtime (rather than a fixed, compile-time-known set of entities and queries) will churn this cache past its bound and repeatedly pay recompilation cost rather than getting a stable steady-state hit rate.

### Connection-string handling in the normalization cache

`ConnectionStringNormalizationCache` (`pengdows.crud/internal/ConnectionStringNormalizationCache.cs`)
is a static, process-lifetime cache backing the read-only/primary connection-string equivalence
check (`AreConnectionStringsEquivalentIgnoringCredentials` in `DatabaseContext.Initialization.cs`).
It is a `BoundedCache` capped at 256 entries, keyed on a SHA-256 digest of the connection string
together with the read-only key/value, application-name setting and read-only suffix — every input
that shapes the cached map, so the same connection string normalized with different parameters
never shares an entry. The cached *value* has credential-like keys (password/user/secret/token/
access) dropped before storage, and the key is a one-way hash, so no raw connection string or
credential stays resident; per-tenant or rotated credentials can't grow the cache past its bound.

### Connection Reuse

**Standard/PreventDatabaseUnload modes**:
- Provider pool reuse (ADO.NET managed)
- DatabaseContext overhead: Minimal (delegate calls)

**SingleWriter mode**:
- All connections: Provider pool (ephemeral, like Standard)
- Write serialization via governor permit (no persistent connection)

**SingleConnection mode**:
- Zero connection allocations (one connection for lifetime)
- Lock contention cost: SemaphoreSlim overhead

### Benchmark Comparison (vs Dapper)

Measured, not projected — see `benchmarks/CrudBenchmarks/results/` for the underlying runs.

| Scenario | Result | Source |
|----------|--------|--------|
| PostgreSQL equal-footing CRUD (identical Npgsql auto-prepare config for all three frameworks), 1–100 records | pengdows.crud within roughly 0–10% of Dapper's mean time across read/filter/aggregate/create/update/delete scenarios (`P÷D` ≈ 0.98–1.10); EF Core ~1.1–1.6x slower than pengdows.crud | `postgres-run-2026-03-15-after-fix.md` |

Other benchmark suites (e.g. `HydrationHotPathBenchmarks.cs`, `SQLiteWriteContentionBenchmarks.cs`, `SqlServerEqualFootingBenchmarks.cs`) live in `benchmarks/CrudBenchmarks/`, but no results for them are checked in on this branch — run them yourself before quoting numbers.

**Design trade-off**: pengdows.crud prioritizes **control + testability** at near-parity performance with raw Dapper; where the library does governance work a raw query path doesn't (connection governance, session-setting enforcement), that cost is visible and is a correctness choice, not an oversight.

---

## Debugging Tips

### Enable Verbose Logging

```csharp
var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
});

var context = new DatabaseContext(
    connectionString,
    factory,
    new DatabaseContextConfiguration { LoggerFactory = loggerFactory }
);
```

**What gets logged**:
- Mode coercion (EventIds.ModeCoerced: 1002)
- Mode mismatch warnings (EventIds.ModeMismatch: 1001)
- Connection lifecycle (EventIds.ConnectionLifecycle: 4001)
- Dialect detection issues (EventIds.DialectDetection: 3001)

### Inspect Metrics

```csharp
context.MetricsUpdated += (sender, metrics) =>
{
    Console.WriteLine($"Open connections: {metrics.ConnectionsCurrent}");
    Console.WriteLine($"Opened: {metrics.ConnectionsOpened}");
    Console.WriteLine($"Closed: {metrics.ConnectionsClosed}");
    Console.WriteLine($"Failures: {metrics.CommandsFailed}");
};
```

### Trace Locking Behavior

Add diagnostics to connection strategies:

```csharp
// In development build, add logging to lock acquisition (internal)
public async Task<ITrackedConnection> AcquireConnection(ExecutionType executionType)
{
    var sw = Stopwatch.StartNew();
    var conn = await GetConnectionCore(executionType);
    _logger.LogDebug("Connection acquired in {ms}ms", sw.ElapsedMilliseconds);
    return conn;
}
```

---

## Version History

### 2.0 (Current)
- Breaking interface changes (`EntityHelper` → `TableGateway`)
- Interface-first design mandate; all public APIs in `pengdows.crud.abstractions`
- API baseline enforcement via `tools/interface-api-check`
- Separated integration tests into dedicated project (`pengdows.crud.IntegrationTests`)
- Streaming APIs (LoadStreamAsync, RetrieveStreamAsync)
- Enhanced fakeDb with connection failure simulation
- Two-level locking architecture
- Mode mismatch detection and warnings
- Comprehensive XML documentation

### 1.0
- Initial release
- Basic CRUD operations (`EntityHelper<TEntity, TRowID>`)
- Multi-database support
- Transaction management
- Audit field tracking

---

## Contributing to This Document

**When to update**:
- Adding new architectural patterns
- Changing internal contracts
- Discovering new common misconceptions
- Performance characteristic changes

**How to update**:
- Keep examples accurate and tested
- Document the "why" not just the "what"
- Add misconceptions discovered in code reviews or AI conversations
- Link to actual source files (line numbers will drift, but principle remains)

**Target audience**: Future you, future contributors, and AI systems trying to understand the codebase.

**Goal**: Make pengdows.crud "graspable" by reading this document alone, without extensive code diving.
