# Future Work

This document tracks features that have been designed or partially specified but are not yet
implemented. Items here are not roadmap commitments — they are recorded so the design thinking
is not lost and can be picked up when the need arises.

---

## Batch Operations

The current batch implementation (`TableGateway.Batch.cs`) handles chunked multi-row INSERT,
UPDATE, and UPSERT with automatic parameter-limit-aware splitting. The following extensions
were designed but not built.

### Provider-optimized bulk load

For very large datasets (tens of thousands of rows), native bulk-load protocols are 10–100×
faster than parameterized multi-row INSERT.

| Database | Mechanism | Notes |
|----------|-----------|-------|
| PostgreSQL | `COPY … FROM STDIN` (binary or CSV) | Requires `NpgsqlBinaryImporter` or `COPY` command |
| SQL Server | `SqlBulkCopy` | ADO.NET class; bypasses row-by-row binding entirely |
| DuckDB | `COPY … FROM` (CSV/Parquet/Arrow) | Analytical workloads; Arrow appender is fastest |
| Oracle | Array binding via `OracleCommand` | Set `ArrayBindCount`; avoids per-row round-trips |
| MySQL/MariaDB | `LOAD DATA INFILE` | Requires `LOCAL INFILE` server permission |
| Firebird | Batch API (Firebird 4.0+) | `FbBatchCommand`; older versions fall back to multi-row INSERT |

None of these are in scope for the current `BatchCreateAsync` / `BatchUpsertAsync` surface.
When added, they should sit behind the existing `Build`/`Execute` split so callers are not
forced to change call sites.

### `ContinueOnError` / partial-batch error handling

Currently, if any `ExecuteNonQueryAsync` call inside a batch loop throws, the exception
propagates immediately and remaining chunks are not executed. A `ContinueOnError` option
would collect per-chunk failures and return a structured result instead of throwing.

Sketch of the intended API:

```csharp
public record BatchError(int ChunkIndex, int StartRow, int EndRow, Exception Exception);

public record BatchResult(int RowsAffected, IReadOnlyList<BatchError> Errors);
```

The decision on whether to add this depends on whether callers actually need partial success
semantics. Most transactional use cases do not — a transaction wrapping the whole batch is
usually the right answer.

### Progress reporting

For long-running batches an `IProgress<BatchProgress>` callback was sketched:

```csharp
public record BatchProgress(int ChunksCompleted, int TotalChunks, int RowsAffected);
```

Would be passed as an optional parameter alongside `CancellationToken`. Low priority unless
a caller actually needs it — the cancellation token already lets the caller abort.

### Resumable / checkpointed batches

The idea: record which chunks completed successfully so a retry can skip them. Requires
stable chunk boundaries (deterministic ordering) and external state storage. Complex enough
that it probably belongs outside the library, in application code that calls `BuildBatchCreate`
and manages the resulting `IReadOnlyList<ISqlContainer>` directly.

### Streaming batch input

Accept `IAsyncEnumerable<TEntity>` instead of `IReadOnlyList<TEntity>` so callers can
generate entities lazily without materializing the full set first. Chunking would need to
buffer `N` rows at a time rather than pre-splitting the full list.

---

## Oracle

### Array binding
Oracle's `OracleCommand.ArrayBindCount` allows a single `ExecuteNonQuery` to insert N rows
with array-valued parameters, avoiding multi-row VALUES syntax entirely. More efficient than
INSERT ALL for large row counts. Requires ODP.NET (Managed or Unmanaged); not available via
the generic ADO.NET `DbProviderFactory` abstraction, so would need provider-specific code
paths.

### Batch UPDATE strategy
The base `SqlDialect.SupportsBatchUpdate` returns `false` for Oracle, meaning batch updates
fall back to one `UPDATE` per entity. PostgreSQL uses `UPDATE FROM VALUES` and SQL Server
uses `MERGE`; Oracle has no direct equivalent without either a global temporary table or
PL/SQL. Design work needed before implementation.

---

## Metrics integration for batch operations

The existing `MetricsCollector` tracks per-command parameter counts and execution times.
Batch operations currently show up as N individual command records. A batch-aware metrics
event (total rows, chunk count, total duration) would make the dashboards more useful for
diagnosing batch throughput.

---

## OpenTelemetry metrics adapter

`pengdows.crud.opentelemetry` bridges `IDatabaseContext.Metrics`/`MetricsUpdated` into
OpenTelemetry without adding an OpenTelemetry dependency to the core package. Built.

Still open: it exports approximate P95/P99 as gauges rather than raw duration
samples/histograms — see the review below.

---

## Open items from architecture/DAL-comparison review (2026-08-12)

