# Benchmarks

This directory contains performance benchmarks for pengdows.crud.

## 🎯 THESIS PROOF SUITE (RECOMMENDED)

**Proves pengdows.crud's superiority in connection management, SQL safety, and performance:**

```bash
./run-thesis-proof.sh
```

**What it proves:**

1. **Connection Pool Management** - Standard mode is safer and better at handling pressure
   - Resists pool exhaustion under high concurrency
   - "Open late, close early" vs EF/Dapper's connection-holding patterns

2. **SQL Generation Perfection** - Handles edge cases that break or complicate EF/Dapper
   - Column names with spaces ("Fred Flintstone", "First Name")
   - Reserved keywords ("select", "from", "where")
   - Automatic correct quoting via dialect system

3. **Raw Performance** - Very close to Dapper (within 5-15%)
   - Compiled property setters (no reflection)
   - Plan caching, StringBuilderLite optimizations
   - Much faster than Entity Framework

**Requirements:** None! Uses SQLite in-memory
**Runtime:** ~10-15 minutes

---

## Quick Start: Simple CRUD Comparison

For a quick, basic comparison of CRUD operations:

```bash
./run-simple-crud.sh
```

**What it tests:**
- ✅ Create (single insert)
- ✅ Read single record
- ✅ Read list of records
- ✅ Update single record
- ✅ Delete single record
- ✅ Batch create (10 records)

**Requirements:** None! Uses SQLite in-memory
**Runtime:** ~2-3 minutes

---

## All Benchmarks

To run all benchmarks:

```bash
cd CrudBenchmarks
dotnet run -c Release
```

To run a specific benchmark:

```bash
cd CrudBenchmarks
dotnet run -c Release --filter "*BenchmarkName*"
```

### Available Benchmarks

#### 🎯 Thesis Proof Benchmarks (Recommended)

| Benchmark | Proves | Requirements |
|-----------|--------|--------------|
| **ConnectionPoolStressBenchmarks** | Standard mode handles pressure better than EF/Dapper | None (SQLite) |
| **SqlGenerationSafetyBenchmarks** | Perfect SQL generation for edge cases | None (SQLite) |
| **RawPerformanceComparison** | Performance parity with Dapper | None (SQLite) |

#### Other Benchmarks

| Benchmark | Description | Requirements |
|-----------|-------------|--------------|
| **SimpleCrudBenchmarks** | Basic CRUD comparison (pengdows vs Dapper vs EF) | None (SQLite) |
| **SqlGenerationBenchmark** | SQL generation and parameter creation performance | None (FakeDb) |
| **ReaderMappingBenchmark** | Reader mapping performance vs Dapper | DuckDB |
| **TypeHandlingBenchmarks** | Advanced type handling (JSONB, arrays, ranges, etc.) | None |
| **PerformanceOptimizationBenchmarks** | Specific optimization validations | None (FakeDb) |
| **RealWorldScenarioBenchmarks** | Complex PostgreSQL scenarios (ENUMs, JSONB, FTS) | Docker + PostgreSQL |
| **PagilaBenchmarks** | Real-world Pagila database scenarios | PostgreSQL + Pagila schema |
| **SqlServerBenchmarks** | SQL Server specific features | SQL Server |

---

## Benchmark Results Interpretation

BenchmarkDotNet output explained:

```
|                    Method |      Mean |    StdDev | Allocated |
|-------------------------- |----------:|----------:|----------:|
| Create_Single_Pengdows    |  1.234 ms |  0.045 ms |   1.23 KB |
| Create_Single_Dapper      |  1.456 ms |  0.052 ms |   1.45 KB |
| Create_Single_EF          |  2.345 ms |  0.089 ms |   3.12 KB |
```

- **Mean**: Average execution time (lower is better)
- **StdDev**: Standard deviation (lower = more consistent)
- **Allocated**: Memory allocated (lower is better)

**Baseline**: The benchmark marked with `Baseline = true` shows as `1.00` - others show relative performance (e.g., `1.5x` means 50% slower)

---

## Tips

**For quick comparisons:** Use `SimpleCrudBenchmarks` or `SqlGenerationBenchmark`

**For comprehensive analysis:** Use `RealWorldScenarioBenchmarks` (requires Docker)

**For specific features:** Use specialized benchmarks like `TypeHandlingBenchmarks` or database-specific ones

**To reduce runtime:** Decrease iteration counts in benchmark attributes:
```csharp
[SimpleJob(warmupCount: 1, iterationCount: 3)]  // Faster but less accurate
```
