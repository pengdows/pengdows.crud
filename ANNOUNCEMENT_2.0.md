# pengdows.crud 2.0: Pool Governor, SingleWriter Re-Architecture, and 13-Database Coverage

`pengdows.crud` is **not an ORM**.

It is a SQL-first, high-performance data access framework for .NET 8. 2.0 makes a significant
architectural bet: that connection governance, multi-provider reliability, and execution safety
belong inside the framework, not on the application team.

**Requires:** .NET 8+. Available on NuGet: `pengdows.crud`, `pengdows.crud.abstractions`, `pengdows.crud.fakeDb`.

## What's New in 2.0

### 1) Pool Governor — and `pengdows.stormgate` (major)

2.0 introduces a true pool governor for read/write slot control and fairness.
It is built into `pengdows.crud` and is not available as a standalone package.

Why it matters:
- Better protection against pool saturation
- Safer high-concurrency operation
- Predictable slot acquisition and timeout behavior
- Separate read and write slot budgets; configurable per pool

While building the full pool governor for `pengdows.crud`, the minimal connection-admission
component was extracted as a separate library: **`pengdows.stormgate`**. StormGate provides
only bounded connection admission; the full governor in `pengdows.crud` adds fairness, read/write
lane separation, and full telemetry. These are different layers:

- **StormGate**: connection admission control — limits how many connections can open concurrently
- **PoolGovernor** (in `pengdows.crud`): database workload governance — controls read/write slot budgets, scheduling, and fairness across the execution pipeline

StormGate is a stripped-down admission controller for teams that aren't on `pengdows.crud`
yet — Dapper shops, raw ADO.NET, Hangfire workers, anything that just needs to stop
connection storms without adopting a full framework. It gates connection opens behind a semaphore
and returns wrapped connections that release their permit automatically when disposed — no manual
bookkeeping, no double-counting. Fail fast with a `TimeoutException` when the gate is saturated.

```csharp
// Drop-in with Dapper — no other changes required
var gate = StormGate.Create(
    MySqlConnectorFactory.Instance,
    connectionString,
    maxConcurrentOpens: 32,
    acquireTimeout: TimeSpan.FromMilliseconds(750),
    logger: loggerFactory.CreateLogger<StormGate>());

await using var conn = await gate.OpenAsync();
var orders = await conn.QueryAsync<Order>("SELECT * FROM orders WHERE customer_id = @id", new { id });
```

Pass an `ILogger` and you get a saturation warning the moment a permit times out, which is
the key signal that you are under-provisioned or leaking connections.

`pengdows.stormgate` is available as a separate NuGet package (MIT, .NET 8+). It is the
on-ramp: when you need read/write lane separation, writer-starvation prevention, per-pool
metrics, and first-class support for 13 databases, that's where `pengdows.crud`'s pool
governor picks up.

### 2) SingleWriter was fundamentally redesigned

SingleWriter in 2.0 is structurally different from 1.0:
- Turnstile-based coordination
- Two connection strings (read vs write intent)
- Separate connection pool behavior by intent
- Better fairness and reduced writer starvation risk

The key architectural shift: writes are no longer just serialized — they are scheduled.
Connection ownership has been replaced by execution scheduling, which means readers and
writers are coordinated at the task level rather than the connection level.

This is a core architecture change, not a tuning tweak.

### 3) Dual connection-string model + pool split

2.0 formalizes read/write separation via connection-string strategy and pool governance,
enabling cleaner read/write intent routing, better compatibility with replica/read-only
patterns, and less accidental contention between readers and writers.

### 4) Vastly improved hydration path

Reader hydration and coercion paths were restructured, not just tuned.

Why it matters:
- Fewer allocations per row — reduced boxing and intermediate object creation
- Fewer reflection paths — compiled accessors replace runtime property lookup on the hot path
- Reduced coercion branching — type dispatch consolidated to minimize per-column decision cost
- More predictable hot-path performance in real workloads at scale

### 5) Native `DbDataSource` support

`DatabaseContext` now accepts a `DbDataSource` directly (e.g., `NpgsqlDataSource`), and auto-creates
one when not supplied.

Why it matters:
- **Native data source** (e.g., `NpgsqlDataSource`) provides shared prepared-statement caching across
  connections — a significant throughput win for PostgreSQL workloads
