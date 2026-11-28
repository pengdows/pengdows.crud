# SQL Server Session Settings

pengdows.crud defines a fixed ANSI session-settings baseline for SQL Server. The goal is deterministic SQL semantics regardless of server defaults, driver behavior, or installation quirks. Whether the `SET` script is actually sent depends on the database's compatibility level, probed once during initialization (`SqlServerDialect.GetSqlServerSessionSettings`):

- **Compatibility level 90 (SQL Server 2005) or higher** - every supported server in practice: the legacy baseline script is empty, so the full baseline is not sent. The driver's login sequence and `sp_reset_connection` already guarantee the baseline on a pooled connection (the log line reads "already compliant"). The modern path still compares each managed option with the server's own defaults, captured once per pool, and sends only the `SET` statements whose option differs from its requirement on each checkout. So "nothing is sent" holds only when the server defaults already match. In the default `Normal` mode that comparison uses the snapshot; `Paranoid` sends every managed statement on every checkout (`SessionSettingsMode`, see `docs/session-settings.md`).
- **Level unknown (the probe failed) or below 90, or the settings probe threw**: the full legacy baseline below is sent on every checkout, so a pooled connection previously changed by external code is corrected before any framework SQL runs.
- **Detection never ran** before the first checkout: the full legacy baseline is used.

There are two lists in `SqlServerDialect.cs`:

- The legacy baseline is the static array `SessionSettingsDef`. Its `SET` script (`DefaultSessionSettings`) and its validation dictionary (`ExpectedSessionSettings`) are both derived from that array, so what is applied and what is checked do not diverge. It is used only on the legacy path above.
- The modern path uses `ManagedOptionRequirements` (`@@OPTIONS` bit and required value per option) plus a `READ COMMITTED` isolation check, built into `ManagedSettings`. It does not use `SessionSettingsDef`, and it manages a different set of options from the legacy baseline (see the sections below).

## Enforced Settings

Both paths enforce the options below except where a section says otherwise: the legacy baseline sends all of them (`ARITHABORT` included); the modern path manages `ANSI_NULLS`, `ANSI_PADDING`, `ANSI_WARNINGS`, `CONCAT_NULL_YIELDS_NULL`, `QUOTED_IDENTIFIER` (ON) and `NUMERIC_ROUNDABORT` (OFF), and does not manage `ARITHABORT`.

### `SET ANSI_NULLS ON`

Controls how `NULL` comparisons evaluate.

With `ANSI_NULLS OFF`, `NULL = NULL` evaluates to `TRUE`. That violates SQL:1992 semantics, which requires `NULL = NULL` to produce `UNKNOWN`. With `ON`, `NULL` comparisons always produce `UNKNOWN` and you must use `IS NULL` / `IS NOT NULL` predicates to test for null values.

SQL Server requires `ANSI_NULLS ON` when creating:
- Indexed views
- Indexes on computed columns
- Filtered indexes

### `SET ANSI_PADDING ON`

Controls whether SQL Server trims trailing spaces from `CHAR` and `VARCHAR` values on storage and comparison.

With `OFF`, behavior is driver-version and server-default dependent. With `ON`, padding behavior is fixed and consistent. Keeping it `ON` prevents hard-to-reproduce data inconsistencies when the same schema is accessed from different driver versions or tools.

### `SET ANSI_WARNINGS ON`

Controls whether SQL Server emits warnings (or raises errors) for conditions like:
- Divide-by-zero in aggregate expressions
- `NULL` values eliminated from aggregate functions
- String truncation

Required for indexed views and indexes on computed columns. With `OFF`, those objects cannot be reliably created or maintained.

### `SET ARITHABORT ON` — legacy path only

Aborts the current statement when an arithmetic overflow or divide-by-zero error occurs.

On the legacy path (compatibility level below 90, unknown, or the probe failed) the full baseline sends `SET ARITHABORT ON`. Two reasons to force this `ON` there:

1. **Plan cache consistency.** SQL Server Management Studio (SSMS) enables `ARITHABORT` by default; older ADO.NET drivers and some frameworks historically did not. This difference causes the same query to compile to different plans depending on whether it was first run from SSMS or an application — the notorious "fast in SSMS, slow in production" problem. Forcing `ON` on every application connection eliminates that divergence.

2. **Indexed view compatibility.** Required alongside `ANSI_WARNINGS ON` when querying indexed views.

On the modern path (compatibility level 90 and above) `ARITHABORT` is **not managed**: `ManagedOptionRequirements` has no `ARITHABORT` entry. At those levels `ANSI_WARNINGS ON` already gives the indexed-view behavior, so the framework does not force the option and it keeps the server's (or the connection's) value. `SqlServerDialect.cs` documents this next to `GetFinalSessionSettings`.

### `SET CONCAT_NULL_YIELDS_NULL ON`

