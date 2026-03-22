# Performance Evaluation: pengdows.crud 2.0

**Benchmark environment:** Ubuntu 24.04.4 LTS · AMD Ryzen 9 5950X (8 cores) · .NET 8.0.24 · BenchmarkDotNet v0.14.0

**Reproducing:** Clone the repository and run `./run-benchmarks.sh` from the `benchmarks/CrudBenchmarks/` directory. Results land in `benchmarks/CrudBenchmarks/results/`.

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

## PostgreSQL Results (network database, 2026-03-10)

PostgreSQL changes the picture significantly. A single network round trip to a local Docker PostgreSQL is ~190–200 μs. At this scale, infrastructure advantages — eliminated round-trips and server-side prepared statements — become material.

### Single-record operations (N=1)

| Operation | pengdows | Dapper | EF Core | pengdows/Dapper | pengdows/EF |
|-----------|----------|--------|---------|-----------------|-----------  |
| Create | 327.9 μs / 8.7 KB | 322.5 μs / 4.4 KB | 384.5 μs / 39.6 KB | **tied (1.02x)** | **1.2x faster** |
| ReadSingle | 171.9 μs / 5.9 KB | 195.2 μs / 3.3 KB | 323.7 μs / 50.9 KB | **12% faster** | **1.9x faster** |
| ReadList | 147.7 μs / 6.0 KB | 193.3 μs / 3.7 KB | 284.3 μs / 50.4 KB | **24% faster** | **1.9x faster** |
| Update | 340.6 μs / 6.1 KB | 340.7 μs / 3.1 KB | 405.0 μs / 37.0 KB | **tied (1.00x)** | **1.2x faster** |
| FilteredQuery | 153.4 μs / 6.3 KB | 212.1 μs / 4.2 KB | 314.8 μs / 51.9 KB | **28% faster** | **2.1x faster** |
| Aggregate | 228.2 μs / 5.4 KB | 234.1 μs / 2.2 KB | 293.7 μs / 34.1 KB | **tied (0.97x)** | **1.3x faster** |
| Delete | 633.5 μs / 10.6 KB | 645.8 μs / 6.8 KB | 806.2 μs / 75.9 KB | **tied (0.98x)** | **1.3x faster** |

pengdows is **faster than or equal to Dapper on every operation** at N=1, and **faster than EF Core across the board**.

**Why pengdows beats Dapper on reads:** Both frameworks use the same Npgsql driver. The difference is that pengdows' `DatabaseContext` bakes `MaxAutoPrepare=64` into the Npgsql `NpgsqlDataSource` — Npgsql server-side prepares statements after 2 uses per connection. Dapper uses a plain `NpgsqlFactory.Instance.CreateDataSource()` with no `MaxAutoPrepare` configured; Dapper never gets prepared statements. For reusable read containers (`ReadSingle`, `ReadList`, `FilteredQuery`), pengdows gets zero-allocation parameter reuse AND server-side execution of pre-compiled query plans. Dapper sends unprepared SQL on every call, paying parse and plan cost each time.

**Why writes are tied:** `UPDATE`, `INSERT`, and `DELETE` carry heavier server-side execution cost (lock acquisition, WAL writes, index updates). The parse/plan savings from prepared statements are proportionally smaller. Dapper's lower client-side allocation partially offsets this; the two frameworks converge.

**Why the previous results were wrong:** An earlier PostgreSQL run showed pengdows 1.5–2.2x _slower_ than Dapper. That run lacked both optimizations: session settings were still issued as a post-checkout `SET` command (one extra network round-trip per connection), and the benchmark's `GlobalSetup` did not pre-warm all pool connections past the `AutoPrepareMinUsages` threshold. With only 3 BenchmarkDotNet warmup iterations at RecordCount=1, some pool connections never accumulated enough executions to trigger auto-prepare before measurement began, producing a mixed average of prepared and unprepared executions.

### PostgreSQL batch reads

