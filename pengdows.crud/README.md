# pengdows.crud

`pengdows.crud` is a SQL-first data access framework for .NET. It favors explicit SQL, provider-aware execution, and small inspectable abstractions over ORM-style query generation.

No LINQ. No tracking. No surprises.

## Public Surface

- `DatabaseContext` / `IDatabaseContext`: connection management, dialect behavior, parameter creation, metrics, quoting, and transactions
- `TableGateway<TEntity, TRowID>` / `ITableGateway<TEntity, TRowID>`: CRUD builders, convenience methods, batch operations, and async streaming
- `ISqlContainer`: composable SQL plus parameter collection and direct execution helpers
- `ITransactionContext`: explicit transaction scope with `Commit`, `Rollback`, and savepoints

## What The Code Supports

- Build-first CRUD: `BuildCreate`, `BuildRetrieve`, `BuildUpdateAsync`, `BuildDelete`, `BuildUpsert`
- Execute later with `LoadSingleAsync`, `LoadListAsync`, `LoadStreamAsync`, or `ISqlContainer` execution methods
- Convenience operations such as `CreateAsync`, `RetrieveOneAsync`, `RetrieveAsync`, `UpdateAsync`, `DeleteAsync`, and `UpsertAsync`
- Batch create, update, delete, and upsert operations with parameter-limit chunking
- Async streaming via `IAsyncEnumerable<TEntity>`
- CancellationToken overloads across async APIs
- `ISqlContainer` execution methods returning `ValueTask`
- Automatic parameter naming through `MakeParameterName(...)`
- Identifier quoting through `WrapObjectName(...)`
- Optimistic concurrency via `[Version]`
- Audit field support via `IAuditValueResolver`
- JSON, enum, GUID, UTC date/time, and binary mappings

## Connection And Transaction Model

- `DbMode` values in the public API are `Standard`, `KeepAlive`, `SingleWriter`, `SingleConnection`, and `Best`
- `DbMode.Best` auto-selects a mode from provider and connection-string characteristics
- `ExecutionType.Read` and `ExecutionType.Write` are available on SQL container execution methods and transaction entry points
- `BeginTransaction(...)` supports both `IsolationLevel` and portable `IsolationProfile`
- `ITransactionContext` exposes `Commit`, `Rollback`, `SavepointAsync`, and `RollbackToSavepointAsync`
- `ModeLockTimeout`, `Metrics`, `MetricsUpdated`, and `ReaderPlanCacheSize` are part of `IDatabaseContext`
- `TransactionScope` is not the intended transaction model; use `BeginTransaction()`

## Key Entity Mapping Facts

- `[Id]` is the row identifier used by row-id CRUD operations
- `[PrimaryKey]` is the business key and may be composite
- `[Id]` and `[PrimaryKey]` are different concepts and should not be placed on the same property
- `RetrieveOneAsync(TRowID)` uses `[Id]`
- `RetrieveOneAsync(TEntity)` uses `[PrimaryKey]`
- Upsert conflict selection prefers `[PrimaryKey]` and falls back to a writable `[Id]`

## Supported Databases

Tested support in this repository includes:

- SQL Server / Express / LocalDB
- PostgreSQL / TimescaleDB / Aurora PostgreSQL
- MySQL / MariaDB / Aurora MySQL
- CockroachDB
- YugabyteDB
- TiDB
- Oracle
- SQLite
- Firebird
- DuckDB
- Snowflake (opt-in; requires credentials)

Providers must support `DbProviderFactory` and `GetSchema("DataSourceInformation")`.

## Getting Started

```bash
dotnet add package pengdows.crud
```

```csharp
using Microsoft.Data.SqlClient;
using pengdows.crud;

var context = new DatabaseContext("your-connection-string", SqlClientFactory.Instance);
var gateway = new TableGateway<MyEntity, long>(context);
```

```csharp
var sc = gateway.BuildRetrieve(new[] { 1L, 2L });
var rows = await gateway.LoadListAsync(sc);
```

```csharp
await using var tx = await context.BeginTransactionAsync();
await gateway.CreateAsync(entity, tx);
await tx.CommitAsync();
```

## More Documentation

Project documentation and provider notes live in the repository wiki and docs:

- https://github.com/pengdows/pengdows.crud/wiki
- https://github.com/pengdows/pengdows.crud/tree/main/docs
