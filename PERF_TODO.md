# Performance checklist (1.1)

This is a working list to track the performance optimization items from the latest allocation run.

## Confirmed in place (keep verifying)
1) Cache per-entity SQL text and parameter layouts
- Implemented via cached templates in `pengdows.crud/EntityHelper.Sql.cs` and query caching in `pengdows.crud/EntityHelper.Core.cs`/`pengdows.crud/EntityHelper.Retrieve.cs`.
- Follow-up: add a small targeted benchmark or unit-level perf test to ensure template reuse is actually hit in hot paths.

## To do (ranked by expected impact)
2) Reuse DbParameter instances for fixed-shape commands
- Build template parameter arrays and reuse per execution (only updating values).
- Goal: reduce `CreateParameter` allocations and per-call dictionary work.

3) Reduce per-row materialization overhead
- Cache ordinals once per reader, use typed getters (avoid boxing/Convert.ChangeType when possible).
- Reduce per-row dictionary lookups in mapper.
 - Implemented typed setters (no per-setter object cast) in `pengdows.crud/DataReaderMapper.cs`; Pagila results were effectively flat.

4) Remove LINQ/string joins in hot paths
- Replace `Select/Join` and string concatenations used in frequently executed paths.
- Ensure log parameter dump is fully allocation-free when log level is off.
 - BuildCreate SQL generation now uses direct appends (see `pengdows.crud/EntityHelper.Core.cs`) and was measured to reduce allocs/time in `SqlGenerationBenchmark`.

5) Tighten list allocations
- Pre-size result lists when row counts are known/estimable (e.g., GetTenFilms).
- Avoid temporary List growth patterns in hot loops.

## Latest measurements
- Small-parameter storage in `pengdows.crud/collections/OrderedDictionary.cs` (inline mode up to 8 entries) did not materially change Pagila end-to-end results or allocations.

## Evidence
- Benchmarks (memory): `benchmarks/CrudBenchmarks/BenchmarkDotNet.Artifacts/results/CrudBenchmarks.PagilaBenchmarks-report-github.md`.
