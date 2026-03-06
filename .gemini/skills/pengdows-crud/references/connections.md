# Connections & Transactions

`pengdows.crud` provides robust, opinionated connection management.

## DatabaseContext (Singleton)

Manages connection pool, metrics, and session initialization. Create one instance per connection string.

## DbMode & Connection Strategies

| Mode | Use Case | Behavior |
|------|----------|----------|
| **Standard** | Production (server DBs) | New connection per operation; relies on provider pooling. |
| **KeepAlive** | LocalDB | Keeps a sentinel connection open to prevent unload. |
| **SingleWriter** | SQLite file-based | Serializes writes via turnstile; readers use ephemeral connections. |
| **SingleConnection** | SQLite `:memory:` | All operations share one pinned connection (thread-safe). |
| **Best** (Default) | Recommended | Auto-selects based on dialect and connection string. |

## Transactions

- **Explicit creation:** Use `using var tx = context.BeginTransaction();`.
- **Pinned connection:** Transactions pin a connection until disposed.
- **Savepoints:** Supports `SavepointAsync` and `RollbackToSavepointAsync`.
- **Isolation Profiles:** Portable profiles that map to optimal native levels:
  - `IsolationProfile.SafeNonBlockingReads`: MVCC snapshot (where supported).
  - `IsolationProfile.StrictConsistency`: Serializable / Full isolation.

## Pool Governor

Prevents connection pool exhaustion by limiting concurrent connections:
- `MaxConcurrentReads` / `MaxConcurrentWrites`: Separate limits.
- `PoolAcquireTimeout`: Throws `PoolSaturatedException` on timeout.
- Turnstile fairness prevents writer starvation.

## Multi-Tenancy

`ITenantContextRegistry` supports **tenant-per-database** physical isolation.
- Each tenant gets a separate `DatabaseContext`.
- Contexts are cached for reuse.
- Different tenants can use different database engines (e.g., Tenant A on SQL Server, Tenant B on PostgreSQL).
