
BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 8.0.122
  [Host]     : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.22 (8.0.2225.52707), X64 RyuJIT AVX2


 Method                       | Mean        | Error     | StdDev    | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
----------------------------- |------------:|----------:|----------:|-------:|--------:|-------:|----------:|------------:|
 ConfigureSimpleParameter     |   0.7632 ns | 0.0116 ns | 0.0109 ns |   1.00 |    0.02 |      - |         - |          NA |
 ConfigureInetParameter       |  66.0829 ns | 0.2558 ns | 0.2268 ns |  86.60 |    1.21 | 0.0072 |     120 B |          NA |
 ConfigureRangeParameter      | 166.2083 ns | 0.7758 ns | 0.7256 ns | 217.81 |    3.10 | 0.0162 |     272 B |          NA |
 ConfigureGeometryParameter   |  19.5499 ns | 0.0149 ns | 0.0132 ns |  25.62 |    0.35 |      - |         - |          NA |
 ConfigureRowVersionParameter |  86.5313 ns | 0.5126 ns | 0.4544 ns | 113.40 |    1.64 | 0.0072 |     120 B |          NA |
 ConfigureNullParameter       |  43.3706 ns | 0.7733 ns | 0.7234 ns |  56.84 |    1.20 | 0.0019 |      32 B |          NA |
 ConfigureUnregisteredType    |  66.0930 ns | 0.1917 ns | 0.1601 ns |  86.61 |    1.19 | 0.0033 |      56 B |          NA |
 ConfigureCachedParameter     | 127.8223 ns | 0.6388 ns | 0.5975 ns | 167.50 |    2.40 | 0.0143 |     240 B |          NA |
 ConvertInetFromString        |  90.5513 ns | 1.5029 ns | 1.4058 ns | 118.66 |    2.40 | 0.0114 |     192 B |          NA |
 ConvertRangeFromString       | 251.2820 ns | 0.7835 ns | 0.7329 ns | 329.29 |    4.57 | 0.0129 |     216 B |          NA |
 ConvertGeometryFromWKT       |  19.8439 ns | 0.1146 ns | 0.1016 ns |  26.00 |    0.38 | 0.0038 |      64 B |          NA |
 GetMapping                   |   7.6130 ns | 0.0073 ns | 0.0065 ns |   9.98 |    0.14 |      - |         - |          NA |
 GetConverter                 |   6.4708 ns | 0.0838 ns | 0.0783 ns |   8.48 |    0.15 |      - |         - |          NA |
 HotPathParameterSetup        |  66.5330 ns | 1.1722 ns | 1.0965 ns |  87.19 |    1.83 | 0.0072 |     120 B |          NA |
