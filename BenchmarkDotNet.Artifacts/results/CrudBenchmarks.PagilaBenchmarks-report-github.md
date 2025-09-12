```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 2 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.119
  [Host]     : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2
  Job-ARXIKU : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2

UnrollFactor=1  

```
| Method                                | Job        | InvocationCount | IterationCount | LaunchCount | WarmupCount | FilmCount | ActorCount | Mean       | Error       | StdDev    | Median     | Allocated |
|-------------------------------------- |----------- |---------------- |--------------- |------------ |------------ |---------- |----------- |-----------:|------------:|----------:|-----------:|----------:|
| GetFilmById_Mine_Traditional          | Job-ARXIKU | 100             | 10             | Default     | 5           | 1000      | 200        |   504.5 μs |    38.84 μs |  25.69 μs |   501.3 μs |  15.35 KB |
| GetFilmById_Mine_FastPath             | Job-ARXIKU | 100             | 10             | Default     | 5           | 1000      | 200        |         NA |          NA |        NA |         NA |        NA |
| GetFilmById_Dapper                    | Job-ARXIKU | 100             | 10             | Default     | 5           | 1000      | 200        |   301.1 μs |    16.45 μs |  10.88 μs |   300.8 μs |   3.47 KB |
| GetFilmActorComposite_Mine            | Job-ARXIKU | 100             | 10             | Default     | 5           | 1000      | 200        |   462.8 μs |    24.44 μs |  16.17 μs |   469.5 μs |  16.31 KB |
| GetFilmActorComposite_Dapper          | Job-ARXIKU | 100             | 10             | Default     | 5           | 1000      | 200        |   296.5 μs |    10.91 μs |   6.49 μs |   294.9 μs |   3.66 KB |
| UpdateFilm_Mine                       | Job-ARXIKU | 100             | 10             | Default     | 5           | 1000      | 200        | 1,018.3 μs |   113.10 μs |  74.81 μs | 1,024.8 μs |  29.44 KB |
| UpdateFilm_Dapper                     | Job-ARXIKU | 100             | 10             | Default     | 5           | 1000      | 200        |   643.5 μs |    29.50 μs |  19.51 μs |   637.2 μs |   4.34 KB |
| InsertThenDeleteFilm_Mine_Traditional | Job-ARXIKU | 100             | 10             | Default     | 5           | 1000      | 200        | 1,399.7 μs |    43.74 μs |  26.03 μs | 1,395.4 μs |  27.26 KB |
| InsertThenDeleteFilm_Mine_FastPath    | Job-ARXIKU | 100             | 10             | Default     | 5           | 1000      | 200        | 1,488.8 μs |   126.01 μs |  83.35 μs | 1,508.6 μs |  28.45 KB |
| InsertThenDeleteFilm_Dapper           | Job-ARXIKU | 100             | 10             | Default     | 5           | 1000      | 200        | 1,151.5 μs |    21.13 μs |  13.98 μs | 1,147.8 μs |   4.39 KB |
| GetTenFilms_Mine_Traditional          | Job-ARXIKU | 100             | 10             | Default     | 5           | 1000      | 200        |   568.6 μs |    54.21 μs |  35.85 μs |   561.8 μs |  17.09 KB |
| GetTenFilms_Mine_FastPath             | Job-ARXIKU | 100             | 10             | Default     | 5           | 1000      | 200        |         NA |          NA |        NA |         NA |        NA |
| GetTenFilms_Dapper                    | Job-ARXIKU | 100             | 10             | Default     | 5           | 1000      | 200        |   331.6 μs |    15.63 μs |  10.34 μs |   330.0 μs |   5.55 KB |
| GetFilmById_Mine_Traditional          | ShortRun   | 1               | 3              | 1           | 3           | 1000      | 200        |   801.6 μs | 4,767.31 μs | 261.31 μs |   677.6 μs |   16.9 KB |
| GetFilmById_Mine_FastPath             | ShortRun   | 1               | 3              | 1           | 3           | 1000      | 200        |         NA |          NA |        NA |         NA |        NA |
| GetFilmById_Dapper                    | ShortRun   | 1               | 3              | 1           | 3           | 1000      | 200        |   364.0 μs |   277.96 μs |  15.24 μs |   363.9 μs |   4.81 KB |
| GetFilmActorComposite_Mine            | ShortRun   | 1               | 3              | 1           | 3           | 1000      | 200        |   820.4 μs | 5,404.78 μs | 296.25 μs |   653.9 μs |   17.9 KB |
| GetFilmActorComposite_Dapper          | ShortRun   | 1               | 3              | 1           | 3           | 1000      | 200        |   416.8 μs |   605.38 μs |  33.18 μs |   402.9 μs |   4.98 KB |
| UpdateFilm_Mine                       | ShortRun   | 1               | 3              | 1           | 3           | 1000      | 200        | 1,270.2 μs | 1,225.29 μs |  67.16 μs | 1,266.5 μs |  32.63 KB |
| UpdateFilm_Dapper                     | ShortRun   | 1               | 3              | 1           | 3           | 1000      | 200        |   820.8 μs |   936.87 μs |  51.35 μs |   813.5 μs |   6.37 KB |
| InsertThenDeleteFilm_Mine_Traditional | ShortRun   | 1               | 3              | 1           | 3           | 1000      | 200        | 1,731.7 μs |   647.65 μs |  35.50 μs | 1,730.8 μs |  29.64 KB |
| InsertThenDeleteFilm_Mine_FastPath    | ShortRun   | 1               | 3              | 1           | 3           | 1000      | 200        | 1,784.5 μs | 1,486.35 μs |  81.47 μs | 1,756.8 μs |  31.58 KB |
| InsertThenDeleteFilm_Dapper           | ShortRun   | 1               | 3              | 1           | 3           | 1000      | 200        | 1,322.3 μs |   760.51 μs |  41.69 μs | 1,331.2 μs |   6.46 KB |
| GetTenFilms_Mine_Traditional          | ShortRun   | 1               | 3              | 1           | 3           | 1000      | 200        |   824.5 μs | 5,459.33 μs | 299.24 μs |   667.9 μs |  18.63 KB |
| GetTenFilms_Mine_FastPath             | ShortRun   | 1               | 3              | 1           | 3           | 1000      | 200        |         NA |          NA |        NA |         NA |        NA |
| GetTenFilms_Dapper                    | ShortRun   | 1               | 3              | 1           | 3           | 1000      | 200        |   451.0 μs |   276.60 μs |  15.16 μs |   449.2 μs |   7.12 KB |

Benchmarks with issues:
  PagilaBenchmarks.GetFilmById_Mine_FastPath: Job-ARXIKU(InvocationCount=100, IterationCount=10, UnrollFactor=1, WarmupCount=5) [FilmCount=1000, ActorCount=200]
  PagilaBenchmarks.GetTenFilms_Mine_FastPath: Job-ARXIKU(InvocationCount=100, IterationCount=10, UnrollFactor=1, WarmupCount=5) [FilmCount=1000, ActorCount=200]
  PagilaBenchmarks.GetFilmById_Mine_FastPath: ShortRun(InvocationCount=1, IterationCount=3, LaunchCount=1, UnrollFactor=1, WarmupCount=3) [FilmCount=1000, ActorCount=200]
  PagilaBenchmarks.GetTenFilms_Mine_FastPath: ShortRun(InvocationCount=1, IterationCount=3, LaunchCount=1, UnrollFactor=1, WarmupCount=3) [FilmCount=1000, ActorCount=200]
