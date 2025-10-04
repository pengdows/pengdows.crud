```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 2 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.119
  [Host]     : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2
  Job-WMMJVD : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2
  Dry        : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2


```
| Method                            | Job        | IterationCount | LaunchCount | RunStrategy | UnrollFactor | WarmupCount | Mean | Error | Ratio | RatioSD | Alloc Ratio |
|---------------------------------- |----------- |--------------- |------------ |------------ |------------- |------------ |-----:|------:|------:|--------:|------------:|
| SqlGeneration_Mine_BuildContainer | Job-WMMJVD | 10             | Default     | Default     | 16           | 3           |   NA |    NA |     ? |       ? |           ? |
| SqlGeneration_Mine_GetSql         | Job-WMMJVD | 10             | Default     | Default     | 16           | 3           |   NA |    NA |     ? |       ? |           ? |
| SqlGeneration_Static              | Job-WMMJVD | 10             | Default     | Default     | 16           | 3           |   NA |    NA |     ? |       ? |           ? |
|                                   |            |                |             |             |              |             |      |       |       |         |             |
| SqlGeneration_Mine_BuildContainer | Dry        | 1              | 1           | ColdStart   | 1            | 1           |   NA |    NA |     ? |       ? |           ? |
| SqlGeneration_Mine_GetSql         | Dry        | 1              | 1           | ColdStart   | 1            | 1           |   NA |    NA |     ? |       ? |           ? |
| SqlGeneration_Static              | Dry        | 1              | 1           | ColdStart   | 1            | 1           |   NA |    NA |     ? |       ? |           ? |

Benchmarks with issues:
  IsolationBenchmarks.SqlGeneration_Mine_BuildContainer: Job-WMMJVD(IterationCount=10, WarmupCount=3)
  IsolationBenchmarks.SqlGeneration_Mine_GetSql: Job-WMMJVD(IterationCount=10, WarmupCount=3)
  IsolationBenchmarks.SqlGeneration_Static: Job-WMMJVD(IterationCount=10, WarmupCount=3)
  IsolationBenchmarks.SqlGeneration_Mine_BuildContainer: Dry(IterationCount=1, LaunchCount=1, RunStrategy=ColdStart, UnrollFactor=1, WarmupCount=1)
  IsolationBenchmarks.SqlGeneration_Mine_GetSql: Dry(IterationCount=1, LaunchCount=1, RunStrategy=ColdStart, UnrollFactor=1, WarmupCount=1)
  IsolationBenchmarks.SqlGeneration_Static: Dry(IterationCount=1, LaunchCount=1, RunStrategy=ColdStart, UnrollFactor=1, WarmupCount=1)