A broader review (isolation semantics, session init, pool governance, audit/lifecycle,
detection, metrics, locking, benchmarking) surfaced a longer list of findings. Everything at P0,
and most of P1, was fixed directly in the same session: isolation `Degraded` truthfulness for
TiDB/Snowflake, SingleWriter turnstile fairness (was gated on a pool-identity hash that never
matched in the common case), session-init fail-closed (`SessionInitializationFailureMode`),
`TenantContextRegistry.MaxTenantCount` TOCTOU race, `PoolGovernor` bounded queue depth
(`MaxQueuedReads`/`MaxQueuedWrites`), audit setters ignoring `[NonUpdateable]`/`[NonInsertable]`,
`AuditCreationPolicy`, `DatabaseDetectionResult` evidence, `IDataSourceInformation.ParsedVersion`,
`ParameterMarkerPattern` (was always empty), an opt-in `EnforceUniqueConnectionString` guard
against two contexts sharing a physical pool, and a transaction reader-lock-lifetime bug found
while tracing the turnstile fix (a `TransactionContext`'s serialization lock was released while
a reader it had opened was still active).

One claimed item checked out as already false against current `main` and needed no fix: the
review's "metrics avg-vs-P95/P99 population mismatch" — `MetricsCollector.RecordCommandDuration`
already feeds the average and the percentile ring from the same success-only population, with a
separate `_failedCommandDuration` EWMA for failures, specifically to keep `Avg`/`P95`/`P99`
describing one consistent population. Also fixed while auditing the locking architecture: stale
comments in `RealAsyncLocker.cs`/`TrackedConnection.cs` that still described `RealAsyncLocker` as
used for SingleWriter mode — it isn't; SingleWriter serializes writes via `PoolGovernor` over
ephemeral, NoOp-locked connections, with no persistent connection to lock.

A second item ("TypeMapRegistry staleness / schema-invalidation policy") turned out to be a
non-issue on closer inspection and was removed rather than listed as open: `TypeMapRegistry`
caches `TableInfo` keyed by the .NET `Type` object and builds it purely from that type's
attributes via reflection — it never reads live database schema, so there's nothing for it to
go stale relative to. Compiled attributes are immutable for a `Type`'s lifetime, so a cache hit
is correct forever; changing a POCO's attributes requires recompiling, which produces a new
process with fresh types anyway. The only way to defeat this is deliberately minting a *new*
`Type` for the *same* logical table repeatedly at runtime (hot-reload/dynamic codegen) instead
of reusing one — a self-inflicted anti-pattern, not a library gap — and even then the cache size
is bounded by how many tables the app actually uses.

What's left:

### P1

- **Audit mutation before execution succeeds.** `SetAuditFields` mutates the in-memory entity
  during `Build*` (Create/Update), before the SQL has actually executed. If execution later
  fails, the entity's audit fields look like the write succeeded even though nothing was
  persisted. Fix requires reordering to resolve → bind → execute → apply-on-success, which
  touches the Build/Execute split fairly deeply.
- **Detection probes are still synchronous — partially fixed.** Split into three phases by risk:
  - Phase 1 (done): `DatabaseDetectionService` now has genuine async twins
    (`DetectProductAsync`/`DetectFromConnectionAsync`/`DetectFromConnectionWithDetailAsync`/
    `DetectFlavorWithDetailAsync`) using `DbCommand.ExecuteScalarAsync` for the round-trip probes
    (Aurora/TiDB/Yugabyte/Cockroach), with `OperationCanceledException` propagating un-wrapped per
    the project's exception-hierarchy convention. Covered by
    `DatabaseDetectionServiceAsyncTests.cs`, which proves the async path is genuinely used (not a
    sync fallback) via a command double whose `ExecuteScalar()` throws and whose
    `ExecuteScalarAsync()` answers the probes. `GetSchema()` stays sync — no true async ADO.NET
    equivalent exists.
  - Phase 2 (not started): thread this through `SqlDialectFactory.CreateDialectAsync` and
    `IConnectionStrategy.HandleDialectDetectionAsync` (new async method on the internal strategy
    interface, implemented across Standard/SingleConnection/KeepAlive) instead of the current
    `.GetAwaiter().GetResult()` wrapper. Still no public API impact.
  - Phase 3 (not started, the risky part): expose it via a `DatabaseContext.CreateAsync` factory.
    Requires rewriting the ~400-line private constructor into a shared async core the sync
    constructor also routes through, making the full test suite the regression gate for that
    rewrite. Deliberately deferred on its own merits, independent of Phases 1–2 being done.

### P2

