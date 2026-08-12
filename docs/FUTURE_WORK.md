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

What's left:

### P1

- **Audit mutation before execution succeeds.** `SetAuditFields` mutates the in-memory entity
  during `Build*` (Create/Update), before the SQL has actually executed. If execution later
  fails, the entity's audit fields look like the write succeeded even though nothing was
  persisted. Fix requires reordering to resolve → bind → execute → apply-on-success, which
  touches the Build/Execute split fairly deeply.
- **Detection probes are still synchronous.** `DatabaseDetectionResult` records probe evidence
  now, but the underlying probes are still sync `ExecuteScalar()` calls reached from an `async`
  method that never actually awaits I/O. Async/cancellable detection was scoped as a
  `DatabaseContext.CreateAsync` factory and deliberately deferred — it requires rewriting the
  ~400-line private constructor into a shared async core the sync constructor also routes
  through, making the full test suite the regression gate for that rewrite.

### P2

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

- **TypeMapRegistry staleness / schema-invalidation policy.** Cached `TableInfo` never
  invalidates on a live schema change; requires a new context/process restart. Needs a
  deliberate decision (accept as documented constraint vs. add invalidation), not just a fix.
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
