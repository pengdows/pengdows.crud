# Performance Evaluation: pengdows.crud 2.0

**Benchmark environment:** Ubuntu 24.04.4 LTS · AMD Ryzen 9 5950X (8 cores) · .NET 8.0.24 · BenchmarkDotNet v0.14.0

**Reproducing:** No `run-benchmarks.sh` exists in this repo — run `dotnet run -c Release --project benchmarks/CrudBenchmarks` directly, or use one of the existing scripts in `benchmarks/` (`run-thesis-proof.sh`, `run-simple-crud.sh`, or `CrudBenchmarks/verify-validation-artifacts.sh`); see `benchmarks/README.md` for current instructions. Results land in `benchmarks/CrudBenchmarks/results/`.

---

## Methodology

The suite uses a purpose-built "equal footing" design — each framework gets the same fair setup:

- **pengdows.crud** pre-builds `ISqlContainer` objects once and reuses them per iteration via `SetParameterValue` — no SQL regeneration, no repeated reflection overhead.
- **Dapper** uses inline SQL strings with anonymous parameter objects — the standard Dapper usage pattern.
- **Entity Framework Core** creates a fresh `DbContext` per iteration with `AsNoTracking()`, matching real-world DI usage.

All three hit the same database. The benchmarks are tested against two databases with very different latency profiles — SQLite (embedded, sub-microsecond round trip) and PostgreSQL (network, ~190 μs round trip) — because the overhead profile of a framework looks very different depending on which dominates: the framework or the wire.

**RecordCount** parameterizes the loop inside each benchmark method. `ReadSingle×N` makes N individual queries. `ReadList` makes ONE query that returns all N rows. This lets you measure both bulk-read and per-row performance in the same suite.

### PostgreSQL-specific design notes

The PostgreSQL benchmark's `DatabaseContext` is constructed with `NpgsqlFactory.Instance` and a standard connection string. The framework bakes two optimizations directly into the Npgsql `NpgsqlDataSource` at startup:

1. **Session settings baked into startup Options** — `standard_conforming_strings`, `client_min_messages`, and `default_transaction_read_only` are injected as `-c key=value` tokens in the connection string `Options` parameter. PostgreSQL treats these as session-level GUC defaults, so `RESET ALL` on pool return restores them. This eliminates the per-checkout `SET` round-trip (~190 μs) that prior versions paid.

2. **Npgsql auto-prepare enabled** — `MaxAutoPrepare=64` and `AutoPrepareMinUsages=2` are baked into the DataSource. After two executions of the same SQL text on a given connection, Npgsql transparently server-side prepares it, eliminating PostgreSQL's parse and plan phase on subsequent calls.

Dapper's benchmark uses `NpgsqlFactory.Instance.CreateDataSource(connStr)` with no auto-prepare configuration — the Npgsql default is disabled (`MaxAutoPrepare=0`). Dapper never gets server-side prepared statements.

The `GlobalSetup` method runs a 20-iteration pre-warming pass over every reusable container before BenchmarkDotNet's own warmup begins. This ensures all five pre-created pool connections (Minimum Pool Size=5) have crossed the `AutoPrepareMinUsages=2` threshold on every statement before measurement starts.

---

## SQLite Results (embedded, 2026-03-04)

SQLite exposes framework overhead most clearly because the database round trip itself takes only ~15–20 μs.

### Single-record operations (N=1)

| Operation | pengdows | Dapper | EF Core | pengdows/Dapper | pengdows/EF |
|-----------|----------|--------|---------|-----------------|-------------|
| Create | 33.9 μs / 8.2 KB | 21.4 μs / 3.7 KB | 87.7 μs / 46.4 KB | **1.58x slower** | **2.6x faster** |
| ReadSingle | 32.4 μs / 7.1 KB | 21.5 μs / 2.7 KB | 122.4 μs / 57.7 KB | **1.51x slower** | **3.8x faster** |
| ReadList | 36.0 μs / 7.6 KB | 23.7 μs / 3.3 KB | 116.8 μs / 57.4 KB | **1.52x slower** | **3.2x faster** |
| Update | 25.6 μs / 6.4 KB | 18.0 μs / 2.0 KB | 82.6 μs / 43.9 KB | **1.42x slower** | **3.2x faster** |
| FilteredQuery | 40.0 μs / 8.9 KB | 26.3 μs / 4.3 KB | 119.8 μs / 59.1 KB | **1.52x slower** | **3.0x faster** |
| Aggregate | 63.1 μs / 5.6 KB | 51.6 μs / 1.3 KB | 111.2 μs / 41.9 KB | **1.22x slower** | **1.8x faster** |

> The Delete benchmark measures INSERT+DELETE per iteration (rows must exist before they can be deleted), so its absolute time reflects two SQL executions. The overhead ratio vs Dapper is the same ~1.4x as other operations.

