# pengdows.crud 2.0 Overview

`pengdows.crud` is a SQL-first data access library for .NET 8. The codebase centers on a small public surface:

- `DatabaseContext` / `IDatabaseContext`
- `TableGateway<TEntity, TRowID>` / `ITableGateway<TEntity, TRowID>`
- `PrimaryKeyTableGateway<TEntity>` / `IPrimaryKeyTableGateway<TEntity>`
- `ISqlContainer`
- `ITransactionContext`

## Core Model

`DatabaseContext` is a long-lived execution coordinator, not an EF-style unit of work. It owns:

- provider and dialect detection
- connection lifecycle and `DbMode`
- parameter naming and identifier quoting
- metrics snapshots and the `MetricsUpdated` event
- transaction creation

`TableGateway<TEntity, TRowID>` is the row-id gateway. Use it when the entity has an `[Id]` column.

`PrimaryKeyTableGateway<TEntity>` is the natural-key gateway. Use it when the entity has no `[Id]` column and is identified only by `[PrimaryKey]` columns.

## Public API Shape

Both gateway styles expose the same three-layer pattern:

1. Build methods that return `ISqlContainer`
2. Load methods that execute a prebuilt container
3. Convenience methods that build and execute in one call

The codebase also exposes:

- batch create/update/upsert/delete
- async streaming via `IAsyncEnumerable<TEntity>`
- `CancellationToken` overloads across async entry points
- optimistic concurrency via `[Version]`
- audit population via `IAuditValueResolver`

## Mapping Rules

- `[Table]` maps the entity to a table
- `[Column]` marks persistent properties
- `[Id]` is the surrogate row identifier
- `[PrimaryKey]` defines the business key and may be composite
- `[Id]` and `[PrimaryKey]` must not appear on the same property
- `[Json]`, `[EnumColumn]`, `[EnumLiteral]`, audit attributes, and versioning attributes are all active in the current mapper

## Connection Modes

The public `DbMode` values are:

- `Standard`
- `KeepAlive`
- `SingleWriter`
- `SingleConnection`
- `Best`

`DbMode.Best` selects a mode from the product and connection-string shape. The codebase documents and tests `Standard` as the normal client/server mode and uses `SingleWriter` or `SingleConnection` for engines that need stricter coordination.

## Transactions

`IDatabaseContext` exposes `BeginTransaction` and `BeginTransactionAsync` overloads for:

- native `IsolationLevel`
- portable `IsolationProfile`

`ITransactionContext` exposes:

- `Commit` / `CommitAsync`
- `Rollback` / `RollbackAsync`
- `SavepointAsync`
- `RollbackToSavepointAsync`

## Supported Products

The repository contains product support for:

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

If detection cannot identify the product, the library falls back to the SQL-92 dialect.

## What This Overview Does Not Claim

The current codebase does not expose:

- LINQ translation
- change tracking
- implicit unit-of-work behavior
- `CreateManyAsync` / `UpdateManyAsync` orchestration APIs
- built-in partial-success batch reporting
