```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 2 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.119
  [Host]     : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2
  Job-YNOEID : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2
  Dry        : .NET 8.0.19 (8.0.1925.36514), X64 RyuJIT AVX2


```
| Method                        | Job        | IterationCount | LaunchCount | RunStrategy | UnrollFactor | WarmupCount | Mean           | Error     | StdDev    | Gen0   | Allocated |
|------------------------------ |----------- |--------------- |------------ |------------ |------------- |------------ |---------------:|----------:|----------:|-------:|----------:|
| ParameterCreation_Mine        | Job-YNOEID | 10             | Default     | Default     | 16           | 3           |      34.196 ns | 0.4357 ns | 0.2882 ns | 0.0072 |     120 B |
| ParameterCreation_Dapper      | Job-YNOEID | 10             | Default     | Default     | 16           | 3           |       6.204 ns | 0.3524 ns | 0.2331 ns | 0.0014 |      24 B |
| ParameterCreation_Mine_String | Job-YNOEID | 10             | Default     | Default     | 16           | 3           |      38.088 ns | 0.2759 ns | 0.1642 ns | 0.0052 |      88 B |
| ParameterCreation_Mine_Int    | Job-YNOEID | 10             | Default     | Default     | 16           | 3           |      29.490 ns | 0.4143 ns | 0.2167 ns | 0.0067 |     112 B |
| ParameterCreation_Mine        | Dry        | 1              | 1           | ColdStart   | 1            | 1           | 609,163.000 ns |        NA | 0.0000 ns |      - |     920 B |
| ParameterCreation_Dapper      | Dry        | 1              | 1           | ColdStart   | 1            | 1           | 420,736.000 ns |        NA | 0.0000 ns |      - |     760 B |
| ParameterCreation_Mine_String | Dry        | 1              | 1           | ColdStart   | 1            | 1           | 610,954.000 ns |        NA | 0.0000 ns |      - |     888 B |
| ParameterCreation_Mine_Int    | Dry        | 1              | 1           | ColdStart   | 1            | 1           | 698,437.000 ns |        NA | 0.0000 ns |      - |     912 B |