On embedded SQLite, pengdows is consistently **1.4–1.6x slower than Dapper** per operation, and **2–4x faster than EF Core**. The ~12 μs overhead vs Dapper comes from the connection pool lifecycle management, parameter binding infrastructure, and typed mapping — all things Dapper does not do. The 30 μs per-operation cost is dominated by the SQLite round trip itself (~18 μs), so the framework overhead is small relative to the work done.

EF Core's per-operation allocation is 6–12x heavier than pengdows. On memory-constrained or high-throughput servers, this compounds.

### Batch reads — where pengdows closes the gap

`ReadList` executes a single SQL query that returns all N rows. This reflects the real-world pattern of fetching a result set, not N individual lookups.

| Records | pengdows ReadList | Dapper ReadList | ReadSingle×N (pengdows) | Speedup vs individual |
|---------|------------------|-----------------|-------------------------|----------------------|
| 1 | 36.0 μs | 23.7 μs | 32.4 μs | — |
| 10 | 46.0 μs | 33.8 μs | 317 μs | **6.9x faster** |
| 100 | 135.9 μs | 126.4 μs | 3,164 μs | **23x faster** |

At 100 records, `ReadList` on SQLite is within **7% of Dapper** — the gap essentially disappears at scale because both are dominated by the same I/O. The critical takeaway: fetching 100 rows with one query costs 136 μs; fetching them one at a time costs 3.2 ms — 23x slower. This holds for all three frameworks.

### Connection lifecycle validation

`ConnectionHoldTime` measures a single connection open + query + close in isolation. It stays flat across all RecordCounts:

| RecordCount | pengdows | Dapper |
|-------------|----------|--------|
| 1 | 32.3 μs | 21.9 μs |
| 10 | 32.0 μs | 20.2 μs |
| 100 | 32.8 μs | 21.0 μs |

This proves the "open late, close early" design is working correctly — connections are not accumulated or held between benchmark iterations regardless of record volume.

---

## PostgreSQL Results (network database, equal-footing baseline, 2026-03-15)

PostgreSQL changes the picture significantly from SQLite: a single network round trip to a local Docker PostgreSQL is ~190 μs. At this scale, infrastructure differences that dominate on SQLite become comparatively small.

**This section previously reported a different, superseded result.** An earlier (2026-03-04) run configured pengdows' `DatabaseContext` with `MaxAutoPrepare=64;AutoPrepareMinUsages=2` baked into its Npgsql `NpgsqlDataSource`, but left Dapper's `NpgsqlDataSource` at Npgsql's default (`MaxAutoPrepare=0`, disabled) — pengdows got server-side prepared statements after 2 uses per connection; Dapper never did. That produced a "pengdows beats Dapper on reads" result driven by an apples-to-oranges driver configuration, not by anything pengdows.crud's architecture actually does better. The 2026-03-15 rerun
(`benchmarks/CrudBenchmarks/results/postgres-run-2026-03-15-after-fix.md`, corresponding to `PostgreSqlEqualFootingBenchmarks.cs`) gives **pengdows, Dapper, and EF Core the identical** `MaxAutoPrepare=64;AutoPrepareMinUsages=2` configuration — the result file itself calls this "the publishable equal-footing baseline." The numbers below are from that run; treat any other PostgreSQL numbers in this repo's history as superseded.

### Single-record operations (N=1)

| Operation | pengdows | Dapper | EF Core | pengdows vs Dapper | pengdows vs EF |
|-----------|----------|--------|---------|---------------------|------------------|
| ReadSingle | 164.1 μs / 5.9 KB | 164.7 μs / 3.1 KB | 254.9 μs / 50.8 KB | **effectively identical** | **1.55x faster** |
| ReadList | 137.3 μs / 6.0 KB | 133.4 μs / 3.5 KB | 224.7 μs / 50.2 KB | 2.9% slower | **1.64x faster** |
| FilteredQuery | 138.9 μs / 6.3 KB | 137.4 μs / 4.1 KB | 223.5 μs / 51.8 KB | 1.1% slower | **1.61x faster** |
| Aggregate | 205.8 μs / 5.5 KB | 202.6 μs / 2.3 KB | 257.6 μs / 34.2 KB | 1.6% slower | **1.25x faster** |
| ConnectionHoldTime | 164.0 μs / 5.9 KB | 164.0 μs / 3.2 KB | 249.8 μs / 50.8 KB | **identical** | **1.52x faster** |
| Create | 326.7 μs / 8.7 KB | 303.5 μs / 4.4 KB | 368.5 μs / 39.7 KB | 7.7% slower | **1.13x faster** |
| Update | 339.3 μs / 6.1 KB | 327.5 μs / 3.2 KB | 387.2 μs / 37.1 KB | 3.6% slower | **1.14x faster** |
| DeleteOnly | 267.6 μs / 7.1 KB | 266.5 μs / 3.3 KB | 332.0 μs / 37.2 KB | 0.4% slower | **1.24x faster** |
| DeleteInsertCycle | 595.3 μs / 10.6 KB | 609.0 μs / 6.9 KB | 718.4 μs / 76.0 KB | **2.2% faster** | **1.21x faster** |

