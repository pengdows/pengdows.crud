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

**PreventDatabaseUnload exists for exactly one reason, and it has nothing to do with performance.** It is not a health check, TCP keepalive, periodic ping, warmed working connection, or concurrency mode. It retains a passive sentinel connection per enabled pool solely to prevent a target database from unloading, deactivating, closing, or pausing.

The mechanism: pengdows.crud's default philosophy (Standard mode) opens a connection late and closes it early — every operation gets a fresh connection from the pool and releases it back immediately after. That's normally harmless, because the ADO.NET connection pool keeps the underlying physical connections warm behind the scenes. But **SQL Server LocalDB is not a normal server** — it's a lightweight, self-managed engine process that watches its own connection count and **automatically unloads the database (shuts the engine instance down) once it observes zero active connections for a while**. If pengdows.crud used plain Standard mode against LocalDB, a quiet period (no requests for a stretch) would let the pool drain to zero open connections, LocalDB would notice and unload the database, and the *next* request would pay the cost of LocalDB re-launching and re-attaching the database file before it could even open a connection.

The mode retains one sentinel connection per enabled pool: the connection opened during construction for dialect detection is kept open instead of being disposed (it is the writer-pool sentinel on a read-write context, the reader-pool sentinel on a read-only one), and when a dedicated `ReadOnlyConnectionString` is configured on a read-write context a second sentinel is opened for the reader pool. **The sentinel never executes application commands, opens transactions, or hands work to callers** — every real read and write still goes through its own fresh ephemeral connection exactly like Standard mode — and, because it is never used for work, session settings are not applied to it. Each sentinel holds one permit from its own pool's governor, so effective working capacity per pool is the configured capacity minus its sentinel (which is why enabled pools are raised to at least 2 — see `docs/connection-pooling.md`).

