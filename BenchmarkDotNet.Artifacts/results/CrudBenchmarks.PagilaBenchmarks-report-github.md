```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.122
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  Job-BMOYPS : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

InvocationCount=100  IterationCount=10  UnrollFactor=1  
WarmupCount=5  

```
| Method                     | FilmCount | ActorCount | Mean     | Error    | StdDev   | Allocated |
|--------------------------- |---------- |----------- |---------:|---------:|---------:|----------:|
| GetFilmById_Mine           | 1000      | 200        | 419.2 μs | 17.41 μs | 11.52 μs |  14.49 KB |
| GetFilmById_Mine_Breakdown | 1000      | 200        | 452.7 μs | 28.01 μs | 18.53 μs |  15.84 KB |
