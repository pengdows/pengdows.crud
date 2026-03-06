# pengdows.crud Architecture Review (Code-Level Answers)

Scope note: Answers are based on this repository (`pengdows.crud`) as of March 6, 2026. `pengdows.poco.mint` source is not present here, so mint-specific internals are marked accordingly.

## Architecture

1. Core public abstractions:
- `IDatabaseContext` + `DatabaseContext` (`pengdows.crud.abstractions/IDatabaseContext.cs`, `pengdows.crud/DatabaseContext.cs`)
- `ISqlContainer` + `SqlContainer` (`pengdows.crud.abstractions/ISqlContainer.cs`, `pengdows.crud/SqlContainer.cs`)
- `ITableGateway<TEntity, TRowID>` + `TableGateway<TEntity, TRowID>` (`pengdows.crud.abstractions/ITableGateway.cs`, `pengdows.crud/TableGateway*.cs`)
- Dialects via `ISqlDialect` and concrete dialects (`pengdows.crud.abstractions/dialects/ISqlDialect.cs`, `pengdows.crud/dialects/*.cs`)
- Wrappers: `ITrackedConnection`/`TrackedConnection`, `ITrackedReader`/`TrackedReader` (`pengdows.crud.abstractions/wrappers/*.cs`, `pengdows.crud/wrappers/*.cs`)
- Transactions: `ITransactionContext` + `TransactionContext` (`pengdows.crud.abstractions/ITransactionContext.cs`, `pengdows.crud/TransactionContext.cs`)
- `EntityHelper` is not a current public abstraction; README states alias removed in v2.0 (`pengdows.crud/README.md`).

2. Lifecycle (normal command path):
- Build SQL in `SqlContainer.Query` + parameters (`SqlContainer.cs`).
- Acquire governor slot in `DatabaseContext.AcquireSlot` -> `PoolGovernor.Acquire` (`DatabaseContext.ConnectionLifecycle.cs`, `PoolGovernor.cs`).
- Checkout/create connection in `DatabaseContext.GetStandardConnectionWithExecutionType` -> `FactoryCreateConnection`.
- Open connection + first-open session setup via `TrackedConnection` callbacks -> `ExecuteSessionSettings/Async`.
- Execute command via `SqlContainer.ExecuteNonQueryAsync`/scalar/reader paths.
- Hydrate:
  - `ExecuteReaderAsync*` returns `TrackedReader` (lease).
  - Gateway hydration via compiled plans (`TableGateway.Reader.cs`, `internal/CompiledMapperFactory.cs`) or generic mapper (`DataReaderMapper.cs`).
- Release:
  - Non-reader: `SqlContainer.Cleanup` -> context strategy release/dispose.
  - Reader: `TrackedReader.Dispose/DisposeAsync` disposes reader/command, closes connection as configured, releases lock, and notifies container.

3. Ownership by class:
- Provider connection creation: `DatabaseContext.FactoryCreateConnection`
- Command creation: `SqlContainer.CreateRawCommand/CreateCommand`
- Parameter binding: `SqlContainer.AddParametersToCommand`
- Session normalization: dialect + `DatabaseContext.ExecuteSessionSettings*`
- Metrics emission: `MetricsCollector`, plus call sites in `SqlContainer`, `TrackedConnection`, `TrackedReader`, `TransactionContext`
- Pool governance: `PoolGovernor` + `DatabaseContext.AcquireSlot/InitializePoolGovernors`
- Transaction scoping: `TransactionContext`

4. Divergent code paths:
- One-shot execution: `ExecuteNonQueryAsync`/`ExecuteScalarCore` + `Cleanup` same call.
- Reader execution: `ExecuteReaderAsyncInternal` transfers lock/connection lifetime to `TrackedReader`.
- Transaction execution: `TransactionContext` supplies pinned connection; normal cleanup skips disposing that pinned connection.
- SQLite `:memory:`: mode/connection handling favors `SingleConnection`; SQLite read-only parameter skipped for memory DB in `SqliteDialect.ApplyConnectionSettingsCore`.
- Read vs write mode: execution type drives governor, metrics lane, and read/write assertions.

