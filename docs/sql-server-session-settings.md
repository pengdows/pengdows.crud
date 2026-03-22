# SQL Server Session Settings

pengdows.crud enforces a fixed ANSI session-settings baseline on every SQL Server connection checkout. The goal is deterministic SQL semantics regardless of server defaults, driver behavior, or installation quirks. A connection from the pool that was previously used by external code with different session settings will be corrected before any framework SQL runs.

The baseline is defined as a single static array in `SqlServerDialect.cs` (`SessionSettingsDef`). Both the `SET` script and the validation dictionary are derived from that array — there is no divergence between what gets applied and what gets checked.

## Enforced Settings

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

### `SET ARITHABORT ON`

Aborts the current statement when an arithmetic overflow or divide-by-zero error occurs.

Two reasons to force this `ON`:

1. **Plan cache consistency.** SQL Server Management Studio (SSMS) enables `ARITHABORT` by default; older ADO.NET drivers and some frameworks historically did not. This difference causes the same query to compile to different plans depending on whether it was first run from SSMS or an application — the notorious "fast in SSMS, slow in production" problem. Forcing `ON` on every application connection eliminates that divergence.

2. **Indexed view compatibility.** Required alongside `ANSI_WARNINGS ON` when querying indexed views.

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

### `SET NOCOUNT ON` — not included

`NOCOUNT` controls whether SQL Server sends the "n row(s) affected" message back to the client after each DML statement.

Including `SET NOCOUNT ON` followed by `SET NOCOUNT OFF` in the same batch cancels out — the net effect is no change. The correct approach if you want the behavior is to include only `SET NOCOUNT ON`.

It is not included in the framework baseline for this reason: `ExecuteNonQueryAsync` returns the row-count provided by the ADO.NET provider. With `NOCOUNT ON`, that count is suppressed from DML statements inside stored procedures and multi-statement batches, which would silently zero out the rows-affected return value. Framework callers that rely on the affected row count for optimistic concurrency detection (version-column mismatch → zero rows returned → conflict detected) would silently lose that signal.

If you want `NOCOUNT ON` for a specific stored procedure call or batch, issue it in your own `ISqlContainer` SQL before the statement.

### `SET XACT_ABORT ON` — not included

When `ON`, any runtime T-SQL error within a transaction automatically rolls back the entire transaction and raises an error to the client.

It is not included because it changes transaction completion semantics in ways that interfere with the framework's explicit `try/catch` + `Rollback()` pattern in `TransactionContext`. The framework already handles rollback explicitly; adding `XACT_ABORT ON` at the session level introduces an implicit parallel path that can conflict with explicit rollback calls, particularly in stored procedure chains where partial rollbacks may be intentional.

Omitting it is a deliberate and defensible choice. Teams with stored procedure-heavy workloads that rely on `XACT_ABORT` for automatic cleanup can issue `SET XACT_ABORT ON` inside those procedures or at the top of specific `ISqlContainer` batches.

---

## Validation in Benchmarks

The `benchmarks/` suite uses `DBCC USEROPTIONS` to capture the active session state and asserts that all seven settings are in effect before timed iterations begin. See `docs/INDEXED_VIEW_VALIDATION.md` for the artifact layout and validation report structure.

`SET STATISTICS XML ON; ... SET STATISTICS XML OFF` appears in benchmark code solely to capture query execution plans. It is ephemeral per-command usage, not a persistent session setting.

---

## Related

- [`docs/session-settings.md`](session-settings.md) — how settings are applied per-connection (mechanism)
- [`docs/INDEXED_VIEW_VALIDATION.md`](INDEXED_VIEW_VALIDATION.md) — benchmark validation of session options
- [`skills/claude/pengdows-crud/primary-keys.md`](../skills/claude/pengdows-crud/primary-keys.md) — clustering strategy and UUID anti-patterns
