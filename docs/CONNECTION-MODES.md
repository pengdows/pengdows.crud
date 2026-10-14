# Connection Mode Invariants (`DbMode`)

This document defines the intent, invariants, and coercion rules for connection management modes in `pengdows.crud`.
It resolves ambiguities so future contributors cannot bikeshed these rules.

## 1. Modes & Lifecycle

The `DbMode` enum values are: `Standard=0`, `PreventDatabaseUnload=1`, `SingleWriter=2`, `SingleConnection=4`, `Best=15`. `KeepAlive` remains an obsolete compatibility alias for `PreventDatabaseUnload`.

The full contract, with the per-database rationale and live-verification trail, is `connection/connection-modes.md`; this page is the short form.

### Standard

- Semantics: Ephemeral pooled connections. New connection for each statement unless inside a transaction.
- Production default for all full server databases (PostgreSQL, SQL Server, Oracle, MySQL, MariaDB, CockroachDB).
- Constructor behavior: Attempts to open a connection at initialization to detect dialect.
  - If Open() fails → throw immediately.
  - If connection opens but dialect cannot be resolved → fall back to SQL-92 dialect (SQL-92 is a fallback behavior, not a distinct DbMode or supported database product).
- Transactions: All reads/writes inside a transaction share the same connection.

### PreventDatabaseUnload

- Semantics: Identical to Standard, except one idle sentinel connection per enabled pool is kept open to prevent the database unloading when its last connection closes.
- The sentinel is never used for commands.
- `Best` selects it for SQL Server LocalDB, Firebird (embedded and client-server) and Db2 LUW. Production-supported; an explicit `Standard` is honored on all three.

### SingleConnection

- Semantics: One pinned connection handles everything — reads, writes, transactions.
- Threadsafe via `RealAsyncLocker`.
- While a transaction is open, it owns the connection: writes through the context wait for it to
  finish, and a read through the context throws `InvalidOperationException` (read through the
  transaction instead). An open plain reader holds the connection until disposed, so a
  transaction waits for it.
- Used for: SQLite/DuckDB isolated `:memory:` (forced) and explicitly selected specialized single-connection deployments.
- Not suitable for production concurrency.

### SingleWriter

- Semantics: Identical to Standard, plus a governor profile: writable connections capped at 1 concurrent writer, read-only connections allow 0 writers; writer-starvation-prevention turnstile enabled.
- Reads:
  - Non-transactional → ephemeral read-only connections that use the read-only preamble.
  - Read-only transactions → ephemeral read-only connections (reader concurrency pauses while writers wait).
  - Write transactions → serialize through the write permit while retaining the connection for the transaction's duration.
- Used for: the file-based embedded engines (SQLite, DuckDB, Microsoft Access, pengdows.flatfile) and SQLite shared caches, where writers must serialize without pinning a connection. `SingleWriter` is a general write-admission policy, not a SQLite workaround: any engine whose safe topology needs one writer at a time gets it.

### Best

- Resolver hint only. Not an actual strategy.
- Defaults to the safest mode based on dialect + connection string:
  - Full servers (SQL Server, PostgreSQL family, MySQL family incl. SingleStore, Oracle, Snowflake, Spanner, Informix, SAP HANA, InterBase, Sybase ASE, non-LUW Db2) → Standard
  - LocalDb, Firebird (embedded or client-server), Db2 LUW → PreventDatabaseUnload
  - SQLite/DuckDB isolated `:memory:` → SingleConnection
  - SQLite/DuckDB file-based (and shared in-memory), Access, FlatFile → SingleWriter
  - Unknown product → Standard

## 2. Provider-Driven Coercion

### Always forced (cannot override):

- SQLite/DuckDB isolated `:memory:` → SingleConnection

### Allowed for SQLite/DuckDB file-based:

- SingleWriter (default for Best)
- SingleConnection (allowed alternative)
- Standard/PreventDatabaseUnload → coerced to SingleWriter with a Warning log, except that DuckDB and Access honor an explicit `Standard` (each documents concurrent connections/writers) and log a risk warning instead; see `connection/connection-modes.md`

