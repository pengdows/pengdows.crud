# Performance Evaluation: pengdows.crud 2.0

**Benchmark environment:** Ubuntu 24.04.4 LTS · AMD Ryzen 9 5950X (8 cores) · .NET 8.0.24 (SQLite run) and 8.0.25 (PostgreSQL run) · BenchmarkDotNet v0.14.0

**Reproducing:** Clone the repository and run `./run-benchmarks.sh` from the repository root (`BENCH_FRAMEWORK` selects `net8.0` or `net10.0`; the default is `net10.0`), or use the `dotnet run` filters at the end of this document. The checked-in runs are in `benchmarks/CrudBenchmarks/results/`. The SQLite section below is the 2026-03-04 run (`run-2026-03-04-session-settings-overhaul.md`), the PostgreSQL section the 2026-03-15 equal-footing run (`postgres-run-2026-03-15-after-fix.md`); newer runs, for example `sqlite-equal-footing-run-2026-10-03.md`, sit beside them.

---

## Methodology

The suite uses a purpose-built "equal footing" design — each framework gets the same fair setup:

- **pengdows.crud** pre-builds `ISqlContainer` objects once and reuses them per iteration via `SetParameterValue` — no SQL regeneration, no repeated reflection overhead.
- **Dapper** uses inline SQL strings with anonymous parameter objects — the standard Dapper usage pattern.
- **Entity Framework Core** creates a fresh `DbContext` per iteration with `AsNoTracking()`, matching real-world DI usage.

On PostgreSQL all three frameworks run with the same Npgsql auto-prepare configuration (see below); an earlier run that gave only pengdows auto-prepare was biased and is superseded.

All three hit the same database. The benchmarks are tested against two databases with very different latency profiles — SQLite (embedded; a whole Dapper single-record operation takes about 22 µs) and PostgreSQL (network, ~190 μs round trip) — because the overhead profile of a framework looks very different depending on which dominates: the framework or the wire.

**RecordCount** parameterizes the loop inside each benchmark method. `ReadSingle×N` makes N individual queries. `ReadList` makes ONE query that returns all N rows. This lets you measure both bulk-read and per-row performance in the same suite.

### PostgreSQL-specific design notes

The PostgreSQL benchmark's `DatabaseContext` is constructed with `NpgsqlFactory.Instance` and a standard connection string. The framework bakes two optimizations directly into the Npgsql `NpgsqlDataSource` at startup (`PostgreSqlDialect.cs`; YugabyteDB's dialect turns auto-prepare off again):

1. **Session settings baked into startup Options** — `standard_conforming_strings`, `client_min_messages`, and `default_transaction_read_only` are injected as `-c key=value` tokens in the connection string `Options` parameter. PostgreSQL treats these as session-level GUC defaults, so `RESET ALL` on pool return restores them. This eliminates the per-checkout `SET` round-trip (about 190 μs; **UNVERIFIED**: no result file contains this figure) that prior versions paid.

2. **Npgsql auto-prepare enabled** — `MaxAutoPrepare=64` and `AutoPrepareMinUsages=2` are baked into the DataSource. After two executions of the same SQL text on a given connection, Npgsql transparently server-side prepares it, eliminating PostgreSQL's parse and plan phase on subsequent calls.

For equal footing the Dapper and EF Core arms use a connection string with the same `MaxAutoPrepare=64;AutoPrepareMinUsages=2` (`PostgreSqlEqualFootingBenchmarks.cs`), so all three frameworks get server-side prepared statements. Without it the Npgsql default is disabled (`MaxAutoPrepare=0`), and the earlier 2026-03-04/10 run, in which only pengdows had auto-prepare, overstated pengdows' read advantage (`postgres-run-2026-03-04.md` carries that warning).

The `GlobalSetup` method runs a 20-iteration pre-warming pass over every reusable container before BenchmarkDotNet's own warmup begins. This ensures all five pre-created pool connections (Minimum Pool Size=5) have crossed the `AutoPrepareMinUsages=2` threshold on every statement before measurement starts.

---

## SQLite Results (embedded, 2026-03-04)

SQLite exposes framework overhead most clearly because a whole Dapper single-record operation takes only about 22 μs, so the database round trip is a small part of it (the bare round trip is not measured separately).

### Single-record operations (N=1)

