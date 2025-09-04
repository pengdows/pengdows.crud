```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 2 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.119
  [Host]     : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2


```
| Method                      | FilmCount | ActorCount | Mean     | Error     | StdDev    | Allocated |
|---------------------------- |---------- |----------- |---------:|----------:|----------:|----------:|
| InsertThenDeleteFilm_Mine   | 1000      | 200        | 1.330 ms | 0.0185 ms | 0.0173 ms |   20.6 KB |
| InsertThenDeleteFilm_Dapper | 1000      | 200        | 1.101 ms | 0.0115 ms | 0.0096 ms |   4.41 KB |
