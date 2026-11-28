# Session Settings Enforcement

pengdows.crud applies dialect-specific session-settings scripts **after** it knows which dialect it is talking to and **only if** the dialect provides non-empty SQL. The script is built per checkout from the settings the dialect manages (`SqlDialect.BuildManagedSessionSettings`), and the `SessionSettingsMode` on the context decides which of them are sent:

- **`Normal` (default):** only the settings whose value differs from the snapshot captured when the context was created are sent (`CaptureSessionDefaultsAsync` at detection, and again for the read-only pool). A setting whose snapshot already complies is not sent again, so a change a caller makes to a pooled connection is not reverted in this mode unless the dialect always sends that setting (SQLite's `query_only` is one such setting; see `docs/connection/connection-modes.md`, section 4).
- **`Paranoid`:** every managed setting that applies is sent on every checkout, whatever the snapshot said. Use it to diagnose odd session state. It also bypasses the PostgreSQL startup-options shortcut.

SQL Server's modern-compatibility path follows the same diff rule; see `docs/sql-server-session-settings.md`.

Most baseline settings are correctness invariants: they keep SQL meaning and stored data
stable across pooled sessions. Examples include SQL Server's ANSI and arithmetic settings,
MySQL strict mode, PostgreSQL `standard_conforming_strings`, Oracle's session time zone, and
SQLite `foreign_keys`. Two settings should be read as defaults instead: PostgreSQL's
`client_min_messages = warning` only limits message noise, while the 30-second
`lock_timeout` used by distributed PostgreSQL-compatible dialects is operational policy.
The distributed dialects use that value as a safety default; an explicit `lock_timeout`
startup option remains the caller's choice where startup options are supported. The ordinary
PostgreSQL baseline does not impose a fixed lock timeout.

## Key guarantees

- Dialect detection completes during `DatabaseContext` initialization (`DatabaseContext.Initialization.cs`), flipping `_sessionSettingsDetectionCompleted`. Any physical connection opened before that flag would block is now a no-op for session SQL.
- Every `TrackedConnection` runs the `onFirstOpen` callback on its first `Open`; the callback (`SessionSettingsToRun`, `DatabaseContext.ConnectionLifecycle.cs`) returns without sending anything when detection has not completed, when the settings were baked into the PostgreSQL data source's startup options (in `Normal` mode only; `Paranoid` skips this shortcut), or when the dialect's cached settings are empty. The cached read-write and read-only settings are built by the dialect (`GetFinalSessionSettings`, `SqlDialect`), and the tests check them through that method.
- The script runs once per `TrackedConnection` wrapper, not once per physical connection. Persistent modes (`SingleConnection`) keep one wrapper for the context's lifetime, so the script runs once. Ephemeral modes (`Standard`, `SingleWriter`) create a wrapper per operation, so the script is built and sent on every checkout, even when the provider's pool hands back an already-open physical connection. In `Normal` mode what is sent on a checkout is the diff described above, which may be empty; in `Paranoid` mode it is the full set (`StickySessionSettingsTests`). On SQLite the managed pragmas are `PRAGMA foreign_keys = ON;`, `PRAGMA query_only = OFF;` and, once the database is known to be in WAL mode (the context reads the journal mode right after it sets WAL, and rebuilds the settings if it changed), `PRAGMA synchronous = NORMAL;`; see `docs/connection/connection-modes.md`, section 4. In `Normal` mode `query_only` is always sent, so a caller that left it on through a container cannot make the next write fail (`SqliteQueryOnlySessionTests`). `foreign_keys` and `synchronous` are sent in `Normal` mode only when their snapshot value differs from the requirement, so a caller that turns foreign keys off on a pooled connection is corrected only in `Paranoid` mode. `query_only` is only ever put OFF: SQLite refuses `BEGIN IMMEDIATE` on a `query_only` connection, which would stop a read transaction beginning, so read-only is `Mode=ReadOnly` in the connection string instead.

## Tests that guard the behavior

1. `MySqlSessionSettingsTests.SessionSettingsAppliedWhenStandardConnectionOpens` – ensures the first Standard-mode connection executes the MySQL SQL mode commands after the dialect has been detected (`pengdows.crud.Tests/MySqlSessionSettingsTests.cs`).
2. `SessionSettingsEnforcementTests.DatabaseContext_AppliesSessionSettingsAfterDialectDetection` – confirms at least one connection received a dialect-generated `SET` command, indicating the callback still fires for a real dialect post-detection (`pengdows.crud.Tests/SessionSettingsEnforcementTests.cs`; it uses MySQL).
3. `StickySessionSettingsTests.ExecuteSessionSettings_AlwaysApplies_AndIncludesForeignKeysPragma` – on SQLite, the settings (including `PRAGMA foreign_keys = ON`) run again on every call to the same connection and on a second physical connection (the “Always SET” policy). `SessionSettingsEnforcementTests.SessionSettingsSkippedWhenDialectProvidesNoStatements` opens two checkouts on SQLite but asserts nothing, so it does not guard the “nothing to send” path.
4. `SessionSettingsApplicationTests.StandardMode_InitializesSessionSettings_ForAnsiQuotes` – kept to verify the MySQL `sql_mode` setting (`RequiredSqlModeFlags` in `MySqlDialect`) still includes ANSI_QUOTES for MySQL/MariaDB, so documentation and tests stay in sync.

If anyone changes `DatabaseContext` to touch `_sessionSettingsDetectionCompleted`, `ConnectionLocalState.SessionSettingsApplied`, or `ExecuteSessionSettings`, rerun these tests and make sure this document still reflects the story before merging.
