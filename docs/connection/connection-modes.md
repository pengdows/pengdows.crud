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

The `DbMode` enum values are: `Standard=0`, `PreventDatabaseUnload=1`, `SingleWriter=2`, `SingleConnection=4`, `Best=15`. `KeepAlive` remains an obsolete compatibility alias for `PreventDatabaseUnload`.

### Standard

- Semantics: Ephemeral pooled connections. New connection for each statement unless inside a transaction.
- Production default for all full server databases (PostgreSQL, SQL Server, Oracle, MySQL, MariaDB, CockroachDB).
- Constructor behavior: Attempts to open a connection at initialization to detect dialect.
  - If Open() fails → throw immediately.
  - If connection opens but dialect cannot be resolved → fall back to SQL-92 dialect (SQL-92 is a fallback behavior, not a distinct DbMode or supported database product).
- Transactions: All reads/writes inside a transaction share the same connection.

### PreventDatabaseUnload

**PreventDatabaseUnload exists for exactly one reason, and it has nothing to do with performance.** It is not a health check, TCP keepalive, periodic ping, warmed working connection, or concurrency mode. It retains passive sentinel connections solely to prevent a target database from unloading, deactivating, closing, or pausing.

The mechanism: pengdows.crud's default philosophy (Standard mode) opens a connection late and closes it early — every operation gets a fresh connection from the pool and releases it back immediately after. That's normally harmless, because the ADO.NET connection pool keeps the underlying physical connections warm behind the scenes. But **SQL Server LocalDB is not a normal server** — it's a lightweight, self-managed engine process that watches its own connection count and **automatically unloads the database (shuts the engine instance down) once it observes zero active connections for a while**. If pengdows.crud used plain Standard mode against LocalDB, a quiet period (no requests for a stretch) would let the pool drain to zero open connections, LocalDB would notice and unload the database, and the *next* request would pay the cost of LocalDB re-launching and re-attaching the database file before it could even open a connection.

The mode opens one sentinel through the normal connection and session-initialization path for every materially separate configured pool. **Sentinels never execute application commands, open transactions, or hand work to callers** — every real read and write still goes through its own fresh ephemeral connection exactly like Standard mode. Each sentinel consumes one permit from its corresponding governor, so effective working capacity is the configured capacity minus the retained sentinel(s).

- Automatically selected for SQL Server LocalDB and Firebird embedded. It is also available explicitly for normal Firebird servers and other providers where the application intentionally wants to prevent database deactivation or auto-pause.
- Separate read and write connection strings receive separate sentinels. A reported Closed/Broken sentinel is replaced lazily, through the normal connection path, after confirming that the context is still active.

### SingleConnection

- Semantics: One pinned connection handles everything — reads, writes, transactions.
- Threadsafe via `RealAsyncLocker`.
- Used for: SQLite/DuckDB `:memory:` and explicitly selected specialized single-connection
  deployments. Durable embedded Firebird is automatically handled by `PreventDatabaseUnload`.
- For SQLite/DuckDB `:memory:`, this is primarily a testing, example, or ephemeral-scratch
  mode. The database is owned by the connection; if that connection closes, its contents
  cannot be recovered by opening another connection.
- Firebird embedded is different: durable embedded storage automatically selects
  `PreventDatabaseUnload`, while `SingleConnection` remains an explicitly selected
  specialized mode. The latter is still a single-concurrency boundary.

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
  - LocalDb → PreventDatabaseUnload
  - SQLite/DuckDB `:memory:` → SingleConnection
  - SQLite/DuckDB file-based → SingleWriter
  - Firebird embedded → PreventDatabaseUnload
  - Unknown product → Standard

## 2. Provider-Driven Coercion

### Always forced (cannot override):

- SQLite/DuckDB `:memory:` → SingleConnection
- Firebird embedded (`.fdb` file, no `Server=`) → PreventDatabaseUnload

### Allowed for SQLite/DuckDB file-based:

- SingleWriter (default for Best)
- SingleConnection (allowed alternative)
- Standard/PreventDatabaseUnload → coerced to SingleWriter with a Warning log

### LocalDb: coerced to PreventDatabaseUnload.

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
  - **Persistent modes** (PreventDatabaseUnload sentinels, SingleConnection): one or more `TrackedConnection` instances
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
  - Standard / PreventDatabaseUnload: `BeginTransaction()` creates a pinned connection for that scope.
  - Write tx → acquires the single write permit and reuses the transaction connection for the scope.
  - Read-only tx → ephemeral read-only connection that still respects governor fairness when writes queue.
  - SingleConnection: all tx use the single pinned connection.

## 6. Failure Behavior

- Non-transactional ephemeral connections: errors bubble at `Execute…` (open-late / close-early).
- Transaction start: `BeginTransaction()` eagerly opens the connection and errors surface immediately.
- Persistent modes (PreventDatabaseUnload/SingleConnection): if a required connection fails to open at ctor, error bubbles immediately.
- No silent deferrals beyond SQL-92 fallback when dialect is unknown.
- **After construction, `PreventDatabaseUnload` and `SingleConnection` have different recovery contracts.** `PreventDatabaseUnload` checks each sentinel lazily and transparently replaces reported `Broken`/`Closed` sentinels. `SingleConnection` cannot safely recreate a connection for disposable `:memory:` databases; a replacement would be a different empty database.

  **For `:memory:` SQLite/DuckDB, this isn't a missing feature — it's unrepairable in principle, not just in the current implementation.** The entire database lives only inside that one connection; there is no separate file or server for a replacement connection to reconnect to. Opening a *new* connection to the same `:memory:` connection string doesn't recover the old data, it silently creates a brand-new, empty database — which would be a much worse failure mode than the current loud "every operation now fails" behavior. So for `:memory:` specifically, treat `SingleConnection` mode as "the data does not survive a connection break," full stop, not as a gap to fix.

  For other single-connection-limited engines with real persistent storage behind the one connection (e.g. Firebird embedded, a `.fdb` file), the connection break is against durable data — reopening a fresh connection to the same file *could* recover access to the database, so repair behavior remains a separate production design question. It must not be applied to `:memory:` databases, where a replacement connection creates a different empty database.

## 7. Heuristics & Tests

- Explicit Standard on embedded → coerced (never throw):
  - SQLite/DuckDB `:memory:` → SingleConnection
  - Firebird embedded → PreventDatabaseUnload
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
- `Standard`, `PreventDatabaseUnload`, and `SingleWriter` are production-supported modes for the deployment shapes where their lifecycle and concurrency policies fit. This includes durable Firebird deployments, subject to the provider's connection and concurrency constraints.
- `SingleConnection` against SQLite/DuckDB `:memory:` is **not a persistence or recovery mode** — it is intended there for tests and ephemeral scratch data. The limitation is structural: the database exists only inside that connection and cannot survive a process restart or dropped connection. Durable-storage Firebird embedded automatically uses `PreventDatabaseUnload`; `SingleConnection` remains an explicit specialized deployment shape.
- Each `DatabaseContext` can be safely used as a singleton (via DI or subclassing).

**Timeouts:**
- Set connection timeouts as low as reasonable to avoid hanging on transient failures.
- Because ephemeral modes reconnect for every call, long timeouts are unnecessary.

**Observability:**
- `TrackedConnection` tracks current and max open connections with thread-safe `Interlocked` counters — useful for tuning pool sizes and spotting load issues.
- Monitor `ModeContentionStats` (see §"Shared connection locking & timeouts" concepts in `connection-pooling.md`) through logs/metrics to see which operations are queuing on the mode lock.

---

This contract is authoritative — implement according to these rules, and contributors must not deviate.
