```

BenchmarkDotNet v0.14.0, macOS 26.0.1 (25A362) [Darwin 25.0.0]
Apple M3 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 8.0.413
  [Host]     : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD
  DefaultJob : .NET 8.0.19 (8.0.1925.36514), Arm64 RyuJIT AdvSIMD


```
| Method                     | Mean     | Error    | StdDev   | Allocated |
|--------------------------- |---------:|---------:|---------:|----------:|
| ConfigureGeometryParameter | 13.08 ns | 0.115 ns | 0.089 ns |         - |
