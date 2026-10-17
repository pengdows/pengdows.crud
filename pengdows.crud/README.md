# pengdows.crud

[![NuGet](https://img.shields.io/nuget/v/pengdows.crud.svg)](https://www.nuget.org/packages/pengdows.crud)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](../LICENSE)
[![Build](https://github.com/pengdows/pengdows.crud/actions/workflows/deploy.yml/badge.svg)](https://github.com/pengdows/pengdows.crud/actions/workflows/deploy.yml)

`pengdows.crud` is a SQL-first data access library for .NET 8+. It favors explicit SQL, inspectable command builders, and provider-aware execution over ORM-style query generation.

No LINQ. No tracking. No hidden unit of work.

## What The Code Exposes

- `DatabaseContext` / `IDatabaseContext` for connection lifecycle, dialect behavior, quoting, parameter creation, metrics, and transactions
- `TableGateway<TEntity, TRowID>` / `ITableGateway<TEntity, TRowID>` for row-id CRUD, business-key retrieval, batch operations, and async streaming
- `PrimaryKeyTableGateway<TEntity>` / `IPrimaryKeyTableGateway<TEntity>` for tables keyed only by `[PrimaryKey]` columns
- `ISqlContainer` for build-first SQL composition plus direct execution helpers
- `ITransactionContext` for explicit commit, rollback, and savepoint control

## Main Capabilities

- Build-first CRUD: `BuildCreate`, `BuildRetrieve`, `BuildUpdateAsync`, `BuildDelete`, `BuildUpsert`
- Load prebuilt containers with `LoadSingleAsync`, `LoadListAsync`, and `LoadStreamAsync`
- Convenience methods such as `CreateAsync`, `RetrieveOneAsync`, `RetrieveAsync`, `UpdateAsync`, `DeleteAsync`, and `UpsertAsync`
- **Native `DbDataSource` support** for shared prepared-statement caching (e.g., `NpgsqlDataSource`)
- Batch create, update, upsert, and delete operations with parameter-limit-aware chunking
- Optimistic concurrency via `[Version]`
- Audit field population via `IAuditValueResolver`
- JSON, enum, GUID, binary, UTC date/time, and advanced provider-specific type mappings
- Metrics snapshots via `IDatabaseContext.Metrics` and live updates via `MetricsUpdated`
- `DbMode` strategies: `Standard`, `PreventDatabaseUnload`, `SingleWriter`, `SingleConnection`, and `Best`
- Typed exception hierarchy: provider `DbException` is translated to structured subtypes (`ConcurrencyConflictException`, `UniqueConstraintViolationException`, `DeadlockException`, etc.)

For production, use `DbMode.Best` by default. It selects the most performant mode that pengdows.crud considers safe for the detected database and connection string. Explicit modes are mainly for tests, deliberate deployment constraints, or provider-specific compatibility decisions. Requests for an unsafe mode are coerced downward: for example, `Standard` with SQLite `:memory:` becomes `SingleConnection`. Access and DuckDB are deliberate exceptions: `Best` selects `SingleWriter` because independent testing found concurrent-writer behavior less reliable than their conditional provider documentation suggests. You may explicitly choose `Standard` for those providers, but bugs dependent on that override are outside the supported safety guarantee.

`PreventDatabaseUnload`/`KeepAlive` is exactly `Standard` mode plus unused passive sentinel connections. The sentinels never execute database work; every real read and write still opens late and closes early through its own ephemeral pooled connection. Their only purpose is to keep the database loaded so the next real operation does not pay the slow database reload/startup cost after an idle period. When `Best` selects this mode for SQL Server LocalDB, Firebird, or Db2 LUW, you may explicitly choose `Standard` to disable the sentinels. This is reasonable for a workload that stays busy enough not to be unloaded while idle; LocalDB logs a performance-only warning because an idle period can make the next request pay the restart cost. `KeepAlive` is the obsolete alias; use `PreventDatabaseUnload` in new code.

## Supported Products

The repository contains concrete support for:

- SQL Server
- PostgreSQL
- Aurora PostgreSQL
- MySQL
- Aurora MySQL
- MariaDB
- Oracle
- SQLite
- Firebird
- DuckDB
- CockroachDB
- YugabyteDB
- TiDB
- Snowflake
- SingleStore (detected at runtime; uses the MySQL dialect)
- IBM Db2 for Linux/Unix/Windows (LUW) — Db2 for z/OS and Db2 for i are not supported
- SAP (Sybase) ASE
- IBM Informix
- SAP HANA
- InterBase
- Google Spanner (PostgreSQL interface)
- Microsoft Access
- FlatFile: pengdows.flatfile (embedded dialect over CSV/delimited/fixed-width/NDJSON files)

When product detection cannot identify the connected database, the library falls back to a conservative SQL-92 dialect.

### Database support by 2.0 version

- **2.0.0 baseline:** SQL Server/Azure SQL/LocalDB, PostgreSQL, Oracle, MySQL, MariaDB, SQLite, DuckDB, Firebird, CockroachDB, YugabyteDB, TiDB, Snowflake, Aurora MySQL, and Aurora PostgreSQL.
- **2.0.1–2.0.5:** No additional `SupportedDatabase` enum values; the 2.0.0 families remained the baseline while compatibility and dialect behavior evolved.
- **2.0.6 additions:** Db2 for LUW, FlatFile, SingleStore, Sybase ASE, Spanner through PGAdapter, Informix, SAP HANA, InterBase, and Access through OLE DB/ACE.

Percona Server for MySQL is detected as MySQL. Citus, TimescaleDB, and Fujitsu Enterprise Postgres are detected as PostgreSQL. These are delegated compatible products, not additional enum values. Support tiers differ: SAP HANA and InterBase require opt-in integration coverage, Access is Windows-only, Db2 support is LUW-only, and Spanner uses its PostgreSQL interface. See the [version matrix and provider notes in the wiki](https://github.com/pengdows/pengdows.crud/wiki/v2-Supported-Databases) for the current details.

## Quick Start

```bash
dotnet add package pengdows.crud
```

```csharp
using Microsoft.Data.SqlClient;
using pengdows.crud;

var context = new DatabaseContext(
    "Server=.;Database=app;Trusted_Connection=True;",
    SqlClientFactory.Instance);

var gateway = new TableGateway<Order, long>(context);

bool created = await gateway.CreateAsync(new Order
{
    Id = 42,
    OrderNumber = "ORD-42"
});

var one = await gateway.RetrieveOneAsync(42L);
var many = await gateway.RetrieveAsync(new long[] { 42, 43 });
```

```csharp
await using var tx = await context.BeginTransactionAsync();
await gateway.UpsertAsync(order, tx);
await tx.CommitAsync();
```

### Constructor Variants

```csharp
using Npgsql;
using pengdows.crud.configuration;
using pengdows.crud.enums;

// Minimal: connection string + factory
var ctx = new DatabaseContext(connectionString, SqlClientFactory.Instance);

// With read-only replica
var ctx = new DatabaseContext(connectionString, SqlClientFactory.Instance,
    readOnlyConnectionString: replicaConnectionString);

// Full configuration object (logger, pool sizes, prepare mode, etc.)
var ctx = new DatabaseContext(
    new DatabaseContextConfiguration
    {
        ConnectionString = connectionString,
        DbMode = DbMode.Standard,
        ReadWriteMode = ReadWriteMode.ReadWrite,
        ReadOnlyConnectionString = replicaConnectionString,
        MaxConcurrentReads = 20,
        MaxConcurrentWrites = 5
    },
    SqlClientFactory.Instance,
    loggerFactory);

// Provider DbDataSource (PostgreSQL prepared-statement sharing)
var dataSource = NpgsqlDataSource.Create(connectionString);
var ctx = new DatabaseContext(configuration, dataSource, NpgsqlFactory.Instance);
```

## Key Mapping Rules

- `[Id]` is the row identifier used by row-id operations
- `[PrimaryKey]` is the business key and may be composite
- `[Id]` and `[PrimaryKey]` must not be placed on the same property
- Use `PrimaryKeyTableGateway<TEntity>` when the entity has no `[Id]` column at all

## Exception Handling

Raw `DbException` from providers is automatically translated to a typed hierarchy:

```
DatabaseException (abstract — carries Database, SqlState, ErrorCode, ConstraintName, IsTransient)
  ├─ DatabaseOperationException
  │    ├─ ConcurrencyConflictException    ← [Version] column mismatch on UpdateAsync
  │    ├─ CommandTimeoutException         ← IsTransient = true
  │    ├─ ConnectionException
  │    │    ├─ FileLockContentionException ← embedded-engine file lock held by another process; IsTransient = false
  │    │    └─ TooManyConnectionsException ← server reached but at its connection limit; IsTransient = true
  │    ├─ ReadOnlyViolationException      ← write refused by a read-only connection, transaction or database
  │    ├─ AmbiguousResultException        ← commit outcome unknown (e.g. CockroachDB 40003); IsTransient = false
  │    ├─ TransactionException
  │    ├─ TransientWriteConflictException ← IsTransient = true
  │    │    ├─ DeadlockException
  │    │    └─ SerializationConflictException
  │    └─ ConstraintViolationException (abstract)
  │         ├─ UniqueConstraintViolationException
  │         ├─ ForeignKeyViolationException
  │         ├─ NotNullViolationException
  │         └─ CheckConstraintViolationException
  ├─ DataMappingException
  └─ SqlGenerationException
```

Non-`DatabaseException` subtypes thrown by the infrastructure:
- `ModeContentionException : TimeoutException` — shared-connection mode lock (e.g. SingleConnection) or transaction-completion lock exceeded `ModeLockTimeout`
- `PoolSaturatedException : TimeoutException` — no governor slot became available within `PoolAcquireTimeout` (includes SingleWriter write-slot waits)
- `PoolForbiddenException : InvalidOperationException` — connection requested from a pool configured to reject all requests (e.g. the write pool of a `ReadOnly` context)
- `TransactionModeNotSupportedException : NotSupportedException` — `BeginTransaction`/`BeginTransactionAsync` with an `IsolationProfile` the database cannot guarantee (`StrictConsistency` on TiDB/Snowflake/SingleStore/Access/FlatFile; `SafeNonBlockingReads` on SQL Server with neither snapshot isolation nor READ_COMMITTED_SNAPSHOT). Isolation never silently weakens: an explicit `IsolationLevel` is raised to the weakest supported level at least as strong, or throws `InvalidOperationException` if none exists. Savepoint calls on dialects without savepoints throw plain `NotSupportedException`.
- `ConnectionFailedException : ConnectionException` — startup connection failure (carries `Phase` and `Role`, plus the underlying SQLSTATE/error code/`IsTransient`); since 2.0.6 `catch (ConnectionException)` and `catch (DatabaseException)` see it too

```csharp
using pengdows.crud.exceptions;

try
{
    await gateway.UpdateAsync(entity);
}
catch (ConcurrencyConflictException)
{
    // [Version] mismatch — reload and retry
}
catch (UniqueConstraintViolationException ex)
{
    // ex.ConstraintName identifies which constraint fired
}
catch (DatabaseException ex) when (ex.IsTransient == true)
{
    // Deadlock, serialization failure, or timeout — safe to retry
}
```

## ISqlContainer Scalar Execution

Three distinct methods replace the ambiguous `ExecuteScalarAsync` from v1:

```csharp
// Throws if no rows or value is null/DBNull and T is non-nullable
int count = await sc.ExecuteScalarRequiredAsync<int>();

// Returns null for both "no rows" and "DBNull value"
string? name = await sc.ExecuteScalarOrNullAsync<string>();

// Unambiguously distinguishes no-row, null, and value
ScalarResult<string> result = await sc.TryExecuteScalarAsync<string>();
if (result.Status == ScalarStatus.Value)   { /* result.Value */ }
if (result.Status == ScalarStatus.Null)    { /* row returned but value was NULL */ }
if (result.Status == ScalarStatus.None)    { /* no rows returned */ }
```

## Documentation

- Repo docs: [docs/](../docs)
- Wiki: https://github.com/pengdows/pengdows.crud/wiki

## Support

If this library saves you time, consider buying me a coffee.

[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20a%20Coffee-support-yellow?logo=buy-me-a-coffee)](https://buymeacoffee.com/pengdows)
