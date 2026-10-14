# Indexed View Benchmark Validation

This page describes the validation helper for SQL Server indexed-view benchmarks. **No benchmark in
`benchmarks/CrudBenchmarks` calls it today**: the helper (`BenchmarkValidation.SqlServer.cs`, with
`IndexedViewEnvironment.cs` and `verify-validation-artifacts.sh`) is in place, but the indexed-view benchmark
families that would use it (the `DirectView` and `AutoSubstitution` variants below) have not been written. The layout
below is what the helper produces for a family that does call it; the one view benchmark that exists,
`MaterializedViewPerformanceBenchmarks`, is PostgreSQL-only and does not use it. Why SQL Server session settings matter
for indexed views: [sql-server-session-settings.md](sql-server-session-settings.md).

A benchmark that calls the helper validates, before the timed iterations start, that the indexed view is indexed, the
optimizer uses the expected objects, and the session settings are correct.

## What is validated

A benchmark variant that uses `BenchmarkValidation.SqlServer` gets, automatically:

- Confirm `dbo.vw_CustomerOrderSummary` has a **unique clustered index**.
- Capture `SET` options via `DBCC USEROPTIONS` and assert the required SQL Server session flags (e.g., `ARITHABORT ON`, `ANSI_WARNINGS ON`, etc.).
- Capture `SET STATISTICS XML ON` output and write the SHOWPLAN XML to `BenchmarkDotNet.Artifacts/validation/<family>/<variant>/plan.xml`.
- Fail fast if the plan does not reference the indexed view (or, in the `NoSetup` variant, if it unexpectedly does).

If the validation fails, the benchmark run aborts with a clear error message containing the plan path, session dump, and the violated assertion.

## Artifact layout

After a successful run of a family that uses the helper, the following structure is populated (family and variant names are illustrative):

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
