```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.122
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  Job-SQIMKT : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

InvocationCount=25  IterationCount=5  UnrollFactor=1  
WarmupCount=3  

```
| Method                                      | ProductCount | CategoryCount | Mean       | Error       | StdDev      | Gen0    | Allocated  |
|-------------------------------------------- |------------- |-------------- |-----------:|------------:|------------:|--------:|-----------:|
| PostgreSQL_JSONB_Query_pengdows             | 1000         | 50            | 1,139.3 μs |    144.2 μs |    37.45 μs |       - |  102.07 KB |
| PostgreSQL_JSONB_Query_EntityFramework      | 1000         | 50            |         NA |          NA |          NA |      NA |         NA |
| PostgreSQL_JSONB_Query_Dapper               | 1000         | 50            |   733.3 μs |    142.6 μs |    37.02 μs |       - |   62.53 KB |
| PostgreSQL_Array_Contains_pengdows          | 1000         | 50            | 5,085.7 μs |  2,192.8 μs |   569.46 μs |       - |  614.02 KB |
| PostgreSQL_Array_Contains_EntityFramework   | 1000         | 50            |         NA |          NA |          NA |      NA |         NA |
| PostgreSQL_FullTextSearch_pengdows          | 1000         | 50            | 8,462.0 μs | 18,691.5 μs | 4,854.12 μs | 80.0000 | 1475.99 KB |
| PostgreSQL_FullTextSearch_EntityFramework   | 1000         | 50            |         NA |          NA |          NA |      NA |         NA |
| PostgreSQL_Geospatial_Query_pengdows        | 1000         | 50            | 2,501.9 μs |    299.2 μs |    46.30 μs |       - |  289.19 KB |
| PostgreSQL_Geospatial_Query_EntityFramework | 1000         | 50            |         NA |          NA |          NA |      NA |         NA |

Benchmarks with issues:
  DatabaseSpecificFeatureBenchmarks.PostgreSQL_JSONB_Query_EntityFramework: Job-SQIMKT(InvocationCount=25, IterationCount=5, UnrollFactor=1, WarmupCount=3) [ProductCount=1000, CategoryCount=50]
  DatabaseSpecificFeatureBenchmarks.PostgreSQL_Array_Contains_EntityFramework: Job-SQIMKT(InvocationCount=25, IterationCount=5, UnrollFactor=1, WarmupCount=3) [ProductCount=1000, CategoryCount=50]
  DatabaseSpecificFeatureBenchmarks.PostgreSQL_FullTextSearch_EntityFramework: Job-SQIMKT(InvocationCount=25, IterationCount=5, UnrollFactor=1, WarmupCount=3) [ProductCount=1000, CategoryCount=50]
  DatabaseSpecificFeatureBenchmarks.PostgreSQL_Geospatial_Query_EntityFramework: Job-SQIMKT(InvocationCount=25, IterationCount=5, UnrollFactor=1, WarmupCount=3) [ProductCount=1000, CategoryCount=50]
