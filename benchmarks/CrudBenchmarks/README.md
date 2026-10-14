# Crud Benchmarks

These benchmarks evaluate pengdows.crud as a database execution and governance layer—not
as a contest for the lowest mean time. Read the [benchmark taxonomy](./BENCHMARK-TAXONOMY.md)
before comparing rows: raw hot-path, equal-footing throughput, production lifecycle,
correctness stress, failure containment, governance, provider capability, and methodology
benchmarks have different winning outcomes.

Pengdows knowingly accepts some microbenchmark overhead for open-late/close-early
connections, write admission, reader fairness, dialect/session correctness, type-safe
mapping, and failure isolation. A faster unsafe arm is an intentional baseline, not an
overall winner.

## Prerequisites

### Docker (Required for some benchmarks)

Benchmarks use **Testcontainers** to automatically spin up and tear down database containers:

- **PostgreSQL benchmarks**: Automatic Testcontainers (postgres:15-alpine or postgres:16-alpine, depending on the class)
- **SQL Server benchmarks**: Automatic Testcontainers (SQL Server 2022)
- **MySQL benchmarks**: Automatic Testcontainers (mysql:8.0)

**No manual Docker setup required!** Each benchmark manages its own container lifecycle:

- Containers start automatically when the benchmark begins
- Containers stop automatically when the benchmark completes
- Multiple benchmarks run sequentially, not concurrently
- No port conflicts or container management needed

## Running Benchmarks

### Run all benchmarks

```bash
dotnet run -c Release
```

### Run specific benchmark suites

```bash
# SQL Generation (no database required)
dotnet run -c Release --filter "*SqlGenerationBenchmark*"

# Type handling (no database required; opt-in)
dotnet run -c Release -- --include-opt-in --filter "*TypeHandlingBenchmarks*"

# Cloning Performance (no database required)
dotnet run -c Release --filter "*CloningPerformanceTest*"

# PostgreSQL CRUD, equal footing (uses Testcontainers)
dotnet run -c Release --filter "*PostgreSqlEqualFooting*"

# SQL Server CRUD, equal footing (uses Testcontainers)
dotnet run -c Release --filter "*SqlServerEqualFooting*"

# Materialized views (requires PostgreSQL; opt-in)
dotnet run -c Release -- --include-opt-in --filter "*MaterializedViewPerformanceBenchmarks*"

# Database-specific features (requires PostgreSQL; opt-in)
dotnet run -c Release -- --include-opt-in --filter "*DatabaseFeatureBenchmarks*"

# MySQL default concurrency stress (opt-in benchmark)
dotnet run -c Release -- --include-opt-in --filter "*MySqlDefaultConcurrencyBenchmarks*"
```

### Opt-in only benchmarks (excluded from normal runs)

Mark a benchmark class with `[OptInBenchmark]` to keep it out of default runs.

```csharp
[OptInBenchmark]
public class MyExperimentalBenchmarks
{
    [Benchmark]
    public void Scenario() { }
}
```

Run it only when explicitly enabled:

```bash
dotnet run -c Release -- --include-opt-in --filter "*MyExperimentalBenchmarks*"
```

You can also enable opt-in classes via environment variable:

```bash
CRUD_BENCH_INCLUDE_OPT_IN=1 dotnet run -c Release -- --filter "*MyExperimentalBenchmarks*"
```

### Methodology benchmarks: what each measurement change does (opt-in)

`PostgreSqlMethodologyBenchmarks` runs the PostgreSQL equal-footing workload (ReadSingle, ReadList,
Create; 20 operations per invocation, reported per operation) under several jobs. Each methodology
change is the difference between two rows of the same benchmark:

