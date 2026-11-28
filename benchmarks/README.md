# Benchmarks

This directory contains performance benchmarks for pengdows.crud.

## 🎯 THESIS PROOF SUITE (RECOMMENDED)

**Evidence behind the product thesis (each benchmark reports what it measured; none is a proof):**

```bash
./run-thesis-proof.sh
```

Note: the script runs `BrokenMappingBenchmarks` and `EqualFootingCrudBenchmarks` only. The classes an earlier version
named (`ConnectionPoolStressBenchmarks`, `SqlGenerationSafetyBenchmarks`, `RawPerformanceComparison`) no longer exist in
`CrudBenchmarks`. The same two classes can be run directly:

```bash
dotnet run -c Release -f net10.0 --project CrudBenchmarks -- --filter "*BrokenMappingBenchmarks*" "*EqualFootingCrudBenchmarks*"
```

**What it proves:**

1. **Connection Pool Management** - no current benchmark. `ConnectionPoolProtectionBenchmarks` was removed on 2026-11-26
   (commit 28d5903a) because its rows were invalid; see [docs/connection-governance-investigation-2026-10.md](../docs/connection-governance-investigation-2026-10.md).
   Pressure evidence now comes from `SqliteLockHolderContentionBenchmarks` and `PortPressure` (below)

2. **SQL Generation Perfection** - Handles edge cases that break or complicate EF/Dapper
   - Column names with spaces ("First Name", "Last Name")
   - Reserved keywords ("select", "from", "where")
   - Automatic correct quoting via dialect system

3. **Raw Performance** — Depends on database
   - **SQLite in-memory** (`EqualFootingCrudBenchmarks`, 2026-10-03): 1.2x to 1.5x Dapper's time per operation (ReadSingle 1.41x, Create 1.51x; connection overhead dominates, no network I/O to amortize), 1.6x to 3.4x faster than EF Core (EF÷P 1.62 to 3.37 at RecordCount=1, [results/sqlite-equal-footing-run-2026-10-03.md](CrudBenchmarks/results/sqlite-equal-footing-run-2026-10-03.md)). Once the Dapper arm does the same per-checkout session work, the gap is 1.04x to 1.26x on a shared-cache memory database and 1.03x to 1.17x on a file database in WAL, both from [results/sqlite-file-writer-patterns-run-2026-10-13.md](CrudBenchmarks/results/sqlite-file-writer-patterns-run-2026-10-13.md) (see also [CrudBenchmarks/README.md](CrudBenchmarks/README.md))
   - **PostgreSQL (equal auto-prepare, equal footing, 2026-11-27 run)**: at parity with Dapper. pengdows ÷ Dapper is 0.959 to 1.031 across every scenario at 1, 10 and 100 records (reads 0.977 to 1.018, writes 0.959 to 1.023). Against EF Core's raw-SQL arm (the same statement Dapper sends) pengdows is 1.2x to 1.6x faster on reads and 1.1x to 1.3x faster on writes. The run's ratio report is git-ignored (`benchmarks/CrudBenchmarks/BenchmarkDotNet.Artifacts/results/CrudBenchmarks.PostgreSqlEqualFootingBenchmarks-20261127-045505-cross-framework-ratios.md`), so it is not checked in; the EF LINQ and compiled arms were not run in it
   - **The real win is query design**: `ReadList` (1 query, N=100 rows) = 187 µs for pengdows, 187 µs for Dapper and 267 µs for EF raw SQL; `ReadSingle×100` = 14,732 µs, 14,700 µs and 22,273 µs. That is about 79x for pengdows and Dapper and 84x for EF raw SQL (2026-11-27)
   - **Allocation cost**: about 1.1x Dapper's heap for one row (`ReadSingle` 3.57 KB vs 3.24 KB) and 1.15x for 100 single-row reads (291.5 KB vs 253.9 KB); EF raw SQL allocates 13x to 16x more than pengdows on `ReadSingle` (2026-11-27)
   - Compiled property setters (no reflection); plan caching, StringBuilderLite optimizations
   - Earlier committed PostgreSQL run: [results/postgres-run-2026-03-15-after-fix.md](CrudBenchmarks/results/postgres-run-2026-03-15-after-fix.md). Superseded for ratios: its Dapper arms left two columns unmapped until 2026-10-19 (see [results/row-scaling-2026-10-19.md](CrudBenchmarks/results/row-scaling-2026-10-19.md))

**Requirements:** None! Uses SQLite in-memory
**Runtime:** ~10-15 minutes

---

## Quick Start: Simple CRUD Comparison

For a quick, basic comparison of CRUD operations:

```bash
./run-simple-crud.sh
```

The script runs `*EqualFootingCrudBenchmarks*`, the same as:
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

BenchmarkDotNet output explained. The table below is an **illustrative layout, not a run**: the numbers are invented to show the columns and must not be quoted as results:

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

---

## Contention and port-pressure experiments

- **`CrudBenchmarks/SqliteLockHolderContentionBenchmarks.cs`:** a writer holds the SQLite lock for 2 s while each framework writes against a shared command timeout (0, 1, 5 s). Lost transactions per run: at 1 s, Dapper lost 15–40 of 50 and EF 13–38 across the 7 runs logged on 2026-11-26 (`CrudBenchmarks.SqliteLockHolderContentionBenchmarks-20261126-182057.log`); the 7 runs logged on 2026-11-27 (`...-20261127-045104.log`) gave Dapper 15–38 and EF 13–38. Pengdows lost 1 in every run of both. At 0 s and 5 s every arm committed everything; in the 2026-11-27 ratio report Pengdows' mean was 2.0 s against 3.9–4.3 s for Dapper and EF. These logs and reports are git-ignored (`BenchmarkDotNet.Artifacts/`) and not checked in. Run with `dotnet run -c Release -f net10.0 --project CrudBenchmarks -- --filter "*SqliteLockHolderContentionBenchmarks*"`.
- **`PortPressure/run-port-pressure.sh`:** a 1,000-way storm against a throwaway PostgreSQL 16 container (`max_connections=25`). **UNVERIFIED / not reproducible from committed artifacts.** The figures from the earlier probe (uncontrolled peak 1,716–1,861 client TIME_WAIT sockets over 3 runs; governed with the default pool 349–534 refusals per run; governed with the pool capped at 20, 0 refusals and peak 41–42) came from an uncommitted probe run. The script only prints to stdout and deletes its sample files on exit, and no result file is checked in. Needs Docker and `ss`; Linux only.
