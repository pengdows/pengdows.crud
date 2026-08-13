## Hydration-Only Benchmark — 2026-08-13

`HydrationHotPathBenchmarks` isolates row materialization from connection-lifecycle policy:
Dapper keeps one permanently open `SqliteConnection` for the whole run; pengdows.crud uses
`DbMode.SingleConnection` so its connection is opened once in `GlobalSetup` and stays open
across all iterations. Both read the same 8-column table via the same SQL shape, both are
prewarmed. Row counts: 100, 1,000, 5,000. This substantiates (independently of the earlier
committed benchmarks) the claim that pengdows.crud's compiled hydration path can compete
directly with Dapper — the result here is stronger than "compete": pengdows is faster.

### Cross-Framework Ratios

`P÷D` = pengdows Mean ÷ Dapper Mean — values < 1.0 mean pengdows is faster.

| RowCount | Pengdows Mean | Dapper Mean | P÷D |
|---------:|--------------:|------------:|----:|
| 100 | 90.12 μs | 135.71 μs | 0.664 |
| 1,000 | 814.51 μs | 1,268.83 μs | 0.642 |
| 5,000 | 3,938.98 μs | 6,107.44 μs | 0.645 |

### Key Findings

- **pengdows is consistently ~35–36% faster than Dapper on hydration-only work**, stable
  across all three row counts (100/1,000/5,000) — this is not a small-N artifact.
- **pengdows also allocates less**: Dapper's Alloc Ratio is 1.92–1.99 relative to pengdows
  (i.e. Dapper allocates roughly twice what pengdows does) at every row count — the inverse
  of the general pattern seen in the equal-footing PostgreSQL CRUD benchmarks, where Dapper
  is the leaner allocator. Isolating pure row materialization (no connection/command
  overhead, no SQL generation) changes which framework's cost model wins.
- **Zero correctness failures** across all six benchmark runs (`Fails = 0` in every row).
- This is a single-database (SQLite), single-machine run — treat as directional evidence
  for the hydration-path claim specifically, not as a restatement of the PostgreSQL
  equal-footing parity result (`postgres-run-2026-03-15-after-fix.md`), which measures a
  different thing (full CRUD round trip against a network database, not isolated
  in-process hydration).

---

### Raw BDN Output

```
BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.110
  [Host]     : .NET 8.0.29 (8.0.2926.32403), X64 RyuJIT AVX2
  Job-TATGRL : .NET 8.0.29 (8.0.2926.32403), X64 RyuJIT AVX2

IterationCount=10  WarmupCount=3
```

| Method | RowCount | Mean | Error | StdDev | P95 | P99 | Ratio | Fails | Gen0 | Gen1 | Allocated | Alloc Ratio |
|--------|---------:|-----:|------:|-------:|----:|----:|------:|------:|-----:|-----:|----------:|------------:|
| HydrationOnly_Pengdows | 100 | 90.12 μs | 0.146 μs | 0.097 μs | 90.23 μs | 90.24 μs | 1.00 | 0 | 1.2207 | - | 20.98 KB | 1.00 |
| HydrationOnly_Dapper | 100 | 135.71 μs | 0.515 μs | 0.341 μs | 136.26 μs | 136.43 μs | 1.51 | 0 | 2.4414 | - | 40.38 KB | 1.93 |
| HydrationOnly_Pengdows | 1000 | 814.51 μs | 1.524 μs | 1.008 μs | 816.18 μs | 816.50 μs | 1.00 | 0 | 11.7188 | 3.9063 | 196.77 KB | 1.00 |
| HydrationOnly_Dapper | 1000 | 1,268.83 μs | 2.156 μs | 1.127 μs | 1,270.08 μs | 1,270.21 μs | 1.56 | 0 | 23.4375 | 9.7656 | 391.96 KB | 1.99 |
| HydrationOnly_Pengdows | 5000 | 3,938.98 μs | 12.565 μs | 7.477 μs | 3,951.78 μs | 3,954.48 μs | 1.00 | 0 | 62.5000 | 39.0625 | 1058.85 KB | 1.00 |
| HydrationOnly_Dapper | 5000 | 6,107.44 μs | 10.870 μs | 6.469 μs | 6,115.87 μs | 6,117.75 μs | 1.55 | 0 | 117.1875 | 85.9375 | 2035.29 KB | 1.92 |

Total run time: 86.72 sec, 6 benchmarks executed.
