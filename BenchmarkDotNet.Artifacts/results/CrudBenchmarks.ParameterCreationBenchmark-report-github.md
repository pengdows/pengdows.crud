```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.122
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  Job-XHQTZQ : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

IterationCount=10  WarmupCount=3  

```
| Method                         | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------- |----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| ParameterCreation_Dapper       |  4.322 ns | 0.1207 ns | 0.0798 ns |  1.00 |    0.03 | 0.0014 |      24 B |        1.00 |
| ParameterCreation_Mine_Named   | 32.427 ns | 0.2796 ns | 0.1849 ns |  7.51 |    0.14 | 0.0048 |      80 B |        3.33 |
| ParameterCreation_Mine_Unnamed | 32.510 ns | 0.3470 ns | 0.2295 ns |  7.52 |    0.14 | 0.0048 |      80 B |        3.33 |
| ParameterCreation_Mine_String  | 33.333 ns | 0.4933 ns | 0.3263 ns |  7.72 |    0.16 | 0.0033 |      56 B |        2.33 |
