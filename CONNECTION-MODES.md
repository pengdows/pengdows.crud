# Connection Mode Invariants (`DbMode`)

This document defines the intent, invariants, and coercion rules for connection management modes in `pengdows.crud`.
It resolves ambiguities so future contributors cannot bikeshed these rules.

## 1. Modes & Lifecycle

### Standard

- Semantics: Ephemeral pooled connections. New connection for each statement unless inside a transaction.
- Production default for all full server databases (PostgreSQL, SQL Server, Oracle, MySQL, MariaDB, CockroachDB).
- Constructor behavior: Attempts to open a connection at initialization to detect dialect.
  - If Open() fails → throw immediately.
  - If connection opens but dialect cannot be resolved → fall back to SQL-92 dialect.
- Transactions: All reads/writes inside a transaction share the same connection.

### KeepAlive

- Semantics: Identical to Standard, except a single pinned idle connection is kept open to prevent unload (e.g. SQL Server LocalDb).
- Pinned connection is never used for commands.
- Not production-safe. Only for LocalDb.

### SingleConnection

- Semantics: One pinned connection handles everything — reads, writes, transactions.
- Threadsafe via `RealAsyncLocker`.
- Used for: SQLite/DuckDB `:memory:` and Firebird embedded.
- Not suitable for production concurrency.

### SingleWriter

- Semantics: One pinned connection for writes.
- Reads:
  - Non-transactional → ephemeral read-only connections.
  - Read-only transactions → ephemeral read-only connection.
  - Write transactions → writer connection.
- Used for: SQLite/DuckDB file-based databases.
- Threadsafe for pinned writer via `RealAsyncLocker`.

### Best

- Resolver hint only. Not an actual strategy.
- Defaults to the safest mode based on dialect + connection string:
  - Full servers → Standard
  - LocalDb → KeepAlive
  - SQLite/DuckDB `:memory:` → SingleConnection
  - SQLite/DuckDB file-based → SingleWriter
  - Firebird embedded → SingleConnection
  - Unknown product → Standard

## 2. Provider-Driven Coercion

### Always forced (cannot override):

- SQLite/DuckDB `:memory:` → SingleConnection
- Firebird embedded (`.fdb` file, no `Server=`) → SingleConnection

### Allowed for SQLite/DuckDB file-based:

- SingleWriter (default for Best)
- SingleConnection (allowed alternative)
- Standard/KeepAlive → coerced to SingleWriter with a Warning log

### LocalDb: coerced to KeepAlive.

### Full servers: always Standard.

### FakeDb: no special case. It emulates a real dialect via `EmulatedProduct` and follows all the above rules.

Logging:

Whenever a user-specified mode is coerced, log at Warning:

```
DbMode override: requested {requested}, coerced to {resolved} — reason: {reason}
```

## 3. Initialization & Dialect Detection

- Dialect detection currently runs at constructor by opening a connection.
- If open fails → throw.
- If open succeeds but product is unknown → fall back to SQL-92 dialect.
- (Future option: move to deferred first-open, but for now eager detection is required.)

## 4. Session Settings & Read-Only

- SessionSettingsPreamble is applied once per physical connection, at open.
  - Not reapplied when a connection is reused from pool or pinned.
- Session settings are enforced at physical connection open. Do not mutate session-scoped settings mid-connection when using pooling.
- ReadWriteMode.ReadOnly:
  - Every opened connection must enforce read-only at connection string and session level (e.g., `ApplicationIntent=ReadOnly`, `SET TRANSACTION READ ONLY`, `PRAGMA query_only = 1`).
  - Write attempts bubble provider errors (not pre-guarded in code).

## 5. Connection Sharing & Transactions

- All commands inside a transaction (read or write) share the same physical connection.
- Rules by mode:
  - Standard / KeepAlive: `BeginTransaction()` creates a pinned connection for that scope.
  - SingleWriter:
    - Write tx → writer connection.
    - Read-only tx → ephemeral read-only connection.
  - SingleConnection: all tx use the single pinned connection.

## 6. Failure Behavior

- Non-transactional ephemeral connections: errors bubble at `Execute…` (open-late / close-early).
- Transaction start: `BeginTransaction()` eagerly opens the connection and errors surface immediately.
- Persistent modes (KeepAlive/SingleWriter/SingleConnection): if pinned connection fails to open at ctor, error bubbles immediately.
- No silent deferrals beyond SQL-92 fallback when dialect is unknown.

## 7. Heuristics & Tests

- Explicit Standard on embedded → coerced (never throw):
  - SQLite/DuckDB `:memory:` → SingleConnection
  - Firebird embedded → SingleConnection
  - SQLite/DuckDB file-based → SingleWriter
- Unknown product with Best → Standard.

## 8. Metrics & Limits

- Connection counting is handled by `TrackedConnection`.
- Counts increment on physical open, decrement on close.
- Includes pinned and ephemeral connections, including transaction connections.
- Limits:
  - `MaxParameterLimit`, `MaxOutputParameters`, `ParameterNameMaxLength` come from `DataSourceInformation`.
  - Mode-independent.
  - Fallback if unknown dialect: `MaxParameterLimit = 255` (lowest common across RDBMS).

## 9. Edge Policies

### Prepare Policy

- Default: prepare is on.
- Unknown dialect → assume prepare works.
- On first failure (unsupported), disable prepare for that connection.
- Dialects may override (`PrepareStatements`, `ShouldDisablePrepareOn(ex)`).

### Cancellation

- Tests must expect `OperationCanceledException`.
- `TaskCanceledException` may occur, but base type is sufficient and consistent.

---

This contract is authoritative — implement according to these rules, and contributors must not deviate.