Controls the result of concatenating a string with `NULL`.

With `OFF`: `'prefix' + NULL → 'prefix'`
With `ON` (ANSI): `'prefix' + NULL → NULL`

The `ON` behavior is what all other supported databases produce. Forcing it here prevents silent cross-database behavioral differences in string-building SQL.

### `SET QUOTED_IDENTIFIER ON`

Controls whether `"double quotes"` are treated as ANSI identifier delimiters or as string literals.

With `OFF`: `"value"` is treated as a string literal (non-ANSI).
With `ON`: `"identifier"` quotes an object name per ANSI SQL.

The framework uses ANSI double-quote quoting (`"name"`) rather than SQL Server's bracket syntax (`[name]`). That choice requires `QUOTED_IDENTIFIER ON` on every connection. Required for indexed views and indexes on computed columns.

### `SET NUMERIC_ROUNDABORT OFF`

Controls whether SQL Server throws an error when a loss of precision occurs in numeric expressions.

With `ON`, even minor rounding in computed columns or numeric arithmetic raises an error. This breaks standard arithmetic on `DECIMAL`/`NUMERIC` columns and prevents creating indexes on computed columns that involve numeric rounding.

`OFF` is the correct production default and matches how virtually every SQL Server installation runs.

---

## Intentional Exclusions

### `NOCOUNT` — not set ON; forced OFF on the modern path

`NOCOUNT` controls whether SQL Server sends the "n row(s) affected" message back to the client after each DML statement.

The framework never sets `NOCOUNT ON`, because `ExecuteNonQueryAsync` returns the row-count provided by the ADO.NET provider. With `NOCOUNT ON`, that count is suppressed from DML statements inside stored procedures and multi-statement batches, which would silently zero out the rows-affected return value. Framework callers that rely on the affected row count for optimistic concurrency detection (version-column mismatch → zero rows returned → conflict detected) would silently lose that signal.

On the modern path the framework does manage the option, in the other direction: `ManagedOptionRequirements` requires `NOCOUNT` **OFF**, so `SET NOCOUNT OFF;` is sent when the server's default has it on. The legacy baseline does not include `NOCOUNT` at all.

If you want `NOCOUNT ON` for a specific stored procedure call or batch, issue it in your own `ISqlContainer` SQL before the statement. Note that the modern path sends `SET NOCOUNT OFF` again on the next checkout when the server default is ON.

### `XACT_ABORT` — never set ON; forced OFF on the modern path

When `ON`, any runtime T-SQL error within a transaction automatically rolls back the entire transaction and raises an error to the client.

The framework does not set it ON, because it changes transaction completion semantics in ways that interfere with the framework's explicit `try/catch` + `Rollback()` pattern in `TransactionContext`. Adding `XACT_ABORT ON` at the session level would introduce an implicit parallel rollback path that can conflict with explicit rollback calls, particularly in stored procedure chains where partial rollbacks may be intentional.

On the modern path the framework manages it as OFF: `ManagedOptionRequirements` requires `XACT_ABORT` OFF, so `SET XACT_ABORT OFF;` is sent when the server's default has it on. The legacy baseline does not include it. Teams with stored procedure-heavy workloads that rely on `XACT_ABORT` for automatic cleanup can issue `SET XACT_ABORT ON` inside those procedures or at the top of specific `ISqlContainer` batches; on the modern path that statement lasts only until the next checkout.

### Other modern-path options

The modern path also requires `IMPLICIT_TRANSACTIONS` OFF and the session isolation level `READ COMMITTED` (`SET TRANSACTION ISOLATION LEVEL READ COMMITTED;` when the session is at another level). Both are sent only when they differ from the requirement, as for the options above.

---

## Validation in Benchmarks

The `benchmarks/` suite uses `DBCC USEROPTIONS` to capture the active session state and applies the baseline itself and asserts that five of the seven are in effect before timed iterations begin (`arithabort`, `ansi_warnings`, `ansi_nulls`, `quoted_identifier`, `concat_null_yields_null`; `ANSI_PADDING` is not asserted, and `numeric_roundabort OFF` is the default so `DBCC USEROPTIONS` omits it). See `docs/INDEXED_VIEW_VALIDATION.md` for the artifact layout and validation report structure.

`SET STATISTICS XML ON; ... SET STATISTICS XML OFF` appears in benchmark code solely to capture query execution plans. It is ephemeral per-command usage, not a persistent session setting.

---

## Related

- [`docs/session-settings.md`](session-settings.md) — how settings are applied per-connection (mechanism)
- [`docs/INDEXED_VIEW_VALIDATION.md`](INDEXED_VIEW_VALIDATION.md) — benchmark validation of session options
- [`skills/claude/pengdows-crud/primary-keys.md`](../skills/claude/pengdows-crud/primary-keys.md) — clustering strategy and UUID anti-patterns
