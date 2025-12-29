```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.122
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  Job-GEIFAH : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

InvocationCount=100  IterationCount=10  UnrollFactor=1  
WarmupCount=5  

```
| Method                       | FilmCount | ActorCount | Mean     | Error    | StdDev   | Allocated |
|----------------------------- |---------- |----------- |---------:|---------:|---------:|----------:|
| GetFilmActorComposite_Mine   | 1000      | 200        | 568.3 μs | 29.45 μs | 19.48 μs |  13.13 KB |
| GetFilmActorComposite_Dapper | 1000      | 200        | 298.3 μs | 17.85 μs | 10.62 μs |   6.38 KB |
