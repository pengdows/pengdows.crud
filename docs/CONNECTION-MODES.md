# Connection Mode Invariants (`DbMode`)

This document defines the intent, invariants, and coercion rules for connection management modes in `pengdows.crud`.
It resolves ambiguities so future contributors cannot bikeshed these rules.

## 1. Modes & Lifecycle

The `DbMode` enum values are: `Standard=0`, `KeepAlive=1`, `SingleWriter=2`, `SingleConnection=4`, `Best=15`.

### Standard

- Semantics: Ephemeral pooled connections. New connection for each statement unless inside a transaction.
- Production default for all full server databases (PostgreSQL, SQL Server, Oracle, MySQL, MariaDB, CockroachDB).
- Constructor behavior: Attempts to open a connection at initialization to detect dialect.
  - If Open() fails → throw immediately.
  - If connection opens but dialect cannot be resolved → fall back to SQL-92 dialect (SQL-92 is a fallback behavior, not a distinct DbMode or supported database product).
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

- Semantics: Identical to Standard, plus a governor profile: writable connections capped at 1 concurrent writer, read-only connections allow 0 writers; writer-starvation-prevention turnstile enabled.
- Reads:
  - Non-transactional → ephemeral read-only connections that use the read-only preamble.
  - Read-only transactions → ephemeral read-only connections (reader concurrency pauses while writers wait).
  - Write transactions → serialize through the write permit while retaining the connection for the transaction's duration.
- Used for: SQLite/DuckDB file-based and shared caches where writers must serialize without pinning a connection.
- **Production default for file-based SQLite/DuckDB** (equal footing with Standard's production status for client-server databases). The turnstile-governed write serialization is purpose-built to eliminate the file-locking errors those engines are otherwise prone to under concurrent writers, while reads still execute fully concurrently on ephemeral connections — a level of write-contention governance most comparable libraries don't provide for these engines at all.

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

- SessionSettingsPreamble is applied once per *logical* connection open (each `TrackedConnection`
  wrapper's first `Open`/`OpenAsync`), not once per *physical* connection. Whether that means
  "once ever" or "every checkout" depends entirely on the mode's wrapper lifetime:
  - **Persistent modes** (KeepAlive's pinned connection, SingleConnection): one `TrackedConnection`
    wrapper lives for the whole context lifetime, so the preamble genuinely executes exactly once.
  - **Ephemeral modes** (Standard, SingleWriter): a fresh `TrackedConnection` wrapper is created
    per operation/checkout, so the preamble **is reapplied on every single checkout** — even when
    the underlying ADO.NET provider pool hands back an already-open physical connection. This is
    deliberate, not a missed optimization: a connection previously used for an unrelated operation
    could have drifted session state (e.g. a stale isolation override, or — for SQL Server —
    `QUOTED_IDENTIFIER`, which this framework's own ANSI double-quote identifier quoting depends on
    to parse at all), so trusting "the pool gave me a connection, it must still be clean" is a
    correctness risk, not just a consistency one. Quantified cost tradeoff (SQL Server pays this
    every operation under `DbMode.Standard`, PostgreSQL bakes settings into the data source's
    startup options instead): see `docs/FUTURE_WORK.md`'s SQL Server session-settings entry.
- Session settings are enforced at logical connection open (per the wrapper-lifetime rules above).
  Do not mutate session-scoped settings mid-connection when using pooling.
- ReadWriteMode.ReadOnly:
  - `SqlContainer` pre-guards every write in code: it throws `NotSupportedException` for
    `ExecutionType.Write` when `ReadWriteMode.ReadOnly` is set, before any provider call is
    made. This covers every database, including ones with no enforcement below it.
  - Where the database itself also enforces read-only, the mechanism is dialect-specific and
    not uniform: PostgreSQL/MySQL/MariaDB/SQLite/DuckDB enforce at the connection/session
    level (distinct SQL/connection-string parameter per dialect — see
    `docs/read-only-enforcement.md`); Oracle only enforces per-transaction (`SET TRANSACTION
    READ ONLY`, no persistent session mode); SQL Server's `ApplicationIntent=ReadOnly` is
    documented by `SqlServerDialect` as an Availability-Group routing hint only — it does
    NOT enforce server-side read-only state. SQLite does not use `PRAGMA query_only`; it
    uses `Mode=ReadOnly` in the connection string.

## 5. Connection Sharing & Transactions

- All commands inside a transaction (read or write) share the same physical connection.
- Rules by mode:
  - Standard / KeepAlive: `BeginTransaction()` creates a pinned connection for that scope.
  - Write tx → acquires the single write permit and reuses the transaction connection for the scope.
  - Read-only tx → ephemeral read-only connection that still respects governor fairness when writes queue.
  - SingleConnection: all tx use the single pinned connection.

## 6. Failure Behavior

- Non-transactional ephemeral connections: errors bubble at `Execute…` (open-late / close-early).
- Transaction start: `BeginTransaction()` eagerly opens the connection and errors surface immediately.
- Persistent modes (KeepAlive/SingleConnection): if pinned connection fails to open at ctor, error bubbles immediately.
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
  - Fallback if unknown dialect: `MaxParameterLimit = 2000` (`SqlDialect`'s base default).

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