- **Auto-creation via reflection**: if you pass only a `DbProviderFactory`, the context probes the
  factory for a `CreateDataSource` override and creates the native data source automatically
- **Universal fallback**: providers that don't implement `CreateDataSource` get a `GenericDbDataSource`
  wrapper — the rest of the framework always uses the data-source code path uniformly, with no
  special-casing per provider
- `IDatabaseContext.DataSource` exposes the resolved data source for diagnostics or advanced use
- Separate read and write data sources are created when read/write connection strings differ

### 6) PrimaryKeyTableGateway — first-class natural-key entities

2.0 introduces `PrimaryKeyTableGateway<TEntity>` (`IPrimaryKeyTableGateway<TEntity>`) for entities that have **no surrogate `[Id]` column** — junction tables, legacy schemas, and DBA-owned tables where the business key IS the only key.

Previously, you had to use raw `ISqlContainer` queries or work around `TableGateway<,>`'s requirement for an `[Id]` column. Now you get the full three-tier API (Build/Load/Convenience) keyed entirely on `[PrimaryKey]` columns:

```csharp
// Before 2.0: manual SQL or awkward workarounds
// After 2.0:
[Table("order_items")]
public class OrderItem
{
    [PrimaryKey(1)] [Column("order_id")] public int OrderId { get; set; }
    [PrimaryKey(2)] [Column("product_id")] public int ProductId { get; set; }
    [Column("quantity")] public int Quantity { get; set; }
}

var gateway = new PrimaryKeyTableGateway<OrderItem>(context);
await gateway.CreateAsync(new OrderItem { OrderId = 1, ProductId = 42, Quantity = 3 });
var item = await gateway.RetrieveOneAsync(new OrderItem { OrderId = 1, ProductId = 42 });
await gateway.BatchDeleteAsync(new[] { item });
await gateway.UpsertAsync(new OrderItem { OrderId = 1, ProductId = 42, Quantity = 5 });
```

Why it matters:
- Full batch support (`BatchCreateAsync`, `BatchUpdateAsync`, `BatchUpsertAsync`, `BatchDeleteAsync`) — all with automatic chunking
- Streaming (`LoadStreamAsync` → `IAsyncEnumerable<TEntity>`) for large result sets
- Audit field support via `IAuditValueResolver` — called once per batch, not per row
- Dialect-specific upsert (MERGE, ON CONFLICT, ON DUPLICATE KEY) keyed on `[PrimaryKey]` columns
- Constructor guard: throws `SqlGenerationException` at startup if entity has no `[PrimaryKey]`

See `docs/primary-keys-pseudokeys.md` for the gateway selection decision guide.

### 7) Batch operations (complete suite)

`BatchCreateAsync`, `BatchUpdateAsync`, `BatchUpsertAsync`, and `BatchDeleteAsync` are all new.
`BuildBatch*` variants build SQL without executing — inspect or modify before sending.

Why it matters:
- Automatic chunking respects `MaxParameterLimit` per database (with safety headroom)
- NULL values are inlined, not parameterized — no wasted parameter slots
- Audit resolver is called once per batch, not once per row
- `[Version]` optimistic concurrency is supported in batch update
- Dialect-specific strategies: `UPDATE FROM VALUES` (PostgreSQL), `MERGE` (SQL Server/Oracle), `ON DUPLICATE KEY` (MySQL/MariaDB)

See `docs/BATCH_OPERATIONS.md` for the full reference.

### 8) Streaming (memory-efficient large result sets)

`LoadStreamAsync` and `RetrieveStreamAsync` return `IAsyncEnumerable<TEntity>`.
Process large result sets row-by-row without buffering into `List<T>`, with full
`CancellationToken` support throughout. No code changes needed beyond switching from
`LoadListAsync` to `LoadStreamAsync`.

### 9) Dialect-aware paging (`AppendPaging`)

`ISqlDialect.AppendPaging(sc, offset, limit)` appends correct pagination SQL for the target database.

Why it matters:
- `OFFSET n ROWS FETCH NEXT m ROWS ONLY` for SQL Server, Oracle, PostgreSQL, Firebird
- `LIMIT m OFFSET n` for MySQL, MariaDB, SQLite, DuckDB, CockroachDB, TiDB
- Guard clauses validate inputs (offset ≥ 0, limit > 0) before touching SQL
- `SupportsOffsetFetch` / `SupportsLimitOffset` flags available for custom logic

