# Benchmark Taxonomy

These benchmarks are not one contest for the lowest mean time. They answer different
questions about a data-access layer. A result is meaningful only within its classification.

## The questions

| Classification | Primary question | Winning outcome |
|---|---|---|
| `RawHotPath` | What is the cost of a narrow operation when database lifecycle is removed or minimized? | Time and allocation, with no safety conclusion |
| `EqualFootingThroughput` | What does each framework cost for the same database operation? | Time, allocation, and server work |
| `ProductionLifecycle` | What happens when the DAL opens, governs, and releases resources correctly? | Bounded resource use and acceptable latency |
| `CorrectnessStress` | Does the workload complete correctly under contention? | Committed operations and zero unexpected failures |
| `FailureContainment` | What happens when a connection, pool, or operation fails? | Scope of failure and continued service |
| `Governance` | Does the DAL control admission, pool pressure, and connection lifetime? | Predictable queueing and bounded connections |
| `ProviderCapability` | Can the DAL express and execute database-specific behavior correctly? | Correct SQL and successful provider execution |
| `Methodology` | Which setup or measurement choice changes the result? | An explained, reproducible delta |

Speed is the primary outcome only for `RawHotPath` and `EqualFootingThroughput`. In the
other classifications, a faster result that loses writes, exhausts a pool, or cannot
recover is not a better result.

## Suite classifications

| Suite | Classification | What it establishes | Result interpretation |
|---|---|---|---|
| `SqlGenerationBenchmark` | `RawHotPath` | Cost of building SQL containers | Code-generation cost only; not end-to-end DAL performance |
| `CloningPerformanceTest` | `RawHotPath`, `ProductionLifecycle` | Reusing SQL containers versus rebuilding them | Supports the inspectable/reusable-container model |
| `HydrationHotPathBenchmarks` | `RawHotPath` | Pure row materialization | Mapping result only; connection acquisition is intentionally excluded |
| `SqlServerHydrationHotPathBenchmarks` | `RawHotPath`, `ProviderCapability` | Hydration with a real SQL Server | Provider/network-specific; does not generalize to SQLite |
| `ReadPathMicroBenchmarks` | `RawHotPath` | Narrow read-path implementation costs | Micro-cost evidence, not a DAL winner |
| `TrackedWrapperLifecycleBenchmarks` | `RawHotPath`, `FailureContainment` | Reader lease and disposal behavior | Lifecycle correctness is part of the result |
| `EqualFootingCrudBenchmarks` | `EqualFootingThroughput` | SQLite CRUD time and allocations across Pengdows, Dapper, and EF | Compare same operations; separately label unsafe/pinned arms |
| `PostgreSqlEqualFootingBenchmarks` | `EqualFootingThroughput` | CRUD cost against PostgreSQL | Network/server latency can compress client-framework differences |
| `SqlServerEqualFootingBenchmarks` | `EqualFootingThroughput` | CRUD cost against SQL Server | Include session-settings and provider costs in the production profile |
| `DuckDbEqualFootingBenchmarks` | `EqualFootingThroughput` | DuckDB CRUD across connection patterns | Show the cost of lifecycle choices; do not treat pinned connections as universally safe |
| `DuckDbEfCoreEqualFootingBenchmarks` (project `DuckDbEfCoreBenchmarks`) | `EqualFootingThroughput`, `Methodology` | Skirrels EF provider with ordinary and compiled EF paths | Provider/context overhead is the finding; StormGate is not a speed optimization |
| `DuckDbReadBenchmarks` | `EqualFootingThroughput`, `ProductionLifecycle` | DuckDB read patterns | The per-call SQL-building Pengdows arm is not comparable to the reused-container arm |
| `ApplesToApplesDapperBenchmarks` | `EqualFootingThroughput` | Dapper versus Pengdows with separated connection and mapping paths | Distinguishes DAL lifecycle cost from pure mapping cost |
| `BrokenMappingBenchmarks` | `FailureContainment`, `ProviderCapability` | Behavior and cost when mappings are incomplete or unusual | Correct failure is more important than a fast invalid result |
| `SQLiteWriteContentionBenchmarks` | `CorrectnessStress`, `Governance` | 100 concurrent writers under aggressive lock pressure | Count failures and latency; `busy_timeout` sensitivity is itself a result |
| `SqliteFileWriterStormBenchmarks` | `CorrectnessStress`, `ProductionLifecycle` | Real WAL-file writer/read storms | Compare committed work, p50/p95/p99/max latency, and queue behavior |
| `SqliteFileWriterBenchmarks` | `ProductionLifecycle`, `EqualFootingThroughput` | Sequential cost of per-operation, pinned, queued, and governed patterns | The small Pengdows overhead is the cost of not pinning a fragile writer |
| `SqliteFileWriterPoisonBenchmarks` | `FailureContainment` | A writer connection dying mid-run | A pinned writer that cannot recover is a failure mode, not a low-cost recovery strategy |
| `SqliteConcurrencyBenchmark` | `CorrectnessStress` | SQLite concurrent operation behavior | Report successful work and failures, not just elapsed time |
| `ConnectionPoolProtectionBenchmarks` | `Governance`, `CorrectnessStress` | Pool exhaustion, write storms, sustained pressure, and hold time | A bounded queue is preferable to unbounded connection pressure |
| `PeakConnectionsBenchmarks` | `Governance` | Peak connection demand under matched load | Compare resource shape, not only throughput |
| `PostgreSqlConnectionGovernanceBenchmarks` | `Governance`, `CorrectnessStress` | PostgreSQL connection governance under pressure | Pool behavior and correctness are primary outcomes |
| `PostgreSqlBackpressureBenchmarks` | `Governance`, `CorrectnessStress` | Backpressure under PostgreSQL load | Queueing is an intentional control, not automatically a regression |
| `PostgreSqlMixedGovernanceBenchmarks` | `Governance`, `CorrectnessStress` | Mixed read/write admission behavior | Evaluate fairness and bounded resource use |
| `SqlServerConnectionGovernanceBenchmarks` | `Governance`, `CorrectnessStress` | SQL Server connection pressure | Separate pool protection from query speed |
| `MySqlDefaultConcurrencyBenchmarks` | `CorrectnessStress`, `Governance` | Error onset under default MySQL concurrency settings | The onset point and failure class matter more than the fastest low-load row |
| `SqliteMethodologyBenchmarks` | `Methodology` | SQLite raw SQL, LINQ, pooled EF, compiled EF, and API paths | Shows what query compilation and context pooling actually change |
| `PostgreSqlMethodologyBenchmarks` | `Methodology` | PostgreSQL raw SQL, pooled EF, LINQ, compiled EF, and API paths | Explains provider, network, pooling, and query-pipeline effects |
| `DatabaseFeatureBenchmarks` | `ProviderCapability`, `EqualFootingThroughput` | JSON, arrays, full text, bulk upsert, and other provider features | Unsupported/invalid EF rows are capability evidence, not missing speed rows |
| `IsolationBenchmarks` | `ProviderCapability`, `ProductionLifecycle` | Isolation-level behavior and cost | Never trade away the requested consistency guarantee for time |
| `ServerExecutionTimeBenchmarks` | `Methodology` | Server time versus client/DAL time | Identifies where a measured difference actually occurs |
| `BenchmarkGuidParameters` | `ProviderCapability`, `RawHotPath` | GUID representation and parameter handling across providers | Type correctness and provider compatibility first |
| `CreateIdLeaseBenchmarks` | `RawHotPath`, `ProductionLifecycle` | ID allocation/lease overhead | Measures the lease mechanism, not end-to-end CRUD |
| `PoolGovernorAcquireBenchmarks` | `RawHotPath`, `Governance` | Admission-token cost | The token cost is the price of bounded concurrency |
| `SentinelHotPathBenchmarks` | `RawHotPath`, `ProductionLifecycle` | Sentinel/keep-alive path cost | Measures database-liveness policy overhead |
| `MaterializedViewPerformanceBenchmarks` | `ProviderCapability`, `EqualFootingThroughput` | Materialized-view strategies | Database strategy result, not a universal ORM ranking |
| `PostgreSqlSyncPoolStormBenchmarks` | `Governance`, `CorrectnessStress` | Synchronous pool saturation | Bounded pressure and failure behavior are primary |

