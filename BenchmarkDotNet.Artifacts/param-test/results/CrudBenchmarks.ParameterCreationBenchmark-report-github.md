```

BenchmarkDotNet v0.14.0, macOS 26.0.1 (25A362) [Darwin 25.0.0]
Apple M3 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 8.0.413
  [Host]     : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD
  Job-RXSZOD : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD

IterationCount=10  WarmupCount=3  

```
| Method                       | Mean     | Error    | StdDev   | Gen0   | Allocated |
|----------------------------- |---------:|---------:|---------:|-------:|----------:|
| ParameterCreation_Mine_Named | 20.10 ns | 0.068 ns | 0.035 ns | 0.0095 |      80 B |
