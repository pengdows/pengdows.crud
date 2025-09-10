```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 2 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.119
  [Host]     : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2
  Job-WHKJOA : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2

InvocationCount=100  IterationCount=10  UnrollFactor=1  
WarmupCount=5  

```
| Method             | FilmCount | ActorCount | Mean     | Error    | StdDev   | Allocated |
|------------------- |---------- |----------- |---------:|---------:|---------:|----------:|
| GetFilmById_Mine   | 1000      | 200        | 492.0 μs | 31.97 μs | 21.14 μs |  15.36 KB |
| GetFilmById_Dapper | 1000      | 200        | 291.4 μs | 16.82 μs | 10.01 μs |    3.5 KB |