**The true performance story is parity with Dapper**, not a pengdows advantage — reads and writes both land within a few percent either direction, well inside run-to-run noise. pengdows allocates roughly 2x more heap than Dapper per operation (the cost of type-safe SQL generation, named parameters, and mapped entities); EF Core allocates 6.8–9.6x more than pengdows on top of being 1.1–1.6x slower.

### PostgreSQL at scale (N=100)

| Operation | pengdows | Dapper | EF Core |
|-----------|----------|--------|---------|
| ReadSingle×100 | 15,666 μs | 15,270 μs | 24,421 μs |
| ReadList (1 query) | 203.6 μs | 185.6 μs | 293.5 μs |
| FilteredQuery | 226.3 μs | 210.5 μs | 323.0 μs |
| Create×100 | 30,371 μs | 28,863 μs | 35,011 μs |
| Update×100 | 31,773 μs | 31,894 μs | 37,135 μs |
| DeleteOnly×100 | 25,727 μs | 25,139 μs | 31,926 μs |

**The 77x number:** `ReadList` at N=100 (one query, 100 rows) costs 204 μs; `ReadSingle×100` (100 individual round trips) costs 15,666 μs — a **77x difference**. All three frameworks show the same pattern; pengdows and Dapper stay within ~10% of each other under both query shapes. This is a query-design argument, not a framework argument: issuing the right number of round trips swamps any ORM's per-call overhead by orders of magnitude — a 3–8% framework difference means almost nothing next to a 7,700% difference caused by the wrong number of round trips.

---

## Cross-database summary

| Scenario | pengdows vs Dapper | pengdows vs EF Core |
|----------|--------------------|----------------------|
| SQLite, single op | 1.4–1.6x slower | **2–4x faster** |
| SQLite, 100-row batch read | **~parity (7% slower)** | ~1.1x faster |
| PostgreSQL, single op (equal footing) | **at parity — within ±3% on reads, ~4–8% on writes** | **1.1–1.6x faster** |
| PostgreSQL, 100-row batch read | **within ~10%** | **1.4–1.5x faster** |
| Memory per operation | ~2x more than Dapper | **5–12x less than EF Core** |

---

## What the numbers mean for your application

**If your bottleneck is raw single-operation throughput against an embedded database** (SQLite, DuckDB) in a tight loop: Dapper will be faster, and that gap is real. Pengdows adds ~12 μs per operation on SQLite. At 10,000 operations/second that is 120 ms/second of overhead — plan for it.

**If your bottleneck is raw single-operation throughput against a network database** (PostgreSQL, SQL Server, Oracle): the ~190 μs network round trip dominates the SQLite-scale overhead almost entirely, and pengdows lands at parity with Dapper — within a few percent either direction — once both get the identical Npgsql auto-prepare configuration. The honest framing here is not "pengdows is faster than Dapper"; it's that a fully-governed data-access architecture (connection governance, dialect handling, generated SQL, parameter management, instrumentation, mapping, lifecycle enforcement) costs approximately nothing extra over a thin mapper, once the driver-level playing field is actually level. Dapper remains the practical floor for minimal ADO.NET overhead — landing within a few percent of that floor while providing everything pengdows.crud provides on top is the actual result.

**If you read rows in sets** — which is the right pattern for almost all real applications: pengdows tracks Dapper closely at every RecordCount on PostgreSQL batch reads (within ~10%), and closes to within 7% on SQLite at 100 rows. More importantly, it beats EF Core comfortably at all scales. Use `LoadListAsync` or `RetrieveStreamAsync` rather than calling `RetrieveOneAsync` in a loop.

**Memory:** pengdows uses roughly 2x more heap than raw Dapper per operation on PostgreSQL (2–3x on SQLite). This reflects real infrastructure: connection pool tracking, `ISqlContainer` state, compiled accessor caches, and type mapping — overhead that provides strong typing, audit fields, optimistic concurrency, and connection safety guarantees. EF Core uses 6–12x more memory than pengdows for the same operations.

**The framework overhead is not query time.** Changing to a faster ORM does not make your PostgreSQL server issue plans faster. Profile your actual queries before optimizing framework choice.

---

## Reproducing these results

```bash
git clone <repo>
cd pengdows.crud/benchmarks/CrudBenchmarks

# SQLite suite (no dependencies needed)
dotnet run -c Release -- --filter "*EqualFooting*"

# PostgreSQL suite (requires Docker)
dotnet run -c Release -- --filter "*PostgreSql*"

# Results written to:
# benchmarks/CrudBenchmarks/BenchmarkDotNet.Artifacts/results/
# benchmarks/CrudBenchmarks/results/
```

The suite includes correctness validation (`BenchmarkCorrectnessArtifacts.cs`) that verifies each framework is actually reading and writing the same data — not just running no-op SQL.
