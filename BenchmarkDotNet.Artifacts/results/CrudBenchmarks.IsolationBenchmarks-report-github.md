```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.122
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  Job-LXTBWT : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

IterationCount=10  WarmupCount=3  

```
| Method                            | Mean          | Error       | StdDev      | Ratio  | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------------------- |--------------:|------------:|------------:|-------:|--------:|-------:|-------:|----------:|------------:|
| SqlGeneration_Mine_BuildContainer |   581.4681 ns |  18.9146 ns |  12.5109 ns |  1.000 |    0.03 | 0.1402 |      - |    2352 B |        1.00 |
| SqlGeneration_Mine_GetSql         |   605.2598 ns |  26.0960 ns |  17.2609 ns |  1.041 |    0.04 | 0.1507 |      - |    2536 B |        1.08 |
| SqlGeneration_Static              |     0.2388 ns |   0.0171 ns |   0.0090 ns |  0.000 |    0.00 |      - |      - |         - |        0.00 |
| ObjectLoading_Mine                | 6,245.9692 ns | 587.1492 ns | 388.3629 ns | 10.746 |    0.67 | 0.3662 | 0.3586 |    6248 B |        2.66 |
| ObjectLoading_Mine_DirectReader   | 2,505.4273 ns |  35.2786 ns |  23.3346 ns |  4.311 |    0.10 | 0.2708 |      - |    4536 B |        1.93 |
| ObjectLoading_Dapper              | 2,503.6594 ns |  51.1193 ns |  33.8123 ns |  4.308 |    0.10 | 0.2708 |      - |    4536 B |        1.93 |
| ParameterCreation_Mine            |    86.7233 ns |   0.7631 ns |   0.4541 ns |  0.149 |    0.00 | 0.0076 |      - |     128 B |        0.05 |
| ParameterCreation_Dapper          |     4.4286 ns |   0.2250 ns |   0.1488 ns |  0.008 |    0.00 | 0.0014 |      - |      24 B |        0.01 |
| ConnectionOverhead_Mine           | 2,352.2975 ns |  64.5227 ns |  42.6777 ns |  4.047 |    0.11 | 0.2441 |      - |    4128 B |        1.76 |
| ConnectionOverhead_Direct         | 2,330.1180 ns |  38.3838 ns |  25.3885 ns |  4.009 |    0.09 | 0.2441 |      - |    4128 B |        1.76 |
