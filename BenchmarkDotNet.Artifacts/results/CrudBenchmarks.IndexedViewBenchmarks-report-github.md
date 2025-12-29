```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.122
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  Job-DCNSPX : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

InvocationCount=50  IterationCount=5  UnrollFactor=1  
WarmupCount=3  

```
| Method                                            | CustomerCount | OrdersPerCustomer | Mean     | Error     | StdDev   | Allocated |
|-------------------------------------------------- |-------------- |------------------ |---------:|----------:|---------:|----------:|
| **GetCustomerSummary_pengdows_IndexedView**           | **1000**          | **10**                | **481.8 μs** |  **24.22 μs** |  **3.75 μs** |  **12.42 KB** |
| GetCustomerSummary_EntityFramework_Aggregation    | 1000          | 10                | 433.0 μs |  90.21 μs | 13.96 μs |  19.57 KB |
| GetCustomerSummary_EntityFramework_WithWorkaround | 1000          | 10                | 695.3 μs | 109.28 μs | 16.91 μs |  25.27 KB |
| GetCustomerSummary_DirectSQL_IndexedView          | 1000          | 10                | 260.0 μs |  82.47 μs | 21.42 μs |   6.98 KB |
| **GetCustomerSummary_pengdows_IndexedView**           | **5000**          | **10**                | **465.9 μs** |  **26.62 μs** |  **6.91 μs** |  **12.43 KB** |
| GetCustomerSummary_EntityFramework_Aggregation    | 5000          | 10                | 409.2 μs |  47.88 μs | 12.43 μs |  19.68 KB |
| GetCustomerSummary_EntityFramework_WithWorkaround | 5000          | 10                | 673.9 μs |  54.23 μs |  8.39 μs |  25.26 KB |
| GetCustomerSummary_DirectSQL_IndexedView          | 5000          | 10                | 231.8 μs |  17.04 μs |  4.43 μs |   7.01 KB |
