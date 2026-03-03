# Read-Only Enforcement

**pengdows.crud** provides robust, deterministic enforcement of read-only intent. This ensures that operations intended to be read-only cannot accidentally modify data, even if the application logic or raw SQL attempts to do so.

## Why Enforcement Matters

In pooled environments, connections are often reused. If a connection was previously used for a write operation and its state was not correctly reset, it could carry over permissions or settings that allow writes during a supposedly read-only operation. 

`pengdows.crud` addresses this by applying enforcement at two levels:
1.  **Connection String Level:** Forcing the database driver to open the physical connection in a read-only state.
2.  **Session SQL Level:** Executing database-specific commands immediately after leasing a connection to lock the session into read-only mode.

## Read-Only Enforcement Matrix

| Database | Connection String | Session SQL | Dual Enforcement | Enforcement Strategy |
| :--- | :---: | :---: | :---: | :--- |
| **PostgreSQL** | Yes | Yes | **Yes** | `Options='-c default_transaction_read_only=on'` + `SET ...` |
| **SQLite** | Yes | Yes | **Yes** | `Mode=ReadOnly` + `PRAGMA query_only = ON` |
| **DuckDB** | Yes | Yes | **Yes** | `access_mode=READ_ONLY` + `SET access_mode = 'read_only'` |
| **SQL Server** | Yes | No | No | `ApplicationIntent=ReadOnly` (Driver-managed) |
| **MySQL / MariaDB** | No | Yes | No | `SET SESSION TRANSACTION READ ONLY` |
| **Snowflake** | No | Yes | No | `ALTER SESSION SET TRANSACTION_READ_ONLY = TRUE` |
| **Oracle** | No | Yes | No | `SET TRANSACTION READ ONLY` |
| **Firebird** | No | Yes | No | `SET TRANSACTION READ ONLY` |

### Dual Enforcement (The Gold Standard)
For **PostgreSQL, SQLite, and DuckDB**, `pengdows.crud` provides "Gold Standard" security. The intent is baked into the connection string (preventing the driver from even attempting a write) AND re-asserted via SQL on every checkout from the pool. This provides multiple layers of defense against "dirty" connections.

## How to Use Read-Only Mode

### 1. Read-Only DatabaseContext
Configure a context to be read-only globally. Any attempt to use `ExecutionType.Write` or start a write transaction will throw a `NotSupportedException`.

```csharp
var config = new DatabaseContextConfiguration {
    ConnectionString = "...",
    ReadWriteMode = ReadWriteMode.ReadOnly
};
var context = new DatabaseContext(config, factory);
```

### 2. Read-Only Operations
Request a read-only connection for a specific operation.

```csharp
using var connection = context.GetConnection(ExecutionType.Read);
// Connection is now enforced based on the matrix above.
```

### 3. Read-Only Transactions
Start a transaction with read-only intent.

```csharp
using var tx = await context.BeginTransactionAsync(IsolationProfile.SafeNonBlockingReads);
// Transaction is locked to read-only at the session level.
```

## Performance Impact
`pengdows.crud` optimizes read-only enforcement to ensure it stays within a **single round-trip (1 RTT)** per lease. Session SQL is combined with the baseline initialization string into a single command batch, so you get security without a latency penalty.