| Job | Settings | Compare with | Shows |
|---|---|---|---|
| `Baseline` | 3 warmups, 10 iterations, workstation GC, nothing pinned (today's suite) | — | — |
| `Pinned` | Postgres container on its own cores (Docker cpuset), benchmark process on the rest (affinity) | `Baseline` | client/server CPU contention |
| `PinnedServerGc` | `Pinned` + server GC | `Pinned` | GC mode (production ASP.NET Core uses server GC) |
| `PinnedPrecise` | `Pinned`, iteration count chosen for a 1% relative error (15–100 iterations) | `Pinned` | whether 10 iterations is enough; read the CI columns |
| `PinnedLatency` | `Pinned` + `tc netem` delay in the container (only when `CRUD_BENCH_NETEM_MS` is set) | `Pinned` | the library's share of a round trip with real latency |

Categories: `ReadSingle`, `ReadList` and `Create` are the equal-footing raw-SQL cells (Dapper is the
baseline; EF Core appears unpooled, as in the main suite, and pooled). `ReadSingle-Api` and
`Create-Api` are a different workload, each framework's own API: pengdows `RetrieveOneAsync`/
`CreateAsync`, EF Core LINQ, `EF.CompileAsyncQuery`, `Add` + `SaveChanges`. Compare them with the
Dapper row of the matching raw-SQL category. `Error` is the half-width of the 99.9% confidence
interval, so each cell's interval is Mean ± Error: call two cells equal only where their intervals
overlap. (BenchmarkDotNet 0.14's `CiLower`/`CiUpper` columns print wrong values, so they aren't used.)

Every case also records what reached the server (`pg_stat_statements`, reset after warm-up). The
run writes `BenchmarkDotNet.Artifacts/results/sqlproof-report.md`: per case, the statements and their
call counts, and an Issues list for any case that sent more than one statement per operation or any
equal-footing cell whose statement differs from the others (identifier quoting, case, whitespace and
table qualifiers ignored).

```bash
dotnet run -c Release -f net10.0 -- --include-opt-in --filter "*PostgreSqlMethodology*"
# one workload only:
dotnet run -c Release -f net10.0 -- --include-opt-in --filter "*PostgreSqlMethodology*" --anyCategories ReadSingle ReadSingle-Api
# cores for the database (default 2, the highest-numbered); add the latency job:
CRUD_BENCH_DB_CORES=2 CRUD_BENCH_NETEM_MS=0.5 dotnet run -c Release -f net10.0 -- --include-opt-in --filter "*PostgreSqlMethodology*"
```

Without `CRUD_BENCH_NETEM_MS`, every job runs over loopback, which has no network latency, so
library overhead looks larger relative to the total than it would in production. The latency job
installs `iproute2-tc` in the container (needs network access for `apk`) and adds `NET_ADMIN`.
Run out of process (not `CRUD_BENCH_INPROC`): affinity and GC mode apply to BenchmarkDotNet's child
process.

### Run with custom iterations

```bash
dotnet run -c Release -- --job short  # Fewer iterations, faster
dotnet run -c Release -- --job long   # More iterations, more accurate
```

## Benchmark Categories

### No Database Required

- **SqlGenerationBenchmark**: SQL query generation performance
- **TypeHandlingBenchmarks**: AdvancedTypeRegistry parameter configuration and CoercionRegistry type conversion (opt-in)
- **CloningPerformanceTest**: SqlContainer cloning vs traditional approach (opt-in)

### SQLite (In-Memory / File)

- **HydrationHotPathBenchmarks**: Pure row materialization hot-path comparison (100, 1,000, 5,000 rows) isolating mapping speed from connection acquisition
- **SQLiteWriteContentionBenchmarks**: Concurrency resilience benchmark stressing 100 concurrent writers under aggressive `busy_timeout=10ms` locks
- **EqualFootingCrudBenchmarks**: CRUD against a shared in-memory database for pengdows.crud (SingleWriter), Dapper and EF Core, with three Dapper writer patterns (see "Reading the Dapper arms")
- **SqliteFileWriterBenchmarks**: the cost question against a real WAL file: one write or read per call, for per-operation Dapper, a pinned writer, a single connection, a `Channel` writer queue and pengdows.crud. Set `CRUD_BENCH_DB_DIR` to a directory on the disk you mean to measure: the OS temp directory is RAM (tmpfs) on many Linux machines
- **SqliteFileWriterStormBenchmarks**: the survival question against the same file: a 100-writer transaction storm and a writers-plus-readers mix, with per-operation latency (P50/P95/P99/max) in a sidecar file next to the results
- **SqliteFileWriterPoisonBenchmarks**: the writer's connection dies mid-run (fakeDb); read the Fails column, not the times

### DuckDB (file)

- **DuckDbEqualFootingBenchmarks**: CRUD against a DuckDB file; pengdows.crud in `Standard` and in `SingleWriter` (what `Best` selects), against the same three Dapper patterns
- **DuckDbEfCoreBenchmarks**: net10-only Skirrels `DuckDB.EFCoreProvider` comparison through `pengdows.stormgate.EntityFrameworkCore`; covers uncompiled LINQ, compiled LINQ, list reads, and `SaveChanges`

Run the provider comparison separately:

```bash
dotnet run -c Release --project benchmarks/DuckDbEfCoreBenchmarks -- --filter '*DuckDbEfCoreEqualFootingBenchmarks*'
```

The EF results are intentionally isolated because Skirrels' provider requires EF Core 10; compare them with the DuckDB pengdows/Dapper rows from `DuckDbEqualFootingBenchmarks` on the same machine.
- **DuckDbReadBenchmarks**: reads only; the plain Dapper arm opens a connection per call like pengdows `Standard`, and `*_DapperPinnedConnection` reuses one open connection. The pengdows arm here builds its SQL container on every call, which the equal-footing classes avoid by reusing one, so its ratio is not comparable with theirs.

### SQL Server Required (Testcontainers - automatic)

- **SqlServerEqualFootingBenchmarks**: CRUD against SQL Server
- **SqlServerHydrationHotPathBenchmarks**: Network row hydration performance against SQL Server 2022
- **SqlServerConnectionGovernanceBenchmarks**: connection pressure against SQL Server (opt-in)

### PostgreSQL Required (Testcontainers - automatic)

- **PostgreSqlEqualFootingBenchmarks**: CRUD against PostgreSQL
- **DatabaseFeatureBenchmarks**: PostgreSQL features (ENUMs, JSONB, arrays, full text search; opt-in)
    - Note: Entity Framework comparisons will fail (NA results) - this is expected and demonstrates EF's limitations
- **MaterializedViewPerformanceBenchmarks**: Materialized-view strategies (opt-in)
- **ServerExecutionTimeBenchmarks**: server time versus client/DAL time (opt-in)
- **PostgreSqlConnectionGovernanceBenchmarks**, **PostgreSqlBackpressureBenchmarks**, **PostgreSqlMixedGovernanceBenchmarks**, **PostgreSqlSyncPoolStormBenchmarks**, **PeakConnectionsBenchmarks**: pool governance and admission under load (all but `PostgreSqlSyncPoolStormBenchmarks` are opt-in)

### Internal

- **IsolationBenchmarks**: Transaction isolation level handling (opt-in)

### MySQL Required (Testcontainers - automatic)

- **MySqlDefaultConcurrencyBenchmarks**: Finds error onset under default MySQL settings with:
  - Read-only concurrent load
  - Write-only concurrent load
  - Random mixed read/write/update load

## Notable Benchmarks

- **HydrationHotPathBenchmarks**: Pure row hydration throughput on SQLite, excluding connection acquisition (~35–36% faster than Dapper, ~50% lower allocations — see [results/hydration-hotpath-run-2026-08-13.md](./results/hydration-hotpath-run-2026-08-13.md); does not generalize to SQL Server, see [results/sqlserver-hydration-hotpath-run-2026-08-13.md](./results/sqlserver-hydration-hotpath-run-2026-08-13.md))
- **SQLiteWriteContentionBenchmarks**: 100-writer lock contention resilience (0 lock exceptions under SingleWriter turnstile vs 268 in Dapper and 348 in EF Core, measured on a shared-cache in-memory database; the Dapper arm there is uncoordinated, a Dapper app with its own writer lock would not race either, and against a WAL file on disk the same storm lost no writes and ran 5 to 6 times slower: see "What the cost buys")
- **PostgreSqlEqualFootingBenchmarks** / **SqlServerEqualFootingBenchmarks**: CRUD against Dapper and EF Core on a real server
- **IsolationBenchmarks**: Transaction isolation level performance (opt-in)
- **DatabaseFeatureBenchmarks**: Advanced PostgreSQL features (opt-in)

## Reading the Dapper arms (SQLite and DuckDB)

Dapper has no single-writer mode, so "the Dapper time" depends on what the application around it does. The
SQLite and DuckDB equal-footing classes therefore carry three Dapper arms, each a pattern found in real code
and libraries:

| Arm | What it does | Pattern |
|---|---|---|
| `*_Dapper` | A new connection per operation, no write coordination | The common default; SQLAlchemy 1.4 used it for file SQLite. Under concurrent SQLite writers it queues inside SQLite and the provider's retry loop, and fails when the queue outlasts the command timeout (see `SQLiteWriteContentionBenchmarks`, and "What the cost buys"). DuckDB documents concurrent in-process writers, so there it is a supported pattern |
| `*_DapperPinnedWriter` | One long-lived writer connection behind a `SemaphoreSlim(1)`; reads stay on per-operation connections | A pinned writer and a lock |
| `*_DapperSingleConnection` | The same one connection and lock for everything, reads included. Its writes are the `PinnedWriter` arms | One connection and a lock |
| `*_DapperWriterQueue` | A `Channel` drained by one background task that holds one connection (file-backed classes only) | EntityFrameworkCore.Sqlite.Concurrency, PepperX.SqliteGate |

Retry-on-exception is not benchmarked: it recovers from contention and does not prevent it. The pengdows.crud
arms are `*_Pengdows` (SQLite: `SingleWriter`; DuckDB: `Standard`) and, on DuckDB, `*_PengdowsSingleWriter`.

All results below: net10.0, mean per operation, library at `5887f286` plus the uncommitted 2026-10-13 SQLite
session-settings change, sources frozen for the whole run (net8.0 is within about 10% unless noted). Raw output is in
the git-ignored `BenchmarkDotNet.Artifacts/`; the numbers are recorded in
`results/sqlite-file-writer-patterns-run-2026-10-13.md`.

### Cost, sequentially (nothing fails, so time is comparable)

File database on disk (ZFS), WAL, every arm running the same session settings, `RecordCount=1`, microseconds:

| | Dapper per-op | Dapper pinned / single connection | Dapper writer queue | pengdows (SingleWriter) |
|---|---:|---:|---:|---:|
| write (own commit) | 10.8 | 6.4 | 10.1 | 11.6 (1.08x per-op) |
| read single row | 9.7 | 5.0 | n/a | 10.9 (1.12x per-op) |

Shared-cache memory database (no WAL), `RecordCount=1`, microseconds; the Dapper arms set `Foreign Keys=True`:

| operation | Dapper per-op | Dapper pinned | pengdows | pengdows / per-op | pengdows / pinned |
|---|---:|---:|---:|---:|---:|
| Create | 26.4 | 7.5 | 30.2 | 1.14 | 4.0 |
| Update | 24.8 | 6.3 | 26.1 | 1.05 | 4.1 |
| DeleteOnly (transaction, rolled back) | 26.6 | 6.8 | 33.7 | 1.26 | 5.0 |
| DeleteInsertCycle | 50.1 | 12.0 | 52.4 | 1.05 | 4.4 |
| ReadSingle | 25.6 | 6.7 | 28.5 | 1.12 | 4.3 |
| ReadList | 31.7 | 11.3 | 35.5 | 1.12 | 3.1 |
| FilteredQuery | 35.0 | 13.9 | 39.0 | 1.11 | 2.8 |
| Aggregate | 61.2 | 41.0 | 64.3 | 1.05 | 1.6 |

DuckDB file (run 2026-10-13 before the SQLite change, which does not touch DuckDB), microseconds:

| DuckDB, file | Dapper per-op | Dapper pinned | pengdows Standard | pengdows SingleWriter |
|---|---:|---:|---:|---:|
| Create | 466 | 349 | 473 | 471 |
| Update | 545 | 413 | 548 | 547 |
| Delete (insert + delete) | 886 | 667 | 895 | 897 |
| ReadSingle | 376 | 289 | 379 | 385 |
| Aggregate | 290 | 236 | 300 | 300 |

What the cost numbers say:

- **Against a per-operation Dapper doing the same work, pengdows.crud is 1.04x to 1.26x** (the delete is the highest, from its
  transaction) on SQLite, and within about 5% on DuckDB. Before the Dapper arms did the same session work, the SQLite
  gap read 1.13x to 1.42x: most of it was the per-checkout pragma, not the library.
- **A pinned connection is faster than any per-operation arm**: about 1.7x on a file in WAL, 3 to 4 times on a memory
  database (no commit to dilute the open), 18 to 25% on DuckDB. That is the price of the ephemeral lease; what it buys is below.
- **A commit is not what slows a write here.** With `synchronous = NORMAL` in WAL a commit is a write to the OS, not a sync
  (a write was 11 µs on ZFS). A `FULL` run would show real disk cost; pengdows.crud has no setting for it yet (SQLITE-006).
- **Multi-row reads favor pengdows.crud**: 0.89 to 0.93 times the per-operation Dapper time at 100 rows, with less allocation.

### What the cost buys

**Survival under a write storm** (100 writers, each a transaction of 50 updates), file database in WAL, `Default Timeout` 1 s
(busy_timeout 10) and 6 s (5000). Failed transactions are the Fails column; times are the whole storm in milliseconds:

| arm | failed | time, busy 10 | time, busy 5000 |
|---|---:|---:|---:|
| Dapper per-op, no coordination | 0 | 441 | 460 |
| Dapper pinned writer + gate | 0 | 72 | 71 |
| Dapper writer queue (Channel) | 0 | 71 | 70 |
| pengdows.crud (SingleWriter) | 0 | 76 | 76 |
| pengdows.crud, defaults (no overrides) | 0 | 76 | 76 |

**No arm lost a write against a WAL file at these settings.** The uncoordinated Dapper arm finished, but 5 to 6 times slower,
because its writers queue inside SQLite and the provider's retry loop instead of in front of it. The earlier headline
("Dapper loses ~400 of 5,000, pengdows 0", `results/sqlite-write-contention-run-2026-08-13.md`) was measured on a
**shared-cache in-memory database**, where a lock conflict is `SQLITE_LOCKED`, which, as SQLite documents it, the busy handler does
not retry; Microsoft's docs also discourage shared cache with WAL. Read that result as "fault injection that makes coordination
visible", and this one as what a WAL file does. Failures appear when the queue takes longer to drain than the command timeout.

**Writers and readers together** (20 writers x 20 single-row commits, 16 readers reading continuously), net8.0 latency sidecar,
busy_timeout 10:

| arm | total ms | write P99 / max (ms) | read P99 / max (ms) | reads completed |
|---|---:|---:|---:|---:|
| Dapper per-op | 832 | 150 / 603 | 0.04 / 4.5 | 214,741 |
| Dapper pinned writer | 59 | 3.1 / 7.1 | 0.29 / 6.9 | 5,390 |
| Dapper single connection | 56 | 0.88 / 4.4 | 0.88 / 4.4 | 335 |
| Dapper writer queue | 57 | 3.1 / 4.3 | 0.28 / 9.2 | 2,263 |
| pengdows.crud | 60 | 2.1 / 39 | 2.1 / 66 | 337 |

Coordinated writers finish in about 60 ms; the uncoordinated arm takes 14 times that and its writers wait up to 600 ms
(P99 150 ms). Allocation is not comparable in this class: the readers loop until the writers finish, so a slower arm runs
more reads. **pengdows.crud completed 337 reads against 5,390 for the pinned writer**: its writer turnstile holds readers
while writers wait (the same as a single connection), where in WAL a reader never blocks a writer. That protects writers from
starving, and it costs reader throughput in this workload; `EnableSingleWriterFairness = false` lets readers run alongside.
Whether the turnstile is worth it for WAL files is an open question (see FUTURE_WORK, SQLITE-008).

**When the writer's connection dies mid-run** (200 transactions, the connection severed during the 100th; fakeDb, so read the
Fails column, not the times), failed transactions:

| arm | failed |
|---|---:|
| Dapper pinned writer, no recovery | 100 |
| Dapper pinned writer with reconnect logic | 1 |
| pengdows.crud | 1 |

The reconnect arm and pengdows.crud end level for subsequent operations: the difference is the
reconnect code a hand-rolled pinned writer has to write and get right. Neither arm recovers the
transaction whose connection died; pengdows.crud has no pinned writer to rebuild, so the permit
and per-operation lifecycle remain usable for the next operation. `BrokenConnectionRecoveryTests`
asserts that behavior across the supported ephemeral connection modes; `SingleConnection` is
explicitly excluded because its database is connection-owned and cannot recover by opening a new
connection.

**When something changes the connection's session** (real SQLite, provider pool on): `PinnedWriterPoisoningTests`,
`SqliteQueryOnlySessionTests`, `SqliteSynchronousAndOptimizeTests`. A pinned Dapper writer keeps `query_only = ON` and
`foreign_keys = OFF` for good; per-operation Dapper inherits `query_only` from the pool; pengdows.crud puts back `foreign_keys`,
`query_only` and `synchronous` on every checkout.

## Results

Benchmark results are saved to `BenchmarkDotNet.Artifacts/results/` with multiple formats:

- `.md` - Markdown tables
- `.html` - HTML reports
- `.csv` - CSV data for analysis
- `-cross-framework-ratios.md` - Derived cross-framework ratios (`P÷D`, `EF÷P`) computed from Mean values

Notes on ratios:
- BenchmarkDotNet `Ratio` is baseline-relative within each benchmark group.
- Cross-framework ratios are emitted in the `-cross-framework-ratios.md` sidecar file.

## Notes

- **BenchmarkDotNet** runs in Release mode by default
- **Memory diagnostics** are enabled for allocation tracking
- **Testcontainers** automatically manage database container lifecycle
    - PostgreSQL: postgres:15-alpine or postgres:16-alpine (per class)
    - SQL Server: mcr.microsoft.com/mssql/server:2022-latest
    - MySQL: mysql:8.0
- **Dataset sizes** controlled by benchmark attributes (e.g., FilmCount=1000, ActorCount=200)
- **Container management**: Each benchmark starts its own container and cleans it up when done
- **No manual setup**: Just run the benchmarks, containers are handled automatically
- **Sequential execution**: Benchmarks run one at a time to avoid resource conflicts
- Some benchmarks may take several minutes to complete due to container startup and data seeding