### 10) Improved transactional behavior and isolation handling

We tightened isolation-level behavior and provider-specific handling for transactional
correctness, reducing cross-provider surprises and improving reliability under real-world
workloads.

### 11) Portable isolation profiles

`IsolationProfile` maps a portable intent to the safest available native isolation level per database.

Why it matters:
- `SafeNonBlockingReads` — MVCC snapshot, no dirty reads, no blocking writers
- `StrictConsistency` — Serializable, fully isolated (financial / critical logic)
- `FastWithRisks` — ReadUncommitted / dirty reads
- `Context.BeginTransaction(IsolationProfile.SafeNonBlockingReads)` works correctly across all 13 providers
  without per-database conditionals in application code

### 12) Transaction savepoints

`ITransactionContext` now exposes savepoints.

```csharp
await txn.SavepointAsync("checkpoint");
// ... partial work ...
await txn.RollbackToSavepointAsync("checkpoint");
```

Supported on PostgreSQL, SQL Server, Oracle, MySQL, MariaDB, Firebird, and CockroachDB.

### 13) `DbMode.Best` — auto-selected connection strategy

Pass `DbMode.Best` and the context selects the optimal mode automatically:
- `:memory:` SQLite/DuckDB → `SingleConnection`
- File-based SQLite/DuckDB → `SingleWriter`
- SQL Server LocalDB → `KeepAlive`
- Everything else → `Standard`

Removes guesswork for new projects and avoids file-locking errors in embedded databases.

### 14) Read-only enforcement (dual-layer)

Read-only intent is now enforced at two independent layers for supported databases.

