# Crud Benchmarks

Performance benchmarks for pengdows.crud comparing against Dapper and Entity Framework.

## Prerequisites

### Docker (Required for some benchmarks)

Benchmarks use **Testcontainers** to automatically spin up and tear down database containers:

- **PostgreSQL benchmarks**: Automatic Testcontainers (postgres:15-alpine)
- **SQL Server benchmarks**: Automatic Testcontainers (SQL Server 2022)

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

# Advanced Types (no database required)
dotnet run -c Release --filter "*AdvancedTypeBenchmarks*"

# Cloning Performance (no database required)
dotnet run -c Release --filter "*CloningPerformanceTest*"

# PostgreSQL CRUD (uses Testcontainers)
dotnet run -c Release --filter "*Pagila*"

# Indexed Views (requires SQL Server)
dotnet run -c Release --filter "*IndexedView*"

# Automatic View Matching (requires SQL Server)
dotnet run -c Release --filter "*AutomaticViewMatching*"

# Database-Specific Features (requires PostgreSQL)
dotnet run -c Release --filter "*DatabaseSpecificFeatureBenchmarks*"

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
- **AdvancedTypeBenchmarks**: Custom type handling (Inet, Range, Geometry, etc.)
- **CloningPerformanceTest**: SqlContainer cloning vs traditional approach
- **WeirdTypeCoercionBenchmarks**: Edge case type conversions

### SQLite (In-Memory / File)

- **HydrationHotPathBenchmarks**: Pure row materialization hot-path comparison (100, 1,000, 5,000 rows) isolating mapping speed from connection acquisition
- **SQLiteWriteContentionBenchmarks**: Concurrency resilience benchmark stressing 100 concurrent writers under aggressive `busy_timeout=10ms` locks

### SQL Server Required (Testcontainers - automatic)

- **IndexedViewBenchmarks**: Indexed view performance
- **AutomaticViewMatchingBenchmarks**: SQL Server query optimizer view matching
- **SqlServerBenchmarks**: SQL Server specific features
- **SqlServerHydrationHotPathBenchmarks**: Network row hydration performance against SQL Server 2022
- **MaterializedViewBenchmarks**: Materialized view patterns

### PostgreSQL Required (Testcontainers - automatic)

- **DatabaseSpecificFeatureBenchmarks**: PostgreSQL features (JSONB, arrays, FTS, geospatial)
    - Note: Entity Framework comparisons will fail (NA results) - this is expected and demonstrates EF's limitations
- **PagilaBenchmarks**: Real-world dataset benchmarks

### Multiple Databases

- **RealWorldScenarioBenchmarks**: Common CRUD scenarios across databases
- **IsolationBenchmarks**: Transaction isolation level handling

### MySQL Required (Testcontainers - automatic)

- **MySqlDefaultConcurrencyBenchmarks**: Finds error onset under default MySQL settings with:
  - Read-only concurrent load
  - Write-only concurrent load
  - Random mixed read/write/update load

## Notable Benchmarks

- **HydrationHotPathBenchmarks**: Pure row hydration throughput on SQLite, excluding connection acquisition (~35–36% faster than Dapper, ~50% lower allocations — see [results/hydration-hotpath-run-2026-08-13.md](./results/hydration-hotpath-run-2026-08-13.md); does not generalize to SQL Server, see [results/sqlserver-hydration-hotpath-run-2026-08-13.md](./results/sqlserver-hydration-hotpath-run-2026-08-13.md))
- **SQLiteWriteContentionBenchmarks**: 100-writer lock contention resilience (0 lock exceptions under SingleWriter turnstile vs 268 in Dapper and 348 in EF Core)
- **PagilaBenchmarks**: Basic CRUD operations vs Dapper/EF using PostgreSQL
- **IndexedViewBenchmarks**: Demonstrates pengdows.crud's indexed view advantages over EF
- **SqlServerBenchmarks**: SQL Server specific features and optimizations
- **IsolationBenchmarks**: Transaction isolation level performance
- **DatabaseSpecificFeatureBenchmarks**: Advanced PostgreSQL features

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
    - PostgreSQL: postgres:15-alpine
    - SQL Server: mcr.microsoft.com/mssql/server:2022-latest
- **Dataset sizes** controlled by benchmark attributes (e.g., FilmCount=1000, ActorCount=200)
- **Container management**: Each benchmark starts its own container and cleans it up when done
- **No manual setup**: Just run the benchmarks, containers are handled automatically
- **Sequential execution**: Benchmarks run one at a time to avoid resource conflicts
- Some benchmarks may take several minutes to complete due to container startup and data seeding
