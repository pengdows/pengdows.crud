```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.122
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  Job-OFXEXD : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

IterationCount=12  WarmupCount=3  

```
| Method                  | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------ |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| MutableDictionaryLookup | 18.51 ns | 0.032 ns | 0.025 ns |  1.00 |    0.00 |         - |          NA |
| FrozenDictionaryLookup  | 29.58 ns | 0.448 ns | 0.350 ns |  1.60 |    0.02 |         - |          NA |