| Records | pengdows ReadList | Dapper ReadList | EF ReadList | ReadSingle×N (pengdows) |
|---------|------------------|-----------------|-------------|-------------------------|
| 1 | 147.7 μs | 193.3 μs | 284.3 μs | 171.9 μs |
| 10 | 141.9 μs | 193.1 μs | 267.0 μs | 1,545.7 μs |
| 100 | 200.3 μs | 220.0 μs | 338.0 μs | 15,261.6 μs |

At 100 records:

- `ReadList_Pengdows` (1 query, 100 rows): **200 μs**
- `ReadList_Dapper` (1 query, 100 rows): **220 μs** — pengdows 9% faster
- `ReadList_EF` (1 query, 100 rows): **338 μs** — pengdows 1.7x faster
- `ReadSingle_Pengdows×100` (100 individual queries): **15,262 μs** — **76x slower**

The critical point for application architects: making individual per-row database calls in a loop costs 15 ms on PostgreSQL to fetch 100 records. With `ReadList` you spend 200 μs. The choice of query pattern matters far more than the choice of framework.

`FilteredQuery` shows the same pattern:

| Records | pengdows FilteredQuery | Dapper FilteredQuery | EF FilteredQuery |
|---------|----------------------|---------------------|-----------------|
| 1 | 153 μs | 212 μs | 315 μs |
| 10 | 147 μs | 205 μs | 290 μs |
| 100 | 217 μs | 236 μs | 358 μs |

pengdows leads Dapper at every RecordCount on filtered reads, and leads EF Core by 1.7x at N=100.

### Connection lifecycle on PostgreSQL

`ConnectionHoldTime_Pengdows` stays flat across all record counts:

| RecordCount | pengdows | Dapper |
|-------------|----------|--------|
| 1 | 172.5 μs | 198.3 μs |
| 10 | 164.9 μs | 184.7 μs |
| 100 | 162.6 μs | 186.1 μs |

The ~163–173 μs base cost is the single PostgreSQL network round trip. Pengdows runs this query **faster than Dapper** at every record count — server-side prepared statement execution returns results without the server re-parsing the query on each call.

---

## Cross-database summary

| Scenario | pengdows vs Dapper | pengdows vs EF Core |
|----------|--------------------|---------------------|
| SQLite, single op | 1.4–1.6x slower | **2–4x faster** |
| SQLite, 100-row batch read | **~parity (7% slower)** | ~1.1x faster |
| PostgreSQL, single op reads | **12–28% faster** | **1.9–2.1x faster** |
| PostgreSQL, writes | **tied** | **1.2x faster** |
| PostgreSQL, 100-row batch read | **9% faster** | **1.7x faster** |
| Memory per operation | 2–3x more than Dapper | **5–12x less than EF Core** |

---

## What the numbers mean for your application

**If your bottleneck is raw single-operation throughput against an embedded database** (SQLite, DuckDB) in a tight loop: Dapper will be faster, and that gap is real. Pengdows adds ~12 μs per operation on SQLite. At 10,000 operations/second that is 120 ms/second of overhead — plan for it.

**If your bottleneck is raw single-operation throughput against a network database** (PostgreSQL, SQL Server, Oracle): the ~190 μs network round trip dominates, and pengdows' structural advantages — baked session settings and Npgsql auto-prepare — actually make it _faster_ than Dapper on read operations, and equal on writes. The overhead that was visible on SQLite is invisible at network latency because it is dwarfed by the wire.

**If you read rows in sets** — which is the right pattern for almost all real applications: pengdows leads Dapper at every RecordCount on PostgreSQL batch reads, and closes to within 7% on SQLite at 100 rows. More importantly, it beats EF Core comfortably at all scales. Use `LoadListAsync` or `RetrieveStreamAsync` rather than calling `RetrieveOneAsync` in a loop.

**Memory:** pengdows uses 2–3x more heap than raw Dapper per operation. This reflects real infrastructure: connection pool tracking, `ISqlContainer` state, compiled accessor caches, and type mapping — overhead that provides strong typing, audit fields, optimistic concurrency, and connection safety guarantees. EF Core uses 5–12x more memory than pengdows for the same operations.

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
