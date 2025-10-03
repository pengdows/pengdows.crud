# Crud Benchmarks

Performance benchmarks for pengdows.crud comparing against Dapper and Entity Framework.

## Prerequisites

### Docker (Required for some benchmarks)

- **PostgreSQL benchmarks**: Use Testcontainers (automatic)
- **SQL Server benchmarks**: Use docker-compose or custom instance

```bash
# Start SQL Server container for benchmarks
docker-compose up -d

# Wait for SQL Server to be ready (about 30 seconds)
docker-compose ps

# Stop SQL Server when done
docker-compose down
```

Or set a custom connection string:

```bash
export SQLSERVER_CONNECTION_STRING="Server=your-server;Database=master;User Id=sa;Password=YourPassword;TrustServerCertificate=true;"
```

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

### SQL Server Required (Docker or custom instance)
- **IndexedViewBenchmarks**: Indexed view performance
- **AutomaticViewMatchingBenchmarks**: SQL Server query optimizer view matching
- **SqlServerBenchmarks**: SQL Server specific features
- **MaterializedViewBenchmarks**: Materialized view patterns

### PostgreSQL Required (Testcontainers - automatic)
- **DatabaseSpecificFeatureBenchmarks**: PostgreSQL features (JSONB, arrays, FTS, geospatial)
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

- BenchmarkDotNet runs in Release mode by default
- Memory diagnostics are enabled for allocation tracking
- PostgreSQL benchmarks use Testcontainers with postgres:15-alpine
- Dataset sizes controlled by attributes: FilmCount=1000, ActorCount=200
- SQL Server benchmarks now support Docker (cross-platform)
- Insert benchmarks perform insert+delete to maintain stable dataset sizes
- Some benchmarks may take several minutes to complete

