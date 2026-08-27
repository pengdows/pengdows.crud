## SQL Server Hydration-Only Benchmark — 2026-08-13

Follow-up to `sqlserver-equal-footing-run-2026-08-13.md`, which found pengdows 1.4-2.0x
slower than Dapper on SQL Server and traced it to a real, per-operation session-settings
`SET` round trip that `DbMode.Standard` re-pays on every single logical checkout. The
question this benchmark answers: is that gap really about pengdows's execution/hydration
work being slower, or is it almost entirely the repeated session-init tax?

Same normalization `HydrationHotPathBenchmarks.cs` already applies for SQLite: pengdows
uses `DbMode.SingleConnection` (session settings applied **once**, in `GlobalSetup`, not
per operation) and Dapper keeps one permanently open `SqlConnection` for the whole run —
identical connection-lifecycle policy on both sides, so this isolates row-materialization
cost from connection/session overhead.

### Result

| RowCount | Pengdows Mean | Dapper Mean | P÷D |
|---------:|--------------:|------------:|----:|
| 100 | 311.7 μs | 263.4 μs | 1.184 |
| 1,000 | 983.8 μs | 781.9 μs | 1.258 |
| 5,000 | 3,838.2 μs | 3,744.0 μs | **1.025** |

Zero correctness failures at every row count.

### This confirms the hypothesis: the SQL Server gap is overwhelmingly the repeated session-init cost, not the actual hydration work

| | `Standard` mode (session-init paid every op) | `SingleConnection` mode (session-init paid once) |
|---|---:|---:|
| Comparable operation | `ReadList` @ RC=100: **P÷D = 1.659** | `HydrationOnly` @ RowCount=100: **P÷D = 1.184** |
| At larger volume | `ReadList` @ RC=100 stays ~1.66x regardless of row count within that query | `HydrationOnly` @ RowCount=5,000: **P÷D = 1.025** — within 2.5% of Dapper |

Once the session-settings tax is amortized to a single one-time cost instead of a
per-operation one, pengdows's actual row-materialization work is close to Dapper's — worse
at small row counts (a fixed per-call overhead is still a larger fraction of a cheap
operation), but converging to near-parity (1.025x) at 5,000 rows, the same amortization
shape already documented for SQLite's `ReadList` (`~7% slower` at 100 rows) and for the
architectural "77x" round-trip-count argument in `docs/perf/benchmark-evaluation.md`.

**Practical implication:** the `sqlserver-equal-footing-run-2026-08-13.md` numbers
represent close to a worst case for amortization — many cheap, independent, ephemeral-
connection operations (`DbMode.Standard`), each re-paying the full session-init tax. A
real workload that holds a connection for meaningful work per checkout (larger result
sets, `DbMode.SingleConnection`/`KeepAlive`, or a request that does several operations per
acquired connection) pays that tax far less often relative to the actual work done, and
the gap shrinks accordingly. This doesn't eliminate the cost documented in
`docs/planning/future-work.md` — `DbMode.Standard` genuinely does pay it on every operation — but it
does mean the *sizeable* multiplier seen there is a property of that specific connection
policy under a workload of small, independent operations, not a general statement about
pengdows's SQL Server execution path being 1.4-2x slower than Dapper across the board.

---

### Raw BDN Output

```
BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.110
  [Host]     : .NET 8.0.29 (8.0.2926.32403), X64 RyuJIT AVX2
  Job-FXYCNB : .NET 8.0.29 (8.0.2926.32403), X64 RyuJIT AVX2

IterationCount=10  WarmupCount=3
```

| Method | RowCount | Mean | Error | StdDev | P95 | P99 | Fails | Gen0 | Gen1 | Allocated | Alloc Ratio |
|--------|---------:|-----:|------:|-------:|----:|----:|------:|-----:|-----:|----------:|------------:|
| HydrationOnly_Pengdows | 100 | 311.7 μs | 5.94 μs | 3.93 μs | 316.7 μs | 317.72 | 0 | 2.4414 | - | 43.48 KB | 1.00 |
| HydrationOnly_Dapper | 100 | 263.4 μs | 9.00 μs | 5.35 μs | 271.2 μs | 273.00 | 0 | 1.9531 | - | 40.4 KB | 0.93 |
| HydrationOnly_Pengdows | 1000 | 983.8 μs | 24.41 μs | 16.15 μs | 1,006.6 μs | 1,013.10 | 0 | 23.4375 | - | 451.21 KB | 1.00 |
| HydrationOnly_Dapper | 1000 | 781.9 μs | 16.97 μs | 11.22 μs | 797.4 μs | 798.89 | 0 | 22.4609 | 6.8359 | 370.88 KB | 0.82 |
| HydrationOnly_Pengdows | 5000 | 3,838.2 μs | 76.24 μs | 45.37 μs | 3,900.1 μs | 3,915.51 | 0 | 125.0000 | 62.5000 | 2353.92 KB | 1.00 |
| HydrationOnly_Dapper | 5000 | 3,744.0 μs | 234.58 μs | 155.16 μs | 4,000.8 μs | 4,033.78 | 0 | 109.3750 | 62.5000 | 1923.06 KB | 0.82 |

Total run time: 101.8 sec, 6 benchmarks executed, 0 exceptions.
