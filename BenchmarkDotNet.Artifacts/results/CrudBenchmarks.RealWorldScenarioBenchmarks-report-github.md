```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.122
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  Job-IRKKMI : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

InvocationCount=10  IterationCount=3  UnrollFactor=1  
WarmupCount=2  

```
| Method                                    | TransactionCount | Mean       | Error    | StdDev   | Allocated |
|------------------------------------------ |----------------- |-----------:|---------:|---------:|----------:|
| ComplexQuery_pengdows                     | 5000             | 1,635.4 μs | 400.2 μs | 21.93 μs |  96.27 KB |
| ComplexQuery_EntityFramework              | 5000             |   628.9 μs | 110.3 μs |  6.05 μs |  67.88 KB |
| ComplexQuery_Dapper                       | 5000             | 1,255.1 μs | 817.4 μs | 44.80 μs |  99.22 KB |
| FullTextSearchAggregation_pengdows        | 5000             | 4,280.7 μs | 115.6 μs |  6.34 μs |  28.22 KB |
| FullTextSearchAggregation_EntityFramework | 5000             |   622.6 μs | 619.0 μs | 33.93 μs |  70.47 KB |
| BulkUpsert_pengdows                       | 5000             |   633.5 μs | 997.7 μs | 54.69 μs |  46.43 KB |
| BulkUpsert_EntityFramework                | 5000             |   698.0 μs | 387.2 μs | 21.23 μs |  22.94 KB |
