## SQL Server Equal-Footing Benchmark — 2026-08-13

`SqlServerEqualFootingBenchmarks.cs` existed in the repo with no committed result — it was
actually broken (see "Bugs found and fixed" below). Fixed and run against a Testcontainers
SQL Server 2022 instance; all three frameworks hit the same database and schema.

### Result summary (RecordCount=100, representative)

| Operation | Pengdows Mean | Dapper Mean | EF Mean | P÷D | EF÷P |
|-----------|--------------:|------------:|--------:|----:|-----:|
| ReadSingle | 42,165 μs | 21,689 μs | 36,572 μs | 1.944 | 0.867 |
| ReadList (1 query) | 538.9 μs | 324.9 μs | 497.5 μs | 1.659 | 0.923 |
| FilteredQuery | 569.9 μs | 339.3 μs | 508.0 μs | 1.680 | 0.891 |
| Create | 69,858 μs | 47,143 μs | 59,422 μs | 1.482 | 0.851 |
| Update | 68,451 μs | 47,018 μs | 58,477 μs | 1.456 | 0.854 |
| DeleteOnly | 78,473 μs | 54,645 μs | 67,495 μs | 1.436 | 0.860 |
| Aggregate | 50,810 μs | 29,428 μs | 41,512 μs | 1.727 | 0.817 |
| ConnectionHoldTime | 445.5 μs | 233.8 μs | 369.5 μs | 1.905 | 0.829 |

Same shape at RecordCount=1 and RecordCount=10 (full data in the raw BDN output below).
Zero correctness failures (`Fails = 0`) across every row at every RecordCount.

**This is a genuinely different result from the PostgreSQL and SQLite benchmarks**: pengdows
is consistently ~1.4–2.0x *slower* than Dapper here, and — unlike every other benchmark in
this suite — also slower than EF Core (`EF÷P` consistently < 1.0, meaning EF beats pengdows
on SQL Server). Do not average this into the PostgreSQL/SQLite parity story; it is a distinct,
real finding specific to this dialect's current implementation, explained below.

### Why: pengdows re-issues its session-settings SET statement on every operation against SQL Server

This is not a benchmark-fairness bug (Dapper and EF get nothing to equalize here — pengdows
applies session settings by design; the question is *how cheaply*). Traced end to end:

- `SqlServerDialect.GetBaseSessionSettings()` returns a batched, semicolon-joined `SET ...`
  statement covering the dialect's baseline session settings, with the comment "Always
  enforce the full baseline on every connection checkout" — an intentional design choice.
- `TrackedConnection.TriggerFirstOpen`/`TriggerFirstOpenAsync`
  (`pengdows/wrappers/TrackedConnection.cs`) gate the session-settings callback on
  `_wasOpened`, an `Interlocked`-guarded flag scoped to **that wrapper instance** — not to
  the underlying physical ADO.NET connection. In `DbMode.Standard` (what this benchmark
  uses), a fresh `TrackedConnection` wrapper is constructed for every logical
  checkout, so `_wasOpened` starts at 0 every time, and the session-settings callback fires
  on **every single operation**, regardless of whether the ADO.NET provider handed back a
  warm pooled physical connection underneath.
- For PostgreSQL, this cost is eliminated by `PostgreSqlDialect.PrepareConnectionStringForDataSource`,
  which bakes the same settings into the Npgsql `NpgsqlDataSource`'s startup `Options`
  as GUC session defaults — PostgreSQL's own `RESET ALL` on pool return restores exactly
  those defaults automatically, so there is nothing left to reapply, and
  `DatabaseContext.ExecuteSessionSettings` skips outright
  (`_rwSettingsBakedIntoDataSource`/`_roSettingsBakedIntoDataSource`, checked in
  `DatabaseContext.ConnectionLifecycle.cs`). This is why PostgreSQL's `ConnectionHoldTime`
  was measured *identical* to Dapper's (164.0 vs 164.0 μs) in the equal-footing PostgreSQL
  run, while here it's roughly double (445.5 vs 233.8 μs) — almost exactly "one extra
  round trip" worth of overhead, matching a single batched `SET` statement's cost at this
  database's latency.
- **`SqlServerDialect` has no equivalent override of `PrepareConnectionStringForDataSource`.**
  The base `SqlDialect` implementation is a no-op, so the bake-and-skip optimization that
  protects PostgreSQL simply doesn't exist for SQL Server — every operation pays a live
  `SET` round trip on top of the actual query.

This is a real, quantified, previously-undocumented cost of pengdows's current SQL Server
dialect, not an artifact of an unfair benchmark. Whether an equivalent bake-in optimization
is even possible for SQL Server (TDS/`SqlClient` doesn't have a direct analog to Postgres's
arbitrary `Options=-c key=value` GUC-default mechanism) is an open question worth a
dedicated investigation — logged in `docs/FUTURE_WORK.md`.

### Bugs found and fixed to make this benchmark runnable at all

Before this run, `SqlServerEqualFootingBenchmarks` had never produced a single valid
result — every one of its 81 sub-benchmarks crashed during `GlobalSetup`'s shared prewarm
step, for two independent reasons found by actually running it:

