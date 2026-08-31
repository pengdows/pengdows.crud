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
- **Actual use case: SQL Server LocalDB only.** `CoerceMode` forces LocalDB → KeepAlive automatically. For SQLite/DuckDB, requesting KeepAlive is always coerced to `SingleWriter` instead — it never actually applies there regardless of what's requested. It's honored (not coerced away) if explicitly requested against a full-server database, but that isn't a recommended or automatically-selected use — LocalDB is the only case where KeepAlive is both reachable and needed.
- *Clarification*: NOT for AWS RDS Proxy or server-side connection keep-alives, and not a substitute for `SingleWriter` on SQLite/DuckDB.

#### 3. SingleWriter (`2`)
- **For file-based SQLite**, where concurrent writers cause disk locking errors (`SQLITE_BUSY`). **For file-based DuckDB**, this is a deliberate policy choice, not a limitation of the engine — DuckDB itself supports concurrent non-conflicting writes within one process; pengdows.crud still serializes writes on it for a single, predictable cross-engine contract.
- Connections are still **ephemeral** (opened late, closed early).
- Write tasks are serialized by `PoolGovernor` with `MaxConcurrentWrites = 1` and a writer-preference turnstile.
- Readers remain concurrent and ephemeral.

#### 4. SingleConnection (`4`)
- All operations (reads and writes) share a **single persistent connection**.
- Serialized via `RealAsyncLocker` (`SemaphoreSlim`).
- **Target Use Case**: SQLite/DuckDB in-memory databases for tests and ephemeral scratch work; durable Firebird embedded deployments where one connection is the supported production shape. For `Data Source=:memory:`, the database disappears with the owning connection and cannot be recovered by reconnecting.

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

## Dynamic Provider Loading & Multi-Tenancy

`DbProviderLoader` resolves `DbProviderFactory` instances from a `DatabaseProviders` config
section (`AssemblyPath`/`AssemblyName` + `FactoryType`, or a `DbProviderFactories.GetFactory`
fallback) instead of requiring compile-time references — this is what `ITenantContextRegistry`
uses under the hood for context-per-tenant multi-tenancy. Register it via
`services.AddDbProviderLoading(configuration)` — the standard DI entry point, typically called
alongside `AddMultiTenancy`.

**Gotcha:** factories are registered as a keyed DI singleton under the config section's own
**key**, not under that section's `ProviderName` field. A tenant's
`IDatabaseContextConfiguration.ProviderName` must match the section key it should resolve to —
setting it to the ADO.NET invariant name instead throws a descriptive `InvalidOperationException`.
`FactoryType`'s static `Instance` accessor may be a property or a field (both real-world ADO.NET
conventions are supported); `AssemblyPath` is contained to the app base directory including
through symlinks. See `docs/connection/dynamic-provider-loading.md` in the repo for the full
resolution order and examples.

`services.AddMultiTenancy(configuration)` binds `MultiTenantOptions` from the `MultiTenant`
config section, including the optional `MultiTenant:MaxTenantCount` cap — set it to enforce
`TenantContextRegistry`'s production safety cap on distinct cached tenants through the standard
DI path (omit it, or leave it `null`, for unbounded).

Multi-tenancy itself is **context-per-tenant**, not row-filtering — no injected
`WHERE tenant_id = @tenant`, and no separate tenant-ID concept anywhere in the API; the
`IDatabaseContext` a caller gets back from `ITenantContextRegistry.GetContext(tenantId)` *is* the
tenant's identity (`ContextCreated`/`ContextRemoved` pass only that context, no tenant-ID
parameter). **There is no designed live tenant-ejection/rotation feature** — the intended way a
tenant's context gets disposed is application shutdown (disposing the registry disposes every
context it created). `TenantContextRegistry.Invalidate`/`InvalidateAll` exist and are tested
(concrete `TenantConnectionResolver.Register(tenant, newConfig)`, not on the interface, followed by
`registry.Invalidate(tenant)`), but using them for live rotation is an application-level choice
outside their designed use case, not a recommended pattern — there is no drain phase and no
protection for a caller that already holds a live context reference when a concurrent `Invalidate`
disposes it (a documented, accepted limitation, not a bug, and one more reason shutdown-only
disposal is the intended model). `ITenantContextRegistry.GetContextAsync(tenant, ct)` is the
non-blocking counterpart to `GetContext` — same cache, same tenant, but a not-yet-cached tenant's
construction doesn't block the calling thread; two concurrent `GetContextAsync` calls racing the
same new tenant dedup to one winner, the loser's already-built context disposed as an orphan. A
tenant list that isn't static configuration (loaded from a control-plane database, provisioned
externally) skips `AddMultiTenancy` entirely: implement `ITenantConnectionResolver` yourself and
register it plus `IDatabaseContextFactory`/`ITenantContextRegistry` directly — see
`docs/examples/CustomTenantResolver-example.cs` in the repo for a worked example. See
`docs/connection/multitenancy.md` in the repo for the full configuration shape, DI wiring,
request-time usage pattern, custom-resolver setup, and lifecycle-event contract, and
`docs/connection/multitenancy-architecture.md` for the deeper library-enforced-vs-deployment-assumed
contract and exact `Invalidate`/`GetContextAsync` concurrency semantics.

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
