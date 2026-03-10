# pengdows.crud 2.0: Pool Governor, SingleWriter Re-Architecture, Faster Hydration, and Snowflake Support

`pengdows.crud` is **not an ORM**.

It is a high-performance, SQL-first framework for:
- fast hydration of table objects, and
- CRUD operations with minimal ceremony,

so developers can eliminate repetitive ADO.NET boilerplate without giving up SQL control.

## What’s New in 2.0

### 1) Pool Governor — and `pengdows.stormgate` (major)

2.0 introduces a true pool governor for read/write slot control and fairness.
It is built into `pengdows.crud` and is not available as a standalone package.

Why it matters:
- Better protection against pool saturation
- Safer high-concurrency operation
- Predictable slot acquisition and timeout behavior
- Separate read and write slot budgets; configurable per pool

While building the pool governor I extracted the core thundering-herd protection into a
separate, minimal library: **`pengdows.stormgate`**.

StormGate is a stripped-down admission controller for teams that aren't on `pengdows.crud`
yet — Dapper shops, raw ADO.NET, Hangfire workers, anything that just needs to stop
connection storms without adopting a full framework. It does one thing: gate concurrent
connection opens behind a `SemaphoreSlim` and fail fast with a `TimeoutException` when the
gate is saturated.

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

The permit is tied to the connection and released automatically on close or dispose —
no manual bookkeeping. Pass an `ILogger` and you get a saturation warning the moment a
permit times out, which is the key signal that you are under-provisioned or leaking
connections.

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

This is a core architecture change, not a tuning tweak.

### 3) Dual connection-string model + pool split
2.0 formalizes read/write separation via connection-string strategy and pool governance.

Why it matters:
- Cleaner read/write intent routing
- Better compatibility with replica/read-only patterns
- Less accidental contention between readers and writers

### 4) Vastly improved hydration path
Reader hydration and coercion paths were heavily optimized.

Why it matters:
- Lower per-row overhead
- Better mapping throughput at scale
- More predictable hot-path performance in real workloads

### 5) Expanded database support, including Snowflake
2.0 extends provider coverage and behavior support, with Snowflake now included.

Why it matters:
- More teams can use `pengdows.crud` without provider-specific forks
- Better cross-database consistency for SQL-first apps

### 6) Improved transactional behavior and isolation handling
We tightened isolation-level behavior and provider-specific handling for transactional correctness.

Why it matters:
- Fewer surprises across providers
- More reliable behavior under real-world workloads

### 7) Native `DbDataSource` support
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

### 8) Batch operations (complete suite)
`BatchCreateAsync`, `BatchUpdateAsync`, `BatchUpsertAsync`, and `BatchDeleteAsync` are all new.
`BuildBatch*` variants build SQL without executing — inspect or modify before sending.

Why it matters:
- Automatic chunking respects `MaxParameterLimit` per database (with safety headroom)
- NULL values are inlined, not parameterized — no wasted parameter slots
- Audit resolver is called once per batch, not once per row
- `[Version]` optimistic concurrency is supported in batch update
- Dialect-specific strategies: `UPDATE FROM VALUES` (PostgreSQL), `MERGE` (SQL Server/Oracle), `ON DUPLICATE KEY` (MySQL/MariaDB)

See `docs/BATCH_OPERATIONS.md` for the full reference.

### 9) Streaming (memory-efficient large result sets)
`LoadStreamAsync` and `RetrieveStreamAsync` return `IAsyncEnumerable<TEntity>`.

Why it matters:
- Process large result sets row-by-row without buffering into `List<T>`
- Full `CancellationToken` support throughout
- No code changes needed beyond switching from `LoadListAsync` to `LoadStreamAsync`

### 10) Dialect-aware paging (`AppendPaging`)
`ISqlDialect.AppendPaging(sc, offset, limit)` appends correct pagination SQL for the target database.

Why it matters:
- `OFFSET n ROWS FETCH NEXT m ROWS ONLY` for SQL Server, Oracle, PostgreSQL, Firebird
- `LIMIT m OFFSET n` for MySQL, MariaDB, SQLite, DuckDB, CockroachDB, TiDB
- Guard clauses validate inputs (offset ≥ 0, limit > 0) before touching SQL
- `SupportsOffsetFetch` / `SupportsLimitOffset` flags available for custom logic

### 11) 13 supported databases
The 1.0 announcement said "Snowflake now included" but that undersells the full scope.
2.0 ships with first-class, integration-tested support for 13 databases:

