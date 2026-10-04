## Read-path micro-benchmarks, before/after — 2026-10-04 (REV-085)

`ReadPathMicroBenchmarks` (opt-in) isolates two hot paths changed on 2026-10-04 from any driver: an
uncontended `PoolGovernor` acquire+release, and gateway hydration of one row (string,
DateTimeOffset, TimeSpan, DateTime, decimal, int) from an in-memory reader that allocates nothing per
row in its typed getters and boxes in `GetValue`, as drivers do. The same benchmark file was run
against `9be016d7` (before) and HEAD (after the PERF-017..027 and DRY changes). 5 warmups, 15
iterations, .NET 10, Release.

```
dotnet run -c Release -f net10.0 -- --include-opt-in --filter '*ReadPathMicro*'
```

    // BenchmarkDotNet v0.14.0
    // Runtime=.NET 10.0.12 (10.0.1226.42308), X64 RyuJIT AVX2

| Method | Before (9be016d7) | After (HEAD) | Change |
|---|---:|---:|---|
| GovernorAcquireRelease | 110.4 ns ± 1.4, 144 B | 83.6 ns ± 0.4, 48 B | −24% time, −67% allocation |
| MapRow | 165.9 ns ± 0.5, 160 B | 169.4 ns ± 0.4, 80 B | +2% time, −50% allocation |

Findings:
- PERF-024 (lazy drain signal, no lock) is a clear win on both axes; the intervals don't overlap.
- Hydration allocates half as much per row (no DateTimeOffset/TimeSpan boxes, PERF-020), but one
  row now takes 3.5 ns longer, also outside the noise. The likely costs are the typed-read dispatch
  (`TypedFieldReader.Read<T>` type test, then `ReadFieldValue`'s try block) and the column-tagging
  try/catch around the row (DEC-013's `ColumnReadException`). Tracked as PERF-030. The allocation
  saving shows up as less GC work at volume (the SQL Server hydration run of the same day measured
  pengdows ahead of Dapper's async path), but the per-row CPU figure is not a win and is reported as
  measured.
