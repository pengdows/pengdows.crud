```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.122
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  Job-BYUMOM : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

IterationCount=5  WarmupCount=3  

```
| Method                    | Mean     | Error    | StdDev  | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------------------------- |---------:|---------:|--------:|------:|-------:|-------:|----------:|------------:|
| BuildRetrieve_Traditional | 531.9 ns | 22.50 ns | 5.84 ns |  1.00 | 0.1402 |      - |    2.3 KB |        1.00 |
| BuildRetrieve_WithCloning | 234.2 ns |  3.20 ns | 0.83 ns |  0.44 | 0.0923 | 0.0005 |   1.51 KB |        0.66 |
| BasicSqlContainer         | 129.4 ns |  3.46 ns | 0.90 ns |  0.24 | 0.0823 |      - |   1.34 KB |        0.59 |
