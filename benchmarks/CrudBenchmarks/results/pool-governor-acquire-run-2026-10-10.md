# PoolGovernor acquire benchmark — 2026-10-10

Command:

```text
CRUD_BENCH_INPROC=1 dotnet run -c Release -f net10.0 --project benchmarks/CrudBenchmarks -- --filter '*PoolGovernorAcquireBenchmarks*'
```

The run used BenchmarkDotNet 0.14.0 with 3 warmup iterations and 12 measured iterations for the out-of-process job. The benchmark isolates uncontended acquire/release, uses 1,000 operations per invocation, and includes both blocking and asynchronous paths. The process was stopped during the in-process cases; the reported cases below are the two completed out-of-process measurements.

| Path | Turnstile | Mean | 99.9% CI |
|---|---:|---:|---:|
| Blocking | no | 21.527 ns | 21.433–21.622 ns |
| Async | no | 28.438 ns | 28.235–28.642 ns |

The run had no benchmark failures. BenchmarkDotNet reported that high-priority process setup was unavailable in the sandbox; this does not change the measured workload, but results should be compared on the same host configuration.
