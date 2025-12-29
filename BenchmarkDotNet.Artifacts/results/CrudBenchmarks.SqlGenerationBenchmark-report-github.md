```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.122
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  Job-OHXADR : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2

IterationCount=10  WarmupCount=3  

```
| Method                             | Mean        | Error     | StdDev    | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------------------------- |------------:|----------:|----------:|------:|-------:|-------:|----------:|------------:|
| SqlGeneration_Mine_BuildContainer  | 531.1183 ns | 5.2363 ns | 3.1160 ns | 1.000 | 0.1402 |      - |    2352 B |        1.00 |
| SqlGeneration_Mine_GetSql          | 550.1152 ns | 4.3889 ns | 2.9030 ns | 1.036 | 0.1507 |      - |    2536 B |        1.08 |
| SqlGeneration_Static               |   0.2295 ns | 0.0030 ns | 0.0020 ns | 0.000 |      - |      - |         - |        0.00 |
| SqlGeneration_Mine_CreateContainer | 140.5453 ns | 0.6714 ns | 0.4441 ns | 0.265 | 0.0823 |      - |    1376 B |        0.59 |
| SqlGeneration_Mine_BuildUpdate     | 968.1361 ns | 4.0366 ns | 2.6700 ns | 1.823 | 0.1659 |      - |    2792 B |        1.19 |
| SqlGeneration_Mine_BuildCreate     | 525.2358 ns | 3.4413 ns | 2.2762 ns | 0.989 | 0.1278 |      - |    2144 B |        0.91 |
| ParameterCreation_Mine             |  78.3661 ns | 0.4308 ns | 0.2849 ns | 0.148 | 0.0076 |      - |     128 B |        0.05 |
| ParameterCreation_Dapper           |   4.0233 ns | 0.0830 ns | 0.0494 ns | 0.008 | 0.0014 |      - |      24 B |        0.01 |
| ParameterCreation_Mine_String      |  31.6722 ns | 0.1483 ns | 0.0776 ns | 0.060 | 0.0033 |      - |      56 B |        0.02 |
| ParameterCreation_Mine_Int         |  33.1621 ns | 0.7172 ns | 0.4744 ns | 0.062 | 0.0048 |      - |      80 B |        0.03 |
| ContainerOperations_AddParameter   | 202.5186 ns | 1.8040 ns | 1.1932 ns | 0.381 | 0.0870 | 0.0005 |    1456 B |        0.62 |
| ContainerOperations_BuildQuery     | 171.0294 ns | 1.8839 ns | 1.2461 ns | 0.322 | 0.0842 | 0.0002 |    1408 B |        0.60 |
