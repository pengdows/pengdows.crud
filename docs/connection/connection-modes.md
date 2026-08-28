# Connection Mode Invariants (`DbMode`)

This document defines the intent, invariants, and coercion rules for connection management modes in `pengdows.crud`.
It resolves ambiguities so future contributors cannot bikeshed these rules.

## 0. Philosophy

`pengdows.crud` handles connections with a strong bias toward performance, predictability, and safe concurrency:

- Open connections late — only when needed.
- Close connections early — as soon as possible.
- Respect database-specific quirks (see `connection-pooling.md` for SQLite and LocalDB rules).

This avoids exhausting the connection pool, avoids leaking resources or unclosed connections, and reduces cost in cloud environments by minimizing active resource usage.

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

**KeepAlive exists for exactly one reason, and it has nothing to do with performance.** It does not warm a connection for faster queries, does not cache anything, and does not optimize any code path — it is purely a workaround for one specific piece of external, uncontrollable engine behavior.

The mechanism: pengdows.crud's default philosophy (Standard mode) opens a connection late and closes it early — every operation gets a fresh connection from the pool and releases it back immediately after. That's normally harmless, because the ADO.NET connection pool keeps the underlying physical connections warm behind the scenes. But **SQL Server LocalDB is not a normal server** — it's a lightweight, self-managed engine process that watches its own connection count and **automatically unloads the database (shuts the engine instance down) once it observes zero active connections for a while**. If pengdows.crud used plain Standard mode against LocalDB, a quiet period (no requests for a stretch) would let the pool drain to zero open connections, LocalDB would notice and unload the database, and the *next* request would pay the cost of LocalDB re-launching and re-attaching the database file before it could even open a connection.

KeepAlive's entire job is to prevent that specific failure mode: it holds one single pinned, idle connection open for the lifetime of the `DatabaseContext`, purely so LocalDB always sees at least one active connection and never decides to unload. **That pinned connection is never used for any command, ever** — every real read and write still goes through its own fresh ephemeral connection exactly like Standard mode. The sentinel's only job is to exist and stay open; it does not participate in the "hot path" in any way, and removing it would not change query latency at all except for the LocalDB-unload-then-relaunch penalty it exists to prevent.

- Automatically selected only for LocalDb (`CoerceMode` forces LocalDb → KeepAlive, and `Best` → KeepAlive for LocalDb). For SQLite/DuckDB, a KeepAlive request is always coerced to SingleWriter instead — it is never actually reachable there regardless of what's requested. On full-server databases it is honored if explicitly requested ("safe but less functional," not unsafe), but it is not the recommended or automatically-selected choice for any of them.
- Not a general production-workload mode the way Standard/SingleWriter are — scoped to the LocalDb sentinel use case specifically.

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
- **Production default for file-based SQLite/DuckDB** (equal footing with Standard's production status for client-server databases). For SQLite, the turnstile-governed write serialization is purpose-built to eliminate the file-locking errors (`SQLITE_BUSY`) the engine is otherwise prone to under concurrent writers. DuckDB's own engine does not actually have this limitation — it supports concurrent non-conflicting writes within one process — so SingleWriter there is pengdows.crud's own deterministic policy choice, not a limitation DuckDB's engine imposes (see `docs/positioning/product-thesis.md`). Reads still execute fully concurrently on ephemeral connections for both — a level of write-contention governance most comparable libraries don't provide for these engines at all.

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
    startup options instead): see `docs/planning/future-work.md`'s SQL Server session-settings entry.
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
- **After construction, `KeepAlive` and `SingleConnection` handle a broken pinned connection very differently — this asymmetry is not obvious from the mode names alone.** `KeepAliveConnectionStrategy` checks sentinel health lazily on every `GetConnection()` call (a cheap unlocked state check in the common case) and transparently repairs — disposes the dead sentinel, opens a fresh one, swaps it in — if it's `Broken`/`Closed`. `SingleConnectionStrategy.GetConnection()` has **no health check at all**; it unconditionally returns the stored connection. If that one connection breaks, every subsequent operation on that context fails against a dead connection for the rest of its lifetime — the only recovery is disposing and reconstructing the whole `DatabaseContext`.

  **For `:memory:` SQLite/DuckDB, this isn't a missing feature — it's unrepairable in principle, not just in the current implementation.** The entire database lives only inside that one connection; there is no separate file or server for a replacement connection to reconnect to. Opening a *new* connection to the same `:memory:` connection string doesn't recover the old data, it silently creates a brand-new, empty database — which would be a much worse failure mode than the current loud "every operation now fails" behavior. So for `:memory:` specifically, treat `SingleConnection` mode as "the data does not survive a connection break," full stop, not as a gap to fix.

  For other single-connection-limited engines with real persistent storage behind the one connection (e.g. Firebird embedded, a `.fdb` file), the connection break is against durable data — reopening a fresh connection to the same file *would* actually recover it, so the current lack of any repair attempt is a genuine, fixable gap there, unlike the `:memory:` case.

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

## 10. Practical Guidance

**Best practices:**
- `Standard`, `SingleWriter`, and `KeepAlive` are all production-supported, each for a different deployment shape: `Standard` for client-server databases, `SingleWriter` for file-based SQLite/DuckDB, `KeepAlive` for single-machine/embedded deployments backed by SQL Server LocalDB.
- `SingleConnection` against `:memory:` SQLite/DuckDB is **never production-suitable, structurally** — an in-memory database has no persistence outside that one connection, so it cannot survive a process restart or a dropped connection (see §6). Reserve it for tests and ephemeral scratch data. Against a durable-storage single-connection engine (e.g. Firebird embedded), `SingleConnection` is at least structurally viable, but remains a narrow/niche case, not general production, and currently has no connection-repair path (see §6 and `docs/planning/future-work.md`).
- Each `DatabaseContext` can be safely used as a singleton (via DI or subclassing).

**Timeouts:**
- Set connection timeouts as low as reasonable to avoid hanging on transient failures.
- Because ephemeral modes reconnect for every call, long timeouts are unnecessary.

**Observability:**
- `TrackedConnection` tracks current and max open connections with thread-safe `Interlocked` counters — useful for tuning pool sizes and spotting load issues.
- Monitor `ModeContentionStats` (see §"Shared connection locking & timeouts" concepts in `connection-pooling.md`) through logs/metrics to see which operations are queuing on the mode lock.

---

This contract is authoritative — implement according to these rules, and contributors must not deviate.