## Arm profiles

The arm name is part of the result. It is not interchangeable with the framework name.

| Arm | Profile | What it is allowed to claim |
|---|---|---|
| Pengdows `Standard` | Production lifecycle | Open late/close early, governed pool, provider-aware session behavior |
| Pengdows `SingleWriter` | Production correctness under constrained writers | Write admission and bounded contention; includes intentional lease overhead |
| Dapper per-operation | Common but ungoverned | Low-load throughput only; stress results expose caller-owned coordination |
| Dapper pinned writer | Unsafe/conditional baseline | Demonstrates the speed benefit and failure/recovery cost of pinning |
| Dapper single connection | Unsafe/conditional baseline | Demonstrates connection reuse; does not represent recoverable writer governance |
| Dapper writer queue | Hand-built coordination baseline | Measures the code and lifecycle cost of rebuilding governance outside the DAL |
| EF ordinary | Standard EF path | Includes EF context/provider lifecycle and EF's internal query cache |
| EF compiled | Explicit query-pipeline optimization | Removes explicit query compilation overhead; does not remove context/connection work |
| EF pooled | Context-pooling methodology arm | Shows the benefit of pooling; not equivalent to compiled queries |
| EF `SaveChanges` | Unit-of-work write path | Measures tracking/change detection/persistence behavior, not query compilation |

## What every result summary should say

Every published result should answer these questions before presenting ratios:

1. What classification is this benchmark?
2. What correctness or governance invariant is being tested?
3. Which arms are production-valid and which are intentionally unsafe baselines?
4. What cost does the safer arm knowingly accept?
5. Were any operations lost, invalid, blocked, or unrecoverable?
6. Is the comparison measuring query execution, mapping, connection lifecycle, or all three?

The suite's thesis is not that Pengdows wins every timing row. It is that Pengdows pays
known, bounded costs to provide database-correct execution behavior and a complete DAL
contract, while remaining in the Dapper performance class for ordinary workloads.

## Internal benchmark families

The benchmarks under `Internal/` are intentionally narrower than the framework comparisons:

- `SqlGenerationBenchmark`, `ProcWrappingStrategyLookupBenchmarks`, and
  `ConnectionStringNormalizationBenchmarks` are `RawHotPath` implementation-cost tests.
- `TypeHandlingBenchmarks`, `SqliteNativeTypesBenchmarks`, `SqliteDateHandlingBenchmarks`,
  `ReaderMappingBenchmark`, and `UpsertCapabilityBenchmarks` are `ProviderCapability` and
  type/mapping tests.
- `TableGatewayClientOnlyBenchmarks`, `PerformanceOptimizationBenchmarks`, and
  `ValueTaskExecutionBenchmarks` are `RawHotPath` allocation/dispatch tests.
- `CreateAsyncAuditOverheadBenchmarks` is `ProductionLifecycle`: it measures the cost of
  audit correctness and state handling, not whether audit fields should exist.
- `IsolationBenchmarks` is `ProviderCapability` plus `ProductionLifecycle`: a weaker
  isolation guarantee is not an optimization.
- `CloningPerformanceTest` is `RawHotPath` plus `ProductionLifecycle`: it measures the
  reusable-container strategy.