- `Best` auto-selects it **only** for SQL Server LocalDB. Other engines can have an idle-triggered reconnect/reactivation cost too (for example Firebird discarding a database's page cache when its last attachment closes under the default `LINGER` setting, Db2 deactivating an implicitly activated database, or SQL Server with `AUTO_CLOSE` explicitly turned on) — but a possible cost is not, by itself, sufficient reason to make `PreventDatabaseUnload` an auto-selected default for them. See "Why auto-selection stays LocalDB-only" below.
- **Why auto-selection stays LocalDB-only**: a heavily-trafficked deployment may never actually drain its connection pool to zero, so an idle-unload cost never materializes in practice — while the sentinel's permit is paid unconditionally regardless. And a deployment deliberately built to scale-to-zero for cost reasons (genuinely cost-optimized serverless products like Azure SQL serverless or Aurora Serverless) would have that intentional behavior silently defeated by a forced sentinel. Only the operator knows which situation applies to their own deployment. So for Firebird, Db2, and SQL Server with `AUTO_CLOSE`, `PreventDatabaseUnload` stays a fully-supported, explicitly-honored **knob** — never an auto-selected default — and `Best` resolves to `Standard` for all of them, exactly like any other full-server database. LocalDB alone is the exception: there is no production LocalDB deployment shape where the auto-shutdown behavior is wanted, so it is selected unconditionally (see the LocalDb coercion rule below).
- Extending the auto-selection list to a new database requires clearing BOTH bars: (1) empirical proof against a live engine showing a real reconnect cost, AND (2) a considered answer to "does essentially every deployment of this database genuinely want protection against this, with no real cost/tradeoff to weigh" — matching LocalDB, not the Firebird/Db2/SQL-Server-`AUTO_CLOSE` cases. Clearing the first bar alone is not sufficient.
- There is one sentinel per enabled pool: the writer pool, plus the reader pool when a dedicated `ReadOnlyConnectionString` is configured (a read-only context has only the reader-pool sentinel). A sentinel that is found Broken or Closed is replaced before the next connection-requiring operation (see §6).

### SingleConnection

- Semantics: One pinned connection handles everything — reads, writes, transactions.
- Threadsafe via `RealAsyncLocker`.
- Used for: SQLite/DuckDB `:memory:` and explicitly selected specialized single-connection
  deployments. Durable embedded Firebird can use `PreventDatabaseUnload` as an explicit,
  operator-chosen alternative (not an automatic selection — see the PreventDatabaseUnload
  section above).
- For SQLite/DuckDB `:memory:`, this is primarily a testing, example, or ephemeral-scratch
  mode. The database is owned by the connection; if that connection closes, its contents
  cannot be recovered by opening another connection.
- Firebird embedded is different: durable embedded storage can explicitly select
  `PreventDatabaseUnload`, while `SingleConnection` remains a separate, explicitly selected
  specialized mode. The latter is still a single-concurrency boundary.
- **A second, distinct gate spans an entire open transaction, on top of the transaction's own
  user-operation/completion locks** (`DatabaseContext.GetSingleConnectionTransactionGate()`, a
  non-reentrant `SemaphoreSlim`). Because every operation shares one physical connection in this
  mode, an ordinary non-transactional write (or read) issued while a transaction is open must
  wait behind that transaction rather than risk being silently absorbed into its uncommitted
  scope and rolled back with it — a hazard unique to sharing one physical connection, which no
  other `DbMode` needs to guard against, so no other mode pays for this gate. A call made *from
  inside* the transaction itself is a no-op against this gate (the transaction already holds it —
  re-acquiring the same non-reentrant semaphore would deadlock).

### SingleWriter

- Semantics: Identical to Standard, plus a governor profile: writable connections capped at 1 concurrent writer, read-only connections allow 0 writers; writer-starvation-prevention turnstile enabled.
- Reads:
  - Non-transactional → ephemeral read-only connections that use the read-only preamble.
  - Read-only transactions → ephemeral read-only connections (reader concurrency pauses while writers wait).
  - Write transactions → serialize through the write permit while retaining the connection for the transaction's duration.
- Used for: SQLite/DuckDB file-based and shared caches where writers must serialize without pinning a connection.
- **The turnstile is held for the writer's entire transaction, not just the moment of acquiring the write permit** — a deliberate tradeoff, not an oversight. It exists specifically to prevent writer starvation: without it, a steady stream of readers could keep a waiting writer permanently queued behind them under `PoolGovernor`'s otherwise reader/writer-symmetric admission. Holding it for the whole transaction span means new readers gate behind an in-flight writer (readers already queued when the writer grabbed the turnstile are not displaced — see `PoolGovernor`'s own remarks), trading a bounded amount of reader latency for a hard guarantee that writers make progress.
- **Production default for file-based SQLite/DuckDB** (equal footing with Standard's production status for client-server databases). For SQLite, the turnstile-governed write serialization is purpose-built to eliminate the file-locking errors (`SQLITE_BUSY`) the engine is otherwise prone to under concurrent writers. DuckDB's own engine handles concurrent *disjoint-row* writes, but concurrent writers targeting the *same* row/resource can fail with a write-write conflict (surfaced as `SerializationConflictException`) — so SingleWriter there is both a deterministic policy choice for the disjoint case AND a fix for the same-resource case, not purely a policy preference (see `docs/positioning/product-thesis.md`). Reads still execute fully concurrently on ephemeral connections for both — a level of write-contention governance most comparable libraries don't provide for these engines at all.
- **The write permit is not the connection.** `PoolGovernor`'s capacity-1 semaphore for writers is enforced via `PoolSlot`/`PoolSlotToken` (`pengdows.crud/infrastructure/PoolSlot.cs`), which holds no reference to any `DbConnection`/`DbCommand` at all — releasing a slot back to the governor is pure counter bookkeeping, independent of whether the connection used during that slot's lifetime succeeded or died. A pinned-writer design (GRDB's `DatabasePool`, Peewee's `SqliteQueueDatabase`) makes the one long-lived writer connection itself part of the concurrency mechanism, so a poisoned or severed connection requires rebuilding the writer object to recover. Here, a bad connection is simply discarded and the next admitted write acquires a fresh ephemeral one — concurrency policy is decoupled from connection identity. See `docs/positioning/product-thesis.md`'s expanded treatment of this distinction.

### Best

- Resolver hint only. Not an actual strategy.
- Defaults to the safest mode based on dialect + connection string:
  - Full servers (PostgreSQL, MySQL/MariaDB, Oracle, SQL Server, Db2, Firebird — embedded or client-server) → Standard
  - LocalDb → PreventDatabaseUnload
  - SQLite/DuckDB `:memory:` → SingleConnection
  - SQLite/DuckDB file-based → SingleWriter
  - Unknown product → Standard

## 2. Provider-Driven Coercion

### Always forced (cannot override):

- SQLite/DuckDB `:memory:` → SingleConnection

### Allowed for SQLite/DuckDB file-based:

- SingleWriter (default for Best)
- SingleConnection (allowed alternative)
- Standard/PreventDatabaseUnload → coerced to SingleWriter with a Warning log — SQLite and DuckDB
  stay hard-coerced with no opt-out; see `SqlDialect.CoerceEmbeddedSingleWriterMode`'s
  `allowStandard` parameter (neither dialect sets it).

### Allowed for Access file-based:

- SingleWriter (default for Best)
- SingleConnection (allowed alternative)
- PreventDatabaseUnload → coerced to SingleWriter with a Warning log (same as SQLite/DuckDB — a
  file-based embedded engine has no use for a "keep an idle server attachment alive" sentinel)
- **Standard → honored, not coerced.** Access is documented by Microsoft as supporting concurrent
  connections/writers, so an explicit `Standard` request is allowed through (`AccessDialect`
  passes `allowStandard: true`; `Best` still resolves to `SingleWriter`).
  `DatabaseContext.WarnOnModeMismatch` logs an evidence-backed risk Warning when this happens
  instead (`AccessDialect.DescribeStandardModeRisk()`).

  Access's risk was re-verified end-to-end on a real Windows machine (2026-09-18) and turned out
  to rest on a real connection-string bug, not just an unverified risk: `AccessDialect` never
  overrode `SupportsExternalPooling`/`PoolingSettingName`, so pengdows.crud injected an ADO.NET-
  style `Pooling=True` into the OLE DB connection string, and Jet/ACE throws the generic
  `"Could not find installable ISAM"` for *any* unrecognized connection property — this broke
  every `DbMode.Standard` connection outright, not just under contention. Fixed by overriding both
  to `false`/`null` (same rationale as DuckDB's "in-process, no external pooling switch"). The
  original justification claimed the driver "pools transparently by default anyway" — that was
  challenged and retracted: a dedicated test (same connection string repeated vs. a fresh
  never-seen string each time) showed no meaningful timing difference, and disabling native
  pooling services outright made opens marginally *faster*. A local file attach has no network
  handshake to amortize, so pooling isn't a meaningful concept here — not that it's silently
  already happening. The fix is unaffected; only the reasoning was corrected. Once fixed, the real hazard was re-confirmed
  fairly: a naive test (bare auto-committing `INSERT`s) misleadingly showed 20/20 success under
  `Standard`, because a single-statement write doesn't hold its lock long enough to collide even
  under real concurrency. Redone with a genuinely held-open transaction (via
  `Context.BeginTransaction()`, through the real public API): `DbMode.Standard` reproduced
  14-18/20 failures (`CommandTimeoutException`, `"Could not update; currently locked."`), while
  `DbMode.SingleWriter` stayed at 0/20 across every run — and the conflict is confirmed NOT
  table-scoped (disjoint writes across two different tables in the same file still collided).

  **Access has no "safe zone" under `Standard`.** The conflict spans disjoint rows in the same
  table AND disjoint rows across different tables in the same file, so there is no "just don't
  touch the same row" escape hatch.

  See `AccessDialect.cs`'s file-level AI SUMMARY and `docs/connection/access-concurrency-verification.md`
  for the full trail, and `AccessDialect.DescribeStandardModeRisk()` for the exact wording
  surfaced to callers. **`SingleWriter` is the only mode confirmed both correct and fully
  concurrent for Access.**

### LocalDb: `Best` and every explicit request — including `Standard` — coerce to PreventDatabaseUnload. `SqlServerDialect.CoerceConnectionMode` forces it unconditionally for LocalDB ("LocalDB requires PreventDatabaseUnload"); there is no opt-out. An explicit non-`Best` request that gets coerced is logged at Warning (see Logging below).

### Full servers (PostgreSQL, MySQL/MariaDB, Oracle, SQL Server, Db2, Firebird): `Best` always selects Standard; every explicit choice — including `PreventDatabaseUnload` — is honored as-is, no warning logged. Firebird (default `LINGER`), Db2 (implicit database activation/deactivation), and SQL Server (with `AUTO_CLOSE`) can each unload a database once its last connection closes, but `PreventDatabaseUnload` is deliberately a knob for the operator to reach for, not an auto-selected default — see the PreventDatabaseUnload section above for why.

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
  - **SingleConnection**: the one pinned `TrackedConnection` wrapper lives for the whole context
    lifetime, so the preamble genuinely executes exactly once. (The `PreventDatabaseUnload`
    sentinel is also long-lived, but it never runs work, so no session settings are applied to it.)
  - **Ephemeral modes** (Standard, SingleWriter): a fresh `TrackedConnection` wrapper is created
    per operation/checkout, so the preamble **is reapplied on every single checkout** — even when
    the underlying ADO.NET provider pool hands back an already-open physical connection. This is
    deliberate, not a missed optimization: a connection previously used for an unrelated operation
    could have drifted session state (e.g. a stale isolation override, or — for SQL Server —
    `QUOTED_IDENTIFIER`, which this framework's own ANSI double-quote identifier quoting depends on
    to parse at all), so trusting "the pool gave me a connection, it must still be clean" is a
    correctness risk, not just a consistency one. For the SQL Server specifics (SQL Server pays this
    every operation under `DbMode.Standard`), see `docs/sql-server-session-settings.md` and
    `docs/session-settings.md`.
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
- **`PreventDatabaseUnload` repairs a lost sentinel lazily.** At the top of every `GetConnection`/`GetConnectionAsync`, any sentinel whose state is `Broken` or `Closed` is disposed (releasing its governor permit) and replaced by a freshly opened connection that takes a new permit from the same pool, with a warning logged. There is no background monitor, so the guarantee is "repaired before the next operation", not continuous; a dropped sentinel never breaks reads or writes, which use their own ephemeral connections. A repair that races context disposal disposes its replacement instead of installing it.
- **`SingleConnection` never recreates its pinned connection.** It cannot safely recreate a connection for disposable `:memory:` databases; a replacement would be a different empty database.

  **For `:memory:` SQLite/DuckDB, this isn't a missing feature — it's unrepairable in principle, not just in the current implementation.** The entire database lives only inside that one connection; there is no separate file or server for a replacement connection to reconnect to. Opening a *new* connection to the same `:memory:` connection string doesn't recover the old data, it silently creates a brand-new, empty database — which would be a much worse failure mode than the current loud "every operation now fails" behavior. So for `:memory:` specifically, treat `SingleConnection` mode as "the data does not survive a connection break," full stop, not as a gap to fix.

  For other single-connection-limited engines with real persistent storage behind the one connection (e.g. Firebird embedded, a `.fdb` file), the connection break is against durable data — reopening a fresh connection to the same file *could* recover access to the database, so repair behavior remains a separate production design question. It must not be applied to `:memory:` databases, where a replacement connection creates a different empty database.

## 7. Heuristics & Tests

- Explicit Standard on embedded → coerced (never throw):
  - SQLite/DuckDB `:memory:` → SingleConnection
  - SQLite/DuckDB file-based → SingleWriter
- Firebird (embedded or client-server) → treated as an ordinary full server database; `Best` and every explicit choice (including `PreventDatabaseUnload`) resolve/honor as requested, no coercion either way.
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

- Default: `CommandPrepareMode.Auto` defers to the dialect's `PrepareStatements` recommendation. The `SqlDialect` base default is `false`; dialects opt in. `Always`/`Never` override the dialect.
- Unknown dialect (SQL-92 fallback) → base default, prepare off under `Auto`.
- A dialect hard veto (`IsPrepareExhausted`, e.g. MySQL `max_prepared_stmt_count` exhaustion) disables prepare regardless of configuration.
- On a prepare failure the dialect classifies as unsupported (`ShouldDisablePrepareOn(ex)`), disable prepare for that connection.

### Cancellation

- Tests must expect `OperationCanceledException`.
- `TaskCanceledException` may occur, but base type is sufficient and consistent.

## 10. Practical Guidance

**Best practices:**
- `Standard`, `PreventDatabaseUnload`, and `SingleWriter` are production-supported modes for the deployment shapes where their lifecycle and concurrency policies fit. This includes durable Firebird deployments, subject to the provider's connection and concurrency constraints.
- `SingleConnection` against SQLite/DuckDB `:memory:` is **not a persistence or recovery mode** — it is intended there for tests and ephemeral scratch data. The limitation is structural: the database exists only inside that connection and cannot survive a process restart or dropped connection. Durable-storage Firebird embedded can explicitly choose `PreventDatabaseUnload` if the operator wants it (not an automatic selection); `SingleConnection` remains a separate, explicit specialized deployment shape.
- Each `DatabaseContext` can be safely used as a singleton (via DI or subclassing).

**Timeouts:**
- Set connection timeouts as low as reasonable to avoid hanging on transient failures.
- Because ephemeral modes reconnect for every call, long timeouts are unnecessary.

**Observability:**
- `TrackedConnection` tracks current and max open connections with thread-safe `Interlocked` counters — useful for tuning pool sizes and spotting load issues.
- Monitor `ModeContentionStats` through logs/metrics to see which operations are queuing on the mode lock.

---

This contract is authoritative — implement according to these rules, and contributors must not deviate.
