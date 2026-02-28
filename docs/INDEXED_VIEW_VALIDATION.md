# Indexed View Benchmark Validation

The new indexed-view benchmarks are proof-based: every scenario validates that the indexed view is indexed, the optimizer uses the expected objects, and the session settings are correct before the timed iterations start.

## What is validated

Each benchmark variant uses `BenchmarkValidation.SqlServer` to automatically:

- Confirm `dbo.vw_CustomerOrderSummary` has a **unique clustered index**.
- Capture `SET` options via `DBCC USEROPTIONS` and assert the required SQL Server session flags (e.g., `ARITHABORT ON`, `ANSI_WARNINGS ON`, etc.).
- Capture `SET STATISTICS XML ON` output and write the SHOWPLAN XML to `BenchmarkDotNet.Artifacts/validation/<family>/<variant>/plan.xml`.
- Fail fast if the plan does not reference the indexed view (or, in the `NoSetup` variant, if it unexpectedly does).

If the validation fails, the benchmark run aborts with a clear error message containing the plan path, session dump, and the violated assertion.

## Artifact layout

After a successful run the following structure is populated:

```
BenchmarkDotNet.Artifacts/validation/
├── DirectView/ViewQuery/
│   ├── plan.xml
│   └── session-options.txt
└── AutoSubstitution/
    ├── NoSetup/
    │   ├── plan.xml
    │   └── session-options.txt
    ├── ManualSetup/
    └── PengdowsAuto/
```

Each `session-options.txt` file contains the `DBCC USEROPTIONS` dump recorded during validation; `plan.xml` holds the actual SHOWPLAN XML proving the indexed view was used (or not, for the `NoSetup` baseline).

## Verifying artifacts

Run the helper script to ensure every variant has emitted both files:

```bash
./benchmarks/CrudBenchmarks/verify-validation-artifacts.sh
```

If the script fails, rerun the corresponding benchmark family so the validation can re-run under a clean SQL Server instance.

## Fairness reporting

`MixedLoadFairnessBenchmarks` prints tail latencies for the writer workload (p50/p95/p99 wait time and p95 execution time) together with the final reader/writer `PoolStatisticsSnapshot`. Use these console logs to compare writer latency under read-heavy load and confirm the governor prevents starvation.
