# Benchmarks

This directory contains performance benchmarks for pengdows.crud.

## 🎯 THESIS PROOF SUITE (RECOMMENDED)

**Proves pengdows.crud's superiority in connection management, SQL safety, and performance:**

```bash
./run-thesis-proof.sh
```

Note: the script's `--filter` names `ConnectionPoolStressBenchmarks`, `SqlGenerationSafetyBenchmarks` and
`RawPerformanceComparison`, which no longer exist in `CrudBenchmarks`; until it is updated, run the classes that
carry these proofs directly:

```bash
dotnet run -c Release -f net10.0 --project CrudBenchmarks -- --filter "*ConnectionPoolProtectionBenchmarks*" "*BrokenMappingBenchmarks*" "*EqualFootingCrudBenchmarks*"
```

**What it proves:**

1. **Connection Pool Management** - Standard mode is safer and better at handling pressure
   - Resists pool exhaustion under high concurrency
   - "Open late, close early" vs EF/Dapper's connection-holding patterns

2. **SQL Generation Perfection** - Handles edge cases that break or complicate EF/Dapper
   - Column names with spaces ("Fred Flintstone", "First Name")
   - Reserved keywords ("select", "from", "where")
   - Automatic correct quoting via dialect system

3. **Raw Performance** — Depends on database
   - **SQLite in-memory** (`EqualFootingCrudBenchmarks`, 2026-10-03): 1.2x to 1.5x Dapper's time per operation (ReadSingle 1.41x, Create 1.51x; connection overhead dominates, no network I/O to amortize), 2.2x to 3.4x faster than EF Core; once the Dapper arm does the same per-checkout session work the gap is 1.04x to 1.26x (see [CrudBenchmarks/README.md](CrudBenchmarks/README.md))
   - **PostgreSQL (equal auto-prepare, equal footing)**: at parity with Dapper — within ±3% on reads, up to +8% on writes (Create 1.077); 1.5x faster than EF Core on reads, 1.1x to 1.2x faster on writes
   - **The real win is query design**: `ReadList` (1 query, N=100 rows) = 204 μs; `ReadSingle×100` = 15,666 μs — 77x difference, same for all three frameworks
   - **Allocation cost**: ~2x more heap than Dapper per operation; ~8x less than EF Core
   - Compiled property setters (no reflection); plan caching, StringBuilderLite optimizations
   - See [results/postgres-run-2026-03-15-after-fix.md](CrudBenchmarks/results/postgres-run-2026-03-15-after-fix.md) for the full equal-footing PostgreSQL run

**Requirements:** None! Uses SQLite in-memory
**Runtime:** ~10-15 minutes

---

## Quick Start: Simple CRUD Comparison

For a quick, basic comparison of CRUD operations:

```bash
./run-simple-crud.sh
```

The script filters on `*SimpleCrud*`, which matches no class today; the equivalent run is
`dotnet run -c Release -f net10.0 --project CrudBenchmarks -- --filter "*EqualFootingCrudBenchmarks*"`.

**What it tests** (`EqualFootingCrudBenchmarks`):
- ✅ Create (single insert)
- ✅ Read single record
- ✅ Read list of records
- ✅ Filtered query and aggregate
- ✅ Update single record
- ✅ Delete only, and delete + insert cycle

**Requirements:** None! Uses SQLite in-memory
**Runtime:** ~2-3 minutes

---

## All Benchmarks

To run all benchmarks:

```bash
cd CrudBenchmarks
dotnet run -c Release
```

To include opt-in benchmarks (excluded by default), add `--include-opt-in` after `--`:

```bash
cd CrudBenchmarks
dotnet run -c Release -- --include-opt-in
```

To run a specific benchmark:

```bash
cd CrudBenchmarks
dotnet run -c Release --filter "*BenchmarkName*"
```

To run the opt-in MySQL default concurrency stress benchmark:

```bash
cd CrudBenchmarks
dotnet run -c Release -- --include-opt-in --filter "*MySqlDefaultConcurrencyBenchmarks*"
```

### Available Benchmarks

#### 🎯 Thesis Proof Benchmarks (Recommended)

| Benchmark | Proves | Requirements |
|-----------|--------|--------------|
| **ConnectionPoolProtectionBenchmarks** | Standard mode handles pressure better than EF/Dapper | None (SQLite) |
| **BrokenMappingBenchmarks** | Correct SQL generation for edge-case names (spaces, reserved keywords) | None (SQLite) |
| **EqualFootingCrudBenchmarks** | Performance relative to Dapper and EF Core | None (SQLite) |

#### Other Benchmarks

Every class is described in [CrudBenchmarks/BENCHMARK-TAXONOMY.md](CrudBenchmarks/BENCHMARK-TAXONOMY.md) and
[CrudBenchmarks/README.md](CrudBenchmarks/README.md). A selection:

| Benchmark | Description | Requirements |
|-----------|-------------|--------------|
| **ApplesToApplesDapperBenchmarks** | Dapper vs pengdows with separated connection and mapping paths | None (SQLite) |
| **SqlGenerationBenchmark** | SQL generation and parameter creation performance | None (FakeDb) |
| **ReaderMappingBenchmark** | Reader mapping performance vs Dapper | DuckDB |
| **TypeHandlingBenchmarks** | Type handling performance (opt-in) | None |
| **PerformanceOptimizationBenchmarks** | Specific optimization validations (opt-in) | None |
| **DuckDbEqualFootingBenchmarks** | CRUD against a DuckDB file | None (DuckDB) |
| **PostgreSqlEqualFootingBenchmarks** | Equal-footing CRUD against PostgreSQL | Docker (Testcontainers) |
| **SqlServerEqualFootingBenchmarks** | Equal-footing CRUD against SQL Server | Docker (Testcontainers) |
| **DatabaseFeatureBenchmarks** | PostgreSQL ENUMs, JSONB, arrays, full text (opt-in) | Docker (Testcontainers) |
| **MySqlDefaultConcurrencyBenchmarks** | Error onset under default MySQL settings (opt-in) | Docker (Testcontainers) |

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

**For quick comparisons:** Use `EqualFootingCrudBenchmarks` or `SqlGenerationBenchmark`

**For comprehensive analysis:** Use `PostgreSqlEqualFootingBenchmarks` (requires Docker)

**For specific features:** Use specialized benchmarks like `TypeHandlingBenchmarks` or database-specific ones

**To reduce runtime:** Decrease iteration counts in benchmark attributes:
```csharp
[SimpleJob(warmupCount: 1, iterationCount: 3)]  // Faster but less accurate
```