SQL Server, PostgreSQL, MySQL, **MariaDB**, Oracle, SQLite, DuckDB, Firebird,
CockroachDB, **YugabyteDB**, **TiDB**, Snowflake, and Aurora variants (auto-detected, no extra setup).

Why it matters:
- MariaDB, YugabyteDB, and TiDB all have dedicated dialects and always-on integration tests
- YugabyteDB auto-prepare is disabled to match YSQL semantics (prevents broken connection errors after pool reuse)
- Aurora MySql/PostgreSQL are auto-detected at runtime and delegate to the correct dialect — no user configuration needed

### 12) Real-time metrics (36 fields)
`IDatabaseContext.Metrics` returns a `DatabaseMetrics` sealed record with full observability.

Why it matters:
- **Connections**: current count, peak, opens/closes, hold durations, long-lived count
- **Commands**: executed, failed, timed out, cancelled, P95/P99 duration
- **Rows**: total read, total affected
- **Transactions**: active, max concurrent, committed, rolled back, P95/P99 duration
- **Errors**: deadlocks, serialization failures, constraint violations
- **Sessions**: initialization count and average init time
- Role-based split: separate `DatabaseRoleMetrics` for read vs write paths
- `MetricsUpdated` event fires without holding locks — safe to subscribe from any thread

### 13) Read-only enforcement (dual-layer)
Read-only intent is now enforced at two independent layers for supported databases.

Why it matters:
- **PostgreSQL, SQLite, DuckDB**: connection-string-level + session SQL (dual enforcement — a dirty
  connection that bypasses session settings still can't write)
- **All other providers**: session SQL enforcement per connection open
- **PostgreSQL baking**: session settings are merged into `Options=-c` startup parameters, eliminating
  a per-checkout `SET` round-trip on warm connections
- `ExecutionType.Read` routes to the appropriate connection automatically

See `docs/read-only-enforcement.md`.

### 14) Portable isolation profiles
`IsolationProfile` maps a portable intent to the safest available native isolation level per database.

Why it matters:
- `SafeNonBlockingReads` — MVCC snapshot, no dirty reads, no blocking writers
- `StrictConsistency` — Serializable, fully isolated (financial / critical logic)
- `FastWithRisks` — ReadUncommitted / dirty reads
- `Context.BeginTransaction(IsolationProfile.SafeNonBlockingReads)` works correctly across all 13 providers
  without per-database conditionals in application code

### 15) Transaction savepoints
`ITransactionContext` now exposes savepoints.

```csharp
await txn.SavepointAsync("checkpoint");
// ... partial work ...
await txn.RollbackToSavepointAsync("checkpoint");
```

Supported on PostgreSQL, SQL Server, Oracle, MySQL, MariaDB, Firebird, and CockroachDB.

### 16) `DbMode.Best` — auto-selected connection strategy
Pass `DbMode.Best` and the context selects the optimal mode automatically.

Why it matters:
- `:memory:` SQLite/DuckDB → `SingleConnection`
- File-based SQLite/DuckDB → `SingleWriter`
- SQL Server LocalDB → `KeepAlive`
- Everything else → `Standard`
- Removes guesswork for new projects and avoids file-locking errors in embedded databases

### 17) Unified GUID storage
GUIDs are stored in the correct format for each database, automatically.

Why it matters:
- `PassThrough` — SQL Server, MySQL/MariaDB: native `DbType.Guid`, let the provider handle it
- `String` — SQLite, Oracle, DuckDB, Snowflake: VARCHAR/NVARCHAR(36), `"D"` format
- `Binary` — Firebird: `CHAR(16) CHARACTER SET OCTETS`, RFC 4122 big-endian byte order
- PostgreSQL: native UUID type via `AdvancedTypeRegistry` — no format conversion needed
- Round-trips correctly across all 13 databases without any manual handling

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

### `AppendPaging` added to `ISqlDialect`
Custom dialect implementations must add this method. If you have a dialect outside this library,
implement `AppendPaging(ISqlContainer sc, int offset, int limit)` to avoid a compile error.

### Public API surface reduction — implementation details removed

A large number of members that leaked internal implementation details in 1.0 have been
removed or moved to `internal`. These were never intended as public API and existed only
because the 1.0 interface boundaries were drawn too broadly.

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

If you’re already on 1.0, 2.0 gives you better safety and performance while preserving explicit SQL control.

## Why this release matters

`pengdows.crud` has always focused on explicit SQL, high performance hydration, and pragmatic CRUD support. 
2.0 strengthens the internals where it matters most: connection management, contention control, hydration speed, and multi-provider reliability.
