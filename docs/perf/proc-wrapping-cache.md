# Stored procedure strategy cache analysis

`ProcWrappingStrategyFactory` only needs to resolve six stored procedure wrapping
strategies. The original change that swapped the cache to `FrozenDictionary` was meant
to reduce lookup overhead, but the fixed set of keys keeps both dictionary options in
the same branch-prediction sweet spot. In practice the call site spends far more time
binding parameters than choosing the strategy, so the frozen variant does not provide a
meaningful advantage while it adds a freezing step to the static constructor.

To make the trade-off easy to evaluate, the benchmark suite now includes
`ProcWrappingStrategyLookupBenchmarks`. The benchmark exercises both the mutable and
frozen implementations across the same lookup sequence the runtime uses. You can
execute it locally with the following steps:

1. Navigate to `benchmarks/CrudBenchmarks`.
2. Run `dotnet run -c Release --filter ProcWrappingStrategyLookupBenchmarks`.

The expectation is that both variants remain within noise of each other because the
cache contains only a handful of entries and they are constructed once. Keeping the
standard dictionary therefore keeps the implementation simple without sacrificing
observable performance.
