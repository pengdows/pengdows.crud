# DbParameter pooling impact

## Implementation snapshot

`SqlDialect` maintains a lightweight `ConcurrentQueue<DbParameter>` so high-churn CRUD paths can reuse parameter instances instead of paying factory allocation costs on every call. Each rent clears the instance back to a neutral state and each return pushes it into a bounded pool (100 entries) to avoid unbounded memory growth.【F:pengdows.crud/dialects/SqlDialect.cs†L73-L83】【F:pengdows.crud/dialects/SqlDialect.cs†L503-L533】

Disposal of `SqlContainer` instances is the trigger that places parameters back into the pool, so callers that wrap containers in `using`/`await using` blocks automatically participate in reuse.【F:pengdows.crud/SqlContainer.cs†L995-L1018】

## Tests that cover the pool

* `ParameterReturnedToPoolIsReusedWithCleanState` proves that a parameter returned to the pool is handed back for the next request and that its observable state (direction, size, precision, scale, DbType, value, and name) is fully reset before reuse.【F:pengdows.crud.Tests/dialects/SqlDialectParameterPoolTests.cs†L19-L43】
* `ParameterPoolRespectsMaxSize` exercises the negative/boundary case by creating more than 100 parameters, returning them, and confirming the queue never grows past the configured cap.【F:pengdows.crud.Tests/dialects/SqlDialectParameterPoolTests.cs†L45-L90】

## Practical guidance

* If you hold on to `DbParameter` instances instead of disposing the owning `SqlContainer`, you opt out of pooling and will see new allocations each time. The existing BenchmarkDotNet run for `ParameterCreationBenchmark` illustrates this: because the sample never returns the parameter, it consistently allocates ~80 bytes per invocation.【F:BenchmarkDotNet.Artifacts/results/CrudBenchmarks.ParameterCreationBenchmark-report-github.md†L11-L16】
* Let the container go out of scope promptly (ideally with `await using`) so every parameter flows through the pool. That eliminates repeat allocations on hot code paths and reduces pressure on the GC without requiring any additional configuration.

In short: the pool already delivers the primary perf win—zero-cost reuse once containers are disposed. Focus on ensuring containers are short-lived; adding another pool layer would not yield measurable gains beyond what is already in place.
