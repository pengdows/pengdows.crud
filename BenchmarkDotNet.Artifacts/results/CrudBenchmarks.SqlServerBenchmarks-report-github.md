```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 2 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.119
  [Host]     : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2
  Job-YGBKRE : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2

InvocationCount=100  IterationCount=10  UnrollFactor=1  
WarmupCount=5  

```
| Method                       | FilmCount | ActorCount | Mean     | Error    | StdDev   | Allocated |
|----------------------------- |---------- |----------- |---------:|---------:|---------:|----------:|
| GetFilmById_Mine             | 1000      | 200        | 544.8 μs | 37.49 μs | 24.80 μs |  11.73 KB |
| GetFilmById_Mine_WithCloning | 1000      | 200        |       NA |       NA |       NA |        NA |

Benchmarks with issues:
  SqlServerBenchmarks.GetFilmById_Mine_WithCloning: Job-YGBKRE(InvocationCount=100, IterationCount=10, UnrollFactor=1, WarmupCount=5) [FilmCount=1000, ActorCount=200]
