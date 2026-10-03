# Read-Only Enforcement

`pengdows.crud` exposes read intent through `ReadWriteMode` on the context and `ExecutionType.Read` on command and transaction entry points.

## What The Public API Looks Like

```csharp
var config = new DatabaseContextConfiguration
{
    ConnectionString = "...",
    ReadWriteMode = ReadWriteMode.ReadOnly
};

var context = new DatabaseContext(config, factory);

await using var tx = await context.BeginTransactionAsync(
    IsolationProfile.SafeNonBlockingReads,
    ExecutionType.Read,
    cancellationToken);
```

There is no public `readOnly: true` transaction argument. Read intent is expressed through `ReadWriteMode` and `ExecutionType`.

## Enforcement Model

The exact session SQL varies by dialect, but the framework can enforce read intent through:

- connection-string shaping when the provider supports it
- dialect-specific session settings when the provider requires it
- transaction creation rules that reject write intent on a read-only context

The details live in the dialect and connection-lifecycle code, not in a separate read-only subsystem with its own public API.

### DuckDB exception

On a `ReadWrite` DuckDB context, read-intent operations do not switch to a read-only connection, even when a `ReadOnlyConnectionString` is configured: a DuckDB read-only connection on the same file can lock out concurrent writers. Only an explicit `ReadWriteMode.ReadOnly` context uses read-only DuckDB connections.
## Read-only violation exceptions

A write attempt is checked against **two separate flags, in sequence**, each throwing a different exception type — a catch block that only handles one will miss the other:

1. **Context-level configuration**: if `context.ReadWriteMode == ReadWriteMode.ReadOnly` (the whole context was configured read-only), throws `NotSupportedException("Write operations are not supported in read-only mode.")`.
2. **Connection/transaction-scoped intent**: checked second, only if the first check passes. `context.IsReadOnlyConnection` is a *different* flag — set per connection/transaction, e.g. a transaction opened with `ExecutionType.Read` on an otherwise-writable context. If set, throws `InvalidOperationException("Transaction is read-only.")` (`InternalConnectionAccessAssertions.AssertIsWriteConnection`).

These are real, production-active guards (not `Debug.Assert`), checked on every write execution.
Both library-generated exceptions implement the public `IReadOnlyViolation` marker interface
(`pengdows.crud.exceptions`). Their existing base types and messages are preserved, so existing
code catching `NotSupportedException` or `InvalidOperationException` continues to work. New
code can catch `IReadOnlyViolation` without inspecting exception messages.

## `ReadOnlyViolationException`

A write the **database** refuses because the transaction, session or database is read-only is
translated into `ReadOnlyViolationException` (`pengdows.crud.exceptions`, extends
`DatabaseOperationException` → `DatabaseException`, implements `IReadOnlyViolation`). It is not
transient. The provider codes, checked live on 2026-10-03 unless marked documented:

| Database | Refusal |
|---|---|
| PostgreSQL, CockroachDB, YugabyteDB | SQLSTATE 25006 |
| MySQL, MariaDB | 1792 (read-only transaction); MySQL 1290 when the message names `--read-only`/`--super-read-only` |
| TiDB | 1836 (read-only mode) |
| SQL Server | 3906 (read-only database) |
| Sybase ASE | 3906 (documented) |
| Oracle | ORA-01456 (read-only transaction); ORA-16000 (documented, read-only database) |
| Informix | -878 |
| Firebird, InterBase | 335544361 (read-only transaction); 335544765 (documented, read-only database) |
| Db2 | -817 (documented, prohibited update such as a read-only standby) |
| Spanner | SQLSTATE P0001 with "not allowed for read-only transactions" |
| SQLite, DuckDB, FlatFile, Access, SAP HANA | their read-only file/connection errors |

### Is a read-intent transaction read-only at the database?

`BeginTransaction(executionType: ExecutionType.Read)` always gets pengdows.crud's own guard (above).
Whether the **database** also refuses a write through it depends on the database (live, 2026-10-03):

- **Yes**: PostgreSQL, CockroachDB, YugabyteDB, MySQL, MariaDB, Oracle, SQLite, FlatFile, Informix,
  SAP HANA.
- **No** (the library's guard is the only protection): SQL Server, Sybase ASE, Db2, Firebird,
  InterBase, DuckDB, Snowflake, Spanner, TiDB (`READ ONLY` is a no-op unless
  `tidb_enable_noop_functions` is set), SingleStore.

If the read-only statement a dialect issues fails, the failure is logged as a warning (the
transaction is then read-write at the database), not swallowed.