- **SQL Server pays a live session-settings SET round trip on every operation under
  `DbMode.Standard`, unlike PostgreSQL.** Quantified by
  `benchmarks/CrudBenchmarks/results/sqlserver-equal-footing-run-2026-08-13.md`: under
  `DbMode.Standard` (a fresh ephemeral connection per operation), pengdows is consistently
  ~1.4-2.0x slower than Dapper (and, unusually, slower than EF Core too) against SQL
  Server, while PostgreSQL and SQLite show parity or better. Root cause traced end to end:
  `TrackedConnection`'s session-settings callback is gated by a per-*wrapper-instance* flag
  (`_wasOpened`), not a per-*physical-connection* flag, so in `DbMode.Standard` every
  logical checkout re-triggers it — regardless of whether the ADO.NET pool handed back a
  warm connection. PostgreSQL avoids this entirely via
  `PostgreSqlDialect.PrepareConnectionStringForDataSource`, which bakes the same settings
  into the Npgsql `NpgsqlDataSource`'s startup `Options` as GUC defaults that `RESET ALL`
  restores automatically on pool return — `SqlServerDialect` has no equivalent override,
  so the bake-and-skip path (`_rwSettingsBakedIntoDataSource`) never applies to it.

  **Important scoping, confirmed by a follow-up benchmark**
  (`sqlserver-hydration-hotpath-run-2026-08-13.md`): the 1.4-2.0x figure is close to a
  worst case for amortization — many small, independent, ephemeral-connection operations,
  each re-paying the full tax. With the session-init cost paid once instead of per
  operation (`DbMode.SingleConnection`, mirroring `HydrationHotPathBenchmarks.cs`'s
  SQLite normalization), the gap drops to 1.18x at 100 rows and **1.025x at 5,000 rows** —
  pengdows's actual row-materialization work is close to Dapper's; the large multiplier is
  specifically a property of `DbMode.Standard` under a workload of small, independent
  operations, not a general statement about the SQL Server execution path. Whether an
  equivalent bake-in is even possible for `DbMode.Standard` on SQL Server (TDS/`SqlClient`
  has no direct analog to Postgres's arbitrary `Options=-c key=value` mechanism) still
  needs its own investigation before deciding whether the Standard-mode cost is fixable or
  an inherent constraint — but it's a narrower problem than "SQL Server is broadly
  slower."
- **Reader latency doesn't distinguish database time from consumer time.** `ExecuteReaderAsync`
  metrics treat the command as complete once the provider returns the reader; time spent by the
  caller consuming rows isn't separated out. Proposed: execute→first-row, first-row→dispose, and
  total reader lease as three distinct timings.
- **Metric cardinality policy for dynamic multi-tenancy.** No deliberate policy yet for
  context/tenant-derived tags (e.g. `db.name`) that could become high-cardinality.
- **No multi-result-set support.** `TrackedReader.NextResult()` throws `NotSupportedException`
  by design; a real feature gap relative to some competitors, not a correctness bug.
- **Stored-procedure multi-result/OUT parameter handling** is less complete than the best
  specialized competitors.
- **Provider driver-version compatibility matrix.** Database-engine coverage is strong; testing
  across multiple meaningful driver releases (Npgsql, SqlClient, MySqlConnector/MySql.Data,
  Oracle providers, etc.) is not.
- **More mutation/fuzz/state-machine testing**, particularly around parameter rendering,
  connection lifecycle, transactions, cancellation, and mapping/coercion.
- **SingleWriter fairness torture test.** The turnstile activation bug is fixed and covered by a
  unit test (`SingleWriterTurnstileActivationTests.cs`), but there's no long-running stress test
  proving writers don't starve under continuous concurrent readers against a real SQLite file.
- **Broader transaction concurrency stress testing.** The specific reader-lock-lifetime gap is
  now covered (`TransactionReaderLockLifetimeTests.cs`), but general multi-threaded torture
  testing of the no-op/real/reusable locker architecture doesn't exist yet.

### P3

- **One immutable capability snapshot.** Capability truth is still spread across
  dialect/detection/context structures rather than one typed, inspectable surface.
- **Benchmark process issues** (misleading `Fails=0` reporting under contention, correctness
  sidecar files not surviving BenchmarkDotNet artifact cleanup) — lives in `benchmarks/`,
  separate from the core library, not touched by this review.
- **Documentation lag** — connection-mode semantics (Standard/SingleWriter/SingleConnection/
  KeepAlive), generated/tested capability tables, and the `crud`-naming/positioning problem (the
  name undersells that this is also an execution-policy/runtime layer).
- **TiDB/MySql.Data prepare workaround** lacks a version number or upstream issue reference in
  its source comment, making it hard to know when the workaround can safely be removed.
