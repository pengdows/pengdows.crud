```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.122
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  Job-DCNSPX : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

InvocationCount=50  IterationCount=5  UnrollFactor=1  
WarmupCount=3  

```
| Method                                             | CustomerCount | OrdersPerCustomer | Mean     | Error     | StdDev   | Allocated |
|--------------------------------------------------- |-------------- |------------------ |---------:|----------:|---------:|----------:|
| **GetCustomerSummary_pengdows_MaterializedView**       | **2000**          | **15**                | **356.5 μs** |  **24.35 μs** |  **3.77 μs** |   **13.8 KB** |
| GetCustomerSummary_Dapper_TableScan                | 2000          | 15                | 308.8 μs |  78.89 μs | 20.49 μs |   4.31 KB |
| GetCustomerSummary_Dapper_ExplicitMaterializedView | 2000          | 15                | 229.1 μs | 101.70 μs | 15.74 μs |   4.23 KB |
| GetCustomerSummary_EntityFramework_TableScan       | 2000          | 15                | 465.1 μs |  37.33 μs |  5.78 μs |  17.33 KB |
| **GetCustomerSummary_pengdows_MaterializedView**       | **5000**          | **15**                | **355.0 μs** |  **20.57 μs** |  **5.34 μs** |  **13.79 KB** |
| GetCustomerSummary_Dapper_TableScan                | 5000          | 15                | 279.7 μs |   8.82 μs |  2.29 μs |   4.25 KB |
| GetCustomerSummary_Dapper_ExplicitMaterializedView | 5000          | 15                | 239.5 μs |  66.99 μs | 17.40 μs |   4.18 KB |
| GetCustomerSummary_EntityFramework_TableScan       | 5000          | 15                | 456.7 μs |  45.41 μs |  7.03 μs |   17.4 KB |
