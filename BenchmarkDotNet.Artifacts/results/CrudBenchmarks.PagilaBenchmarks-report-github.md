```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 2 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.119
  [Host]     : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2
  Job-YGBKRE : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2

InvocationCount=100  IterationCount=10  UnrollFactor=1  
WarmupCount=5  

```
| Method           | FilmCount | ActorCount | Mean     | Error    | StdDev   | Allocated |
|----------------- |---------- |----------- |---------:|---------:|---------:|----------:|
| GetFilmById_Mine | 1000      | 200        | 440.6 μs | 22.40 μs | 14.81 μs |  13.93 KB |