## PoolGovernor

5. Full implementation + call sites:
- Implementation: `pengdows.crud/infrastructure/PoolGovernor.cs`.
- Token/RAII release: `pengdows.crud/infrastructure/PoolSlot.cs`.
- Production call sites:
  - Create: `DatabaseContext.InitializePoolGovernors/CreateGovernor`.
  - Acquire: `DatabaseContext.AcquireSlot`.
  - KeepAlive pinned attach: `AttachPinnedSlotIfNeeded`.
  - Snapshot: `DatabaseContext.GetPoolStatisticsSnapshot`.
- Extensive test call sites under `pengdows.crud.Tests/PoolGovernor*Tests.cs`.

6. Admission policy:
- Primary semaphore (`SemaphoreSlim`) with optional fairness turnstile.
- Queueing via semaphore waits and tracked queued counters.
- Timeout enforced with deadline budget (`_acquireTimeout`) across turnstile+slot.
- Cancellation supported; cancellation count tracked.
- Rejection:
  - forbidden pools (`Max=0`) throw `PoolForbiddenException`.
  - saturation timeout throws `PoolSaturatedException`.

7. Governor metrics emitted (snapshot fields):
- wait time: `TotalWaitTicks`
- queue depth: `Queued`, `PeakQueued`, `TurnstileQueued`, `PeakTurnstileQueued`
- active leases: `InUse`, `PeakInUse`
- timeout count: `TotalSlotTimeouts`, `TotalTurnstileTimeouts`
- throughput: `TotalAcquired`
- read/write split: separate reader/writer governors (labelled snapshots)

8. Governor before checkout?
- For standard connection acquisition paths, yes: slot acquired before `FactoryCreateConnection`.
- `SingleConnection` mode disables governors entirely.

9. Bypass paths:
- Transactions: do not bypass; transaction creation calls context `GetConnection`, which in standard strategy acquires slot.
- Raw SQL via `SqlContainer`: does not bypass.
- Schema/metadata/dialect detection uses init/persistent paths and may not go through normal per-op sloting.
- No obvious “bulk bypass”; bulk APIs still route through gateway/container execution.
- Tests include direct wrapper/dialect usage that bypasses production lifecycle.

10. Max pool coordination:
- Governor sizing derived from effective provider pool config + explicit overrides in `InitializePoolGovernors`.
- Connection strings are also adjusted (`ApplyMaxPoolSize`) so provider pool and governor are intended to align.

11. Governor dimensions:
- Distinguishes read vs write via separate governors.
- Provider distinction is indirect through per-context dialect + pool-key hash.
- Named contexts/tenants are isolated by separate `DatabaseContext` instances (and keys), not by a global governor registry.

## Connection lifecycle

12. Connection wrapper + creation points:
- Wrapper: `TrackedConnection` (`pengdows.crud/wrappers/TrackedConnection.cs`).
- Production creation: `DatabaseContext.FactoryCreateConnection`.

13. Tracked state:
- open/closed via underlying state + event handlers
- first-open flag: `WasOpened`
- slot attached/released flags
- `LocalState` (prepare/session settings applied)
- mode contention hooks and metrics timing
- no explicit “transaction-bound” field; transaction binding is contextual (`TransactionContext` holds pinned connection)

14. Double release/dispose prevention:
- Dispose idempotence from base `SafeAsyncDisposableBase`.
- Slot release guarded by interlocked `_slotReleased` + `_slotAttached`.

15. Close vs retain decision:
- Strategy decides release semantics:
  - `StandardConnectionStrategy`: dispose on release
  - `KeepAliveConnectionStrategy`: retain persistent sentinel
  - `SingleConnectionStrategy`: retain persistent connection
- Transaction pinned connection retained until transaction complete/dispose.

16. Two “retain” exceptions code locations:
- SQLite `:memory:` read-only parameter exception: `SqliteDialect.ApplyConnectionSettingsCore` + `GetReadOnlyConnectionString`.
- Active transaction exception: `TransactionContext.CloseAndDisposeConnection*` returns early when `conn` is the pinned `_connection`.