### Access file-based and pengdows.flatfile: same policy as above (`Best` and PreventDatabaseUnload → SingleWriter; Access honors `Standard`, FlatFile coerces it). Neither has an in-memory mode.

### LocalDb: `Best` selects PreventDatabaseUnload; an explicit `Standard` is honored with a performance-only warning; other explicit requests resolve to PreventDatabaseUnload.

### Firebird (embedded or client-server): `Best` selects PreventDatabaseUnload; every explicit choice is honored. Embedded Firebird is not pinned to a single connection.

### Db2: `Best` selects PreventDatabaseUnload on a detected Db2 LUW server, Standard otherwise; every explicit choice is honored.

### `Best` never overrides a configured maximum pool size below 2: where it would select PreventDatabaseUnload (whose sentinel needs a second pooled connection) it uses Standard instead.

### Every other client-server database (SQL Server, PostgreSQL family, MySQL family, Oracle, Snowflake, Spanner, Informix, SAP HANA, InterBase, Sybase ASE): `Best` selects Standard; every explicit choice is honored as-is.

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

- SessionSettingsPreamble is applied once per `TrackedConnection` wrapper, at its first open.
  - Pinned (`SingleConnection`): the one wrapper lives for the context, so it runs once.
  - Ephemeral (`Standard`, `SingleWriter`): a wrapper is created per operation, so it runs on every checkout, even when the provider's pool returns an already-open physical connection. This is deliberate; see `docs/connection/connection-modes.md`, section 4.
- Do not mutate session-scoped settings mid-connection when using pooling.
- ReadWriteMode.ReadOnly:
  - `SqlContainer` pre-guards every write in code: it throws `ReadOnlyContextException` (a `NotSupportedException`) for `ExecutionType.Write` before any provider call is made.
  - Where the database can also enforce read-only, the mechanism is dialect-specific (for example, `SET TRANSACTION READ ONLY`, or SQLite's `Mode=ReadOnly` connection-string parameter). SQLite resets session-scoped `query_only` to `OFF` rather than using it for read-only enforcement; see `read-only-enforcement.md` and the detailed connection-mode contract.

## 5. Connection Sharing & Transactions

- All commands inside a transaction (read or write) share the same physical connection.
- Rules by mode:
  - Standard / PreventDatabaseUnload: `BeginTransaction()` creates a pinned connection for that scope.
  - Write tx → acquires the single write permit and reuses the transaction connection for the scope.
  - Read-only tx → ephemeral read-only connection that still respects governor fairness when writes queue.
  - SingleConnection: all tx use the single pinned connection; while one is open, plain reads
    through the context are rejected and plain writes wait.

## 6. Failure Behavior

- Non-transactional ephemeral connections: errors bubble at `Execute…` (open-late / close-early).
- Transaction start: `BeginTransaction()` eagerly opens the connection and errors surface immediately.
- Persistent modes (PreventDatabaseUnload/SingleConnection): if pinned connection fails to open at ctor, error bubbles immediately.
- No silent deferrals beyond SQL-92 fallback when dialect is unknown.

## 7. Heuristics & Tests

- Explicit Standard on embedded → coerced (never throw):
  - SQLite/DuckDB isolated `:memory:` → SingleConnection
  - SQLite file-based, FlatFile → SingleWriter
  - DuckDB file-based, Access: `Best`/PreventDatabaseUnload → SingleWriter; an explicit `Standard` is honored with a risk warning
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
- On a prepare failure the dialect classifies as unsupported (`ShouldDisablePrepareOn(ex)`), disable prepare for that connection; a dialect veto (`IsPrepareExhausted`) disables it regardless of configuration.

### Cancellation

- Tests must expect `OperationCanceledException`.
- `TaskCanceledException` may occur, but base type is sufficient and consistent.

---

This contract is authoritative — implement according to these rules, and contributors must not deviate.