1. **`new SqlParameter("Age", 0)` (line ~315, in `PreWarmFrameworkCachesAsync`)** — a C#
   overload-resolution gotcha: the literal `0` implicitly converts to `SqlDbType` (where
   `BigInt = 0`), so the compiler resolved this to the `SqlParameter(string, SqlDbType)`
   constructor instead of `SqlParameter(string, object)`, producing a parameter with its
   type set but **no value ever assigned**. SQL Server's error —
   `The parameterized query '(@Age bigint,@Limit int)...' expects the parameter '@Age',
   which was not supplied` — names the exact symptom (`@Age bigint`, confirming the
   `SqlDbType.BigInt` misinterpretation). Fixed by changing the placeholder value from `0`
   to `30`, matching the value the real timed `ReadList_*` methods already use.
2. **`efCtx.Database.SqlQueryRaw<double>(aggregateSql)` with an unaliased `AVG(salary)`
   column** — EF Core wraps `SqlQueryRaw<TScalar>` in a derived-table subquery
   (`SELECT ... FROM (<sql>) AS t`), and SQL Server requires every column in a derived
   table to have a name. Fixed by aliasing the aggregate: `SELECT AVG(salary) AS Value ...`.
   The actual timed `Aggregate_EntityFramework` benchmark method uses raw
   `DbCommand.ExecuteScalarAsync()` instead (no derived-table wrapping), so it was never
   affected — only the shared prewarm helper was.

Both bugs lived only in the prewarm helper, not in the timed benchmark methods themselves,
so fixing them didn't change what's actually being measured.

---

### Raw BDN Output

```
BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.110
  [Host]     : .NET 8.0.29 (8.0.2926.32403), X64 RyuJIT AVX2
  Job-HMSDSD : .NET 8.0.29 (8.0.2926.32403), X64 RyuJIT AVX2

IterationCount=10  WarmupCount=3
```

#### RecordCount=1

| Scenario | Pengdows Mean | Dapper Mean | EF Mean | P÷D | EF÷P |
|----------|--------------:|------------:|--------:|----:|-----:|
| Aggregate | 548.56 μs | 320.75 μs | 423.75 μs | 1.710 | 0.772 |
| Breakdown_BuildVsExecute | 453.78 μs | 230.66 μs | 375.97 μs | 1.967 | 0.829 |
| ConnectionHoldTime | 445.37 μs | 236.58 μs | 392.02 μs | 1.883 | 0.880 |
| Create | 709.34 μs | 487.11 μs | 621.44 μs | 1.456 | 0.876 |
| DeleteOnly | 793.52 μs | 572.91 μs | 704.67 μs | 1.385 | 0.888 |
| FilteredQuery | 449.83 μs | 237.80 μs | 390.07 μs | 1.892 | 0.867 |
| ReadList | 453.86 μs | 229.61 μs | 378.79 μs | 1.977 | 0.835 |
| ReadSingle | 452.17 μs | 232.61 μs | 377.46 μs | 1.944 | 0.835 |
| Update | 700.40 μs | 473.86 μs | 590.55 μs | 1.478 | 0.843 |

#### RecordCount=10

| Scenario | Pengdows Mean | Dapper Mean | EF Mean | P÷D | EF÷P |
|----------|--------------:|------------:|--------:|----:|-----:|
| Aggregate | 5,100.69 μs | 3,059.95 μs | 4,056.35 μs | 1.667 | 0.795 |
| Breakdown_BuildVsExecute | 4,314.72 μs | 2,185.53 μs | 3,723.28 μs | 1.974 | 0.863 |
| ConnectionHoldTime | 440.51 μs | 227.53 μs | 381.28 μs | 1.936 | 0.866 |
| Create | 7,364.31 μs | 4,766.11 μs | 5,973.99 μs | 1.545 | 0.811 |
| DeleteOnly | 7,746.86 μs | 5,757.50 μs | 7,175.07 μs | 1.346 | 0.926 |
| FilteredQuery | 461.53 μs | 241.30 μs | 384.68 μs | 1.913 | 0.834 |
| ReadList | 478.45 μs | 238.46 μs | 383.17 μs | 2.006 | 0.801 |
| ReadSingle | 4,325.41 μs | 2,171.91 μs | 3,682.16 μs | 1.992 | 0.851 |
| Update | 6,825.91 μs | 4,604.25 μs | 5,851.45 μs | 1.483 | 0.857 |

#### RecordCount=100

| Scenario | Pengdows Mean | Dapper Mean | EF Mean | P÷D | EF÷P |
|----------|--------------:|------------:|--------:|----:|-----:|
| Aggregate | 50,810.18 μs | 29,428.17 μs | 41,511.49 μs | 1.727 | 0.817 |
| Breakdown_BuildVsExecute | 43,315.61 μs | 21,323.35 μs | 37,017.78 μs | 2.031 | 0.855 |
| ConnectionHoldTime | 445.51 μs | 233.81 μs | 369.47 μs | 1.905 | 0.829 |
| Create | 69,858.10 μs | 47,143.46 μs | 59,421.92 μs | 1.482 | 0.851 |
| DeleteOnly | 78,472.94 μs | 54,644.67 μs | 67,495.40 μs | 1.436 | 0.860 |
| FilteredQuery | 569.92 μs | 339.30 μs | 508.03 μs | 1.680 | 0.891 |
| ReadList | 538.88 μs | 324.89 μs | 497.50 μs | 1.659 | 0.923 |
| ReadSingle | 42,165.48 μs | 21,688.99 μs | 36,571.63 μs | 1.944 | 0.867 |
| Update | 68,451.42 μs | 47,018.32 μs | 58,476.78 μs | 1.456 | 0.854 |

Total run time: 1,378 sec (~23 min), 81 benchmarks executed, 0 exceptions.