17. Other hidden retention cases:
- KeepAlive sentinel.
- SingleConnection mode persistent connection.
- Active reader lease (connection held until `TrackedReader` disposal).

18. Cancellation handling:
- Open: `OpenConnectionAsync` and `TrackedConnection.OpenAsync(cancellationToken)`.
- Session settings: `ExecuteSessionSettingsAsync` honors cancellation token.
- Command execution: async command methods pass cancellation token.
- Reader consumption: `TrackedReader.ReadAsync(cancellationToken)`.

19. Exception-path release guarantees:
- `try/finally` in `SqlContainer` execution methods.
- `Cleanup` disposes command/connection where ownership not transferred.
- `TrackedReader` disposal hook (`IReaderLifetimeListener`) decrements active reader tracking.

20. Leak-proof status:
- Not formally proven; strongly defended by disposal patterns and many targeted tests. So: unlikely, not mathematically impossible.

## DataReader wrapper

21. Implementation + call sites:
- Implementation: `pengdows.crud/wrappers/TrackedReader.cs`.
- Production creation: `SqlContainer.ExecuteReaderAsyncInternal`.

22. “Done” detection:
- `Read()/ReadAsync()` auto-dispose when row fetch returns false.
- `NextResult()` is intentionally unsupported (throws).
- Explicit `Dispose/DisposeAsync` also completes cleanup.

23. Handling matrix:
- Multi-result sets: not supported.
- Partial enumeration: deterministic if caller disposes (or uses `await using`).
- Exceptions during enumeration: caller exception; cleanup still deterministic if dispose path executes.
- Async streaming: supported through async reads and gateway streaming helpers.
- Schema-only readers: no special path in wrapper; passthrough behavior.

24. Exact close/release on completion:
- `TrackedReader.DisposeManaged/DisposeManagedAsync`:
  - dispose reader
  - dispose command
  - close/dispose connection when `_shouldCloseConnection`
  - dispose connection locker
  - notify lifetime listener

25. Reader metrics granularity:
- Records rows read / rows affected on dispose.
- No separate first-row latency or full-consumption latency metric.
- Command execution timing is tracked in `SqlContainer` before reader handoff.

26. Slow consumer vs slow SQL distinguished?
- Not directly. Command timing and lease/hold metrics exist, but no explicit first-row vs consumer-phase split.

27. Early abandon with dispose deterministic?
- Yes; explicit dispose path is deterministic and releases lock/connection per wrapper flags.

## Session settings

28. Build/cache/execute model:
- Built by dialect (`GetFinalSessionSettings(readOnly)`).
- Cached once on context init in `_cachedReadWriteSessionSettings/_cachedReadOnlySessionSettings`.
- Executed on first open callback in `FactoryCreateConnection` via `ExecuteSessionSettings*`.

29. Always or conditional?
- Conditional on `_sessionSettingsDetectionCompleted`; if false, skipped.
- If cached text is empty, marks local state and returns.
- Applied on first open of each physical tracked connection.

30. Per-dialect session settings (from code):
- SQL Server: baseline ANSI `SET` batch (`SqlServerDialect.GetBaseSessionSettings`, `DefaultSessionSettings`)
- PostgreSQL: single `SET ... default_transaction_read_only=on/off` + baseline options
- MySQL/MariaDB: baseline `sql_mode` + `SET SESSION transaction_read_only = 1/0`
- Oracle: base `ALTER SESSION SET NLS_DATE_FORMAT...`; read-only enforced at tx start (`SET TRANSACTION READ ONLY`)
- CockroachDB: PostgreSQL baseline + `SET client_encoding='UTF8'; SET lock_timeout='30s';`
- YugabyteDB: PostgreSQL baseline + same encoding/lock timeout overrides
- SQLite: `PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL;`
- DuckDB: empty session SQL (read-only via connection string)
- Firebird: `SET NAMES UTF8; SET SQL DIALECT 3;`
- Snowflake: single `ALTER SESSION SET ...` batch
- TiDB: MySQL baseline + `SET tidb_pessimistic_txn_default = ON;`

