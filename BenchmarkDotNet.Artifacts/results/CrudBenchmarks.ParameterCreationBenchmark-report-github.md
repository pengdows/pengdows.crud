```

BenchmarkDotNet v0.14.0, macOS 26.0.1 (25A362) [Darwin 25.0.0]
Apple M3 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 8.0.413
  [Host]     : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD
  Job-UAEAPA : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD

IterationCount=10  WarmupCount=3  

```
| Method                         | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------- |----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| ParameterCreation_Dapper       |  2.036 ns | 0.1260 ns | 0.0750 ns |  1.00 |    0.05 | 0.0029 |      24 B |        1.00 |
| ParameterCreation_Mine_Named   | 21.242 ns | 0.5282 ns | 0.3494 ns | 10.45 |    0.39 | 0.0095 |      80 B |        3.33 |
| ParameterCreation_Mine_Unnamed | 22.886 ns | 0.4235 ns | 0.2520 ns | 11.25 |    0.40 | 0.0095 |      80 B |        3.33 |
| ParameterCreation_Mine_String  | 24.755 ns | 0.6016 ns | 0.3979 ns | 12.17 |    0.45 | 0.0067 |      56 B |        2.33 |
