```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.122
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  Job-IRKKMI : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

InvocationCount=10  IterationCount=3  UnrollFactor=1  
WarmupCount=2  

```
| Method                              | OrderCount | OrderDetailCount | Mean      | Error     | StdDev    | Allocated |
|------------------------------------ |----------- |----------------- |----------:|----------:|----------:|----------:|
| CustomerAggregation_pengdows        | 10000      | 50000            |  7.535 ms | 0.1848 ms | 0.0101 ms |  23.64 KB |
| CustomerAggregation_EntityFramework | 10000      | 50000            | 10.996 ms | 1.5018 ms | 0.0823 ms |  41.86 KB |
| CustomerAggregation_Dapper          | 10000      | 50000            |  7.312 ms | 0.0341 ms | 0.0019 ms |  19.26 KB |
| ProductSales_pengdows               | 10000      | 50000            | 21.934 ms | 2.2895 ms | 0.1255 ms |   23.3 KB |
| ProductSales_EntityFramework        | 10000      | 50000            | 12.443 ms | 2.0322 ms | 0.1114 ms |   38.8 KB |
| MonthlyRevenue_pengdows             | 10000      | 50000            | 18.695 ms | 1.1614 ms | 0.0637 ms |  15.63 KB |
| MonthlyRevenue_EntityFramework      | 10000      | 50000            | 22.700 ms | 3.0067 ms | 0.1648 ms |   30.7 KB |