31. Read-only vs read-write differentiation:
- Through `GetFinalSessionSettings(readOnly)` and separate cached read-only/write strings.
- Some providers use connection-string read-only parameters instead (SQLite/DuckDB/SQL Server routing hint).

32. Single batch vs multi-roundtrip:
- Framework sends one command text per session setup call.
- Dialect text may contain multiple statements; intended as 1 command/RTT where provider allows.

33. Separate metrics?
- Yes: session init duration via `RecordSessionInitDuration`, separate from command duration.

34. Can caller mutate session state?
- Yes, caller can execute arbitrary SQL (`SET`, `PRAGMA`, etc.) through containers/commands. Framework re-applies baseline on new connection first-open, not after every statement.

## Transactions

35. Abstractions/lifecycle:
- Interface: `ITransactionContext`.
- Impl: `TransactionContext` created via `DatabaseContext.BeginTransaction*`.
- Holds pinned connection + transaction; commit/rollback/savepoints; auto-rollback on dispose if incomplete.

36. Inside transaction ownership:
- Connection owner: `TransactionContext` (pinned `_connection`).
- Transaction owner: `TransactionContext` (`_transaction`).
- Release deferred until commit/rollback/dispose finalizers.
- Metrics stop in `CompleteTransactionMetrics` when completion/dispose happens.

37. Nested transactions?
- Real nested: rejected (`BeginTransaction*` on `TransactionContext` throws).
- Savepoints: supported if dialect supports savepoints.

38. Prevent accidental release of tx-owned connection:
- `TransactionContext.CloseAndDisposeConnection*` no-ops for reference-equal pinned connection.
- `SqlContainer.Cleanup` also avoids context release when context is `TransactionContext`.

39. Read-only transactions enforced?
- Best-effort per dialect via `TryEnterReadOnlyTransaction*` plus context/read-only assertions.
- Strongness varies by provider (e.g., Oracle explicit tx SQL, SQL Server relies more on permissions/intent).

40. Isolation abstraction/validation:
- `IsolationProfile` maps to `IsolationLevel` in context APIs.
- Transaction creation adjusts provider-specific constraints (e.g., Cockroach forced Serializable, DuckDB provider default path).

## SqlContainer

41. Full API/implementation locations:
- API: `pengdows.crud.abstractions/ISqlContainer.cs`.
- Implementation: `pengdows.crud/SqlContainer.cs`.

42. What it holds:
- SQL text builder (`SqlQueryBuilder`)
- parameter dictionary + render sequence/map
- query state flags (`HasWhereAppended`)
- context/dialect references
- command type is per-execution argument (not stored as persistent field)
- no explicit “expected result shape” object stored as a formal contract

43. Parameter construction:
- Manual creation: `CreateDbParameter*`, `AddParameter*`
- Entity metadata path exists in gateway binders (outside `SqlContainer`)
- Anonymous-object bulk binding is not a primary `SqlContainer` abstraction
- Provider-aware types via dialect mapping and provider-specific parameter handling

44. Holds provider resources?
- No persistent connection/command held across calls.
- Connections/commands are local variables in execution methods; reader path hands ownership to `TrackedReader`.

45. Shapes representable:
- scalar, non-query, single-row hint (`ExecuteReaderSingleRowAsync`), multi-row reader.
- Multiple result sets intentionally unsupported in tracked reader policy.

## Hydration

46. Optimized hydration pipeline:
- Entity hot path in `TableGateway.Reader.cs` + compiled mapper factory (`internal/CompiledMapperFactory.cs`).
- General mapper in `DataReaderMapper.cs` with plan cache.

47. Hydration mechanisms:
- Cached delegates and compiled expressions.
- Ordinal/result-shape mapping cached per schema hash.
- Not per-row reflection.

48. Null handling:
- `DBNull` checks and type coercion helpers (`TypeCoercionHelper`, mapper logic).

49. Provider type normalization:
- Dialect parameter/value prep + coercion options (`Provider = _dialect.DatabaseType`).
- Reader wrapper has provider quirks handling (notably Npgsql timestamp behavior).