Why it matters:
- **PostgreSQL, SQLite, DuckDB**: connection-string-level + session SQL (dual enforcement — a dirty
  connection that bypasses session settings still can't write)
- **All other providers**: session SQL enforcement per connection open
- **PostgreSQL baking**: session settings are merged into `Options=-c` startup parameters, eliminating
  a per-checkout `SET` round-trip on warm connections
- `ExecutionType.Read` routes to the appropriate connection automatically

See `docs/read-only-enforcement.md`.

### 15) Real-time metrics (36 fields)

`IDatabaseContext.Metrics` returns a `DatabaseMetrics` sealed record with full observability.
Metrics are collected inside the execution pipeline rather than inferred externally — because
the pool governor owns admission and execution, every connection open, slot acquisition, and
command dispatch is a measured event.

Why it matters:
- **Connections**: current count, peak, opens/closes, hold durations, long-lived count
- **Commands**: executed, failed, timed out, cancelled, P95/P99 duration
- **Rows**: total read, total affected
- **Transactions**: active, max concurrent, committed, rolled back, P95/P99 duration
- **Errors**: deadlocks, serialization failures, constraint violations
- **Sessions**: initialization count and average init time
- Role-based split: separate `DatabaseRoleMetrics` for read vs write paths
- `MetricsUpdated` event fires without holding locks — safe to subscribe from any thread

### 16) Expanded database support — 13 databases

2.0 ships with first-class, integration-tested support for 13 databases:

SQL Server, PostgreSQL, MySQL, **MariaDB**, Oracle, SQLite, DuckDB, Firebird,
CockroachDB, **YugabyteDB**, **TiDB**, Snowflake, and Aurora variants (auto-detected, no extra setup).

- MariaDB, YugabyteDB, and TiDB all have dedicated dialects and always-on integration tests
- YugabyteDB auto-prepare is disabled to match YSQL semantics (prevents broken connection errors after pool reuse)
- Aurora MySql/PostgreSQL are auto-detected at runtime and delegate to the correct dialect — no user configuration needed

### 17) Database-specific optimizations (dialect & performance)

2.0 introduces several database-specific optimizations that improve both throughput and reliability.

- **Oracle**: Improved pool isolation for read-only vs read-write contexts via automatic connection string discrimination, preventing cross-intent pool pollution in the ODP.NET driver.
- **Consolidated RTTs**: All dialects now consolidate baseline and intent session settings into a **single batched command (1 RTT)** during connection initialization.
- **Standardized GUIDs**: GUID storage is now fully deterministic across all 13 providers — see [Unified GUID storage](#18-unified-guid-storage) below.

### 18) Unified GUID storage

GUIDs are stored in the correct format for each database, automatically.

Why it matters:
- `PassThrough` — SQL Server, MySQL/MariaDB: native `DbType.Guid`, let the provider handle it
- `String` — SQLite, Oracle, DuckDB, Snowflake: VARCHAR/NVARCHAR(36), `"D"` format
- `Binary` — Firebird: `CHAR(16) CHARACTER SET OCTETS`, RFC 4122 big-endian byte order
- PostgreSQL: native UUID type via `AdvancedTypeRegistry` — no format conversion needed
- Round-trips correctly across all 13 databases without any manual handling

### 19) Uniform exception hierarchy

2.0 introduces a typed, provider-agnostic exception hierarchy. Every database failure the
framework translates becomes a typed `DatabaseException` subclass — independent of which of
the 13 providers threw it.

```
DatabaseException (abstract root — namespace pengdows.crud.exceptions)
    Properties: Database, SqlState, ErrorCode, ConstraintName, IsTransient
    InnerException: raw provider exception, always preserved for diagnostics
├── DatabaseOperationException
│   ├── ConstraintViolationException (abstract)
│   │   ├── UniqueConstraintViolationException   — duplicate key / PK / unique index
│   │   ├── ForeignKeyViolationException         — missing FK reference or blocked delete
│   │   ├── NotNullViolationException            — required column missing
│   │   └── CheckConstraintViolationException    — database check rule rejected values
│   ├── TransientWriteConflictException (abstract, IsTransient = true)
│   │   ├── DeadlockException                   — DB chose this txn as deadlock victim
│   │   └── SerializationConflictException      — snapshot/serializable write conflict
│   ├── ConcurrencyConflictException            — [Version] UPDATE returned 0 rows affected
│   │                                             (framework-generated, not provider-translated)
│   ├── CommandTimeoutException                 — command timed out (IsTransient = true)
│   ├── ConnectionException                     — connection-level failure
│   └── TransactionException                    — transaction state failure
├── DataMappingException                        — hydration/coercion failure
└── SqlGenerationException                      — framework SQL generation error
```

Why it matters:
- Catch `UniqueConstraintViolationException` regardless of whether the database is SQL Server,
  PostgreSQL, MySQL, SQLite, or any of the other 11 providers — no provider-specific `catch` blocks
- `IsTransient` and the `TransientWriteConflictException` base class make retry logic portable
- `ConstraintName` identifies which constraint fired, independent of the provider error format
- `InnerException` preserves the raw provider exception — diagnostic detail is never discarded
- `ConcurrencyConflictException` is framework-generated from `[Version]` column returning
  0 rows affected; it is not a translated provider exception

Translation is automatic. The framework intercepts raw provider exceptions at execution time
and translates them via per-family translators (SQL Server, PostgreSQL/CockroachDB/YugabyteDB,
MySQL/MariaDB/TiDB, SQLite). Non-database exceptions and internal exceptions propagate unchanged.
The translator infrastructure is entirely `internal` — `DatabaseException` and its subclasses
are the public surface.

**Throw sites for `ConnectionException`, `TransactionException`, `SqlGenerationException`, `DataMappingException`:**

- `ConnectionException` — thrown by provider translators when connection-level errors are detected (SQL Server error codes 10053/10054/10060/233/10061; Postgres SQLSTATE class `08xx`; MySQL error codes 1040/1042/1043/1044; SQLite error codes 14 `SQLITE_CANTOPEN` / 26 `SQLITE_NOTADB`).
- `TransactionException` — thrown by `TransactionContext` when `BeginTransaction`, `Commit`, or `Rollback` fails at the driver level. After a failure, `IsCompleted` is `true` (the connection has already been released); `Dispose` will not attempt a second rollback.
- `SqlGenerationException` — thrown by `TypeMapRegistry` for entity metadata programmer errors (missing `[Table]` attribute, empty column name, enum `DbType` not string/numeric, duplicate column names, no `[Id]`/`[PrimaryKey]`, `[PrimaryKey]` order errors, invalid `[Version]` or audit field types). Always uses `SupportedDatabase.Unknown`. Fires at entity registration or gateway construction, never during query execution.
- `DataMappingException` — thrown by `DataReaderMapper` in strict mode (`MapperOptions.Strict = true`) when a column value cannot be coerced to the target property type. Always uses `SupportedDatabase.Unknown`. `InnerException` contains the original coercion error.

`OperationCanceledException` is never wrapped — it propagates as-is.

## Breaking Changes from 1.0

### `EntityHelper<TEntity, TRowID>` renamed to `TableGateway<TEntity, TRowID>`
Update all references. The `IEntityHelper<,>` interface is replaced by `ITableGateway<,>`.

### `ITrackedConnection` is now internal
`ITrackedConnection` has been moved from the public API to `internal`. Any code that referenced
it by name (variable declarations, casts, method signatures) must be updated to use `IDbConnection`
or `IDatabaseContext` instead. Internal test infrastructure can use `InternalsVisibleTo` to retain
direct access.

### `SessionSettingsPreamble` removed from `IDatabaseContext`
The `SessionSettingsPreamble` property has been removed. Session settings are now pre-computed at
context construction time and applied automatically per connection open. There is no equivalent
public property; use `context.Dialect.GetFinalSessionSettings(readOnly: false)` if you need the
raw SQL for diagnostics (cast to `ISqlDialect` implementation type).

### Several `IDatabaseContext` properties renamed or added
- `MaxNumberOfConnections` → `PeakOpenConnections`
- `Name`, `RootId`, `ReadWriteMode`, `ModeLockTimeout`, `Metrics`, `MetricsUpdated` added
- `Dialect` property added (was previously accessible only via `ISqlDialectProvider`)
- `AssertIsReadConnection()` and `AssertIsWriteConnection()` removed from `IDatabaseContext` (internal-only checks)

### `IColumnInfo` property rename
- `IsIdIsWritable` → `IsIdWritable`

### `Pooling=false` in connection strings now throws at construction
Setting `Pooling=false` in your connection string is no longer silently accepted.
`DatabaseContext` will throw `InvalidOperationException` during construction if pooling
is explicitly disabled.

**Why:** pengdows.crud's "open late, close early" model assumes a pool is present on every
checkout. Without pooling, each operation opens a new physical connection, defeating the
pool governor slot budgets and breaking connection-count metrics.

**Migration:** remove `Pooling=false` from your connection string. If you need a single
persistent connection (e.g. SQLite in-memory), use `DbMode.SingleConnection` instead —
it bypasses the pool entirely and holds one connection for the context's lifetime.

### Hot-path execution methods return `ValueTask` (not `Task`)
`ExecuteNonQueryAsync`, `ExecuteScalarRequiredAsync`, `ExecuteScalarOrNullAsync`, and `ExecuteReaderAsync`
all return `ValueTask` / `ValueTask<T>`. Any callers that stored the result as `Task<T>` must update
their variable declarations. `await` calls require no change.

### `AppendPaging` and `IsUniqueViolation` added to `ISqlDialect`
Two new non-default members were added to `ISqlDialect`. If you have a custom dialect
implementation outside this library, both must be implemented to avoid a compile error:

- `void AppendPaging(ISqlQueryBuilder query, int offset, int limit)` — appends dialect-specific
  paging SQL; see "What's New #9" for behavior details.
- `bool IsUniqueViolation(DbException ex)` — returns true when the exception represents a
  unique/primary-key constraint violation; used by the exception translation pipeline.

All other new `ISqlDialect` members added in 2.0 (`ClassifyException`, `AnalyzeException`,
`IsForeignKeyViolation`, `IsNotNullViolation`, `IsCheckConstraintViolation`, `WrapSimpleName`,
`ReplaceNeutralTokens`, `RenderMergeOnClause`, `PrepareParameterValue`) have default
implementations and require no action from custom dialect implementors.

### Public API surface reduction — implementation details removed

A large number of members that leaked internal implementation details in 1.0 have been
removed or moved to `internal`. These were never intended as public API and existed only
because the 1.0 interface boundaries were drawn too broadly.

The tables below are representative of the categories of removal. The complete diff is
tracked in `pengdows.crud.abstractions/ApiBaseline/interfaces.txt`. Net change across the
full public surface: approximately **40 fewer signatures** after removals, with additions
for `Dialect`, `DataSource`, `AppendPaging`, metrics members, and the exception hierarchy.

If you referenced any of these directly, the migration path is noted for each group.

#### `IDatabaseContext` methods removed

| Removed | Why / Migration |
|---------|-----------------|
| `GetLock()` | Internal coordination; consumers never needed to acquire the mode lock directly |
| `CloseAndDisposeConnection(ITrackedConnection)` | Connection lifecycle is managed internally; callers should `await using` their containers and readers |
| `CloseAndDisposeConnectionAsync(ITrackedConnection)` | Same as above |
| `AssertIsReadConnection()` | Internal invariant check; no consumer use case |
| `AssertIsWriteConnection()` | Internal invariant check; no consumer use case |
| `SessionSettingsPreamble` (property) | Replaced by automatic per-checkout session application; see `GetBaseSessionSettings()` for diagnostics |

#### `ISqlDialect` methods removed

All of these took `ITrackedConnection` (now internal) or were pure implementation
details of the dialect initialization pipeline.

| Removed | Why / Migration |
|---------|-----------------|
| `ApplyConnectionSettings(IDbConnection, IDatabaseContext, bool)` | Applied automatically at connection checkout; not a consumer concern |
| `DetectDatabaseInfo(ITrackedConnection)` | Triggered automatically during `DatabaseContext` construction |
| `DetectDatabaseInfoAsync(ITrackedConnection)` | Same as above |
| `GetDataSourceInformationSchema(ITrackedConnection)` | Internal schema probe; use `IDatabaseContext.DataSourceInfo` for metadata |
| `GetDatabaseVersion(ITrackedConnection)` | Internal probe; version accessible via `IDatabaseContext.DataSourceInfo` |
| `GetMajorVersion(string)` | Internal version parsing helper |
| `ParseVersion(string)` | Internal version parsing helper |
| `InitializeUnknownProductInfo()` | Internal fallback path for unrecognized database products |
| `IsReadCommittedSnapshotOn(ITrackedConnection)` | Internal SQL Server RCSI probe |
| `IsSnapshotIsolationOn(ITrackedConnection)` | Internal SQL Server snapshot isolation probe |
| `ShouldDisablePrepareOn(Exception)` | Internal prepare-state veto logic |
| `TryEnterReadOnlyTransaction(ITransactionContext)` | Applied automatically when `ReadWriteMode.ReadOnly` is set |
| `TryEnterReadOnlyTransactionAsync(ITransactionContext, CancellationToken)` | Same as above |

#### `ISqlContainer` method removed

| Removed | Why / Migration |
|---------|-----------------|
| `CreateCommand(ITrackedConnection)` | Internal command factory; use `ExecuteNonQueryAsync` / `ExecuteReaderAsync` instead |

#### `IDataReaderMapper` overloads removed

The raw-`IDataReader` overloads were replaced by `ITrackedReader` versions that carry
connection-lifecycle tracking.

| Removed | Replacement |
|---------|-------------|
| `LoadAsync<T>(IDataReader, CancellationToken)` | `LoadAsync<T>(ITrackedReader, CancellationToken)` |
| `LoadAsync<T>(IDataReader, IMapperOptions, CancellationToken)` | `LoadAsync<T>(ITrackedReader, IMapperOptions, CancellationToken)` |
| `StreamAsync<T>(IDataReader, IMapperOptions, CancellationToken)` | `StreamAsync<T>(ITrackedReader, IMapperOptions, CancellationToken)` |

#### Net API baseline change

1.0 public surface → 2.0: approximately **40 fewer signatures** after removals, the
`IsIdIsWritable → IsIdWritable` rename, and new additions (`Dialect`, `DataSource`,
`AppendPaging`, metrics members, etc.).

## From 1.0 to 2.0

- API usage remains familiar
- Internal behavior is significantly stronger under load
- Concurrency and execution semantics are more explicit and robust

If you're already on 1.0, 2.0 gives you better safety and performance while preserving explicit SQL control.

## Migrating from 1.0

The breaking changes above cover everything that requires a code update. API usage
stays familiar — the work is in removing calls to internals that shouldn't have been
public in the first place.