| Operation | pengdows | Dapper | EF Core | pengdows/Dapper | pengdows/EF |
|-----------|----------|--------|---------|-----------------|-------------|
| Create | 33.9 μs / 8.2 KB | 21.4 μs / 3.7 KB | 87.7 μs / 46.4 KB | **1.58x slower** | **2.6x faster** |
| ReadSingle | 32.4 μs / 7.1 KB | 21.5 μs / 2.7 KB | 122.4 μs / 57.7 KB | **1.51x slower** | **3.8x faster** |
| ReadList | 36.0 μs / 7.6 KB | 23.7 μs / 3.3 KB | 116.8 μs / 57.4 KB | **1.52x slower** | **3.2x faster** |
| Update | 25.6 μs / 6.4 KB | 18.0 μs / 2.0 KB | 82.6 μs / 43.9 KB | **1.42x slower** | **3.2x faster** |
| FilteredQuery | 40.0 μs / 8.9 KB | 26.3 μs / 4.3 KB | 119.8 μs / 59.1 KB | **1.52x slower** | **3.0x faster** |
| Aggregate | 63.1 μs / 5.6 KB | 51.6 μs / 1.3 KB | 111.2 μs / 41.9 KB | **1.22x slower** | **1.8x faster** |

> There is no Delete row in this table. The DeleteOnly and DeleteInsertCycle benchmarks measure INSERT+DELETE per iteration, so their absolute times reflect two SQL executions. The previously quoted ~1.4x Delete ratio had no source row and is removed.

On embedded SQLite, pengdows is **1.2x to 1.6x slower than Dapper** per operation (Aggregate 1.22x; the other rows 1.42x to 1.58x), and **1.8x to 3.8x faster than EF Core**. The ~12 μs overhead vs Dapper comes from the connection pool lifecycle management, parameter binding infrastructure, and typed mapping — all things Dapper does not do. The ~30 μs per-operation cost is mostly the SQLite round trip, which is not measured separately, so the framework overhead is small relative to the work done.

EF Core's per-operation allocation is 5.7x to 8.1x that of pengdows at N=1 on SQLite (table above). On memory-constrained or high-throughput servers, this compounds.

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

The flat timing shows that the per-query cost does not grow with the record count. It does not measure connection lifecycle directly, so it does not prove the "open late, close early" design; that claim needs a direct measurement of connections opened, held and closed.

---

## PostgreSQL Results (network database, 2026-03-15, equal footing)

> **Superseded for ratios:** the current PostgreSQL run is 2026-11-27 (pengdows ÷ Dapper 0.959 to 1.031; see `benchmarks/README.md`). The tables below are the 2026-03-15 run, whose Dapper arms left two columns unmapped until 2026-10-19 (`benchmarks/CrudBenchmarks/results/row-scaling-2026-10-19.md`).

A single network round trip to a local Docker PostgreSQL is ~140-200 μs, so framework overhead is a small share of every operation. All three frameworks use identical Npgsql auto-prepare configuration. `P÷D` is pengdows mean ÷ Dapper mean (below 1.0 means pengdows is faster); `EF÷P` is EF Core mean ÷ pengdows mean.

> The `Update` and `ConnectionHoldTime` rows were found to carry methodology asymmetries on 2026-09-19 (the source of truth is the note at the top of `postgres-run-2026-03-15-after-fix.md`); the benchmark was fixed and a live re-run showed `Update` on par (357.7 μs vs 360.0 μs). Treat those two rows as pending a full re-run.

### Single-record operations (N=1)

| Operation | pengdows | Dapper | EF Core | P÷D | EF÷P |
|-----------|----------|--------|---------|-----|------|
| Create | 326.7 μs / 8.7 KB | 303.5 μs / 4.4 KB | 368.5 μs / 39.7 KB | 1.08 | 1.13 |
| ReadSingle | 164.1 μs / 5.9 KB | 164.7 μs / 3.1 KB | 254.9 μs / 50.8 KB | 1.00 | 1.55 |
| ReadList | 137.3 μs / 6.0 KB | 133.4 μs / 3.5 KB | 224.7 μs / 50.2 KB | 1.03 | 1.64 |
| Update | 339.3 μs / 6.1 KB | 327.5 μs / 3.2 KB | 387.2 μs / 37.1 KB | 1.04 | 1.14 |
| FilteredQuery | 138.9 μs / 6.3 KB | 137.4 μs / 4.1 KB | 223.5 μs / 51.8 KB | 1.01 | 1.61 |
| Aggregate | 205.8 μs / 5.5 KB | 202.6 μs / 2.3 KB | 257.6 μs / 34.2 KB | 1.02 | 1.25 |
| DeleteOnly | 267.6 μs / 7.1 KB | 266.5 μs / 3.3 KB | 332.0 μs / 37.2 KB | 1.00 | 1.24 |
| DeleteInsertCycle | 595.3 μs / 10.6 KB | 608.9 μs / 6.9 KB | 718.4 μs / 76.0 KB | 0.98 | 1.21 |