50. Custom projections:
- Supports POCO/DTO projections through mapper/gateway mapping.
- Structs and constructor-bound immutable types depend on mapper path and configured mapping support (supported in generic mapper pipeline).

51. Column-to-member mapping rules:
- Attribute-driven (`[Column]`, keys/audit/version attributes) via type map registry.
- Name-based fallbacks and schema-based plan construction in mappers.

52. Ordinal lookups cached?
- Yes, per result shape/plan cache in mapper/gateway pipelines.

53. Rebuilt per command vs cached:
- Rebuilt: command instances, parameter value objects, some per-query text.
- Cached: type metadata (`TypeMapRegistry`), mapper plans, prepared statement/cache entries (when enabled), dialect/session cache.

54. Boxing/metadata lookup avoidance:
- Compiled delegates, cached accessors/plans, parameter cloning/copiers, query template caches.

55. Fast paths:
- Scalar paths in `SqlContainer`.
- Entity hydration fast path in `TableGateway` compiled mapper plan.
- Single-row command behavior hint path.

56. Hydration perf proof:
- Tests: `ReaderHydrationHotPathTests`, `DataReaderMapperFastPathTests`, `CompiledMapperOptimizationTests`.
- Benchmarks: `benchmarks/CrudBenchmarks/Internal/ReaderMappingBenchmark.cs`.

## Entity / TableGateway semantics

57. `[Id]`/`[PrimaryKey]` discovery:
- `TypeMapRegistry` builds `TableInfo` and marks special columns.

58. Rule enforcement locations:
- `[Id]` single-column enforced in `TypeMapRegistry.CaptureSpecialColumns` (`TooManyColumns` on multiple).
- `[PrimaryKey]` composite/order rules in `ValidatePrimaryKeys`.
- `[Id]` cannot also be PK enforced by `PrimaryKeyOnRowIdColumn` throw.
- Full CRUD-by-row-id requirements enforced in `TableGateway` methods that require `_idColumn`.

59. Violation behavior:
- Runtime exceptions during type-map build or operation invocation; no compile-time source-gen enforcement.

60. Lookup choice (`Id` vs `PrimaryKey`):
- `RetrieveOneAsync(TRowID)` uses row-id path.
- `RetrieveOneAsync(TEntity)` builds WHERE by primary key and throws if no PK.

61. Upsert conflict target:
- `TableGateway.ResolveUpsertKey()`:
  - primary keys first
  - else writable id
  - else throw

62. Native upsert support vs fallback:
- Uses capabilities from `DataSourceInfo`:
  - `SupportsMerge`, `SupportsInsertOnConflict`, `SupportsOnDuplicateKey`.
- If none, throws `NotSupportedException` (no generic emulation fallback here).

63. CRUD behavior by key shape:
- only `[Id]`: row-id CRUD works; entity PK lookup path unavailable.
- `[Id]` + `[PrimaryKey]`: both row-id and business-key flows available.
- extra unique indexes not declared `[PrimaryKey]`: not auto-used by gateway key logic.

64. `TableGateway` vs `EntityHelper` responsibility:
- `TableGateway` is canonical implementation/API.
- `EntityHelper` not present as active class in this repo (legacy alias removed).

## Dialect layer

65. Supported dialect classes:
- `SqlServerDialect`, `PostgreSqlDialect`, `CockroachDbDialect`, `YugabyteDbDialect`, `TiDbDialect`, `MySqlDialect`, `MariaDbDialect`, `SqliteDialect`, `OracleDialect`, `FirebirdDialect`, `DuckDbDialect`, `SnowflakeDialect`, fallback `Sql92Dialect`.

66. Dialect interface and implemented members:
- Interface: `pengdows.crud.abstractions/dialects/ISqlDialect.cs`.
- Implementations in `pengdows.crud/dialects/*.cs` + shared base `SqlDialect.cs`.

67. Dialect-owned behaviors (yes):
- quoting, parameter syntax, paging/limit SQL, identity retrieval, upsert rendering, read-only mechanisms, session settings, type/parameter shaping, transient/error classification hooks, isolation/read-only txn helpers.

