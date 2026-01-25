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
```

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

### SQL Server Required (Testcontainers - automatic)

- **IndexedViewBenchmarks**: Indexed view performance
- **AutomaticViewMatchingBenchmarks**: SQL Server query optimizer view matching
- **SqlServerBenchmarks**: SQL Server specific features
- **MaterializedViewBenchmarks**: Materialized view patterns

### PostgreSQL Required (Testcontainers - automatic)

- **DatabaseSpecificFeatureBenchmarks**: PostgreSQL features (JSONB, arrays, FTS, geospatial)
    - Note: Entity Framework comparisons will fail (NA results) - this is expected and demonstrates EF's limitations
- **PagilaBenchmarks**: Real-world dataset benchmarks

### Multiple Databases

- **RealWorldScenarioBenchmarks**: Common CRUD scenarios across databases
- **IsolationBenchmarks**: Transaction isolation level handling

## Notable Benchmarks

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

