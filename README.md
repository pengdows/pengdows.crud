# pengdows.crud

[![NuGet](https://img.shields.io/nuget/v/pengdows.crud.svg)](https://www.nuget.org/packages/pengdows.crud)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Build](https://github.com/pengdows/pengdows.crud/actions/workflows/deploy.yml/badge.svg)](https://github.com/pengdows/pengdows.crud/actions/workflows/deploy.yml)
[![Coverage](https://codecov.io/gh/pengdows/pengdows.crud/branch/main/graph/badge.svg)](https://codecov.io/gh/pengdows/pengdows.crud)

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
- `DbMode` strategies: `Standard`, `KeepAlive`, `SingleWriter`, `SingleConnection`, and `Best`
- Typed exception hierarchy: provider `DbException` is translated to structured subtypes (`ConcurrencyConflictException`, `UniqueConstraintViolationException`, `DeadlockException`, etc.)

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

When product detection cannot identify the connected database, the library falls back to a conservative SQL-92 dialect.

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
- `ModeContentionException : TimeoutException` — SingleWriter/SingleConnection lock timed out
- `PoolSaturatedException : TimeoutException` — internal connection pool exhausted
- `PoolForbiddenException : InvalidOperationException` — write attempted on read-only context
- `TransactionModeNotSupportedException : NotSupportedException` — savepoint or read-only tx on unsupported dialect
- `ConnectionFailedException : Exception` — startup connection failure (carries `Phase` and `Role`)

```csharp
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

- Repo docs: [docs/](./docs)
- Wiki: https://github.com/pengdows/pengdows.crud/wiki

## Support

If this library saves you time, consider buying me a coffee.

[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20a%20Coffee-support-yellow?logo=buy-me-a-coffee)](https://buymeacoffee.com/pengdows)