68. Intentionally not abstracted:
- Raw SQL composition choices and business-level query semantics remain caller-visible.
- Some provider tuning choices are still context-level decisions.

69. Cockroach/Yugabyte handling:
- Separate dialect classes inheriting PostgreSQL dialect with overrides/capability differences.

70. Error classification (retry/deadlock/etc):
- Base `ISqlDialect.ClassifyException` default uses message heuristics (deadlock/serialization/constraint/timeout).
- Retry policy itself is not a full framework-level orchestration here.

71. Abstraction leaks:
- Some provider-specific checks exist outside pure dialect boundaries (connection strategy/init code, provider reflection/tuning).
- Not purely dialect-contained, but most SQL semantics are dialect-owned.

## Read-only enforcement

72. Implementation summary:
- Mix of connection-string enforcement, session SQL, and transaction-level SQL depending on dialect.

73. Per-provider mechanisms:
- SQL Server: connection string `ApplicationIntent=ReadOnly` (routing hint), plus permissions for hard enforcement.
- PostgreSQL: connection parameter options + session setting `default_transaction_read_only`.
- MySQL/MariaDB: session variable `transaction_read_only` and transaction entry helpers.
- Oracle: `SET TRANSACTION READ ONLY` at tx start; no persistent session read-only.
- SQLite: `Mode=ReadOnly` connection string (except memory).
- DuckDB: `access_mode=READ_ONLY` connection string.
- Firebird: read-only transaction/session SQL.
- Snowflake: no session-level read-only; relies on role/credentials.

74. Dual enforcement support:
- PostgreSQL/MySQL-family can combine connection/session/tx-level approaches.
- SQL Server dual hard-enforcement requires external permissions.

75. Where “marked read-only” decided:
- `DatabaseContext` read/write mode + optional dedicated read connection string.
- `ShouldUseReadOnlyForReadIntent`, dialect connection settings, and transaction read-only flag.

76. Can caller escalate read-only connection?
- Depends on provider and credentials.
- Framework blocks write APIs in read-only mode and read-only transactions, but SQL Server routing hint is not hard enforcement by itself.

## Metrics / observability

77. Emitted metrics:
- Connection open/close/hold/current/peak/long-lived
- Command success/fail/timeout/cancel durations + p95/p99
- Parameter maxima, rows read/affected
- Prepared/cached/evicted statement counters
- Transaction active/max/committed/rolled back duration + p95/p99
- Error categories (deadlock/serialization/constraint)
- Session init count/duration
- Pool governor snapshot metrics

78. Collection points:
- Governor: `PoolGovernor`
- Connection wrapper: `TrackedConnection`
- Command execution: `SqlContainer`
- Reader wrapper: `TrackedReader`
- Hydration: indirect via rows-read counts; no dedicated hydration timer metric

79. Distinguishable phases:
- checkout wait: yes (governor wait ticks)
- session setup: yes
- command exec: yes
- reader consumption: partially (rows + connection hold), no dedicated first-row metric
- total lease: approximate via connection hold/pool hold metrics
- timeout + exception categories: yes

80. Pluggability:
- Internal metrics collector + public snapshots/events (`MetricsUpdated`), not native OpenTelemetry/EventCounters out of the box.

81. Tags/dimensions:
- Read/write split by role metrics.
- Provider/db implied by context/dialect, but not a rich dynamic tag system in the metric model.

## Correctness / discipline

82. Raw provider resource escape points:
- Public abstractions intentionally expose wrappers and `CreateCommand(ITrackedConnection)`.
- `ITrackedConnection` extends `IDbConnection`; advanced callers can access raw ADO patterns through wrapper surface.

83. Extension points for custom SQL with governance:
- `IDatabaseContext.CreateSqlContainer` + execution methods.
- `TableGateway.BuildBaseRetrieve` and custom gateway inheritance patterns.

84. Custom hydration with wrapped readers:
- Yes: caller can consume `ITrackedReader` directly and map manually.

85. APIs allowing resource hold past intended scope:
- `ITrackedReader` and `ITrackedConnection` can be held by caller; lifecycle discipline is expected.

