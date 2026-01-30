# pengdows.crud Internal Architecture

This document explains the internal design of pengdows.crud version 1.1 for developers working on the library itself or AI assistants trying to understand how it works.

**Audience**: Library maintainers, contributors, and AI systems analyzing the codebase.

**Purpose**: Prevent misconceptions and hallucinations by documenting architectural decisions, internal contracts, and design trade-offs.

---

## Terminology

**CRITICAL**: `DatabaseContext` is not equivalent to Entity Framework's `DbContext`.

`DatabaseContext` is a **singleton execution coordinator** bound to a specific provider + connection string.

**Concurrent callers are supported:**
- **Standard**: parallel operations using ephemeral connections
- **KeepAlive/SingleWriter/SingleConnection**: operations serialize on shared connection lock

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
- **Explicit connection modes**: User chooses Standard/KeepAlive/SingleWriter/SingleConnection

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
| **KeepAlive** | ✅ Fully concurrent | Sentinel connection stays open but unused. Operations get ephemeral connections. |
| **SingleWriter** | ⚠️ Writes serialize | One persistent write connection (serialized). Reads get ephemeral connections (concurrent). |
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

// Thread B (concurrent attempt)
await container.ExecuteNonQueryAsync();
// BLOCKS on _userLock until reader disposed
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
// pengdows.crud/DatabaseContext.Metrics.cs:156
handler.Invoke(this, metrics);  // No lock held during callback
```

**WARNING**: Do not call back into the same DatabaseContext from event handlers. This is documented but not enforced. Adding a lock would cause deadlocks.

**Why no lock?** Standard .NET event pattern. Subscribers are observers, not controllers. Lock during callback = guaranteed deadlock if subscriber tries to use the context.

---

## Connection Lifecycle Management

### Connection Ownership Rules

**Ephemeral Connections (Standard/KeepAlive modes)**:
- Created per operation
- Owned by the operation
- Disposed when operation completes
- Come from provider's connection pool (ADO.NET managed)

**Persistent Connections (SingleWriter/SingleConnection modes)**:
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
| SQL Server | LocalDB | `KeepAlive` | **REQUIRED** - Prevents instance unload |

**Coercion** (forced mode change):
- SQLite `:memory:` + Standard → **Coerced to SingleConnection** (correctness)
- SQLite file + Standard → **Coerced to SingleWriter** (safety, prevents SQLITE_BUSY)
- Firebird embedded → **Coerced to SingleConnection** (vendor limitation)

**Mode Mismatch Warnings** (safe but suboptimal):
- PostgreSQL + SingleConnection → Logs warning (limits concurrency unnecessarily)
- SQL Server + SingleWriter → Logs warning (limits concurrency unnecessarily)

See `/home/alaricd/prj/pengdows/1.1/pengdows.crud/DatabaseContext.Initialization.cs:561-661` for full coercion logic.

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
  - SingleWriter mode: Write connection is shared
  - SingleConnection mode: The one connection is shared
  - KeepAlive mode: Sentinel connection is shared (but never used for work)

- **NoOpAsyncLocker** for **ephemeral connections**:
  - Standard mode: Each operation gets its own connection
  - SingleWriter mode: Read connections are ephemeral

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
var value = await container.ExecuteScalarAsync<int>();
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

## Strategy Pattern Architecture

### IConnectionStrategy Implementations

**Standard mode** (`StandardConnectionStrategy`):
- Every operation: Create → Open → Use → Close → Dispose
- Connection from provider pool
- No persistent connection

**KeepAlive mode** (`KeepAliveConnectionStrategy`):
- One sentinel connection kept open (never used)
- Prevents database unload (LocalDB, embedded SQLite)
- All work uses ephemeral connections (like Standard)

**SingleWriter mode** (`SingleWriterConnectionStrategy`):
- One persistent **write** connection (held open)
- Ephemeral **read** connections (created per read)
- Optimal for SQLite files (WAL enables many readers + one writer)

**SingleConnection mode** (`SingleConnectionStrategy`):
- One persistent connection for **all** operations
- Required for SQLite `:memory:` (each connection = separate database)
- All operations serialize at connection lock

### Strategy Selection Logic

**Initialization** (DatabaseContext.Initialization.cs:464):
```csharp
var requestedMode = ConnectionMode;
ConnectionMode = CoerceMode(requestedMode, product, isLocalDb, isFirebirdEmbedded);
WarnOnModeMismatch(ConnectionMode, product, wasCoerced: requestedMode != ConnectionMode);
```

**Strategy instantiation** (strategies/connection/ConnectionStrategyFactory.cs):
```csharp
public static IConnectionStrategy Create(DbMode mode, IDatabaseContext context)
{
    return mode switch
    {
        DbMode.Standard => new StandardConnectionStrategy(context),
        DbMode.KeepAlive => new KeepAliveConnectionStrategy(context),
        DbMode.SingleWriter => new SingleWriterConnectionStrategy(context),
        DbMode.SingleConnection => new SingleConnectionStrategy(context),
        _ => new StandardConnectionStrategy(context)
    };
}
```

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
// pengdows.crud/wrappers/TrackedReader.cs:216-235
public async Task<bool> ReadAsync(CancellationToken cancellationToken)
{
    if (_disposed) return false;

    var result = await _reader.ReadAsync(cancellationToken);

    if (!result)
    {
        await DisposeAsync();  // ← Auto-dispose on end-of-results
    }

    return result;
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
| **Connection Pooling** | Always provider-managed | Mode-dependent (Standard=pooled, SingleWriter=pinned) |
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
// Problem: In SingleWriter mode, each context creates a persistent write connection
// Outcome: Multiple write connections to SQLite → SQLITE_BUSY errors
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

**Correct**: DatabaseContext **uses** provider pooling in Standard/KeepAlive modes.

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
- Firebird embedded → **REQUIRED** (vendor limitation)
- Small embedded databases → **OPTIMAL** (no connection overhead)

**When it's suboptimal**:
- PostgreSQL, SQL Server, MySQL → Use Standard instead (supports concurrency)

**Correct**: SingleConnection is the **right choice** for certain databases. Not dangerous, just specialized.

### 7. "Best mode always selects Standard"

**❌ WRONG**. Best mode is **database-specific**.

**Actual selection**:
- PostgreSQL/MySQL/Oracle → Standard (full concurrency)
- SQLite `:memory:` → SingleConnection (required for correctness)
- SQLite file → SingleWriter (optimal for WAL)
- SQL Server LocalDB → KeepAlive (prevents unload)

**Correct**: Best = "most functional safe mode for this specific database".

### 8. "Re-entrancy in MetricsUpdated is prevented by locking"

**❌ WRONG**. Re-entrancy is **documented but not prevented**.

**Why no lock?**
- Lock during callback = guaranteed deadlock if subscriber uses context
- Standard .NET event pattern: fire without locks
- Subscribers expected to be observers, not controllers

**What's actually done**:
- Warning in XML docs (DatabaseContext.cs:47-48)
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

### IConnectionStrategy ↔ DatabaseContext

**Contract**:
- Strategy **must** return ephemeral connections via internal GetConnection for Standard/KeepAlive modes
- Strategy **must** return the persistent connection for SingleWriter write operations
- Strategy **must** return ephemeral read connections for SingleWriter read operations
- Strategy **must** return the persistent connection for SingleConnection all operations

**Enforcement**: Strategy implementations in `pengdows.crud/strategies/connection/`

### ITrackedConnection ↔ TrackedReader

**Contract**:
- Connection **must** remain open for reader's lifetime
- Connection **must not** be used by other operations while reader is active
- Connection lock **must** be held from reader creation until reader disposal
- Auto-disposal **must** release connection and lock

**Enforcement**: TrackedReader.cs:91-150 (disposal logic)

### MetricsCollector ↔ DatabaseContext

**Contract**:
- MetricsCollector fires MetricsChanged event **without holding locks**
- DatabaseContext subscribes in constructor (DatabaseContext.Metrics.cs:320)
- DatabaseContext unsubscribes in disposal (DatabaseContext.Metrics.cs:320)
- Event handler (OnMetricsCollectorUpdated) **must not** acquire locks

**Enforcement**: DatabaseContext.Metrics.cs:137-157

### SqlDialect ↔ DataSourceInformation

**Contract**:
- DataSourceInformation detected once during initialization
- SqlDialect **must** remain immutable after construction
- Dialect selection based on SupportedDatabase enum
- Vendor-specific behaviors encapsulated in dialect implementation

**Enforcement**: DatabaseContext.Initialization.cs:422-460

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

**Benchmark**: `benchmarks/CrudBenchmarks/ReaderMappingBenchmark.cs`

**Results** (AMD Ryzen 9 5950X, .NET 8.0.22):
- **100 rows**: 161.6 µs vs 969.7 µs = **6.0x faster** than pure reflection
- **1,000 rows**: 1.76 ms vs 9.80 ms = **5.57x faster** than pure reflection
- **Per-row**: ~1,700ns vs ~9,700ns

**How it works**:
1. First query: Introspect reader schema, build plan
2. Generate Expression tree for property setter
3. Compile to delegate (cached)
4. Subsequent queries: Direct delegate invocation (no reflection)

**Code**: `pengdows.crud/EntityHelper.Mapping.cs:BuildReaderPlan()`

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

### Connection Reuse

**Standard/KeepAlive modes**:
- Provider pool reuse (ADO.NET managed)
- DatabaseContext overhead: Minimal (delegate calls)

**SingleWriter mode**:
- Write connection: Zero allocation (reused)
- Read connections: Provider pool

**SingleConnection mode**:
- Zero connection allocations (one connection for lifetime)
- Lock contention cost: SemaphoreSlim overhead

### Benchmark Comparison (vs Dapper)

**Note**: No official benchmarks yet, but expected characteristics:

| Operation | pengdows.crud | Dapper | Notes |
|-----------|---------------|---------|-------|
| Simple query | ~Equal | Baseline | Both use compiled setters |
| Complex mapping | Slower | Faster | Dapper more optimized |
| SQL generation | N/A | N/A | Dapper doesn't generate SQL |
| Streaming | ~Equal | ~Equal | Both use IAsyncEnumerable |

**Design trade-off**: pengdows.crud prioritizes **control + testability** over **absolute performance**.

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
    Console.WriteLine($"Open connections: {metrics.CurrentOpenConnections}");
    Console.WriteLine($"Total created: {metrics.TotalConnectionsCreated}");
    Console.WriteLine($"Reused: {metrics.TotalConnectionsReused}");
    Console.WriteLine($"Failures: {metrics.TotalConnectionFailures}");
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

### 1.1 (Current)
- Streaming APIs (LoadStreamAsync, RetrieveStreamAsync)
- Enhanced fakeDb with connection failure simulation
- Two-level locking architecture
- Mode mismatch detection and warnings
- Comprehensive XML documentation
- This architecture document

### 1.0
- Initial release
- Basic CRUD operations
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