pengdows is at parity with Dapper: within about 10% on every scenario and record count (`P÷D` 0.94-1.10), with Create the slowest at about 5-8% (SQL generation overhead). pengdows is 1.1-1.6x faster than EF Core. Both frameworks use the same Npgsql driver and the same server-side prepared statements, so the wire dominates.

### PostgreSQL batch reads

| Records | pengdows ReadList | Dapper ReadList | EF ReadList | ReadSingle×N (pengdows) |
|---------|------------------|-----------------|-------------|-------------------------|
| 1 | 137.3 μs | 133.4 μs | 224.7 μs | 164.1 μs |
| 10 | 143.6 μs | 140.5 μs | 231.0 μs | 1,616.1 μs |
| 100 | 203.6 μs | 185.6 μs | 293.5 μs | 15,666.1 μs |

At 100 records `ReadList` (one query, 100 rows) costs 204 μs for pengdows, 186 μs for Dapper and 294 μs for EF Core; 100 individual `ReadSingle` queries cost 15,666 μs for pengdows (about 77x slower), 15,270 μs for Dapper and 24,421 μs for EF Core. The pattern is the same in every framework, so the choice of query pattern matters far more than the choice of framework.

`FilteredQuery` shows the same pattern: 139 / 137 / 224 μs (pengdows / Dapper / EF Core) at N=1, 226 / 211 / 323 μs at N=100.

### Connection lifecycle on PostgreSQL

`ConnectionHoldTime` stays flat across record counts (pengdows 164.0 / 165.7 / 170.1 μs at N=1 / 10 / 100, Dapper 164.0 / 163.9 / 180.9 μs); the ~164 μs base cost is the single PostgreSQL network round trip. This row is one of the two pending a full re-run (see the note above).

---

## Cross-database summary

| Scenario | pengdows vs Dapper | pengdows vs EF Core |
|----------|--------------------|---------------------|
| SQLite, single op | 1.2–1.6x slower | **1.8–3.8x faster** |
| SQLite, 100-row batch read | **~parity (7% slower)** | ~1.1x faster |
| PostgreSQL, reads | **parity (within about 10%)** | **1.4–1.6x faster** |
| PostgreSQL, writes | **parity (Create about 5–8% slower)** | **1.1–1.3x faster** |
| PostgreSQL, 100-row batch read | **parity (about 10% slower)** | **1.4x faster** |
| Memory per operation | 1.5–2.4x Dapper on PostgreSQL; 2.1–4.3x on SQLite (N=1) | **EF Core uses 4.6–8.6x (PostgreSQL) and 5.7–8.1x (SQLite) more** |

---

## What the numbers mean for your application

**If your bottleneck is raw single-operation throughput against an embedded database** (SQLite, DuckDB) in a tight loop: Dapper will be faster, and that gap is real. Pengdows adds ~12 μs per operation on SQLite. At 10,000 operations/second that is 120 ms/second of overhead — plan for it.

**If your bottleneck is raw single-operation throughput against a network database** (measured here on PostgreSQL): the ~140–200 μs network round trip dominates, and with equal Npgsql auto-prepare configuration pengdows is at parity with Dapper (within about 10%). The overhead that was visible on SQLite is largely hidden at network latency because it is dwarfed by the wire. The framework also bakes the auto-prepare settings and the session settings into the Npgsql data source, so you get them without configuring them yourself; a Dapper user would have to set `MaxAutoPrepare` explicitly.

**If you read rows in sets** — which is the right pattern for almost all real applications: pengdows is within about 10% of Dapper at every RecordCount on PostgreSQL batch reads, and within 7% on SQLite at 100 rows. More importantly, it beats EF Core comfortably at all scales. Use `LoadListAsync` or `RetrieveStreamAsync` rather than calling `RetrieveOneAsync` in a loop.

**Memory:** pengdows uses about 1.5x to 2.4x the heap of raw Dapper per operation on PostgreSQL, and 2.1x to 4.3x on SQLite (N=1). This reflects real infrastructure: connection pool tracking, `ISqlContainer` state, compiled accessor caches, and type mapping — overhead that provides strong typing, audit fields, optimistic concurrency, and connection safety guarantees. EF Core uses 4.6x to 8.6x more memory than pengdows on PostgreSQL and 5.7x to 8.1x on SQLite (N=1), for the same operations.

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