86. Manual ADO.NET interop status:
- Supported for advanced users via tracked wrappers; not forbidden, but higher-risk if caller mismanages scope.

## mint

87. How mint inspects metadata:
- Not answerable from this repository; `pengdows.poco.mint` source absent.

88. How mint decides `[Id]`/`[PrimaryKey]`/`[Column]`:
- Not answerable from this repository.

89. Business-key inference vs hints:
- Not answerable from this repository.

90. CLI/web shared generation core:
- Not answerable from this repository.

91. Non-interactive deterministic CI mode:
- Not answerable from this repository.

92. Dialect-difference handling in mint introspection:
- Not answerable from this repository.

## Tests / proof

93. Leak-related test evidence (examples):
- `ExecuteReaderWriteConnectionLeakTests.cs`
- `CancellationLeakTests.cs`
- `TransactionStreamingTests.cs`
- Wrapper tests in `pengdows.crud.Tests/wrappers/*`

94. PoolGovernor stress/burst tests:
- `PoolGovernorTests.cs`
- `PoolGovernorFairnessTests.cs`
- `PoolGovernorTurnstileTests.cs`
- `PoolGovernorDeadlineTests.cs`
- `PoolGovernorCancellationTests.cs`
- `PoolGovernorMetricsTests.cs`

95. Cross-provider behavior tests (examples):
- Dialect-specific suites in `pengdows.crud.Tests/*Dialect*Tests.cs`
- Read-only/session tests: `ReadOnlySessionSettingsTests.cs`, `SessionSettingsEnforcementTests.cs`, `DuckDbReadOnlyTransactionResetTests.cs`

96. Benchmarks present:
- Hydration: `benchmarks/CrudBenchmarks/Internal/ReaderMappingBenchmark.cs`
- Session setup and provider comparisons: `ViewPerformanceBenchmarks.cs`, `ConnectionPoolProtectionBenchmarks.cs`, other suites under `benchmarks/CrudBenchmarks/`
- No dedicated standalone benchmark named exactly “governor overhead”, but pool protection/concurrency benchmarks exercise related costs.

97. Asserted invariants vs “seems to work”:
- Many explicit invariants are tested (governor behavior, transaction completion semantics, read-only/session enforcement, key validation).
- Some system-level guarantees (global leak impossibility under all provider failures) remain empirical, not formally proven.

## Push on weak spots

98. Where abstraction leaks/gets ugly:
- `ITrackedConnection : IDbConnection` exposes low-level operations directly.
- Some provider-specific behavior leaks outside dialect (strategy/init/reflection paths).

99. Most provider-fragile parts:
- Prepare-statement behavior and provider reset semantics.
- Session-setting SQL assumptions and batching acceptance.
- Read-only enforcement semantics differences.

100. What likely breaks first under stress scenarios:
- Async streaming misuse: callers not disposing readers promptly.
- Provider upgrades: changed exception codes/messages, changed prepare/session behavior.
- Distributed SQL edges: lock/isolation semantics (Cockroach/Yugabyte/TiDB nuances).
- Very large result sets: consumer-side memory/throughput patterns, long-held leases.
- Transaction-heavy workloads: completion lock contention and provider-specific transaction limits.

## Primary evidence files

- Core context/lifecycle: `pengdows.crud/DatabaseContext*.cs`
- SQL execution: `pengdows.crud/SqlContainer.cs`
- Transactions: `pengdows.crud/TransactionContext.cs`
- Governor: `pengdows.crud/infrastructure/PoolGovernor.cs`, `PoolSlot.cs`
- Wrappers: `pengdows.crud/wrappers/TrackedConnection.cs`, `TrackedReader.cs`
- Type mapping/keys: `pengdows.crud/TypeMapRegistry.cs`, `TableGateway*.cs`
- Dialects: `pengdows.crud/dialects/*.cs`, `SqlDialectFactory.cs`
- Metrics: `pengdows.crud/internal/MetricsCollector.cs`, `pengdows.crud.abstractions/metrics/*.cs`, `pengdows.crud/metrics/*.cs`
- Representative tests: `pengdows.crud.Tests/*`
