# pengdows.crud

[![NuGet](https://img.shields.io/nuget/v/pengdows.crud.svg)](https://www.nuget.org/packages/pengdows.crud)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Build](https://github.com/pengdows/pengdows.crud/actions/workflows/deploy.yml/badge.svg)](https://github.com/pengdows/pengdows.crud/actions/workflows/deploy.yml)
[![Coverage](https://codecov.io/gh/pengdows/pengdows.crud/branch/main/graph/badge.svg)](https://codecov.io/gh/pengdows/pengdows.crud)

What is new in each release: [CHANGELOG.md](CHANGELOG.md).

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

## Supported Products

The repository contains concrete support for:

- SQL Server
- PostgreSQL
  - Citus (detected as PostgreSQL; new in 2.0.6)
  - TimescaleDB (detected as PostgreSQL; new in 2.0.6)
  - Fujitsu Enterprise Postgres (detected as PostgreSQL; new in 2.0.6)
- Aurora PostgreSQL
- MySQL
  - Percona Server for MySQL (detected as MySQL; new in 2.0.6)
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
- pengdows.flatfile (embedded dialect over CSV/delimited/fixed-width/NDJSON files)

When product detection cannot identify the connected database, the library falls back to a conservative SQL-92 dialect.

### New in 2.0.6: nine more databases

2.0.0 to 2.0.5 supported the same fourteen products. 2.0.6 adds nine, each with a `SupportedDatabase` value of its own
and a full integration suite (green on net8.0 and net10.0; see [docs/integration-status.md](docs/integration-status.md)):

| Database | How it is supported | Integration suite |
|---|---|---|
| IBM Db2 for Linux/Unix/Windows (LUW) | its own dialect; Db2 for z/OS and Db2 for i are not supported | always on |
| FlatFile: pengdows.flatfile (CSV, delimited, fixed-width, NDJSON files) | its own dialect | always on |
| SingleStore | detected at runtime (it reports itself as plain MySQL), MySQL dialect | always on |
| SAP (Sybase) ASE | its own dialect | always on |
| Google Cloud Spanner (PostgreSQL interface) | its own dialect | always on |
| IBM Informix | its own dialect | always on |
| SAP HANA | its own dialect | opt-in (`INCLUDE_SAPHANA=true`; the image needs 16 to 32 GB of RAM) |
| Embarcadero InterBase | its own dialect | opt-in (`INCLUDE_INTERBASE=true`; node-locked license) |
| Microsoft Access (Jet/ACE) | its own dialect | runs on Windows only |

### Four more products, supported through their parent

These are not separate `SupportedDatabase` values. Each reports itself as MySQL or PostgreSQL, so detection resolves it
to the parent's dialect (it is a subsidiary of that type, and `Product` reports the parent). Each was verified with no code change: it passed the same integration suite as the vanilla engine, with the same number of checks.

| Product | Detected as | Integration suite |
|---|---|---|
| Percona Server for MySQL | MySQL | always on, in the MySQL matrix |
| Citus | PostgreSQL | always on, in the PostgreSQL matrix |
| TimescaleDB | PostgreSQL | always on, in the PostgreSQL matrix |
| Fujitsu Enterprise Postgres | PostgreSQL | always on, in the PostgreSQL matrix |

EDB Postgres Extended very likely works the same way, but it has no public image to test against, so it is not claimed.
Aurora MySQL and Aurora PostgreSQL are detected at runtime and use the MySQL and PostgreSQL dialects. Details and
version thresholds: [docs/supported-databases.md](docs/supported-databases.md).

### What “supported” means

In `pengdows.crud`, supported means substantially more than opening a connection or successfully running `SELECT 1`.
A database product is supported only when all of the following are true:

- A usable .NET 8+ provider is available. It must expose a real `DbProviderFactory`, support the provider operations
  used by `DatabaseContext`, and return usable `GetSchema("DataSourceInformation")` metadata. A test-only mock or an
  unverified provider is not support evidence.
- The product has a dialect, either dedicated or explicitly documented as a compatible delegation, that owns its
  identifier quoting, parameter markers and binding, capability flags, session setup, transaction/isolation behavior,
  exception translation and provider-specific type rules.
- The dialect covers the minimum SQL emitted by `TableGateway`: quoted and parameterized SELECT/INSERT/UPDATE/DELETE,
  generated-key retrieval, optimistic-version predicates, audit writes, batch statements, and the database's supported
  upsert form (`MERGE`, `ON CONFLICT` or `ON DUPLICATE KEY UPDATE`). Paging, read-only transactions, savepoints,
  stored procedures and other features must be capability-gated or documented when the product cannot provide them.
- The complete provider-to-CLR type contract is exercised, not just scalar CRUD: nulls, numerics and precision,
  dates/times and UTC behavior, Guid text/binary representations, booleans, strings, binary/LOB values, enums, JSON,
  generated IDs, and applicable arrays, vectors, ranges, intervals, spatial and network values. Unsupported or lossy
  conversions must fail explicitly rather than silently becoming defaults.
- Real-provider integration tests pass on both `net8.0` and `net10.0`. This includes the shared
  `pengdows.crud.IntegrationTests` suites for CRUD and generated keys, type round-trips and mapping, parameters,
  transactions/isolation/savepoints, upserts and optimistic concurrency, audit fields, error translation, identifier
  quoting, read-only behavior, connection modes/pooling and provider-specific database behavior. The `testbed` adds
  live-provider provisioning, schema creation and database-mode smoke checks. Capability-based exclusions must be
  explicit and justified; “connects” or “SELECT 1” alone is not a passing support result.
- The supported server/version floor, recommended version, provider limitations and any opt-in or platform requirement
  are documented with real evidence.

The full contract, test expectations, version floors and per-database limitations are maintained in
[docs/supported-databases.md](docs/supported-databases.md). A database that has only passed connectivity or basic
CRUD is considered partially supported until it completes this contract.

## Quick Start

```bash
dotnet add package pengdows.crud
```

```bash
dotnet add package pengdows.crud.analyzers
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

## Roslyn Analyzers

`pengdows.crud.analyzers` adds framework-specific Roslyn rules for common misuse patterns in custom gateway code.

Current rules include:

- `PGC001`: pengdows.crud components must be registered as singletons (flags `AddScoped`/`AddTransient`)
- `PGC008`: do not inject raw values into SQL predicates or joins; parameterize the value instead
- `PGC025`: public/protected gateway methods that execute database work must accept an `IDatabaseContext`/`ITransactionContext` parameter and route execution through `ctx = context ?? Context`
- `PGC026`: use `WrapObjectName("alias.column")` instead of `WrapObjectName("alias") + "." + WrapObjectName("column")`
- `PGC028` (error): do not use APIs kept public only for 2.x binary compatibility (`IDatabaseContext.DataSource`, `SqlStandardLevel`/`MaxSupportedStandard`/`StandardCompliance`, internal bookkeeping types, the inert type attributes) or assign `DatabaseContext.ReadWriteMode`/`ProcWrappingStyle`, which are fixed at construction (their setters do nothing)
- `PGC029` (error, opt-in): with `<PengdowsMultiTenancy>true</PengdowsMultiTenancy>` in the consuming project, every call to a gateway execution/build method must pass a non-null `IDatabaseContext`/`ITransactionContext`; relying on the gateway's default context would silently pick the wrong tenant. Off unless the property is set

```csharp
// Bad
sc.Query.Append($" WHERE o.customer_id = {customerId}");
sc.Query.Append(" INNER JOIN customers c ON c.id = " + customerId);

// Good
sc.Query.Append(" WHERE ");
sc.Query.Append(sc.WrapObjectName("o.customer_id"));
sc.Query.Append(" = ");
var p = sc.AddParameterWithValue("customerId", DbType.Int64, customerId);
sc.Query.Append(sc.MakeParameterName(p));
```

`IS NULL` and `IS NOT NULL` are the normal exceptions. For custom `SELECT` queries, prefer `BuildBaseRetrieve(...)` so table naming, quoting, and aliases start from the gateway's safe defaults.

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
- `TransactionModeNotSupportedException : NotSupportedException` — `BeginTransaction`/`BeginTransactionAsync` with an `IsolationProfile` the database cannot guarantee (`StrictConsistency` on TiDB/Snowflake/SingleStore/Access/FlatFile; `SafeNonBlockingReads` on SQL Server without snapshot isolation). Isolation never silently weakens: an explicit `IsolationLevel` is raised to the weakest supported level at least as strong, or throws `InvalidOperationException` if none exists. Savepoint calls on dialects without savepoints throw plain `NotSupportedException`.
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

- Repo docs: [docs/](./docs)
- Wiki: https://github.com/pengdows/pengdows.crud/wiki

## Support

If this library saves you time, consider buying me a coffee.

[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20a%20Coffee-support-yellow?logo=buy-me-a-coffee)](https://buymeacoffee.com/pengdows)
