## CrudBenchmarks.ApplesToApplesDapperBenchmarks-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  Job-WXJLOL : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

WarmupCount=3  

```
| Method                                     | Job        | IterationCount | LaunchCount | RecordCount | Mean        | Error      | StdDev    | P95         | P99      | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------------------------------- |----------- |--------------- |------------ |------------ |------------:|-----------:|----------:|------------:|---------:|------:|--------:|--------:|----------:|------------:|
| **ReadSingle_Pengdows_ReadySql_NewConnection** | **Job-WXJLOL** | **10**             | **Default**     | **1**           |    **29.44 μs** |   **0.374 μs** |  **0.247 μs** |    **29.74 μs** |    **29.78** |  **1.00** |    **0.01** |  **0.4272** |   **7.05 KB** |        **1.00** |
| ReadSingle_Dapper_ReadySql_NewConnection   | Job-WXJLOL | 10             | Default     | 1           |    17.89 μs |   0.049 μs |  0.029 μs |    17.94 μs |    17.94 |  0.61 |    0.00 |  0.1221 |   2.26 KB |        0.32 |
|                                            |            |                |             |             |             |            |           |             |          |       |         |         |           |             |
| ReadSingle_Pengdows_ReadySql_NewConnection | ShortRun   | 3              | 1           | 1           |    29.30 μs |   0.729 μs |  0.040 μs |    29.33 μs |    29.33 |  1.00 |    0.00 |  0.4272 |   7.05 KB |        1.00 |
| ReadSingle_Dapper_ReadySql_NewConnection   | ShortRun   | 3              | 1           | 1           |    17.62 μs |   2.085 μs |  0.114 μs |    17.73 μs |    17.74 |  0.60 |    0.00 |  0.1221 |   2.26 KB |        0.32 |
|                                            |            |                |             |             |             |            |           |             |          |       |         |         |           |             |
| **ReadSingle_Pengdows_ReadySql_NewConnection** | **Job-WXJLOL** | **10**             | **Default**     | **10**          |   **294.51 μs** |   **3.147 μs** |  **2.081 μs** |   **296.66 μs** |   **296.69** |  **1.00** |    **0.01** |  **3.9063** |  **70.61 KB** |        **1.00** |
| ReadSingle_Dapper_ReadySql_NewConnection   | Job-WXJLOL | 10             | Default     | 10          |   180.71 μs |   5.291 μs |  3.500 μs |   183.37 μs |   183.65 |  0.61 |    0.01 |  1.2207 |  22.64 KB |        0.32 |
|                                            |            |                |             |             |             |            |           |             |          |       |         |         |           |             |
| ReadSingle_Pengdows_ReadySql_NewConnection | ShortRun   | 3              | 1           | 10          |   292.69 μs |  60.101 μs |  3.294 μs |   295.91 μs |   296.29 |  1.00 |    0.01 |  3.9063 |  70.61 KB |        1.00 |
| ReadSingle_Dapper_ReadySql_NewConnection   | ShortRun   | 3              | 1           | 10          |   176.59 μs |   4.609 μs |  0.253 μs |   176.76 μs |   176.77 |  0.60 |    0.01 |  1.2207 |  22.64 KB |        0.32 |
|                                            |            |                |             |             |             |            |           |             |          |       |         |         |           |             |
| **ReadSingle_Pengdows_ReadySql_NewConnection** | **Job-WXJLOL** | **10**             | **Default**     | **100**         | **2,967.55 μs** |  **66.645 μs** | **44.082 μs** | **3,003.40 μs** | **3,004.08** |  **1.00** |    **0.02** | **42.9688** | **705.48 KB** |        **1.00** |
| ReadSingle_Dapper_ReadySql_NewConnection   | Job-WXJLOL | 10             | Default     | 100         | 1,870.96 μs |   7.895 μs |  4.698 μs | 1,877.16 μs | 1,877.69 |  0.63 |    0.01 | 13.6719 | 225.78 KB |        0.32 |
|                                            |            |                |             |             |             |            |           |             |          |       |         |         |           |             |
| ReadSingle_Pengdows_ReadySql_NewConnection | ShortRun   | 3              | 1           | 100         | 2,912.79 μs | 123.079 μs |  6.746 μs | 2,918.60 μs | 2,919.05 |  1.00 |    0.00 | 42.9688 | 705.48 KB |        1.00 |
| ReadSingle_Dapper_ReadySql_NewConnection   | ShortRun   | 3              | 1           | 100         | 1,790.60 μs |  96.344 μs |  5.281 μs | 1,795.79 μs | 1,796.44 |  0.61 |    0.00 | 13.6719 | 225.77 KB |        0.32 |

---

## CrudBenchmarks.BrokenMappingBenchmarks-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  Job-PYBLCY : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2


```
| Method                          | Job        | IterationCount | LaunchCount | WarmupCount | Mean         | Error      | StdDev    | P95          | P99      | Gen0    | Gen1   | Allocated |
|-------------------------------- |----------- |--------------- |------------ |------------ |-------------:|-----------:|----------:|-------------:|---------:|--------:|-------:|----------:|
| Create_Pengdows                 | Job-PYBLCY | 5              | Default     | 2           |    33.893 μs |  0.5043 μs | 0.0780 μs |    33.961 μs |    33.96 |  0.6714 |      - |   11.8 KB |
| Create_Dapper                   | Job-PYBLCY | 5              | Default     | 2           |     9.075 μs |  0.1938 μs | 0.0300 μs |     9.102 μs |     9.10 |  0.3967 |      - |   6.65 KB |
| Create_EntityFramework          | Job-PYBLCY | 5              | Default     | 2           |    29.120 μs |  0.2623 μs | 0.0406 μs |    29.154 μs |    29.16 |  0.8545 |      - |  13.97 KB |
| ReadSingle_Pengdows             | Job-PYBLCY | 5              | Default     | 2           |    33.414 μs |  0.7524 μs | 0.1954 μs |    33.654 μs |    33.69 |  0.4883 |      - |   8.16 KB |
| ReadSingle_Dapper               | Job-PYBLCY | 5              | Default     | 2           |     6.841 μs |  0.0671 μs | 0.0174 μs |     6.861 μs |     6.86 |  0.1526 |      - |   2.52 KB |
| ReadSingle_EntityFramework      | Job-PYBLCY | 5              | Default     | 2           |   168.587 μs |  2.6364 μs | 0.4080 μs |   168.940 μs |   168.95 |  1.9531 |      - |  38.65 KB |
| ReadList_Pengdows               | Job-PYBLCY | 5              | Default     | 2           |   107.456 μs |  0.9255 μs | 0.1432 μs |   107.628 μs |   107.65 |  1.3428 |      - |  22.24 KB |
| ReadList_Dapper                 | Job-PYBLCY | 5              | Default     | 2           |    40.174 μs |  0.9874 μs | 0.1528 μs |    40.359 μs |    40.39 |  0.6104 |      - |  10.55 KB |
| ReadList_EntityFramework        | Job-PYBLCY | 5              | Default     | 2           |   148.215 μs |  3.3534 μs | 0.8709 μs |   149.360 μs |   149.51 |  1.7090 |      - |  28.46 KB |
| Update_Pengdows                 | Job-PYBLCY | 5              | Default     | 2           |    32.000 μs |  0.8821 μs | 0.1365 μs |    32.119 μs |    32.13 |  0.7324 |      - |   12.9 KB |
| Update_Dapper                   | Job-PYBLCY | 5              | Default     | 2           |     8.256 μs |  0.1263 μs | 0.0328 μs |     8.291 μs |     8.29 |  0.4730 |      - |   7.78 KB |
| Update_EntityFramework          | Job-PYBLCY | 5              | Default     | 2           |   101.573 μs |  1.3134 μs | 0.3411 μs |   102.023 μs |   102.09 |  1.3428 |      - |  23.59 KB |
| Delete_Pengdows                 | Job-PYBLCY | 5              | Default     | 2           |    61.431 μs |  2.0049 μs | 0.5207 μs |    61.908 μs |    61.93 |  0.9766 |      - |  16.38 KB |
| Delete_Dapper                   | Job-PYBLCY | 5              | Default     | 2           |    42.517 μs |  0.9428 μs | 0.2448 μs |    42.842 μs |    42.89 |  0.6714 |      - |  11.42 KB |
| Delete_EntityFramework          | Job-PYBLCY | 5              | Default     | 2           |    68.224 μs |  0.7395 μs | 0.1921 μs |    68.449 μs |    68.48 |  0.9766 |      - |  17.24 KB |
| FilteredQuery_Pengdows          | Job-PYBLCY | 5              | Default     | 2           |    57.865 μs |  0.4813 μs | 0.0745 μs |    57.942 μs |    57.95 |  0.6104 |      - |   10.7 KB |
| FilteredQuery_Dapper            | Job-PYBLCY | 5              | Default     | 2           |    21.561 μs |  0.2869 μs | 0.0745 μs |    21.654 μs |    21.66 |  0.2441 |      - |   4.45 KB |
| FilteredQuery_EntityFramework   | Job-PYBLCY | 5              | Default     | 2           |   147.793 μs |  1.5843 μs | 0.2452 μs |   148.008 μs |   148.01 |  1.7090 |      - |  28.48 KB |
| Upsert_Pengdows                 | Job-PYBLCY | 5              | Default     | 2           |    37.247 μs |  0.5691 μs | 0.0881 μs |    37.337 μs |    37.34 |  1.0376 |      - |  17.48 KB |
| Upsert_Dapper                   | Job-PYBLCY | 5              | Default     | 2           |    12.692 μs |  0.1263 μs | 0.0196 μs |    12.709 μs |    12.71 |  0.7172 |      - |  11.82 KB |
| Upsert_EntityFramework          | Job-PYBLCY | 5              | Default     | 2           |    34.058 μs |  0.7629 μs | 0.1981 μs |    34.288 μs |    34.30 |  1.2207 |      - |  20.92 KB |
| AggregateCount_Pengdows         | Job-PYBLCY | 5              | Default     | 2           |    33.186 μs |  0.1691 μs | 0.0262 μs |    33.218 μs |    33.22 |  0.4272 |      - |   7.23 KB |
| AggregateCount_Dapper           | Job-PYBLCY | 5              | Default     | 2           |     7.649 μs |  0.0534 μs | 0.0139 μs |     7.666 μs |     7.67 |  0.1068 |      - |    1.8 KB |
| AggregateCount_EntityFramework  | Job-PYBLCY | 5              | Default     | 2           |     7.242 μs |  0.0234 μs | 0.0061 μs |     7.250 μs |     7.25 |  0.0916 |      - |    1.6 KB |
| ReadWithKeyword_Pengdows        | Job-PYBLCY | 5              | Default     | 2           |    38.283 μs |  0.4471 μs | 0.0692 μs |    38.365 μs |    38.38 |  0.4883 |      - |   8.41 KB |
| ReadWithKeyword_Dapper          | Job-PYBLCY | 5              | Default     | 2           |    10.146 μs |  0.1865 μs | 0.0484 μs |    10.187 μs |    10.19 |  0.1678 |      - |   2.79 KB |
| ReadWithKeyword_EntityFramework | Job-PYBLCY | 5              | Default     | 2           |   137.978 μs |  2.3712 μs | 0.6158 μs |   138.742 μs |   138.81 |  1.4648 |      - |  27.17 KB |
| FilterByKeyword_Pengdows        | Job-PYBLCY | 5              | Default     | 2           |    95.965 μs |  0.7141 μs | 0.1855 μs |    96.188 μs |    96.22 |  1.2207 |      - |  21.31 KB |
| FilterByKeyword_Dapper          | Job-PYBLCY | 5              | Default     | 2           |    27.034 μs |  0.1336 μs | 0.0207 μs |    27.052 μs |    27.05 |  0.5798 |      - |   9.56 KB |
| FilterByKeyword_EntityFramework | Job-PYBLCY | 5              | Default     | 2           |   137.216 μs |  1.0896 μs | 0.1686 μs |   137.377 μs |   137.38 |  1.4648 |      - |  27.17 KB |
| UpdateKeyword_Pengdows          | Job-PYBLCY | 5              | Default     | 2           |    26.224 μs |  0.6131 μs | 0.0949 μs |    26.280 μs |    26.28 |  0.4272 |      - |      7 KB |
| UpdateKeyword_Dapper            | Job-PYBLCY | 5              | Default     | 2           |     4.387 μs |  0.0775 μs | 0.0201 μs |     4.407 μs |     4.41 |  0.1221 |      - |   2.05 KB |
| UpdateKeyword_EntityFramework   | Job-PYBLCY | 5              | Default     | 2           |    23.970 μs |  0.7244 μs | 0.1881 μs |    24.115 μs |    24.13 |  0.4883 |      - |   8.11 KB |
| BatchCreate_Pengdows            | Job-PYBLCY | 5              | Default     | 2           |   340.228 μs |  4.5642 μs | 1.1853 μs |   341.619 μs |   341.68 |  7.3242 |      - | 119.84 KB |
| BatchCreate_Dapper              | Job-PYBLCY | 5              | Default     | 2           |    96.682 μs |  1.9536 μs | 0.3023 μs |    96.993 μs |    97.03 |  4.1504 |      - |  68.27 KB |
| BatchCreate_EntityFramework     | Job-PYBLCY | 5              | Default     | 2           |   309.825 μs |  2.6477 μs | 0.6876 μs |   310.753 μs |   310.90 |  8.3008 |      - | 141.48 KB |
| BatchRead_Pengdows              | Job-PYBLCY | 5              | Default     | 2           |   325.378 μs |  3.2709 μs | 0.5062 μs |   325.861 μs |   325.88 |  4.8828 |      - |  81.35 KB |
| BatchRead_Dapper                | Job-PYBLCY | 5              | Default     | 2           |    69.542 μs |  0.7129 μs | 0.1103 μs |    69.672 μs |    69.69 |  1.4648 |      - |  24.84 KB |
| BatchRead_EntityFramework       | Job-PYBLCY | 5              | Default     | 2           | 1,679.876 μs | 10.7591 μs | 1.6650 μs | 1,681.416 μs | 1,681.46 | 23.4375 |      - | 386.67 KB |
| ReadAll_Pengdows                | Job-PYBLCY | 5              | Default     | 2           |   141.493 μs |  1.4960 μs | 0.2315 μs |   141.755 μs |   141.78 |  1.9531 |      - |  32.79 KB |
| ReadAll_Dapper                  | Job-PYBLCY | 5              | Default     | 2           |    43.457 μs |  0.3795 μs | 0.0986 μs |    43.590 μs |    43.61 |  0.9155 |      - |  15.41 KB |
| ReadAll_EntityFramework         | Job-PYBLCY | 5              | Default     | 2           |   132.696 μs |  1.0301 μs | 0.1594 μs |   132.874 μs |   132.90 |  1.4648 |      - |  25.83 KB |
| DeleteByKeyword_Pengdows        | Job-PYBLCY | 5              | Default     | 2           |    65.287 μs |  2.4120 μs | 0.6264 μs |    65.874 μs |    65.88 |  0.9766 |      - |  16.53 KB |
| DeleteByKeyword_Dapper          | Job-PYBLCY | 5              | Default     | 2           |    48.630 μs |  0.6153 μs | 0.1598 μs |    48.846 μs |    48.88 |  0.6714 |      - |  11.55 KB |
| DeleteByKeyword_EntityFramework | Job-PYBLCY | 5              | Default     | 2           |    72.171 μs |  0.8212 μs | 0.1271 μs |    72.292 μs |    72.30 |  0.9766 |      - |  17.46 KB |
| Create_Pengdows                 | ShortRun   | 3              | 1           | 3           |    32.946 μs |  1.5704 μs | 0.0861 μs |    33.012 μs |    33.02 |  0.6714 |      - |   11.8 KB |
| Create_Dapper                   | ShortRun   | 3              | 1           | 3           |     9.197 μs |  0.4802 μs | 0.0263 μs |     9.223 μs |     9.23 |  0.3967 |      - |   6.65 KB |
| Create_EntityFramework          | ShortRun   | 3              | 1           | 3           |    29.352 μs |  0.4158 μs | 0.0228 μs |    29.374 μs |    29.38 |  0.8545 |      - |  13.97 KB |
| ReadSingle_Pengdows             | ShortRun   | 3              | 1           | 3           |    33.603 μs |  7.9719 μs | 0.4370 μs |    34.029 μs |    34.08 |  0.4883 |      - |   8.16 KB |
| ReadSingle_Dapper               | ShortRun   | 3              | 1           | 3           |     6.784 μs |  0.3791 μs | 0.0208 μs |     6.801 μs |     6.80 |  0.1526 |      - |   2.52 KB |
| ReadSingle_EntityFramework      | ShortRun   | 3              | 1           | 3           |   167.941 μs | 18.6518 μs | 1.0224 μs |   168.950 μs |   169.08 |  2.1973 | 0.2441 |  38.65 KB |
| ReadList_Pengdows               | ShortRun   | 3              | 1           | 3           |   108.701 μs |  3.1118 μs | 0.1706 μs |   108.852 μs |   108.86 |  1.3428 |      - |  22.24 KB |
| ReadList_Dapper                 | ShortRun   | 3              | 1           | 3           |    39.210 μs |  2.0395 μs | 0.1118 μs |    39.308 μs |    39.32 |  0.6104 |      - |  10.55 KB |
| ReadList_EntityFramework        | ShortRun   | 3              | 1           | 3           |   149.247 μs | 11.1656 μs | 0.6120 μs |   149.819 μs |   149.88 |  1.7090 |      - |  28.46 KB |
| Update_Pengdows                 | ShortRun   | 3              | 1           | 3           |    32.380 μs |  1.8788 μs | 0.1030 μs |    32.472 μs |    32.48 |  0.7324 |      - |   12.9 KB |
| Update_Dapper                   | ShortRun   | 3              | 1           | 3           |     8.223 μs |  0.1172 μs | 0.0064 μs |     8.229 μs |     8.23 |  0.4730 |      - |   7.78 KB |
| Update_EntityFramework          | ShortRun   | 3              | 1           | 3           |   102.746 μs | 31.3060 μs | 1.7160 μs |   104.435 μs |   104.67 |  1.3428 |      - |  23.59 KB |
| Delete_Pengdows                 | ShortRun   | 3              | 1           | 3           |    62.739 μs |  7.2343 μs | 0.3965 μs |    63.128 μs |    63.17 |  0.9766 |      - |  16.38 KB |
| Delete_Dapper                   | ShortRun   | 3              | 1           | 3           |    43.190 μs |  2.1729 μs | 0.1191 μs |    43.307 μs |    43.32 |  0.6714 |      - |  11.42 KB |
| Delete_EntityFramework          | ShortRun   | 3              | 1           | 3           |    66.304 μs |  0.7936 μs | 0.0435 μs |    66.337 μs |    66.34 |  0.9766 |      - |  17.24 KB |
| FilteredQuery_Pengdows          | ShortRun   | 3              | 1           | 3           |    59.490 μs |  2.6000 μs | 0.1425 μs |    59.595 μs |    59.60 |  0.6104 |      - |   10.7 KB |
| FilteredQuery_Dapper            | ShortRun   | 3              | 1           | 3           |    21.485 μs |  1.2856 μs | 0.0705 μs |    21.549 μs |    21.55 |  0.2441 |      - |   4.45 KB |
| FilteredQuery_EntityFramework   | ShortRun   | 3              | 1           | 3           |   148.856 μs | 12.0479 μs | 0.6604 μs |   149.506 μs |   149.60 |  1.4648 |      - |  28.48 KB |
| Upsert_Pengdows                 | ShortRun   | 3              | 1           | 3           |    38.212 μs |  1.3320 μs | 0.0730 μs |    38.281 μs |    38.29 |  1.0376 |      - |  17.48 KB |
| Upsert_Dapper                   | ShortRun   | 3              | 1           | 3           |    12.278 μs |  0.3381 μs | 0.0185 μs |    12.294 μs |    12.30 |  0.7172 |      - |  11.82 KB |
| Upsert_EntityFramework          | ShortRun   | 3              | 1           | 3           |    33.916 μs |  2.1307 μs | 0.1168 μs |    34.031 μs |    34.05 |  1.2207 |      - |  20.92 KB |
| AggregateCount_Pengdows         | ShortRun   | 3              | 1           | 3           |    32.655 μs |  2.4643 μs | 0.1351 μs |    32.788 μs |    32.81 |  0.4272 |      - |   7.23 KB |
| AggregateCount_Dapper           | ShortRun   | 3              | 1           | 3           |     7.469 μs |  0.2768 μs | 0.0152 μs |     7.480 μs |     7.48 |  0.1068 |      - |    1.8 KB |
| AggregateCount_EntityFramework  | ShortRun   | 3              | 1           | 3           |     6.845 μs |  0.1610 μs | 0.0088 μs |     6.853 μs |     6.85 |  0.0916 |      - |    1.6 KB |
| ReadWithKeyword_Pengdows        | ShortRun   | 3              | 1           | 3           |    37.118 μs |  2.6586 μs | 0.1457 μs |    37.233 μs |    37.24 |  0.4883 |      - |   8.41 KB |
| ReadWithKeyword_Dapper          | ShortRun   | 3              | 1           | 3           |    10.073 μs |  0.7331 μs | 0.0402 μs |    10.113 μs |    10.12 |  0.1678 |      - |   2.79 KB |
| ReadWithKeyword_EntityFramework | ShortRun   | 3              | 1           | 3           |   140.216 μs | 20.0581 μs | 1.0995 μs |   141.146 μs |   141.21 |  1.4648 |      - |  27.17 KB |
| FilterByKeyword_Pengdows        | ShortRun   | 3              | 1           | 3           |    96.211 μs |  6.9660 μs | 0.3818 μs |    96.587 μs |    96.63 |  1.2207 |      - |  21.31 KB |
| FilterByKeyword_Dapper          | ShortRun   | 3              | 1           | 3           |    28.456 μs |  1.0814 μs | 0.0593 μs |    28.511 μs |    28.52 |  0.5798 |      - |   9.56 KB |
| FilterByKeyword_EntityFramework | ShortRun   | 3              | 1           | 3           |   140.742 μs | 11.2978 μs | 0.6193 μs |   141.351 μs |   141.43 |  1.4648 |      - |  27.17 KB |
| UpdateKeyword_Pengdows          | ShortRun   | 3              | 1           | 3           |    25.469 μs |  0.3913 μs | 0.0214 μs |    25.490 μs |    25.49 |  0.4272 |      - |      7 KB |
| UpdateKeyword_Dapper            | ShortRun   | 3              | 1           | 3           |     4.340 μs |  0.2997 μs | 0.0164 μs |     4.356 μs |     4.36 |  0.1221 |      - |   2.05 KB |
| UpdateKeyword_EntityFramework   | ShortRun   | 3              | 1           | 3           |    24.337 μs |  0.6153 μs | 0.0337 μs |    24.370 μs |    24.37 |  0.4883 |      - |   8.11 KB |
| BatchCreate_Pengdows            | ShortRun   | 3              | 1           | 3           |   329.407 μs |  3.1730 μs | 0.1739 μs |   329.576 μs |   329.60 |  7.3242 |      - | 119.84 KB |
| BatchCreate_Dapper              | ShortRun   | 3              | 1           | 3           |    98.279 μs |  5.7363 μs | 0.3144 μs |    98.489 μs |    98.49 |  4.1504 |      - |  68.27 KB |
| BatchCreate_EntityFramework     | ShortRun   | 3              | 1           | 3           |   298.628 μs |  6.7151 μs | 0.3681 μs |   298.858 μs |   298.86 |  8.3008 |      - | 141.48 KB |
| BatchRead_Pengdows              | ShortRun   | 3              | 1           | 3           |   336.889 μs |  8.6083 μs | 0.4718 μs |   337.354 μs |   337.41 |  4.8828 |      - |  81.35 KB |
| BatchRead_Dapper                | ShortRun   | 3              | 1           | 3           |    67.304 μs |  4.4310 μs | 0.2429 μs |    67.542 μs |    67.57 |  1.4648 |      - |  24.84 KB |
| BatchRead_EntityFramework       | ShortRun   | 3              | 1           | 3           | 1,688.984 μs | 59.1907 μs | 3.2444 μs | 1,692.174 μs | 1,692.57 | 23.4375 |      - | 386.67 KB |
| ReadAll_Pengdows                | ShortRun   | 3              | 1           | 3           |   153.707 μs | 12.1122 μs | 0.6639 μs |   154.350 μs |   154.42 |  1.9531 |      - |  32.79 KB |
| ReadAll_Dapper                  | ShortRun   | 3              | 1           | 3           |    44.303 μs |  0.5190 μs | 0.0284 μs |    44.328 μs |    44.33 |  0.9155 |      - |  15.41 KB |
| ReadAll_EntityFramework         | ShortRun   | 3              | 1           | 3           |   134.036 μs | 11.0609 μs | 0.6063 μs |   134.518 μs |   134.55 |  1.4648 |      - |  25.83 KB |
| DeleteByKeyword_Pengdows        | ShortRun   | 3              | 1           | 3           |    66.044 μs |  2.7822 μs | 0.1525 μs |    66.194 μs |    66.21 |  0.9766 |      - |  16.53 KB |
| DeleteByKeyword_Dapper          | ShortRun   | 3              | 1           | 3           |    46.271 μs |  1.5945 μs | 0.0874 μs |    46.325 μs |    46.33 |  0.6714 |      - |  11.55 KB |
| DeleteByKeyword_EntityFramework | ShortRun   | 3              | 1           | 3           |    70.701 μs |  5.6230 μs | 0.3082 μs |    70.994 μs |    71.03 |  0.9766 |      - |  17.46 KB |

---

## CrudBenchmarks.ConnectionPoolProtectionBenchmarks-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  Job-AOMBCL : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

IterationCount=3  

```
| Method                             | Job        | LaunchCount | WarmupCount | Mean       | Error         | StdDev      | P95        | P99    | Gen0     | Gen1    | Allocated   |
|----------------------------------- |----------- |------------ |------------ |-----------:|--------------:|------------:|-----------:|-------:|---------:|--------:|------------:|
| PoolExhaustion_Pengdows            | Job-AOMBCL | Default     | 1           |   3.626 ms |     7.7399 ms |   0.4243 ms |   4.042 ms |   4.09 |  31.2500 |       - |   626.16 KB |
| PoolExhaustion_Dapper              | Job-AOMBCL | Default     | 1           |   1.664 ms |     0.2329 ms |   0.0128 ms |   1.675 ms |   1.68 |   9.7656 |       - |   165.61 KB |
| PoolExhaustion_EntityFramework     | Job-AOMBCL | Default     | 1           |   2.986 ms |     5.6522 ms |   0.3098 ms |   3.289 ms |   3.32 | 179.6875 | 62.5000 |  2834.12 KB |
| WriteStorm_Pengdows                | Job-AOMBCL | Default     | 1           | 130.611 ms |   463.5032 ms |  25.4062 ms | 155.662 ms | 158.88 |        - |       - | 15175.31 KB |
| WriteStorm_Dapper                  | Job-AOMBCL | Default     | 1           |         NA |            NA |          NA |         NA |     NA |       NA |      NA |          NA |
| WriteStorm_EntityFramework         | Job-AOMBCL | Default     | 1           |         NA |            NA |          NA |         NA |     NA |       NA |      NA |          NA |
| MixedOps_Pengdows                  | Job-AOMBCL | Default     | 1           | 656.329 ms | 1,579.0570 ms |  86.5534 ms | 741.323 ms | 753.28 |        - |       - |   673.71 KB |
| MixedOps_Dapper                    | Job-AOMBCL | Default     | 1           | 151.223 ms |     2.2085 ms |   0.1211 ms | 151.343 ms | 151.36 |        - |       - |   401.58 KB |
| MixedOps_EntityFramework           | Job-AOMBCL | Default     | 1           | 164.594 ms |   397.1164 ms |  21.7673 ms | 185.976 ms | 188.98 |        - |       - |  3147.53 KB |
| SustainedPressure_Pengdows         | Job-AOMBCL | Default     | 1           |   4.666 ms |     1.2304 ms |   0.0674 ms |   4.731 ms |   4.74 |  70.3125 |       - |  1232.05 KB |
| SustainedPressure_Dapper           | Job-AOMBCL | Default     | 1           |   1.899 ms |     0.0988 ms |   0.0054 ms |   1.902 ms |   1.90 |  17.5781 |       - |   304.69 KB |
| SustainedPressure_EntityFramework  | Job-AOMBCL | Default     | 1           |  11.721 ms |     4.9685 ms |   0.2723 ms |  11.990 ms |  12.02 | 343.7500 | 31.2500 |  5643.33 KB |
| ConnectionHoldTime_Pengdows        | Job-AOMBCL | Default     | 1           |   4.602 ms |     2.0235 ms |   0.1109 ms |   4.687 ms |   4.69 |  62.5000 |       - |  1236.04 KB |
| ConnectionHoldTime_Dapper          | Job-AOMBCL | Default     | 1           |   1.887 ms |     0.0602 ms |   0.0033 ms |   1.890 ms |   1.89 |  17.5781 |       - |   308.67 KB |
| ConnectionHoldTime_EntityFramework | Job-AOMBCL | Default     | 1           |  11.815 ms |     4.1444 ms |   0.2272 ms |  12.039 ms |  12.07 | 343.7500 | 31.2500 |  5647.31 KB |
| PoolExhaustion_Pengdows            | ShortRun   | 1           | 3           |   3.266 ms |     0.9731 ms |   0.0533 ms |   3.302 ms |   3.30 |  31.2500 |       - |   626.15 KB |
| PoolExhaustion_Dapper              | ShortRun   | 1           | 3           |   1.644 ms |     0.1087 ms |   0.0060 ms |   1.650 ms |   1.65 |   9.7656 |       - |   165.58 KB |
| PoolExhaustion_EntityFramework     | ShortRun   | 1           | 3           |   2.561 ms |     0.3016 ms |   0.0165 ms |   2.575 ms |   2.58 | 183.5938 | 62.5000 |  2834.16 KB |
| WriteStorm_Pengdows                | ShortRun   | 1           | 3           | 125.332 ms |   532.5851 ms |  29.1928 ms | 142.777 ms | 142.90 |        - |       - | 14926.19 KB |
| WriteStorm_Dapper                  | ShortRun   | 1           | 3           |         NA |            NA |          NA |         NA |     NA |       NA |      NA |          NA |
| WriteStorm_EntityFramework         | ShortRun   | 1           | 3           |         NA |            NA |          NA |         NA |     NA |       NA |      NA |          NA |
| MixedOps_Pengdows                  | ShortRun   | 1           | 3           | 756.302 ms | 4,113.2119 ms | 225.4590 ms | 959.147 ms | 977.16 |        - |       - |   687.71 KB |
| MixedOps_Dapper                    | ShortRun   | 1           | 3           | 151.365 ms |     4.2778 ms |   0.2345 ms | 151.568 ms | 151.58 |        - |       - |   437.29 KB |
| MixedOps_EntityFramework           | ShortRun   | 1           | 3           | 152.199 ms |     0.5001 ms |   0.0274 ms | 152.225 ms | 152.23 |        - |       - |  3262.89 KB |
| SustainedPressure_Pengdows         | ShortRun   | 1           | 3           |   4.670 ms |     0.7321 ms |   0.0401 ms |   4.709 ms |   4.71 |  62.5000 |       - |  1232.06 KB |
| SustainedPressure_Dapper           | ShortRun   | 1           | 3           |   1.965 ms |     0.1189 ms |   0.0065 ms |   1.971 ms |   1.97 |  15.6250 |       - |   304.69 KB |
| SustainedPressure_EntityFramework  | ShortRun   | 1           | 3           |  12.140 ms |     1.9644 ms |   0.1077 ms |  12.247 ms |  12.26 | 343.7500 | 31.2500 |  5643.33 KB |
| ConnectionHoldTime_Pengdows        | ShortRun   | 1           | 3           |   4.635 ms |     0.2304 ms |   0.0126 ms |   4.647 ms |   4.65 |  70.3125 |       - |  1236.03 KB |
| ConnectionHoldTime_Dapper          | ShortRun   | 1           | 3           |   2.022 ms |     0.0635 ms |   0.0035 ms |   2.025 ms |   2.03 |  15.6250 |       - |   308.67 KB |
| ConnectionHoldTime_EntityFramework | ShortRun   | 1           | 3           |  12.457 ms |     2.3116 ms |   0.1267 ms |  12.581 ms |  12.60 | 343.7500 | 31.2500 |  5647.31 KB |

Benchmarks with issues:
  ConnectionPoolProtectionBenchmarks.WriteStorm_Dapper: Job-AOMBCL(IterationCount=3, WarmupCount=1)
  ConnectionPoolProtectionBenchmarks.WriteStorm_EntityFramework: Job-AOMBCL(IterationCount=3, WarmupCount=1)
  ConnectionPoolProtectionBenchmarks.WriteStorm_Dapper: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)
  ConnectionPoolProtectionBenchmarks.WriteStorm_EntityFramework: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)

---

## CrudBenchmarks.DatabaseFeatureBenchmarks-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  Job-TWPNWE : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

IterationCount=3  

```
| Method                                    | Job        | InvocationCount | LaunchCount | UnrollFactor | WarmupCount | TransactionCount | Parallelism | OperationsPerRun | Mean      | Error      | StdDev    | P95       | P99   | Gen0     | Gen1     | Allocated  |
|------------------------------------------ |----------- |---------------- |------------ |------------- |------------ |----------------- |------------ |----------------- |----------:|-----------:|----------:|----------:|------:|---------:|---------:|-----------:|
| ComplexQuery_Pengdows                     | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  2.297 ms |  0.7735 ms | 0.0424 ms |  2.331 ms |  2.33 |        - |        - |   55.41 KB |
| ComplexQuery_Dapper                       | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  1.575 ms |  0.3062 ms | 0.0168 ms |  1.586 ms |  1.59 |        - |        - |   40.15 KB |
| ComplexQuery_EntityFramework              | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  2.024 ms |  0.3363 ms | 0.0184 ms |  2.042 ms |  2.04 |        - |        - |   96.51 KB |
| FullTextSearch_Pengdows                   | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  1.863 ms |  0.5387 ms | 0.0295 ms |  1.881 ms |  1.88 |        - |        - |   27.81 KB |
| FullTextSearch_Dapper                     | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  1.428 ms |  0.1322 ms | 0.0072 ms |  1.433 ms |  1.43 |        - |        - |    6.11 KB |
| FullTextSearch_EntityFramework            | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  1.721 ms |  0.5353 ms | 0.0293 ms |  1.750 ms |  1.75 |        - |        - |   54.35 KB |
| BulkUpsert_Pengdows                       | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  4.717 ms |  1.2817 ms | 0.0703 ms |  4.787 ms |  4.80 |        - |        - |  174.54 KB |
| BulkUpsert_Dapper                         | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  3.507 ms |  1.5493 ms | 0.0849 ms |  3.585 ms |  3.59 |        - |        - |   113.3 KB |
| BulkUpsert_EntityFramework                | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  4.824 ms | 17.5951 ms | 0.9644 ms |  5.772 ms |  5.90 |        - |        - |  207.18 KB |
| JsonbQuery_Pengdows                       | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  2.061 ms |  0.3229 ms | 0.0177 ms |  2.076 ms |  2.08 |        - |        - |   43.77 KB |
| JsonbQuery_Dapper                         | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  1.501 ms |  0.2608 ms | 0.0143 ms |  1.515 ms |  1.52 |        - |        - |   22.13 KB |
| JsonbQuery_EntityFramework                | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  1.838 ms |  0.3389 ms | 0.0186 ms |  1.855 ms |  1.86 |        - |        - |   74.94 KB |
| ArrayContains_Pengdows                    | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  1.757 ms |  0.8423 ms | 0.0462 ms |  1.787 ms |  1.79 |        - |        - |   40.62 KB |
| ArrayContains_Dapper                      | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  1.331 ms |  0.2113 ms | 0.0116 ms |  1.338 ms |  1.34 |        - |        - |   20.03 KB |
| ArrayContains_EntityFramework             | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  1.756 ms |  0.6388 ms | 0.0350 ms |  1.782 ms |  1.78 |        - |        - |   67.63 KB |
| ComplexQuery_Pengdows_Concurrent          | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               | 25.768 ms | 10.3452 ms | 0.5671 ms | 26.321 ms | 26.39 | 200.0000 | 100.0000 | 3506.51 KB |
| ComplexQuery_Dapper_Concurrent            | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               | 17.307 ms | 12.9849 ms | 0.7117 ms | 17.849 ms | 17.88 | 100.0000 |        - | 2558.73 KB |
| ComplexQuery_EntityFramework_Concurrent   | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               | 22.004 ms | 27.2937 ms | 1.4961 ms | 22.978 ms | 23.00 | 300.0000 | 100.0000 | 6140.36 KB |
| FullTextSearch_Pengdows_Concurrent        | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               | 20.667 ms | 11.2718 ms | 0.6178 ms | 21.256 ms | 21.32 | 100.0000 |        - | 1737.83 KB |
| FullTextSearch_Dapper_Concurrent          | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               | 15.922 ms | 13.4064 ms | 0.7349 ms | 16.513 ms | 16.55 |        - |        - |  359.07 KB |
| FullTextSearch_EntityFramework_Concurrent | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               | 21.086 ms | 51.7825 ms | 2.8384 ms | 23.158 ms | 23.25 | 200.0000 | 100.0000 | 3449.61 KB |
| BulkUpsert_Pengdows_Concurrent            | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  6.191 ms |  3.9508 ms | 0.2166 ms |  6.352 ms |  6.36 |        - |        - | 1145.23 KB |
| BulkUpsert_Dapper_Concurrent              | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  4.755 ms |  0.8882 ms | 0.0487 ms |  4.803 ms |  4.81 |        - |        - |  863.46 KB |
| BulkUpsert_EntityFramework_Concurrent     | Job-TWPNWE | 10              | Default     | 1            | 2           | 5000             | 16          | 64               |  7.495 ms | 13.8775 ms | 0.7607 ms |  8.200 ms |  8.27 | 200.0000 |        - | 3326.85 KB |
| ComplexQuery_Pengdows                     | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  1.779 ms |  0.1197 ms | 0.0066 ms |  1.785 ms |  1.79 |        - |        - |   54.65 KB |
| ComplexQuery_Dapper                       | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  1.449 ms |  0.1502 ms | 0.0082 ms |  1.456 ms |  1.46 |   1.9531 |        - |   39.61 KB |
| ComplexQuery_EntityFramework              | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  1.551 ms |  0.1089 ms | 0.0060 ms |  1.557 ms |  1.56 |   5.8594 |        - |   95.51 KB |
| FullTextSearch_Pengdows                   | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  1.647 ms |  0.0337 ms | 0.0018 ms |  1.649 ms |  1.65 |        - |        - |   27.04 KB |
| FullTextSearch_Dapper                     | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  1.369 ms |  0.1537 ms | 0.0084 ms |  1.378 ms |  1.38 |        - |        - |    5.62 KB |
| FullTextSearch_EntityFramework            | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  1.475 ms |  0.0956 ms | 0.0052 ms |  1.480 ms |  1.48 |   1.9531 |        - |   53.51 KB |
| BulkUpsert_Pengdows                       | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  4.111 ms |  0.4434 ms | 0.0243 ms |  4.135 ms |  4.14 |   7.8125 |        - |  171.03 KB |
| BulkUpsert_Dapper                         | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  3.407 ms |  1.8574 ms | 0.1018 ms |  3.508 ms |  3.52 |   3.9063 |        - |  112.81 KB |
| BulkUpsert_EntityFramework                | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  3.768 ms |  0.7073 ms | 0.0388 ms |  3.803 ms |  3.81 |   7.8125 |        - |  203.48 KB |
| JsonbQuery_Pengdows                       | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  1.732 ms |  0.3198 ms | 0.0175 ms |  1.750 ms |  1.75 |        - |        - |    43.1 KB |
| JsonbQuery_Dapper                         | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  1.395 ms |  0.3248 ms | 0.0178 ms |  1.412 ms |  1.41 |        - |        - |    21.5 KB |
| JsonbQuery_EntityFramework                | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  1.483 ms |  0.1212 ms | 0.0066 ms |  1.488 ms |  1.49 |   3.9063 |        - |   74.11 KB |
| ArrayContains_Pengdows                    | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  1.445 ms |  0.0844 ms | 0.0046 ms |  1.449 ms |  1.45 |   1.9531 |        - |   39.94 KB |
| ArrayContains_Dapper                      | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  1.240 ms |  0.0814 ms | 0.0045 ms |  1.245 ms |  1.25 |        - |        - |   19.54 KB |
| ArrayContains_EntityFramework             | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  1.422 ms |  0.1393 ms | 0.0076 ms |  1.428 ms |  1.43 |   1.9531 |        - |      63 KB |
| ComplexQuery_Pengdows_Concurrent          | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               | 22.155 ms |  5.1633 ms | 0.2830 ms | 22.434 ms | 22.47 | 187.5000 |  93.7500 | 3484.73 KB |
| ComplexQuery_Dapper_Concurrent            | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               | 15.570 ms |  2.6459 ms | 0.1450 ms | 15.679 ms | 15.68 | 140.6250 |  46.8750 | 2536.94 KB |
| ComplexQuery_EntityFramework_Concurrent   | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               | 17.279 ms |  9.5375 ms | 0.5228 ms | 17.782 ms | 17.84 | 375.0000 | 156.2500 | 6113.51 KB |
| FullTextSearch_Pengdows_Concurrent        | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               | 20.646 ms |  7.7192 ms | 0.4231 ms | 21.027 ms | 21.06 |        - |        - | 1730.56 KB |
| FullTextSearch_Dapper_Concurrent          | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               | 14.891 ms |  3.8622 ms | 0.2117 ms | 15.079 ms | 15.10 |  15.6250 |        - |  359.03 KB |
| FullTextSearch_EntityFramework_Concurrent | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               | 16.469 ms |  7.2716 ms | 0.3986 ms | 16.819 ms | 16.85 | 187.5000 |  93.7500 | 3423.71 KB |
| BulkUpsert_Pengdows_Concurrent            | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  5.527 ms |  2.5875 ms | 0.1418 ms |  5.666 ms |  5.68 |  70.3125 |  15.6250 | 1126.43 KB |
| BulkUpsert_Dapper_Concurrent              | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  4.562 ms |  1.7881 ms | 0.0980 ms |  4.659 ms |  4.67 |  46.8750 |   7.8125 |  844.48 KB |
| BulkUpsert_EntityFramework_Concurrent     | ShortRun   | Default         | 1           | 16           | 3           | 5000             | 16          | 64               |  5.689 ms |  2.4304 ms | 0.1332 ms |  5.774 ms |  5.78 | 210.9375 |  85.9375 | 3304.44 KB |

---

## CrudBenchmarks.EqualFootingCrudBenchmarks-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  Job-WXJLOL : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

WarmupCount=3  

```
| Method                                   | Job        | IterationCount | LaunchCount | RecordCount | Mean         | Error        | StdDev     | P95          | P99       | Ratio | RatioSD | Gen0     | Gen1    | Allocated  | Alloc Ratio |
|----------------------------------------- |----------- |--------------- |------------ |------------ |-------------:|-------------:|-----------:|-------------:|----------:|------:|--------:|---------:|--------:|-----------:|------------:|
| **Create_Pengdows**                          | **Job-WXJLOL** | **10**             | **Default**     | **1**           |     **32.83 μs** |     **0.089 μs** |   **0.046 μs** |     **32.87 μs** |     **32.87** |  **1.00** |    **0.00** |   **0.4883** |       **-** |    **8.23 KB** |        **1.10** |
| Create_Dapper                            | Job-WXJLOL | 10             | Default     | 1           |     21.62 μs |     0.075 μs |   0.049 μs |     21.68 μs |     21.69 |  0.66 |    0.00 |   0.2136 |       - |     3.7 KB |        0.49 |
| Create_EntityFramework                   | Job-WXJLOL | 10             | Default     | 1           |     84.56 μs |     0.187 μs |   0.124 μs |     84.70 μs |     84.74 |  2.58 |    0.01 |   2.8076 |  0.1221 |   46.38 KB |        6.18 |
| ReadSingle_Pengdows                      | Job-WXJLOL | 10             | Default     | 1           |     32.75 μs |     0.088 μs |   0.058 μs |     32.84 μs |     32.84 |  1.00 |    0.00 |   0.4272 |       - |     7.5 KB |        1.00 |
| ReadSingle_Dapper                        | Job-WXJLOL | 10             | Default     | 1           |     20.55 μs |     0.046 μs |   0.030 μs |     20.59 μs |     20.61 |  0.63 |    0.00 |   0.1526 |       - |    2.66 KB |        0.36 |
| ReadSingle_EntityFramework               | Job-WXJLOL | 10             | Default     | 1           |    117.83 μs |     0.359 μs |   0.214 μs |    118.17 μs |    118.24 |  3.60 |    0.01 |   3.4180 |       - |   57.68 KB |        7.69 |
| ReadList_Pengdows                        | Job-WXJLOL | 10             | Default     | 1           |     36.47 μs |     0.089 μs |   0.059 μs |     36.54 μs |     36.56 |  1.11 |    0.00 |   0.4883 |       - |    8.06 KB |        1.07 |
| ReadList_Dapper                          | Job-WXJLOL | 10             | Default     | 1           |     23.15 μs |     0.094 μs |   0.062 μs |     23.24 μs |     23.25 |  0.71 |    0.00 |   0.1831 |       - |    3.28 KB |        0.44 |
| ReadList_EntityFramework                 | Job-WXJLOL | 10             | Default     | 1           |    112.48 μs |     0.197 μs |   0.103 μs |    112.60 μs |    112.62 |  3.43 |    0.01 |   3.4180 |       - |   57.42 KB |        7.66 |
| Update_Pengdows                          | Job-WXJLOL | 10             | Default     | 1           |     25.93 μs |     0.206 μs |   0.136 μs |     26.12 μs |     26.14 |  0.79 |    0.00 |   0.3662 |       - |    6.41 KB |        0.85 |
| Update_Dapper                            | Job-WXJLOL | 10             | Default     | 1           |     16.62 μs |     0.189 μs |   0.113 μs |     16.81 μs |     16.82 |  0.51 |    0.00 |   0.1221 |       - |    2.04 KB |        0.27 |
| Update_EntityFramework                   | Job-WXJLOL | 10             | Default     | 1           |     79.49 μs |     0.157 μs |   0.094 μs |     79.62 μs |     79.66 |  2.43 |    0.00 |   2.6855 |  0.1221 |   43.92 KB |        5.86 |
| Delete_Pengdows                          | Job-WXJLOL | 10             | Default     | 1           |     59.72 μs |     0.262 μs |   0.156 μs |     59.86 μs |     59.86 |  1.82 |    0.01 |   0.8545 |       - |   14.55 KB |        1.94 |
| Delete_Dapper                            | Job-WXJLOL | 10             | Default     | 1           |     40.31 μs |     0.943 μs |   0.624 μs |     40.73 μs |     40.79 |  1.23 |    0.02 |   0.3052 |       - |    5.66 KB |        0.76 |
| Delete_EntityFramework                   | Job-WXJLOL | 10             | Default     | 1           |    163.41 μs |     1.342 μs |   0.888 μs |    164.63 μs |    164.82 |  4.99 |    0.03 |   5.3711 |  0.2441 |   90.19 KB |       12.03 |
| FilteredQuery_Pengdows                   | Job-WXJLOL | 10             | Default     | 1           |     39.81 μs |     0.073 μs |   0.038 μs |     39.86 μs |     39.87 |  1.22 |    0.00 |   0.5493 |       - |    9.37 KB |        1.25 |
| FilteredQuery_Dapper                     | Job-WXJLOL | 10             | Default     | 1           |     25.07 μs |     0.108 μs |   0.071 μs |     25.13 μs |     25.13 |  0.77 |    0.00 |   0.2441 |       - |     4.3 KB |        0.57 |
| FilteredQuery_EntityFramework            | Job-WXJLOL | 10             | Default     | 1           |    115.89 μs |     0.356 μs |   0.212 μs |    116.17 μs |    116.19 |  3.54 |    0.01 |   3.4180 |       - |   59.08 KB |        7.88 |
| Aggregate_Pengdows                       | Job-WXJLOL | 10             | Default     | 1           |     62.07 μs |     0.163 μs |   0.085 μs |     62.15 μs |     62.16 |  1.90 |    0.00 |   0.2441 |       - |    5.94 KB |        0.79 |
| Aggregate_Dapper                         | Job-WXJLOL | 10             | Default     | 1           |     49.92 μs |     0.091 μs |   0.047 μs |     49.99 μs |     50.01 |  1.52 |    0.00 |   0.0610 |       - |    1.31 KB |        0.17 |
| Aggregate_EntityFramework                | Job-WXJLOL | 10             | Default     | 1           |    106.84 μs |     0.673 μs |   0.445 μs |    107.52 μs |    107.55 |  3.26 |    0.01 |   2.5635 |  0.1221 |    41.9 KB |        5.59 |
| BatchCreate_Pengdows                     | Job-WXJLOL | 10             | Default     | 1           |     32.85 μs |     0.333 μs |   0.198 μs |     33.16 μs |     33.22 |  1.00 |    0.01 |   0.4883 |       - |    8.63 KB |        1.15 |
| BatchCreate_Dapper                       | Job-WXJLOL | 10             | Default     | 1           |     23.22 μs |     0.087 μs |   0.058 μs |     23.29 μs |     23.30 |  0.71 |    0.00 |   0.2441 |       - |    4.15 KB |        0.55 |
| BatchCreate_EntityFramework              | Job-WXJLOL | 10             | Default     | 1           |     85.18 μs |     0.112 μs |   0.074 μs |     85.28 μs |     85.30 |  2.60 |    0.00 |   2.8076 |  0.1221 |   46.96 KB |        6.26 |
| BulkCreate_Pengdows                      | Job-WXJLOL | 10             | Default     | 1           |     41.94 μs |     0.133 μs |   0.079 μs |     42.03 μs |     42.06 |  1.28 |    0.00 |   0.5493 |       - |    9.64 KB |        1.29 |
| BulkCreate_Dapper                        | Job-WXJLOL | 10             | Default     | 1           |     23.72 μs |     0.062 μs |   0.037 μs |     23.78 μs |     23.78 |  0.72 |    0.00 |   0.3052 |       - |     5.4 KB |        0.72 |
| BulkCreate_EntityFramework               | Job-WXJLOL | 10             | Default     | 1           |     85.92 μs |     0.078 μs |   0.047 μs |     85.98 μs |     86.00 |  2.62 |    0.00 |   2.8076 |  0.1221 |   47.16 KB |        6.29 |
| BulkVsLoop_SingleInserts_Pengdows        | Job-WXJLOL | 10             | Default     | 1           |     32.67 μs |     0.122 μs |   0.073 μs |     32.79 μs |     32.82 |  1.00 |    0.00 |   0.5493 |       - |    9.19 KB |        1.23 |
| BulkVsLoop_BatchCreate_Pengdows          | Job-WXJLOL | 10             | Default     | 1           |     41.67 μs |     0.073 μs |   0.043 μs |     41.74 μs |     41.75 |  1.27 |    0.00 |   0.5493 |       - |    9.64 KB |        1.29 |
| Breakdown_BuildVsExecute_Pengdows        | Job-WXJLOL | 10             | Default     | 1           |     32.74 μs |     0.648 μs |   0.429 μs |     33.01 μs |     33.02 |  1.00 |    0.01 |   0.4272 |       - |    7.55 KB |        1.01 |
| Breakdown_BuildVsExecute_Dapper          | Job-WXJLOL | 10             | Default     | 1           |     19.85 μs |     0.055 μs |   0.037 μs |     19.89 μs |     19.90 |  0.61 |    0.00 |   0.1526 |       - |    2.71 KB |        0.36 |
| Breakdown_BuildVsExecute_EntityFramework | Job-WXJLOL | 10             | Default     | 1           |    117.72 μs |     0.203 μs |   0.121 μs |    117.91 μs |    117.92 |  3.59 |    0.01 |   3.4180 |       - |   57.73 KB |        7.70 |
| ConnectionHoldTime_Pengdows              | Job-WXJLOL | 10             | Default     | 1           |     33.29 μs |     0.107 μs |   0.071 μs |     33.39 μs |     33.41 |  1.02 |    0.00 |   0.4272 |       - |    7.54 KB |        1.01 |
| ConnectionHoldTime_Dapper                | Job-WXJLOL | 10             | Default     | 1           |     20.45 μs |     0.168 μs |   0.111 μs |     20.61 μs |     20.65 |  0.62 |    0.00 |   0.1526 |       - |     2.7 KB |        0.36 |
| ConnectionHoldTime_EntityFramework       | Job-WXJLOL | 10             | Default     | 1           |    120.54 μs |     2.797 μs |   1.850 μs |    123.65 μs |    124.00 |  3.68 |    0.05 |   3.4180 |       - |   57.72 KB |        7.70 |
|                                          |            |                |             |             |              |              |            |              |           |       |         |          |         |            |             |
| Create_Pengdows                          | ShortRun   | 3              | 1           | 1           |     32.85 μs |     4.828 μs |   0.265 μs |     33.11 μs |     33.14 |  1.01 |    0.01 |   0.4883 |       - |    8.23 KB |        1.10 |
| Create_Dapper                            | ShortRun   | 3              | 1           | 1           |     21.72 μs |     1.063 μs |   0.058 μs |     21.77 μs |     21.78 |  0.67 |    0.00 |   0.2136 |       - |     3.7 KB |        0.49 |
| Create_EntityFramework                   | ShortRun   | 3              | 1           | 1           |     83.60 μs |     1.881 μs |   0.103 μs |     83.67 μs |     83.68 |  2.57 |    0.00 |   2.8076 |  0.1221 |   46.38 KB |        6.18 |
| ReadSingle_Pengdows                      | ShortRun   | 3              | 1           | 1           |     32.54 μs |     0.436 μs |   0.024 μs |     32.56 μs |     32.57 |  1.00 |    0.00 |   0.4272 |       - |     7.5 KB |        1.00 |
| ReadSingle_Dapper                        | ShortRun   | 3              | 1           | 1           |     19.93 μs |     0.594 μs |   0.033 μs |     19.96 μs |     19.96 |  0.61 |    0.00 |   0.1526 |       - |    2.66 KB |        0.36 |
| ReadSingle_EntityFramework               | ShortRun   | 3              | 1           | 1           |    120.20 μs |    32.336 μs |   1.772 μs |    121.95 μs |    122.18 |  3.69 |    0.05 |   3.4180 |       - |   57.68 KB |        7.69 |
| ReadList_Pengdows                        | ShortRun   | 3              | 1           | 1           |     35.46 μs |     3.091 μs |   0.169 μs |     35.62 μs |     35.65 |  1.09 |    0.00 |   0.4883 |       - |    8.06 KB |        1.07 |
| ReadList_Dapper                          | ShortRun   | 3              | 1           | 1           |     22.85 μs |     0.255 μs |   0.014 μs |     22.87 μs |     22.87 |  0.70 |    0.00 |   0.1831 |       - |    3.28 KB |        0.44 |
| ReadList_EntityFramework                 | ShortRun   | 3              | 1           | 1           |    114.55 μs |    27.722 μs |   1.520 μs |    116.05 μs |    116.24 |  3.52 |    0.04 |   3.4180 |       - |   57.42 KB |        7.66 |
| Update_Pengdows                          | ShortRun   | 3              | 1           | 1           |     25.42 μs |     2.653 μs |   0.145 μs |     25.57 μs |     25.59 |  0.78 |    0.00 |   0.3662 |       - |    6.41 KB |        0.85 |
| Update_Dapper                            | ShortRun   | 3              | 1           | 1           |     16.83 μs |     3.145 μs |   0.172 μs |     17.00 μs |     17.02 |  0.52 |    0.00 |   0.1221 |       - |    2.04 KB |        0.27 |
| Update_EntityFramework                   | ShortRun   | 3              | 1           | 1           |     79.82 μs |     2.461 μs |   0.135 μs |     79.95 μs |     79.97 |  2.45 |    0.00 |   2.6855 |  0.1221 |   43.92 KB |        5.86 |
| Delete_Pengdows                          | ShortRun   | 3              | 1           | 1           |     60.85 μs |     2.106 μs |   0.115 μs |     60.96 μs |     60.98 |  1.87 |    0.00 |   0.8545 |       - |   14.55 KB |        1.94 |
| Delete_Dapper                            | ShortRun   | 3              | 1           | 1           |     40.67 μs |    19.165 μs |   1.051 μs |     41.70 μs |     41.84 |  1.25 |    0.03 |   0.3052 |       - |    5.66 KB |        0.76 |
| Delete_EntityFramework                   | ShortRun   | 3              | 1           | 1           |    163.66 μs |     1.178 μs |   0.065 μs |    163.73 μs |    163.73 |  5.03 |    0.00 |   5.3711 |  0.2441 |   90.19 KB |       12.03 |
| FilteredQuery_Pengdows                   | ShortRun   | 3              | 1           | 1           |     40.07 μs |     1.585 μs |   0.087 μs |     40.16 μs |     40.17 |  1.23 |    0.00 |   0.5493 |       - |    9.37 KB |        1.25 |
| FilteredQuery_Dapper                     | ShortRun   | 3              | 1           | 1           |     24.90 μs |     0.639 μs |   0.035 μs |     24.92 μs |     24.93 |  0.77 |    0.00 |   0.2441 |       - |     4.3 KB |        0.57 |
| FilteredQuery_EntityFramework            | ShortRun   | 3              | 1           | 1           |    116.58 μs |    10.561 μs |   0.579 μs |    117.15 μs |    117.23 |  3.58 |    0.02 |   3.4180 |       - |   59.08 KB |        7.88 |
| Aggregate_Pengdows                       | ShortRun   | 3              | 1           | 1           |     61.31 μs |     4.085 μs |   0.224 μs |     61.48 μs |     61.49 |  1.88 |    0.01 |   0.2441 |       - |    5.94 KB |        0.79 |
| Aggregate_Dapper                         | ShortRun   | 3              | 1           | 1           |     50.00 μs |     5.028 μs |   0.276 μs |     50.27 μs |     50.30 |  1.54 |    0.01 |   0.0610 |       - |    1.31 KB |        0.17 |
| Aggregate_EntityFramework                | ShortRun   | 3              | 1           | 1           |    106.54 μs |     3.389 μs |   0.186 μs |    106.73 μs |    106.75 |  3.27 |    0.01 |   2.5635 |  0.1221 |    41.9 KB |        5.59 |
| BatchCreate_Pengdows                     | ShortRun   | 3              | 1           | 1           |     32.60 μs |     2.517 μs |   0.138 μs |     32.73 μs |     32.75 |  1.00 |    0.00 |   0.4883 |       - |    8.63 KB |        1.15 |
| BatchCreate_Dapper                       | ShortRun   | 3              | 1           | 1           |     23.12 μs |     2.045 μs |   0.112 μs |     23.22 μs |     23.23 |  0.71 |    0.00 |   0.2441 |       - |    4.15 KB |        0.55 |
| BatchCreate_EntityFramework              | ShortRun   | 3              | 1           | 1           |     85.90 μs |     0.716 μs |   0.039 μs |     85.93 μs |     85.93 |  2.64 |    0.00 |   2.8076 |  0.1221 |   46.96 KB |        6.26 |
| BulkCreate_Pengdows                      | ShortRun   | 3              | 1           | 1           |     41.12 μs |     2.424 μs |   0.133 μs |     41.24 μs |     41.25 |  1.26 |    0.00 |   0.5493 |       - |    9.64 KB |        1.29 |
| BulkCreate_Dapper                        | ShortRun   | 3              | 1           | 1           |     24.18 μs |     0.662 μs |   0.036 μs |     24.21 μs |     24.21 |  0.74 |    0.00 |   0.3052 |       - |     5.4 KB |        0.72 |
| BulkCreate_EntityFramework               | ShortRun   | 3              | 1           | 1           |     83.56 μs |     1.719 μs |   0.094 μs |     83.63 μs |     83.63 |  2.57 |    0.00 |   2.8076 |  0.1221 |   47.16 KB |        6.29 |
| BulkVsLoop_SingleInserts_Pengdows        | ShortRun   | 3              | 1           | 1           |     32.29 μs |     1.992 μs |   0.109 μs |     32.39 μs |     32.41 |  0.99 |    0.00 |   0.5493 |       - |    9.19 KB |        1.23 |
| BulkVsLoop_BatchCreate_Pengdows          | ShortRun   | 3              | 1           | 1           |     40.93 μs |     1.370 μs |   0.075 μs |     41.00 μs |     41.01 |  1.26 |    0.00 |   0.5493 |       - |    9.64 KB |        1.29 |
| Breakdown_BuildVsExecute_Pengdows        | ShortRun   | 3              | 1           | 1           |     33.07 μs |     0.553 μs |   0.030 μs |     33.10 μs |     33.10 |  1.02 |    0.00 |   0.4272 |       - |    7.55 KB |        1.01 |
| Breakdown_BuildVsExecute_Dapper          | ShortRun   | 3              | 1           | 1           |     20.30 μs |     0.244 μs |   0.013 μs |     20.31 μs |     20.31 |  0.62 |    0.00 |   0.1526 |       - |    2.71 KB |        0.36 |
| Breakdown_BuildVsExecute_EntityFramework | ShortRun   | 3              | 1           | 1           |    121.26 μs |    33.254 μs |   1.823 μs |    123.06 μs |    123.30 |  3.73 |    0.05 |   3.4180 |       - |   57.73 KB |        7.70 |
| ConnectionHoldTime_Pengdows              | ShortRun   | 3              | 1           | 1           |     32.88 μs |     0.278 μs |   0.015 μs |     32.89 μs |     32.89 |  1.01 |    0.00 |   0.4272 |       - |    7.54 KB |        1.01 |
| ConnectionHoldTime_Dapper                | ShortRun   | 3              | 1           | 1           |     20.38 μs |     0.508 μs |   0.028 μs |     20.40 μs |     20.40 |  0.63 |    0.00 |   0.1526 |       - |     2.7 KB |        0.36 |
| ConnectionHoldTime_EntityFramework       | ShortRun   | 3              | 1           | 1           |    120.23 μs |    33.848 μs |   1.855 μs |    122.06 μs |    122.31 |  3.69 |    0.05 |   3.4180 |       - |   57.72 KB |        7.70 |
|                                          |            |                |             |             |              |              |            |              |           |       |         |          |         |            |             |
| **Create_Pengdows**                          | **Job-WXJLOL** | **10**             | **Default**     | **10**          |    **325.09 μs** |     **0.632 μs** |   **0.418 μs** |    **325.66 μs** |    **325.86** |  **1.00** |    **0.00** |   **4.8828** |       **-** |   **82.34 KB** |        **1.11** |
| Create_Dapper                            | Job-WXJLOL | 10             | Default     | 10          |    213.28 μs |     0.983 μs |   0.585 μs |    214.00 μs |    214.07 |  0.66 |    0.00 |   2.1973 |       - |   37.02 KB |        0.50 |
| Create_EntityFramework                   | Job-WXJLOL | 10             | Default     | 10          |    845.28 μs |     1.288 μs |   0.766 μs |    846.25 μs |    846.34 |  2.61 |    0.01 |  28.3203 |  2.9297 |  463.83 KB |        6.24 |
| ReadSingle_Pengdows                      | Job-WXJLOL | 10             | Default     | 10          |    323.88 μs |     1.480 μs |   0.979 μs |    325.18 μs |    325.50 |  1.00 |    0.00 |   4.3945 |       - |   74.37 KB |        1.00 |
| ReadSingle_Dapper                        | Job-WXJLOL | 10             | Default     | 10          |    199.59 μs |     1.335 μs |   0.883 μs |    200.80 μs |    200.90 |  0.62 |    0.00 |   1.4648 |       - |   26.01 KB |        0.35 |
| ReadSingle_EntityFramework               | Job-WXJLOL | 10             | Default     | 10          |  1,208.45 μs |     9.334 μs |   5.555 μs |  1,216.25 μs |  1,217.61 |  3.73 |    0.02 |  35.1563 |  3.9063 |   576.2 KB |        7.75 |
| ReadList_Pengdows                        | Job-WXJLOL | 10             | Default     | 10          |     45.63 μs |     0.189 μs |   0.125 μs |     45.82 μs |     45.86 |  0.14 |    0.00 |   0.5493 |       - |    9.84 KB |        0.13 |
| ReadList_Dapper                          | Job-WXJLOL | 10             | Default     | 10          |     32.99 μs |     0.113 μs |   0.075 μs |     33.10 μs |     33.13 |  0.10 |    0.00 |   0.3052 |       - |    5.98 KB |        0.08 |
| ReadList_EntityFramework                 | Job-WXJLOL | 10             | Default     | 10          |    123.45 μs |     0.640 μs |   0.423 μs |    124.07 μs |    124.27 |  0.38 |    0.00 |   3.4180 |       - |   60.89 KB |        0.82 |
| Update_Pengdows                          | Job-WXJLOL | 10             | Default     | 10          |    253.81 μs |     1.505 μs |   0.896 μs |    254.67 μs |    254.68 |  0.78 |    0.00 |   3.9063 |       - |   64.13 KB |        0.86 |
| Update_Dapper                            | Job-WXJLOL | 10             | Default     | 10          |    170.70 μs |     0.583 μs |   0.347 μs |    171.04 μs |    171.07 |  0.53 |    0.00 |   1.2207 |       - |   20.46 KB |        0.28 |
| Update_EntityFramework                   | Job-WXJLOL | 10             | Default     | 10          |    787.33 μs |     9.136 μs |   6.043 μs |    796.76 μs |    799.56 |  2.43 |    0.02 |  26.3672 |  2.9297 |  439.29 KB |        5.91 |
| Delete_Pengdows                          | Job-WXJLOL | 10             | Default     | 10          |    590.67 μs |     1.011 μs |   0.669 μs |    591.62 μs |    591.82 |  1.82 |    0.01 |   8.7891 |       - |  145.62 KB |        1.96 |
| Delete_Dapper                            | Job-WXJLOL | 10             | Default     | 10          |    404.15 μs |     2.028 μs |   1.061 μs |    405.16 μs |    405.16 |  1.25 |    0.00 |   3.4180 |       - |   56.71 KB |        0.76 |
| Delete_EntityFramework                   | Job-WXJLOL | 10             | Default     | 10          |  1,633.11 μs |     1.571 μs |   0.935 μs |  1,634.31 μs |  1,634.45 |  5.04 |    0.01 |  54.6875 |  7.8125 |  901.95 KB |       12.13 |
| FilteredQuery_Pengdows                   | Job-WXJLOL | 10             | Default     | 10          |     49.18 μs |     0.137 μs |   0.091 μs |     49.31 μs |     49.32 |  0.15 |    0.00 |   0.6714 |       - |   11.15 KB |        0.15 |
| FilteredQuery_Dapper                     | Job-WXJLOL | 10             | Default     | 10          |     34.46 μs |     0.182 μs |   0.121 μs |     34.61 μs |     34.62 |  0.11 |    0.00 |   0.4272 |       - |    6.99 KB |        0.09 |
| FilteredQuery_EntityFramework            | Job-WXJLOL | 10             | Default     | 10          |    125.75 μs |     0.941 μs |   0.622 μs |    126.75 μs |    126.91 |  0.39 |    0.00 |   3.4180 |       - |   62.55 KB |        0.84 |
| Aggregate_Pengdows                       | Job-WXJLOL | 10             | Default     | 10          |    608.51 μs |     1.768 μs |   1.052 μs |    610.03 μs |    610.17 |  1.88 |    0.01 |   2.9297 |       - |   58.74 KB |        0.79 |
| Aggregate_Dapper                         | Job-WXJLOL | 10             | Default     | 10          |    500.57 μs |     1.624 μs |   0.967 μs |    501.59 μs |    501.67 |  1.55 |    0.01 |        - |       - |   12.49 KB |        0.17 |
| Aggregate_EntityFramework                | Job-WXJLOL | 10             | Default     | 10          |  1,071.24 μs |     1.086 μs |   0.568 μs |  1,071.80 μs |  1,071.87 |  3.31 |    0.01 |  25.3906 |  1.9531 |  418.37 KB |        5.63 |
| BatchCreate_Pengdows                     | Job-WXJLOL | 10             | Default     | 10          |    331.34 μs |     1.153 μs |   0.686 μs |    332.18 μs |    332.40 |  1.02 |    0.00 |   4.8828 |       - |    86.4 KB |        1.16 |
| BatchCreate_Dapper                       | Job-WXJLOL | 10             | Default     | 10          |    224.76 μs |     1.046 μs |   0.692 μs |    225.63 μs |    225.76 |  0.69 |    0.00 |   2.4414 |       - |   41.55 KB |        0.56 |
| BatchCreate_EntityFramework              | Job-WXJLOL | 10             | Default     | 10          |    853.62 μs |     1.136 μs |   0.752 μs |    854.75 μs |    854.82 |  2.64 |    0.01 |  28.3203 |  2.9297 |  469.69 KB |        6.32 |
| BulkCreate_Pengdows                      | Job-WXJLOL | 10             | Default     | 10          |     99.76 μs |     0.209 μs |   0.124 μs |     99.89 μs |     99.93 |  0.31 |    0.00 |   2.6855 |  0.1221 |   44.56 KB |        0.60 |
| BulkCreate_Dapper                        | Job-WXJLOL | 10             | Default     | 10          |     88.94 μs |     0.321 μs |   0.212 μs |     89.23 μs |     89.30 |  0.27 |    0.00 |   2.4414 |  0.1221 |   41.78 KB |        0.56 |
| BulkCreate_EntityFramework               | Job-WXJLOL | 10             | Default     | 10          |    147.83 μs |     0.155 μs |   0.081 μs |    147.96 μs |    148.00 |  0.46 |    0.00 |   5.1270 |  0.4883 |   84.26 KB |        1.13 |
| BulkVsLoop_SingleInserts_Pengdows        | Job-WXJLOL | 10             | Default     | 10          |    324.56 μs |     0.514 μs |   0.340 μs |    324.94 μs |    324.98 |  1.00 |    0.00 |   5.3711 |       - |   91.95 KB |        1.24 |
| BulkVsLoop_BatchCreate_Pengdows          | Job-WXJLOL | 10             | Default     | 10          |     97.31 μs |     0.181 μs |   0.120 μs |     97.41 μs |     97.42 |  0.30 |    0.00 |   2.6855 |  0.1221 |   44.56 KB |        0.60 |
| Breakdown_BuildVsExecute_Pengdows        | Job-WXJLOL | 10             | Default     | 10          |    328.36 μs |     1.500 μs |   0.992 μs |    329.54 μs |    329.75 |  1.01 |    0.00 |   4.3945 |       - |   74.42 KB |        1.00 |
| Breakdown_BuildVsExecute_Dapper          | Job-WXJLOL | 10             | Default     | 10          |    208.19 μs |     0.514 μs |   0.340 μs |    208.64 μs |    208.67 |  0.64 |    0.00 |   1.4648 |       - |   26.05 KB |        0.35 |
| Breakdown_BuildVsExecute_EntityFramework | Job-WXJLOL | 10             | Default     | 10          |  1,174.65 μs |     2.675 μs |   1.592 μs |  1,176.65 μs |  1,176.69 |  3.63 |    0.01 |  35.1563 |  3.9063 |  576.25 KB |        7.75 |
| ConnectionHoldTime_Pengdows              | Job-WXJLOL | 10             | Default     | 10          |     32.31 μs |     0.148 μs |   0.098 μs |     32.45 μs |     32.47 |  0.10 |    0.00 |   0.4272 |       - |    7.54 KB |        0.10 |
| ConnectionHoldTime_Dapper                | Job-WXJLOL | 10             | Default     | 10          |     20.61 μs |     0.071 μs |   0.042 μs |     20.65 μs |     20.66 |  0.06 |    0.00 |   0.1526 |       - |     2.7 KB |        0.04 |
| ConnectionHoldTime_EntityFramework       | Job-WXJLOL | 10             | Default     | 10          |    118.90 μs |     0.559 μs |   0.333 μs |    119.45 μs |    119.54 |  0.37 |    0.00 |   3.4180 |       - |   57.72 KB |        0.78 |
|                                          |            |                |             |             |              |              |            |              |           |       |         |          |         |            |             |
| Create_Pengdows                          | ShortRun   | 3              | 1           | 10          |    326.10 μs |    20.228 μs |   1.109 μs |    326.92 μs |    326.96 |  1.00 |    0.00 |   4.8828 |       - |   82.34 KB |        1.11 |
| Create_Dapper                            | ShortRun   | 3              | 1           | 10          |    208.97 μs |     8.319 μs |   0.456 μs |    209.42 μs |    209.48 |  0.64 |    0.00 |   2.1973 |       - |   37.02 KB |        0.50 |
| Create_EntityFramework                   | ShortRun   | 3              | 1           | 10          |    842.02 μs |    57.020 μs |   3.125 μs |    845.10 μs |    845.52 |  2.59 |    0.01 |  28.3203 |  2.9297 |  463.83 KB |        6.24 |
| ReadSingle_Pengdows                      | ShortRun   | 3              | 1           | 10          |    325.15 μs |    12.772 μs |   0.700 μs |    325.82 μs |    325.90 |  1.00 |    0.00 |   4.3945 |       - |   74.37 KB |        1.00 |
| ReadSingle_Dapper                        | ShortRun   | 3              | 1           | 10          |    205.61 μs |     8.855 μs |   0.485 μs |    206.07 μs |    206.12 |  0.63 |    0.00 |   1.4648 |       - |   26.01 KB |        0.35 |
| ReadSingle_EntityFramework               | ShortRun   | 3              | 1           | 10          |  1,193.60 μs |   275.874 μs |  15.122 μs |  1,208.32 μs |  1,210.03 |  3.67 |    0.04 |  35.1563 |  3.9063 |   576.2 KB |        7.75 |
| ReadList_Pengdows                        | ShortRun   | 3              | 1           | 10          |     46.11 μs |     4.447 μs |   0.244 μs |     46.35 μs |     46.38 |  0.14 |    0.00 |   0.5493 |       - |    9.84 KB |        0.13 |
| ReadList_Dapper                          | ShortRun   | 3              | 1           | 10          |     32.62 μs |     0.825 μs |   0.045 μs |     32.65 μs |     32.66 |  0.10 |    0.00 |   0.3052 |       - |    5.98 KB |        0.08 |
| ReadList_EntityFramework                 | ShortRun   | 3              | 1           | 10          |    122.59 μs |     9.372 μs |   0.514 μs |    123.09 μs |    123.14 |  0.38 |    0.00 |   3.4180 |       - |   60.89 KB |        0.82 |
| Update_Pengdows                          | ShortRun   | 3              | 1           | 10          |    251.78 μs |    75.901 μs |   4.160 μs |    255.87 μs |    256.44 |  0.77 |    0.01 |   3.9063 |       - |   64.13 KB |        0.86 |
| Update_Dapper                            | ShortRun   | 3              | 1           | 10          |    165.73 μs |    15.264 μs |   0.837 μs |    166.55 μs |    166.65 |  0.51 |    0.00 |   1.2207 |       - |   20.46 KB |        0.28 |
| Update_EntityFramework                   | ShortRun   | 3              | 1           | 10          |    794.99 μs |    32.433 μs |   1.778 μs |    796.74 μs |    796.99 |  2.45 |    0.01 |  26.3672 |  2.9297 |  439.29 KB |        5.91 |
| Delete_Pengdows                          | ShortRun   | 3              | 1           | 10          |    592.48 μs |    16.326 μs |   0.895 μs |    593.00 μs |    593.01 |  1.82 |    0.00 |   8.7891 |       - |  145.62 KB |        1.96 |
| Delete_Dapper                            | ShortRun   | 3              | 1           | 10          |    393.49 μs |    36.109 μs |   1.979 μs |    395.37 μs |    395.57 |  1.21 |    0.01 |   3.4180 |       - |   56.71 KB |        0.76 |
| Delete_EntityFramework                   | ShortRun   | 3              | 1           | 10          |  1,626.60 μs |    44.058 μs |   2.415 μs |  1,628.66 μs |  1,628.82 |  5.00 |    0.01 |  54.6875 |  7.8125 |  901.95 KB |       12.13 |
| FilteredQuery_Pengdows                   | ShortRun   | 3              | 1           | 10          |     48.92 μs |     0.490 μs |   0.027 μs |     48.95 μs |     48.95 |  0.15 |    0.00 |   0.6714 |       - |   11.15 KB |        0.15 |
| FilteredQuery_Dapper                     | ShortRun   | 3              | 1           | 10          |     35.39 μs |     3.611 μs |   0.198 μs |     35.59 μs |     35.61 |  0.11 |    0.00 |   0.4272 |       - |    6.99 KB |        0.09 |
| FilteredQuery_EntityFramework            | ShortRun   | 3              | 1           | 10          |    127.57 μs |    11.613 μs |   0.637 μs |    128.20 μs |    128.28 |  0.39 |    0.00 |   3.4180 |       - |   62.55 KB |        0.84 |
| Aggregate_Pengdows                       | ShortRun   | 3              | 1           | 10          |    611.54 μs |    51.834 μs |   2.841 μs |    614.35 μs |    614.71 |  1.88 |    0.01 |   2.9297 |       - |   58.74 KB |        0.79 |
| Aggregate_Dapper                         | ShortRun   | 3              | 1           | 10          |    515.39 μs |    25.082 μs |   1.375 μs |    516.75 μs |    516.93 |  1.59 |    0.00 |        - |       - |   12.49 KB |        0.17 |
| Aggregate_EntityFramework                | ShortRun   | 3              | 1           | 10          |  1,071.01 μs |   103.262 μs |   5.660 μs |  1,076.50 μs |  1,077.13 |  3.29 |    0.02 |  25.3906 |  1.9531 |  418.37 KB |        5.63 |
| BatchCreate_Pengdows                     | ShortRun   | 3              | 1           | 10          |    332.90 μs |    10.806 μs |   0.592 μs |    333.48 μs |    333.56 |  1.02 |    0.00 |   4.8828 |       - |    86.4 KB |        1.16 |
| BatchCreate_Dapper                       | ShortRun   | 3              | 1           | 10          |    221.42 μs |     7.712 μs |   0.423 μs |    221.78 μs |    221.81 |  0.68 |    0.00 |   2.4414 |       - |   41.55 KB |        0.56 |
| BatchCreate_EntityFramework              | ShortRun   | 3              | 1           | 10          |    863.81 μs |    21.984 μs |   1.205 μs |    864.75 μs |    864.81 |  2.66 |    0.01 |  28.3203 |  2.9297 |  469.69 KB |        6.32 |
| BulkCreate_Pengdows                      | ShortRun   | 3              | 1           | 10          |     96.29 μs |     8.143 μs |   0.446 μs |     96.56 μs |     96.57 |  0.30 |    0.00 |   2.6855 |  0.1221 |   44.56 KB |        0.60 |
| BulkCreate_Dapper                        | ShortRun   | 3              | 1           | 10          |     87.87 μs |     1.328 μs |   0.073 μs |     87.93 μs |     87.93 |  0.27 |    0.00 |   2.4414 |  0.1221 |   41.78 KB |        0.56 |
| BulkCreate_EntityFramework               | ShortRun   | 3              | 1           | 10          |    147.82 μs |     4.910 μs |   0.269 μs |    148.08 μs |    148.12 |  0.45 |    0.00 |   5.1270 |  0.4883 |   84.26 KB |        1.13 |
| BulkVsLoop_SingleInserts_Pengdows        | ShortRun   | 3              | 1           | 10          |    323.09 μs |    12.934 μs |   0.709 μs |    323.76 μs |    323.83 |  0.99 |    0.00 |   5.3711 |       - |   91.95 KB |        1.24 |
| BulkVsLoop_BatchCreate_Pengdows          | ShortRun   | 3              | 1           | 10          |     97.62 μs |     3.528 μs |   0.193 μs |     97.74 μs |     97.74 |  0.30 |    0.00 |   2.6855 |  0.1221 |   44.56 KB |        0.60 |
| Breakdown_BuildVsExecute_Pengdows        | ShortRun   | 3              | 1           | 10          |    322.77 μs |    25.684 μs |   1.408 μs |    323.97 μs |    324.06 |  0.99 |    0.00 |   4.3945 |       - |   74.42 KB |        1.00 |
| Breakdown_BuildVsExecute_Dapper          | ShortRun   | 3              | 1           | 10          |    201.63 μs |     8.080 μs |   0.443 μs |    201.91 μs |    201.91 |  0.62 |    0.00 |   1.4648 |       - |   26.05 KB |        0.35 |
| Breakdown_BuildVsExecute_EntityFramework | ShortRun   | 3              | 1           | 10          |  1,191.27 μs |   198.075 μs |  10.857 μs |  1,201.97 μs |  1,203.43 |  3.66 |    0.03 |  35.1563 |  3.9063 |  576.25 KB |        7.75 |
| ConnectionHoldTime_Pengdows              | ShortRun   | 3              | 1           | 10          |     32.41 μs |     0.875 μs |   0.048 μs |     32.46 μs |     32.46 |  0.10 |    0.00 |   0.4272 |       - |    7.54 KB |        0.10 |
| ConnectionHoldTime_Dapper                | ShortRun   | 3              | 1           | 10          |     20.05 μs |     1.323 μs |   0.073 μs |     20.12 μs |     20.12 |  0.06 |    0.00 |   0.1526 |       - |     2.7 KB |        0.04 |
| ConnectionHoldTime_EntityFramework       | ShortRun   | 3              | 1           | 10          |    119.31 μs |    27.030 μs |   1.482 μs |    120.76 μs |    120.94 |  0.37 |    0.00 |   3.4180 |       - |   57.72 KB |        0.78 |
|                                          |            |                |             |             |              |              |            |              |           |       |         |          |         |            |             |
| **Create_Pengdows**                          | **Job-WXJLOL** | **10**             | **Default**     | **100**         |  **3,225.13 μs** |     **9.635 μs** |   **6.373 μs** |  **3,233.09 μs** |  **3,235.34** | **1.009** |    **0.00** |  **46.8750** |       **-** |  **823.44 KB** |       **1.108** |
| Create_Dapper                            | Job-WXJLOL | 10             | Default     | 100         |  2,172.28 μs |     9.555 μs |   4.997 μs |  2,176.58 μs |  2,176.65 | 0.680 |    0.00 |  19.5313 |       - |  370.31 KB |       0.498 |
| Create_EntityFramework                   | Job-WXJLOL | 10             | Default     | 100         |  8,468.03 μs |    24.585 μs |  14.630 μs |  8,489.44 μs |  8,493.34 | 2.649 |    0.01 | 281.2500 | 31.2500 | 4638.51 KB |       6.242 |
| ReadSingle_Pengdows                      | Job-WXJLOL | 10             | Default     | 100         |  3,196.41 μs |     5.887 μs |   3.894 μs |  3,200.53 μs |  3,200.94 | 1.000 |    0.00 |  42.9688 |       - |  743.06 KB |       1.000 |
| ReadSingle_Dapper                        | Job-WXJLOL | 10             | Default     | 100         |  2,022.34 μs |    71.778 μs |  47.477 μs |  2,078.94 μs |  2,080.30 | 0.633 |    0.01 |  15.6250 |       - |  259.46 KB |       0.349 |
| ReadSingle_EntityFramework               | Job-WXJLOL | 10             | Default     | 100         | 11,876.52 μs |    26.794 μs |  15.945 μs | 11,895.44 μs | 11,899.36 | 3.716 |    0.01 | 343.7500 | 31.2500 |  5761.3 KB |       7.753 |
| ReadList_Pengdows                        | Job-WXJLOL | 10             | Default     | 100         |    135.55 μs |     0.159 μs |   0.095 μs |    135.65 μs |    135.65 | 0.042 |    0.00 |   1.4648 |       - |   27.31 KB |       0.037 |
| ReadList_Dapper                          | Job-WXJLOL | 10             | Default     | 100         |    123.40 μs |     0.196 μs |   0.117 μs |    123.51 μs |    123.53 | 0.039 |    0.00 |   1.9531 |       - |   32.58 KB |       0.044 |
| ReadList_EntityFramework                 | Job-WXJLOL | 10             | Default     | 100         |    209.53 μs |     0.552 μs |   0.365 μs |    210.10 μs |    210.31 | 0.066 |    0.00 |   5.3711 |  0.4883 |   95.23 KB |       0.128 |
| Update_Pengdows                          | Job-WXJLOL | 10             | Default     | 100         |  2,505.15 μs |     9.745 μs |   6.446 μs |  2,514.40 μs |  2,515.71 | 0.784 |    0.00 |  39.0625 |       - |  640.71 KB |       0.862 |
| Update_Dapper                            | Job-WXJLOL | 10             | Default     | 100         |  1,672.16 μs |     7.858 μs |   5.198 μs |  1,677.97 μs |  1,679.25 | 0.523 |    0.00 |  11.7188 |       - |  203.98 KB |       0.275 |
| Update_EntityFramework                   | Job-WXJLOL | 10             | Default     | 100         |  7,889.23 μs |    13.929 μs |   8.289 μs |  7,901.98 μs |  7,904.55 | 2.468 |    0.00 | 265.6250 | 31.2500 | 4392.32 KB |       5.911 |
| Delete_Pengdows                          | Job-WXJLOL | 10             | Default     | 100         |  5,905.40 μs |    11.193 μs |   7.403 μs |  5,913.42 μs |  5,914.09 | 1.848 |    0.00 |  85.9375 |       - | 1455.57 KB |       1.959 |
| Delete_Dapper                            | Job-WXJLOL | 10             | Default     | 100         |  3,921.72 μs |     8.754 μs |   4.579 μs |  3,928.42 μs |  3,929.54 | 1.227 |    0.00 |  31.2500 |       - |  566.48 KB |       0.762 |
| Delete_EntityFramework                   | Job-WXJLOL | 10             | Default     | 100         | 16,817.56 μs |    45.009 μs |  29.771 μs | 16,855.08 μs | 16,858.21 | 5.261 |    0.01 | 531.2500 | 62.5000 | 9018.95 KB |      12.138 |
| FilteredQuery_Pengdows                   | Job-WXJLOL | 10             | Default     | 100         |    152.55 μs |     0.414 μs |   0.274 μs |    152.86 μs |    152.93 | 0.048 |    0.00 |   1.7090 |       - |   29.06 KB |       0.039 |
| FilteredQuery_Dapper                     | Job-WXJLOL | 10             | Default     | 100         |    137.43 μs |     0.323 μs |   0.213 μs |    137.64 μs |    137.64 | 0.043 |    0.00 |   1.9531 |       - |   34.05 KB |       0.046 |
| FilteredQuery_EntityFramework            | Job-WXJLOL | 10             | Default     | 100         |    219.21 μs |     0.294 μs |   0.175 μs |    219.44 μs |    219.47 | 0.069 |    0.00 |   5.8594 |  0.4883 |   97.35 KB |       0.131 |
| Aggregate_Pengdows                       | Job-WXJLOL | 10             | Default     | 100         |  6,176.73 μs |    87.459 μs |  57.849 μs |  6,218.27 μs |  6,219.41 | 1.932 |    0.02 |  31.2500 |       - |   586.8 KB |       0.790 |
| Aggregate_Dapper                         | Job-WXJLOL | 10             | Default     | 100         |  5,010.37 μs |    36.148 μs |  23.910 μs |  5,045.06 μs |  5,049.46 | 1.568 |    0.01 |        - |       - |  124.29 KB |       0.167 |
| Aggregate_EntityFramework                | Job-WXJLOL | 10             | Default     | 100         | 10,671.37 μs |    14.028 μs |   8.348 μs | 10,683.25 μs | 10,686.01 | 3.339 |    0.00 | 250.0000 | 31.2500 | 4182.91 KB |       5.629 |
| BatchCreate_Pengdows                     | Job-WXJLOL | 10             | Default     | 100         |  3,342.12 μs |     7.274 μs |   4.811 μs |  3,348.62 μs |  3,348.77 | 1.046 |    0.00 |  50.7813 |       - |  863.37 KB |       1.162 |
| BatchCreate_Dapper                       | Job-WXJLOL | 10             | Default     | 100         |  2,249.54 μs |    13.156 μs |   8.702 μs |  2,259.89 μs |  2,260.33 | 0.704 |    0.00 |  23.4375 |       - |  414.92 KB |       0.558 |
| BatchCreate_EntityFramework              | Job-WXJLOL | 10             | Default     | 100         |  8,510.99 μs |    41.706 μs |  24.819 μs |  8,552.84 μs |  8,562.96 | 2.663 |    0.01 | 281.2500 | 31.2500 | 4696.41 KB |       6.320 |
| BulkCreate_Pengdows                      | Job-WXJLOL | 10             | Default     | 100         |  3,740.65 μs |     6.218 μs |   4.113 μs |  3,747.26 μs |  3,748.64 | 1.170 |    0.00 |  23.4375 |  7.8125 |  385.49 KB |       0.519 |
| BulkCreate_Dapper                        | Job-WXJLOL | 10             | Default     | 100         |  3,414.33 μs |     8.127 μs |   4.836 μs |  3,420.63 μs |  3,420.79 | 1.068 |    0.00 |  23.4375 |  7.8125 |  408.86 KB |       0.550 |
| BulkCreate_EntityFramework               | Job-WXJLOL | 10             | Default     | 100         |  3,453.21 μs |     6.150 μs |   3.660 μs |  3,458.54 μs |  3,459.13 | 1.080 |    0.00 |  27.3438 |  7.8125 |  454.56 KB |       0.612 |
| BulkVsLoop_SingleInserts_Pengdows        | Job-WXJLOL | 10             | Default     | 100         |  3,343.11 μs |     9.732 μs |   6.437 μs |  3,350.68 μs |  3,350.97 | 1.046 |    0.00 |  54.6875 |       - |  918.84 KB |       1.237 |
| BulkVsLoop_BatchCreate_Pengdows          | Job-WXJLOL | 10             | Default     | 100         |  3,717.52 μs |     1.714 μs |   1.134 μs |  3,718.74 μs |  3,718.76 | 1.163 |    0.00 |  23.4375 |  7.8125 |  385.49 KB |       0.519 |
| Breakdown_BuildVsExecute_Pengdows        | Job-WXJLOL | 10             | Default     | 100         |  3,289.11 μs |     9.585 μs |   6.340 μs |  3,297.44 μs |  3,298.41 | 1.029 |    0.00 |  42.9688 |       - |  743.11 KB |       1.000 |
| Breakdown_BuildVsExecute_Dapper          | Job-WXJLOL | 10             | Default     | 100         |  1,987.09 μs |     8.105 μs |   5.361 μs |  1,994.53 μs |  1,995.30 | 0.622 |    0.00 |  15.6250 |       - |   259.5 KB |       0.349 |
| Breakdown_BuildVsExecute_EntityFramework | Job-WXJLOL | 10             | Default     | 100         | 12,000.53 μs |    23.975 μs |  14.267 μs | 12,024.29 μs | 12,027.33 | 3.754 |    0.01 | 343.7500 | 31.2500 | 5761.35 KB |       7.754 |
| ConnectionHoldTime_Pengdows              | Job-WXJLOL | 10             | Default     | 100         |     33.11 μs |     0.387 μs |   0.256 μs |     33.29 μs |     33.30 | 0.010 |    0.00 |   0.4272 |       - |    7.54 KB |       0.010 |
| ConnectionHoldTime_Dapper                | Job-WXJLOL | 10             | Default     | 100         |     20.57 μs |     0.069 μs |   0.046 μs |     20.64 μs |     20.65 | 0.006 |    0.00 |   0.1526 |       - |     2.7 KB |       0.004 |
| ConnectionHoldTime_EntityFramework       | Job-WXJLOL | 10             | Default     | 100         |    118.68 μs |     0.330 μs |   0.197 μs |    118.86 μs |    118.86 | 0.037 |    0.00 |   3.4180 |       - |   57.72 KB |       0.078 |
|                                          |            |                |             |             |              |              |            |              |           |       |         |          |         |            |             |
| Create_Pengdows                          | ShortRun   | 3              | 1           | 100         |  3,210.25 μs |   146.412 μs |   8.025 μs |  3,216.93 μs |  3,217.41 | 0.973 |    0.00 |  46.8750 |       - |  823.44 KB |       1.108 |
| Create_Dapper                            | ShortRun   | 3              | 1           | 100         |  2,169.36 μs |   251.843 μs |  13.804 μs |  2,181.96 μs |  2,183.12 | 0.657 |    0.00 |  19.5313 |       - |  370.31 KB |       0.498 |
| Create_EntityFramework                   | ShortRun   | 3              | 1           | 100         |  8,506.49 μs | 2,197.735 μs | 120.465 μs |  8,625.08 μs |  8,641.41 | 2.577 |    0.03 | 281.2500 | 31.2500 | 4638.51 KB |       6.242 |
| ReadSingle_Pengdows                      | ShortRun   | 3              | 1           | 100         |  3,300.73 μs |   213.828 μs |  11.721 μs |  3,311.98 μs |  3,313.21 | 1.000 |    0.00 |  42.9688 |       - |  743.06 KB |       1.000 |
| ReadSingle_Dapper                        | ShortRun   | 3              | 1           | 100         |  2,000.63 μs |   750.450 μs |  41.135 μs |  2,041.00 μs |  2,046.70 | 0.606 |    0.01 |  15.6250 |       - |  259.46 KB |       0.349 |
| ReadSingle_EntityFramework               | ShortRun   | 3              | 1           | 100         | 11,790.71 μs |   534.443 μs |  29.295 μs | 11,813.09 μs | 11,814.30 | 3.572 |    0.01 | 343.7500 | 31.2500 |  5761.3 KB |       7.753 |
| ReadList_Pengdows                        | ShortRun   | 3              | 1           | 100         |    135.67 μs |     8.206 μs |   0.450 μs |    136.11 μs |    136.17 | 0.041 |    0.00 |   1.4648 |       - |   27.31 KB |       0.037 |
| ReadList_Dapper                          | ShortRun   | 3              | 1           | 100         |    122.27 μs |     1.818 μs |   0.100 μs |    122.36 μs |    122.37 | 0.037 |    0.00 |   1.9531 |  0.1221 |   32.58 KB |       0.044 |
| ReadList_EntityFramework                 | ShortRun   | 3              | 1           | 100         |    215.03 μs |    10.649 μs |   0.584 μs |    215.57 μs |    215.63 | 0.065 |    0.00 |   5.3711 |  0.4883 |   95.23 KB |       0.128 |
| Update_Pengdows                          | ShortRun   | 3              | 1           | 100         |  2,468.53 μs |    51.489 μs |   2.822 μs |  2,470.84 μs |  2,470.99 | 0.748 |    0.00 |  39.0625 |       - |  640.71 KB |       0.862 |
| Update_Dapper                            | ShortRun   | 3              | 1           | 100         |  1,641.34 μs |    80.562 μs |   4.416 μs |  1,645.69 μs |  1,646.23 | 0.497 |    0.00 |  11.7188 |       - |  203.98 KB |       0.275 |
| Update_EntityFramework                   | ShortRun   | 3              | 1           | 100         |  7,985.40 μs |   714.883 μs |  39.185 μs |  8,024.06 μs |  8,029.14 | 2.419 |    0.01 | 265.6250 | 31.2500 | 4392.32 KB |       5.911 |
| Delete_Pengdows                          | ShortRun   | 3              | 1           | 100         |  5,860.74 μs |   210.279 μs |  11.526 μs |  5,872.02 μs |  5,873.36 | 1.776 |    0.01 |  85.9375 |       - | 1455.57 KB |       1.959 |
| Delete_Dapper                            | ShortRun   | 3              | 1           | 100         |  3,997.83 μs | 2,133.066 μs | 116.921 μs |  4,112.92 μs |  4,128.80 | 1.211 |    0.03 |  31.2500 |       - |  566.48 KB |       0.762 |
| Delete_EntityFramework                   | ShortRun   | 3              | 1           | 100         | 16,460.66 μs |   152.702 μs |   8.370 μs | 16,468.90 μs | 16,470.03 | 4.987 |    0.02 | 531.2500 | 62.5000 | 9018.95 KB |      12.138 |
| FilteredQuery_Pengdows                   | ShortRun   | 3              | 1           | 100         |    150.68 μs |     1.765 μs |   0.097 μs |    150.74 μs |    150.74 | 0.046 |    0.00 |   1.7090 |       - |   29.06 KB |       0.039 |
| FilteredQuery_Dapper                     | ShortRun   | 3              | 1           | 100         |    138.50 μs |     4.160 μs |   0.228 μs |    138.73 μs |    138.75 | 0.042 |    0.00 |   1.9531 |       - |   34.05 KB |       0.046 |
| FilteredQuery_EntityFramework            | ShortRun   | 3              | 1           | 100         |    220.51 μs |    12.148 μs |   0.666 μs |    221.16 μs |    221.25 | 0.067 |    0.00 |   5.8594 |  0.4883 |   97.35 KB |       0.131 |
| Aggregate_Pengdows                       | ShortRun   | 3              | 1           | 100         |  6,103.09 μs |   308.692 μs |  16.920 μs |  6,117.76 μs |  6,118.93 | 1.849 |    0.01 |  31.2500 |       - |   586.8 KB |       0.790 |
| Aggregate_Dapper                         | ShortRun   | 3              | 1           | 100         |  5,058.86 μs | 1,083.734 μs |  59.403 μs |  5,116.61 μs |  5,123.25 | 1.533 |    0.02 |        - |       - |  124.29 KB |       0.167 |
| Aggregate_EntityFramework                | ShortRun   | 3              | 1           | 100         | 10,801.48 μs |   265.621 μs |  14.560 μs | 10,815.76 μs | 10,817.49 | 3.272 |    0.01 | 250.0000 | 31.2500 | 4182.91 KB |       5.629 |
| BatchCreate_Pengdows                     | ShortRun   | 3              | 1           | 100         |  3,314.40 μs |    63.035 μs |   3.455 μs |  3,317.29 μs |  3,317.49 | 1.004 |    0.00 |  50.7813 |       - |  863.37 KB |       1.162 |
| BatchCreate_Dapper                       | ShortRun   | 3              | 1           | 100         |  2,215.76 μs |   193.622 μs |  10.613 μs |  2,226.12 μs |  2,227.33 | 0.671 |    0.00 |  23.4375 |       - |  414.92 KB |       0.558 |
| BatchCreate_EntityFramework              | ShortRun   | 3              | 1           | 100         |  8,554.96 μs | 2,112.741 μs | 115.806 μs |  8,669.16 μs |  8,684.51 | 2.592 |    0.03 | 281.2500 | 31.2500 | 4696.41 KB |       6.320 |
| BulkCreate_Pengdows                      | ShortRun   | 3              | 1           | 100         |  3,132.97 μs |     2.739 μs |   0.150 μs |  3,133.09 μs |  3,133.09 | 0.949 |    0.00 |  23.4375 |  7.8125 |  385.49 KB |       0.519 |
| BulkCreate_Dapper                        | ShortRun   | 3              | 1           | 100         |  3,451.81 μs |   308.172 μs |  16.892 μs |  3,468.47 μs |  3,470.69 | 1.046 |    0.01 |  23.4375 |  7.8125 |  408.86 KB |       0.550 |
| BulkCreate_EntityFramework               | ShortRun   | 3              | 1           | 100         |  3,478.00 μs |   198.498 μs |  10.880 μs |  3,488.73 μs |  3,490.17 | 1.054 |    0.00 |  27.3438 |  7.8125 |  454.56 KB |       0.612 |
| BulkVsLoop_SingleInserts_Pengdows        | ShortRun   | 3              | 1           | 100         |  3,198.12 μs |    61.705 μs |   3.382 μs |  3,201.45 μs |  3,201.91 | 0.969 |    0.00 |  54.6875 |       - |  918.84 KB |       1.237 |
| BulkVsLoop_BatchCreate_Pengdows          | ShortRun   | 3              | 1           | 100         |  3,729.47 μs |    80.006 μs |   4.385 μs |  3,733.79 μs |  3,734.38 | 1.130 |    0.00 |  23.4375 |  7.8125 |  385.49 KB |       0.519 |
| Breakdown_BuildVsExecute_Pengdows        | ShortRun   | 3              | 1           | 100         |  3,294.24 μs |   280.658 μs |  15.384 μs |  3,309.41 μs |  3,311.39 | 0.998 |    0.01 |  42.9688 |       - |  743.11 KB |       1.000 |
| Breakdown_BuildVsExecute_Dapper          | ShortRun   | 3              | 1           | 100         |  2,025.81 μs |   213.601 μs |  11.708 μs |  2,037.32 μs |  2,038.74 | 0.614 |    0.00 |  15.6250 |       - |   259.5 KB |       0.349 |
| Breakdown_BuildVsExecute_EntityFramework | ShortRun   | 3              | 1           | 100         | 11,884.70 μs | 1,293.701 μs |  70.912 μs | 11,954.44 μs | 11,963.08 | 3.601 |    0.02 | 343.7500 | 31.2500 | 5761.35 KB |       7.754 |
| ConnectionHoldTime_Pengdows              | ShortRun   | 3              | 1           | 100         |     33.06 μs |     2.512 μs |   0.138 μs |     33.19 μs |     33.20 | 0.010 |    0.00 |   0.4272 |       - |    7.54 KB |       0.010 |
| ConnectionHoldTime_Dapper                | ShortRun   | 3              | 1           | 100         |     20.77 μs |     0.292 μs |   0.016 μs |     20.78 μs |     20.78 | 0.006 |    0.00 |   0.1526 |       - |     2.7 KB |       0.004 |
| ConnectionHoldTime_EntityFramework       | ShortRun   | 3              | 1           | 100         |    119.16 μs |    33.609 μs |   1.842 μs |    120.97 μs |    121.22 | 0.036 |    0.00 |   3.4180 |       - |   57.72 KB |       0.078 |

---

## CrudBenchmarks.IndexedViewPerformanceBenchmarks-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  Job-XOISIN : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

WarmupCount=3  

```
| Method                           | Job        | InvocationCount | IterationCount | LaunchCount | UnrollFactor | CustomerCount | OrdersPerCustomer | Mean       | Error        | StdDev      | P95        | P99      | Gen0     | Gen1     | Gen2    | Allocated  |
|--------------------------------- |----------- |---------------- |--------------- |------------ |------------- |-------------- |------------------ |-----------:|-------------:|------------:|-----------:|---------:|---------:|---------:|--------:|-----------:|
| **FullAggregate_Pengdows**           | **Job-XOISIN** | **50**              | **5**              | **Default**     | **1**            | **5000**          | **50**                | **1,907.1 μs** |     **74.55 μs** |    **19.36 μs** | **1,928.6 μs** | **1,929.60** |  **40.0000** |  **20.0000** |       **-** |  **680.32 KB** |
| FullAggregate_Dapper             | Job-XOISIN | 50              | 5              | Default     | 1            | 5000          | 50                | 1,816.6 μs |    120.55 μs |    31.31 μs | 1,855.1 μs | 1,859.16 |  60.0000 |  20.0000 |       - | 1150.74 KB |
| FullAggregate_EntityFramework    | Job-XOISIN | 50              | 5              | Default     | 1            | 5000          | 50                | 2,418.5 μs |    701.83 μs |   108.61 μs | 2,544.1 μs | 2,561.60 | 100.0000 |  40.0000 |       - | 1698.96 KB |
| QueryIndexedView_Pengdows        | Job-XOISIN | 50              | 5              | Default     | 1            | 5000          | 50                |   497.2 μs |     30.97 μs |     4.79 μs |   502.4 μs |   502.88 |        - |        - |       - |    12.2 KB |
| QueryIndexedView_Dapper          | Job-XOISIN | 50              | 5              | Default     | 1            | 5000          | 50                |   294.0 μs |     38.09 μs |     9.89 μs |   305.6 μs |   306.75 |        - |        - |       - |    8.78 KB |
| QueryIndexedView_EntityFramework | Job-XOISIN | 50              | 5              | Default     | 1            | 5000          | 50                |   415.7 μs |     60.60 μs |    15.74 μs |   431.7 μs |   433.74 |        - |        - |       - |   24.92 KB |
| FullAggregate_Pengdows           | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 50                | 1,821.0 μs |    641.57 μs |    35.17 μs | 1,844.7 μs | 1,845.41 |  41.0156 |  19.5313 |       - |  679.06 KB |
| FullAggregate_Dapper             | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 50                | 1,788.0 μs |    169.96 μs |     9.32 μs | 1,794.8 μs | 1,795.13 |  70.3125 |  27.3438 |       - | 1150.71 KB |
| FullAggregate_EntityFramework    | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 50                | 2,390.6 μs |    121.54 μs |     6.66 μs | 2,396.0 μs | 2,396.33 | 101.5625 |  39.0625 |       - | 1698.03 KB |
| QueryIndexedView_Pengdows        | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 50                |   407.6 μs |     26.29 μs |     1.44 μs |   408.7 μs |   408.81 |   0.4883 |        - |       - |   12.12 KB |
| QueryIndexedView_Dapper          | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 50                |   241.3 μs |     58.59 μs |     3.21 μs |   243.7 μs |   243.81 |   0.4883 |        - |       - |     8.7 KB |
| QueryIndexedView_EntityFramework | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 50                |   278.6 μs |     95.50 μs |     5.23 μs |   283.8 μs |   284.47 |   1.4648 |        - |       - |    24.8 KB |
| **FullAggregate_Pengdows**           | **Job-XOISIN** | **50**              | **5**              | **Default**     | **1**            | **10000**         | **50**                | **3,360.2 μs** |    **326.17 μs** |    **50.47 μs** | **3,405.5 μs** | **3,406.21** |  **60.0000** |  **40.0000** | **20.0000** | **1354.85 KB** |
| FullAggregate_Dapper             | Job-XOISIN | 50              | 5              | Default     | 1            | 10000         | 50                | 3,988.8 μs |    189.22 μs |    49.14 μs | 4,035.8 μs | 4,038.01 | 160.0000 | 100.0000 | 40.0000 | 2303.51 KB |
| FullAggregate_EntityFramework    | Job-XOISIN | 50              | 5              | Default     | 1            | 10000         | 50                | 6,002.3 μs |  3,952.01 μs |   611.58 μs | 6,661.8 μs | 6,720.55 | 220.0000 | 160.0000 | 40.0000 | 3319.08 KB |
| QueryIndexedView_Pengdows        | Job-XOISIN | 50              | 5              | Default     | 1            | 10000         | 50                |   553.9 μs |     87.03 μs |    22.60 μs |   581.0 μs |   585.16 |        - |        - |       - |   12.18 KB |
| QueryIndexedView_Dapper          | Job-XOISIN | 50              | 5              | Default     | 1            | 10000         | 50                |   311.5 μs |     55.79 μs |    14.49 μs |   329.9 μs |   332.45 |        - |        - |       - |    8.77 KB |
| QueryIndexedView_EntityFramework | Job-XOISIN | 50              | 5              | Default     | 1            | 10000         | 50                |   406.3 μs |     83.19 μs |    12.87 μs |   420.4 μs |   421.80 |        - |        - |       - |   24.91 KB |
| FullAggregate_Pengdows           | ShortRun   | Default         | 3              | 1           | 16           | 10000         | 50                | 3,568.0 μs |     80.16 μs |     4.39 μs | 3,572.3 μs | 3,572.92 |  93.7500 |  85.9375 | 31.2500 |    1358 KB |
| FullAggregate_Dapper             | ShortRun   | Default         | 3              | 1           | 16           | 10000         | 50                | 4,378.6 μs |  1,785.20 μs |    97.85 μs | 4,475.0 μs | 4,487.34 | 171.8750 | 125.0000 | 39.0625 |  2303.5 KB |
| FullAggregate_EntityFramework    | ShortRun   | Default         | 3              | 1           | 16           | 10000         | 50                | 6,697.3 μs | 33,604.77 μs | 1,841.99 μs | 8,509.9 μs | 8,760.65 | 218.7500 | 140.6250 | 31.2500 | 3318.85 KB |
| QueryIndexedView_Pengdows        | ShortRun   | Default         | 3              | 1           | 16           | 10000         | 50                |   416.9 μs |    118.52 μs |     6.50 μs |   423.3 μs |   424.12 |   0.4883 |        - |       - |   12.12 KB |
| QueryIndexedView_Dapper          | ShortRun   | Default         | 3              | 1           | 16           | 10000         | 50                |   237.6 μs |     45.47 μs |     2.49 μs |   240.0 μs |   240.27 |   0.4883 |        - |       - |     8.7 KB |
| QueryIndexedView_EntityFramework | ShortRun   | Default         | 3              | 1           | 16           | 10000         | 50                |   277.9 μs |     79.02 μs |     4.33 μs |   282.1 μs |   282.73 |   1.4648 |        - |       - |   24.79 KB |

---

## CrudBenchmarks.Internal.CloningPerformanceTest-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  Job-XWLHDM : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

WarmupCount=3  

```
| Method                    | Job        | IterationCount | LaunchCount | Mean      | Error     | StdDev   | P95       | P99    | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|-------------------------- |----------- |--------------- |------------ |----------:|----------:|---------:|----------:|-------:|------:|-------:|-------:|----------:|------------:|
| BuildRetrieve_Traditional | Job-XWLHDM | 5              | Default     | 543.26 ns | 18.631 ns | 4.838 ns | 549.25 ns | 549.76 |  1.00 | 0.1278 |      - |    2144 B |        1.00 |
| BuildRetrieve_WithCloning | Job-XWLHDM | 5              | Default     | 210.82 ns |  3.565 ns | 0.552 ns | 211.47 ns | 211.59 |  0.39 | 0.0772 | 0.0002 |    1296 B |        0.60 |
| BasicSqlContainer         | Job-XWLHDM | 5              | Default     | 101.95 ns |  2.529 ns | 0.391 ns | 102.20 ns | 102.21 |  0.19 | 0.0272 |      - |     456 B |        0.21 |
|                           |            |                |             |           |           |          |           |        |       |        |        |           |             |
| BuildRetrieve_Traditional | ShortRun   | 3              | 1           | 620.88 ns |  9.159 ns | 0.502 ns | 621.36 ns | 621.42 |  1.00 | 0.1278 |      - |    2144 B |        1.00 |
| BuildRetrieve_WithCloning | ShortRun   | 3              | 1           | 217.40 ns | 10.978 ns | 0.602 ns | 217.99 ns | 218.06 |  0.35 | 0.0772 | 0.0002 |    1296 B |        0.60 |
| BasicSqlContainer         | ShortRun   | 3              | 1           |  99.38 ns |  4.151 ns | 0.228 ns |  99.55 ns |  99.56 |  0.16 | 0.0272 |      - |     456 B |        0.21 |

---

## CrudBenchmarks.Internal.ConnectionStringNormalizationBenchmarks-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

Job=ShortRun  InvocationCount=1  IterationCount=3  
LaunchCount=1  UnrollFactor=1  WarmupCount=3  

```
| Method            | Mean        | Error       | StdDev    | P95         | P99       | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------ |------------:|------------:|----------:|------------:|----------:|------:|--------:|----------:|------------:|
| Normalize_NoCache | 13,269.0 ns | 11,398.3 ns | 624.78 ns | 13,882.8 ns | 13,958.16 |  1.00 |    0.06 |    3056 B |        1.00 |
| Normalize_Cached  |    588.3 ns |    532.0 ns |  29.16 ns |    617.0 ns |    621.00 |  0.04 |    0.00 |     736 B |        0.24 |

---

## CrudBenchmarks.Internal.IsolationBenchmarks-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  Job-WXJLOL : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

WarmupCount=3  

```
| Method                            | Job        | IterationCount | LaunchCount | Mean           | Error         | StdDev      | P95            | P99       | Ratio  | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------------------- |----------- |--------------- |------------ |---------------:|--------------:|------------:|---------------:|----------:|-------:|--------:|-------:|-------:|----------:|------------:|
| SqlGeneration_Mine_BuildContainer | Job-WXJLOL | 10             | Default     |    557.2420 ns |     1.1035 ns |   0.6567 ns |    558.0501 ns |    558.13 |  1.000 |    0.00 | 0.1278 |      - |    2144 B |        1.00 |
| SqlGeneration_Mine_GetSql         | Job-WXJLOL | 10             | Default     |    580.1939 ns |     4.3282 ns |   2.5757 ns |    584.5962 ns |    585.16 |  1.041 |    0.00 | 0.1383 | 0.0010 |    2328 B |        1.09 |
| SqlGeneration_Static              | Job-WXJLOL | 10             | Default     |      0.2388 ns |     0.0023 ns |   0.0012 ns |      0.2402 ns |      0.24 |  0.000 |    0.00 |      - |      - |         - |        0.00 |
| ObjectLoading_Mine                | Job-WXJLOL | 10             | Default     |  9,415.7849 ns |   122.8516 ns |  81.2588 ns |  9,522.2833 ns |  9,531.46 | 16.897 |    0.14 | 0.3967 | 0.3815 |    7088 B |        3.31 |
| ObjectLoading_Mine_DirectReader   | Job-WXJLOL | 10             | Default     |  6,882.2986 ns |   128.9466 ns |  85.2902 ns |  6,993.7795 ns |  7,015.18 | 12.351 |    0.15 | 0.2899 | 0.2747 |    5208 B |        2.43 |
| ObjectLoading_Dapper              | Job-WXJLOL | 10             | Default     |  3,360.0135 ns |    70.9495 ns |  46.9287 ns |  3,405.9248 ns |  3,406.65 |  6.030 |    0.08 | 0.1411 | 0.1373 |    2648 B |        1.24 |
| ParameterCreation_Mine            | Job-WXJLOL | 10             | Default     |     82.0116 ns |     0.1324 ns |   0.0876 ns |     82.1290 ns |     82.16 |  0.147 |    0.00 | 0.0076 |      - |     128 B |        0.06 |
| ParameterCreation_Dapper          | Job-WXJLOL | 10             | Default     |     51.1840 ns |     0.2475 ns |   0.1637 ns |     51.3788 ns |     51.43 |  0.092 |    0.00 | 0.0225 |      - |     376 B |        0.18 |
| ConnectionOverhead_Mine           | Job-WXJLOL | 10             | Default     |  6,479.6088 ns |    61.5066 ns |  36.6016 ns |  6,535.2673 ns |  6,536.87 | 11.628 |    0.06 | 0.2594 | 0.2441 |    4832 B |        2.25 |
| ConnectionOverhead_Direct         | Job-WXJLOL | 10             | Default     |    944.0409 ns |    51.3104 ns |  33.9386 ns |    992.2835 ns |    995.63 |  1.694 |    0.06 | 0.0477 | 0.0458 |    1056 B |        0.49 |
|                                   |            |                |             |                |               |             |                |           |        |         |        |        |           |             |
| SqlGeneration_Mine_BuildContainer | ShortRun   | 3              | 1           |    548.9731 ns |    63.3747 ns |   3.4738 ns |    551.4808 ns |    551.59 |  1.000 |    0.01 | 0.1278 |      - |    2144 B |        1.00 |
| SqlGeneration_Mine_GetSql         | ShortRun   | 3              | 1           |    574.7966 ns |    94.1832 ns |   5.1625 ns |    579.8892 ns |    580.57 |  1.047 |    0.01 | 0.1383 | 0.0010 |    2328 B |        1.09 |
| SqlGeneration_Static              | ShortRun   | 3              | 1           |      0.1790 ns |     0.1502 ns |   0.0082 ns |      0.1870 ns |      0.19 |  0.000 |    0.00 |      - |      - |         - |        0.00 |
| ObjectLoading_Mine                | ShortRun   | 3              | 1           | 10,509.9757 ns | 5,515.4690 ns | 302.3215 ns | 10,747.8678 ns | 10,762.08 | 19.145 |    0.49 | 0.3967 | 0.3815 |    6832 B |        3.19 |
| ObjectLoading_Mine_DirectReader   | ShortRun   | 3              | 1           |  6,942.7555 ns | 1,364.9974 ns |  74.8201 ns |  6,999.9297 ns |  7,003.02 | 12.647 |    0.14 | 0.2899 | 0.2747 |    4952 B |        2.31 |
| ObjectLoading_Dapper              | ShortRun   | 3              | 1           |  3,495.7628 ns | 4,925.0824 ns | 269.9604 ns |  3,762.0905 ns |  3,796.91 |  6.368 |    0.43 | 0.1373 | 0.1297 |    2392 B |        1.12 |
| ParameterCreation_Mine            | ShortRun   | 3              | 1           |     80.5965 ns |    10.9795 ns |   0.6018 ns |     81.1885 ns |     81.27 |  0.147 |    0.00 | 0.0076 |      - |     128 B |        0.06 |
| ParameterCreation_Dapper          | ShortRun   | 3              | 1           |     51.6666 ns |     2.5805 ns |   0.1414 ns |     51.8020 ns |     51.82 |  0.094 |    0.00 | 0.0225 |      - |     376 B |        0.18 |
| ConnectionOverhead_Mine           | ShortRun   | 3              | 1           |  6,441.4007 ns | 1,795.7922 ns |  98.4334 ns |  6,521.6253 ns |  6,526.98 | 11.734 |    0.17 | 0.2594 | 0.2441 |    4576 B |        2.13 |
| ConnectionOverhead_Direct         | ShortRun   | 3              | 1           |    948.8345 ns |   565.6729 ns |  31.0064 ns |    978.4237 ns |    981.58 |  1.728 |    0.05 | 0.0477 | 0.0458 |     800 B |        0.37 |

---

## CrudBenchmarks.Internal.PerformanceOptimizationBenchmarks-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  Job-SPJUDM : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

IterationCount=10  WarmupCount=3  

```
| Method                        | Mean     | Error   | StdDev  | P95      | P99    | Gen0   | Allocated |
|------------------------------ |---------:|--------:|--------:|---------:|-------:|-------:|----------:|
| RetrieveOne_Baseline_WithList | 677.0 ns | 5.30 ns | 3.51 ns | 681.6 ns | 681.73 | 0.1078 |   1.77 KB |

---

## CrudBenchmarks.Internal.ProcWrappingStrategyLookupBenchmarks-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  Job-KZPGKK : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

WarmupCount=3  

```
| Method                  | Job        | IterationCount | LaunchCount | Mean     | Error    | StdDev   | P95      | P99   | Ratio | Allocated | Alloc Ratio |
|------------------------ |----------- |--------------- |------------ |---------:|---------:|---------:|---------:|------:|------:|----------:|------------:|
| MutableDictionaryLookup | Job-KZPGKK | 12             | Default     | 21.16 ns | 0.090 ns | 0.065 ns | 21.27 ns | 21.27 |  1.00 |         - |          NA |
| FrozenDictionaryLookup  | Job-KZPGKK | 12             | Default     | 26.90 ns | 0.310 ns | 0.242 ns | 27.32 ns | 27.34 |  1.27 |         - |          NA |
|                         |            |                |             |          |          |          |          |       |       |           |             |
| MutableDictionaryLookup | ShortRun   | 3              | 1           | 19.14 ns | 0.416 ns | 0.023 ns | 19.16 ns | 19.16 |  1.00 |         - |          NA |
| FrozenDictionaryLookup  | ShortRun   | 3              | 1           | 29.63 ns | 2.715 ns | 0.149 ns | 29.72 ns | 29.72 |  1.55 |         - |          NA |

---

## CrudBenchmarks.Internal.ReaderMappingBenchmark-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  Job-XWLHDM : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

WarmupCount=3  

```
| Method                        | Job        | IterationCount | LaunchCount | RowCount | Mean        | Error     | StdDev   | P95         | P99       | Ratio | RatioSD | Gen0     | Gen1     | Allocated  | Alloc Ratio |
|------------------------------ |----------- |--------------- |------------ |--------- |------------:|----------:|---------:|------------:|----------:|------:|--------:|---------:|---------:|-----------:|------------:|
| **PengdowsCrud_OptimizedMapping** | **Job-XWLHDM** | **5**              | **Default**     | **100**      |    **267.6 μs** |   **1.69 μs** |  **0.26 μs** |    **267.8 μs** |    **267.84** |  **1.00** |    **0.00** |   **1.4648** |        **-** |   **24.17 KB** |        **1.00** |
| PureReflection_NoOptimization | Job-XWLHDM | 5              | Default     | 100      |  1,009.1 μs |   5.29 μs |  1.37 μs |  1,010.8 μs |  1,010.97 |  3.77 |    0.01 |  31.2500 |   7.8125 |  535.08 KB |       22.14 |
| Dapper_OptimizedMapping       | Job-XWLHDM | 5              | Default     | 100      |    248.6 μs |   2.47 μs |  0.38 μs |    249.0 μs |    249.02 |  0.93 |    0.00 |   2.4414 |        - |   41.89 KB |        1.73 |
|                               |            |                |             |          |             |           |          |             |           |       |         |          |          |            |             |
| PengdowsCrud_OptimizedMapping | ShortRun   | 3              | 1           | 100      |    268.1 μs |   8.83 μs |  0.48 μs |    268.6 μs |    268.67 |  1.00 |    0.00 |   1.4648 |        - |   24.17 KB |        1.00 |
| PureReflection_NoOptimization | ShortRun   | 3              | 1           | 100      |    998.7 μs | 150.24 μs |  8.24 μs |  1,006.8 μs |  1,007.96 |  3.72 |    0.03 |  31.2500 |   7.8125 |  535.08 KB |       22.14 |
| Dapper_OptimizedMapping       | ShortRun   | 3              | 1           | 100      |    249.3 μs |  22.67 μs |  1.24 μs |    250.5 μs |    250.64 |  0.93 |    0.00 |   2.4414 |        - |   41.89 KB |        1.73 |
|                               |            |                |             |          |             |           |          |             |           |       |         |          |          |            |             |
| **PengdowsCrud_OptimizedMapping** | **Job-XWLHDM** | **5**              | **Default**     | **1000**     |    **624.7 μs** |   **4.30 μs** |  **1.12 μs** |    **626.0 μs** |    **626.11** |  **1.00** |    **0.00** |  **11.7188** |   **3.9063** |  **199.97 KB** |        **1.00** |
| PureReflection_NoOptimization | Job-XWLHDM | 5              | Default     | 1000     |  9,945.7 μs |  40.54 μs |  6.27 μs |  9,952.9 μs |  9,953.77 | 15.92 |    0.03 | 312.5000 | 265.6250 | 5344.48 KB |       26.73 |
| Dapper_OptimizedMapping       | Job-XWLHDM | 5              | Default     | 1000     |    490.1 μs |   1.53 μs |  0.24 μs |    490.3 μs |    490.35 |  0.78 |    0.00 |  23.4375 |   9.7656 |  386.44 KB |        1.93 |
|                               |            |                |             |          |             |           |          |             |           |       |         |          |          |            |             |
| PengdowsCrud_OptimizedMapping | ShortRun   | 3              | 1           | 1000     |    623.3 μs |  18.02 μs |  0.99 μs |    624.3 μs |    624.44 |  1.00 |    0.00 |  11.7188 |   3.9063 |  199.97 KB |        1.00 |
| PureReflection_NoOptimization | ShortRun   | 3              | 1           | 1000     | 10,057.9 μs | 236.32 μs | 12.95 μs | 10,070.0 μs | 10,071.27 | 16.14 |    0.03 | 312.5000 | 265.6250 | 5344.48 KB |       26.73 |
| Dapper_OptimizedMapping       | ShortRun   | 3              | 1           | 1000     |    488.0 μs |   6.53 μs |  0.36 μs |    488.3 μs |    488.33 |  0.78 |    0.00 |  23.4375 |  10.2539 |  386.44 KB |        1.93 |

---

## CrudBenchmarks.Internal.SqlGenerationBenchmark-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  Job-WXJLOL : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

WarmupCount=3  

```
| Method                             | Job        | IterationCount | LaunchCount | Mean          | Error       | StdDev    | P95           | P99      | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------------------------- |----------- |--------------- |------------ |--------------:|------------:|----------:|--------------:|---------:|------:|-------:|-------:|----------:|------------:|
| SqlGeneration_Mine_BuildContainer  | Job-WXJLOL | 10             | Default     |   544.6403 ns |   2.1434 ns | 1.4177 ns |   546.7943 ns |   546.97 | 1.000 | 0.1278 |      - |    2144 B |        1.00 |
| SqlGeneration_Mine_GetSql          | Job-WXJLOL | 10             | Default     |   563.8966 ns |   1.2015 ns | 0.7150 ns |   564.8442 ns |   565.08 | 1.035 | 0.1383 | 0.0010 |    2328 B |        1.09 |
| SqlGeneration_Static               | Job-WXJLOL | 10             | Default     |     0.1846 ns |   0.0012 ns | 0.0007 ns |     0.1856 ns |     0.19 | 0.000 |      - |      - |         - |        0.00 |
| SqlGeneration_Mine_CreateContainer | Job-WXJLOL | 10             | Default     |   109.3885 ns |   0.3644 ns | 0.2410 ns |   109.6624 ns |   109.71 | 0.201 | 0.0272 |      - |     456 B |        0.21 |
| SqlGeneration_Mine_BuildUpdate     | Job-WXJLOL | 10             | Default     | 1,017.1864 ns |   2.4384 ns | 1.6129 ns | 1,019.3607 ns | 1,019.71 | 1.868 | 0.1411 |      - |    2376 B |        1.11 |
| SqlGeneration_Mine_BuildCreate     | Job-WXJLOL | 10             | Default     |   573.8647 ns |   1.8231 ns | 1.2059 ns |   575.5824 ns |   575.59 | 1.054 | 0.0906 |      - |    1520 B |        0.71 |
| ParameterCreation_Mine             | Job-WXJLOL | 10             | Default     |    81.0130 ns |   0.0365 ns | 0.0217 ns |    81.0370 ns |    81.04 | 0.149 | 0.0076 |      - |     128 B |        0.06 |
| ParameterCreation_Dapper           | Job-WXJLOL | 10             | Default     |    51.7838 ns |   0.0960 ns | 0.0571 ns |    51.8544 ns |    51.87 | 0.095 | 0.0225 |      - |     376 B |        0.18 |
| ParameterCreation_Mine_String      | Job-WXJLOL | 10             | Default     |    35.1279 ns |   0.0439 ns | 0.0261 ns |    35.1629 ns |    35.17 | 0.064 | 0.0033 |      - |      56 B |        0.03 |
| ParameterCreation_Mine_Int         | Job-WXJLOL | 10             | Default     |    31.5971 ns |   0.0349 ns | 0.0207 ns |    31.6265 ns |    31.64 | 0.058 | 0.0048 |      - |      80 B |        0.04 |
| ContainerOperations_AddParameter   | Job-WXJLOL | 10             | Default     |   182.8499 ns |   0.7943 ns | 0.5254 ns |   183.5507 ns |   183.68 | 0.336 | 0.0715 |      - |    1200 B |        0.56 |
| ContainerOperations_BuildQuery     | Job-WXJLOL | 10             | Default     |   146.5317 ns |   0.2233 ns | 0.1477 ns |   146.6881 ns |   146.72 | 0.269 | 0.0558 |      - |     936 B |        0.44 |
|                                    |            |                |             |               |             |           |               |          |       |        |        |           |             |
| SqlGeneration_Mine_BuildContainer  | ShortRun   | 3              | 1           |   547.3218 ns |  16.7868 ns | 0.9201 ns |   548.2251 ns |   548.35 | 1.000 | 0.1278 |      - |    2144 B |        1.00 |
| SqlGeneration_Mine_GetSql          | ShortRun   | 3              | 1           |   563.0574 ns |  18.4096 ns | 1.0091 ns |   563.8226 ns |   563.86 | 1.029 | 0.1383 | 0.0010 |    2328 B |        1.09 |
| SqlGeneration_Static               | ShortRun   | 3              | 1           |     0.0270 ns |   0.0257 ns | 0.0014 ns |     0.0280 ns |     0.03 | 0.000 |      - |      - |         - |        0.00 |
| SqlGeneration_Mine_CreateContainer | ShortRun   | 3              | 1           |   100.3051 ns |   0.3839 ns | 0.0210 ns |   100.3258 ns |   100.33 | 0.183 | 0.0272 |      - |     456 B |        0.21 |
| SqlGeneration_Mine_BuildUpdate     | ShortRun   | 3              | 1           | 1,000.0843 ns | 167.4193 ns | 9.1768 ns | 1,009.1255 ns | 1,010.36 | 1.827 | 0.1411 |      - |    2376 B |        1.11 |
| SqlGeneration_Mine_BuildCreate     | ShortRun   | 3              | 1           |   585.9531 ns |   9.7212 ns | 0.5329 ns |   586.2895 ns |   586.30 | 1.071 | 0.0906 |      - |    1520 B |        0.71 |
| ParameterCreation_Mine             | ShortRun   | 3              | 1           |    80.4621 ns |   1.3201 ns | 0.0724 ns |    80.5309 ns |    80.54 | 0.147 | 0.0076 |      - |     128 B |        0.06 |
| ParameterCreation_Dapper           | ShortRun   | 3              | 1           |    51.2040 ns |   2.2185 ns | 0.1216 ns |    51.2765 ns |    51.28 | 0.094 | 0.0225 |      - |     376 B |        0.18 |
| ParameterCreation_Mine_String      | ShortRun   | 3              | 1           |    34.5079 ns |   1.0551 ns | 0.0578 ns |    34.5648 ns |    34.57 | 0.063 | 0.0033 |      - |      56 B |        0.03 |
| ParameterCreation_Mine_Int         | ShortRun   | 3              | 1           |    31.1274 ns |   2.9876 ns | 0.1638 ns |    31.2882 ns |    31.31 | 0.057 | 0.0048 |      - |      80 B |        0.04 |
| ContainerOperations_AddParameter   | ShortRun   | 3              | 1           |   184.7979 ns |   8.9351 ns | 0.4898 ns |   185.1845 ns |   185.21 | 0.338 | 0.0715 |      - |    1200 B |        0.56 |
| ContainerOperations_BuildQuery     | ShortRun   | 3              | 1           |   152.3762 ns |   6.1059 ns | 0.3347 ns |   152.6030 ns |   152.61 | 0.278 | 0.0558 |      - |     936 B |        0.44 |

---

## CrudBenchmarks.Internal.TableGatewayClientOnlyBenchmarks-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  Job-WXJLOL : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

WarmupCount=3  

```
| Method               | Job        | IterationCount | LaunchCount | Mean     | Error    | StdDev  | P95      | P99    | Gen0   | Allocated |
|--------------------- |----------- |--------------- |------------ |---------:|---------:|--------:|---------:|-------:|-------:|----------:|
| BuildRetrieve_Single | Job-WXJLOL | 10             | Default     | 625.5 ns |  0.73 ns | 0.44 ns | 626.0 ns | 626.07 | 0.1049 |   1.73 KB |
| MapReader_Single     | Job-WXJLOL | 10             | Default     | 548.8 ns |  0.78 ns | 0.52 ns | 549.6 ns | 549.58 | 0.0620 |   1.02 KB |
| BuildRetrieve_Single | ShortRun   | 3              | 1           | 651.8 ns | 38.10 ns | 2.09 ns | 653.9 ns | 654.10 | 0.1049 |   1.73 KB |
| MapReader_Single     | ShortRun   | 3              | 1           | 514.1 ns | 13.21 ns | 0.72 ns | 514.6 ns | 514.67 | 0.0620 |   1.02 KB |

---

## CrudBenchmarks.Internal.TypeHandlingBenchmarks-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  Job-WXJLOL : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

WarmupCount=3  

```
| Method                            | Job        | IterationCount | LaunchCount | Mean        | Error      | StdDev    | P95         | P99    | Ratio    | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------------- |----------- |--------------- |------------ |------------:|-----------:|----------:|------------:|-------:|---------:|--------:|-------:|----------:|------------:|
| Baseline_SimpleParameter          | Job-WXJLOL | 10             | Default     |   0.8016 ns |  0.0109 ns | 0.0072 ns |   0.8118 ns |   0.81 |     1.00 |    0.01 |      - |         - |          NA |
| AdvancedType_Inet_Configure       | Job-WXJLOL | 10             | Default     |  59.9716 ns |  0.1174 ns | 0.0777 ns |  60.0777 ns |  60.09 |    74.82 |    0.65 | 0.0072 |     120 B |          NA |
| AdvancedType_Range_Configure      | Job-WXJLOL | 10             | Default     | 163.9122 ns |  0.1962 ns | 0.1298 ns | 164.0973 ns | 164.15 |   204.50 |    1.76 | 0.0162 |     272 B |          NA |
| AdvancedType_Geometry_Configure   | Job-WXJLOL | 10             | Default     |  15.2327 ns |  0.0131 ns | 0.0087 ns |  15.2427 ns |  15.24 |    19.00 |    0.16 |      - |         - |          NA |
| AdvancedType_RowVersion_Configure | Job-WXJLOL | 10             | Default     |  82.8593 ns |  0.1525 ns | 0.1009 ns |  83.0006 ns |  83.04 |   103.38 |    0.89 | 0.0072 |     120 B |          NA |
| AdvancedType_Null_Configure       | Job-WXJLOL | 10             | Default     |  37.8983 ns |  0.1255 ns | 0.0747 ns |  38.0218 ns |  38.04 |    47.28 |    0.41 | 0.0019 |      32 B |          NA |
| AdvancedType_Cached_Configure     | Job-WXJLOL | 10             | Default     | 121.9837 ns |  0.2064 ns | 0.1228 ns | 122.1416 ns | 122.17 |   152.19 |    1.31 | 0.0143 |     240 B |          NA |
| Coercion_Guid_Write               | Job-WXJLOL | 10             | Default     |  24.6367 ns |  0.1719 ns | 0.1137 ns |  24.8200 ns |  24.86 |    30.74 |    0.30 | 0.0038 |      64 B |          NA |
| Coercion_RowVersion_Write         | Job-WXJLOL | 10             | Default     |  22.5173 ns |  0.0174 ns | 0.0104 ns |  22.5293 ns |  22.53 |    28.09 |    0.24 |      - |         - |          NA |
| Coercion_Json_Write               | Job-WXJLOL | 10             | Default     |  28.5757 ns |  0.0259 ns | 0.0172 ns |  28.5980 ns |  28.60 |    35.65 |    0.31 | 0.0033 |      56 B |          NA |
| Coercion_HStore_Write             | Job-WXJLOL | 10             | Default     | 223.8698 ns |  0.2344 ns | 0.1550 ns | 224.0346 ns | 224.05 |   279.30 |    2.40 | 0.0224 |     376 B |          NA |
| Coercion_Range_Write              | Job-WXJLOL | 10             | Default     | 193.7044 ns |  0.3535 ns | 0.2338 ns | 194.0137 ns | 194.02 |   241.67 |    2.09 | 0.0110 |     184 B |          NA |
| Coercion_TimeSpan_Write           | Job-WXJLOL | 10             | Default     |  18.1012 ns |  0.0380 ns | 0.0251 ns |  18.1396 ns |  18.15 |    22.58 |    0.20 | 0.0029 |      48 B |          NA |
| Coercion_DateTimeOffset_Write     | Job-WXJLOL | 10             | Default     |  17.5224 ns |  0.1009 ns | 0.0667 ns |  17.6114 ns |  17.61 |    21.86 |    0.20 | 0.0038 |      64 B |          NA |
| Coercion_IntArray_Write           | Job-WXJLOL | 10             | Default     |  22.6825 ns |  0.0557 ns | 0.0368 ns |  22.7270 ns |  22.74 |    28.30 |    0.25 |      - |         - |          NA |
| Coercion_StringArray_Write        | Job-WXJLOL | 10             | Default     |  20.7203 ns |  0.0934 ns | 0.0618 ns |  20.7952 ns |  20.80 |    25.85 |    0.23 |      - |         - |          NA |
| Coercion_Guid_Read                | Job-WXJLOL | 10             | Default     |  16.4724 ns |  0.0370 ns | 0.0220 ns |  16.5055 ns |  16.51 |    20.55 |    0.18 | 0.0038 |      64 B |          NA |
| Coercion_RowVersion_Read          | Job-WXJLOL | 10             | Default     |  10.9779 ns |  0.0195 ns | 0.0116 ns |  10.9931 ns |  11.00 |    13.70 |    0.12 |      - |         - |          NA |
| Coercion_Json_Read                | Job-WXJLOL | 10             | Default     | 324.6279 ns |  0.5628 ns | 0.3723 ns | 325.1667 ns | 325.38 |   405.01 |    3.49 | 0.0076 |     128 B |          NA |
| Coercion_HStore_Read              | Job-WXJLOL | 10             | Default     | 804.9281 ns |  1.1740 ns | 0.7766 ns | 805.8642 ns | 805.91 | 1,004.24 |    8.64 | 0.0887 |    1496 B |          NA |
| Coercion_Range_Read               | Job-WXJLOL | 10             | Default     | 228.9610 ns |  0.7295 ns | 0.4341 ns | 229.5169 ns | 229.62 |   285.65 |    2.50 | 0.0186 |     312 B |          NA |
| Coercion_TimeSpan_Read            | Job-WXJLOL | 10             | Default     |  15.0810 ns |  0.0413 ns | 0.0246 ns |  15.1080 ns |  15.11 |    18.82 |    0.16 | 0.0029 |      48 B |          NA |
| Coercion_DateTimeOffset_Read      | Job-WXJLOL | 10             | Default     |  15.9847 ns |  0.0355 ns | 0.0235 ns |  16.0193 ns |  16.02 |    19.94 |    0.17 | 0.0038 |      64 B |          NA |
| Coercion_IntArray_Read            | Job-WXJLOL | 10             | Default     |  10.5810 ns |  0.0094 ns | 0.0062 ns |  10.5885 ns |  10.59 |    13.20 |    0.11 |      - |         - |          NA |
| Coercion_StringArray_Read         | Job-WXJLOL | 10             | Default     |   8.0688 ns |  0.0133 ns | 0.0088 ns |   8.0784 ns |   8.08 |    10.07 |    0.09 |      - |         - |          NA |
| Complex_JsonParsing               | Job-WXJLOL | 10             | Default     | 459.1695 ns |  0.7576 ns | 0.5011 ns | 459.9853 ns | 460.09 |   572.86 |    4.94 | 0.0091 |     152 B |          NA |
| Complex_HStoreParsing             | Job-WXJLOL | 10             | Default     | 911.4659 ns |  0.4860 ns | 0.2542 ns | 911.7446 ns | 911.76 | 1,137.15 |    9.74 | 0.1230 |    2072 B |          NA |
| Complex_RangeParsing              | Job-WXJLOL | 10             | Default     | 356.8874 ns |  0.6698 ns | 0.3986 ns | 357.4593 ns | 357.63 |   445.26 |    3.84 | 0.0186 |     312 B |          NA |
| ProviderSpecific_PostgreSqlGuid   | Job-WXJLOL | 10             | Default     |  28.1607 ns |  0.0192 ns | 0.0114 ns |  28.1717 ns |  28.17 |    35.13 |    0.30 | 0.0043 |      72 B |          NA |
| ProviderSpecific_SqlServerJson    | Job-WXJLOL | 10             | Default     |  28.4613 ns |  0.0484 ns | 0.0288 ns |  28.4937 ns |  28.50 |    35.51 |    0.31 | 0.0033 |      56 B |          NA |
| ProviderSpecific_MySqlBoolean     | Job-WXJLOL | 10             | Default     |  29.3369 ns |  0.0248 ns | 0.0164 ns |  29.3608 ns |  29.37 |    36.60 |    0.31 | 0.0029 |      48 B |          NA |
| HotPath_MixedCoercion             | Job-WXJLOL | 10             | Default     | 254.2495 ns |  0.4318 ns | 0.2569 ns | 254.6738 ns | 254.78 |   317.20 |    2.73 | 0.0210 |     352 B |          NA |
| HotPath_CachedLookup              | Job-WXJLOL | 10             | Default     | 101.2707 ns |  0.2649 ns | 0.1385 ns | 101.4270 ns | 101.45 |   126.35 |    1.09 | 0.0210 |     352 B |          NA |
| HotPath_AdvancedTypeParameter     | Job-WXJLOL | 10             | Default     |  59.5366 ns |  0.0969 ns | 0.0577 ns |  59.6158 ns |  59.64 |    74.28 |    0.64 | 0.0072 |     120 B |          NA |
| Lookup_GetMapping                 | Job-WXJLOL | 10             | Default     |   7.6907 ns |  0.0097 ns | 0.0058 ns |   7.6980 ns |   7.70 |     9.60 |    0.08 |      - |         - |          NA |
| Lookup_GetConverter               | Job-WXJLOL | 10             | Default     |   6.7062 ns |  0.0125 ns | 0.0083 ns |   6.7159 ns |   6.72 |     8.37 |    0.07 |      - |         - |          NA |
| Coercion_InetConverter            | Job-WXJLOL | 10             | Default     |  86.0580 ns |  0.0825 ns | 0.0546 ns |  86.1392 ns |  86.15 |   107.37 |    0.92 | 0.0114 |     192 B |          NA |
| Coercion_RangeConverter           | Job-WXJLOL | 10             | Default     | 232.3088 ns |  0.3355 ns | 0.2219 ns | 232.6564 ns | 232.70 |   289.83 |    2.49 | 0.0129 |     216 B |          NA |
| Coercion_GeometryConverter        | Job-WXJLOL | 10             | Default     |  17.1231 ns |  0.0302 ns | 0.0200 ns |  17.1534 ns |  17.16 |    21.36 |    0.18 | 0.0038 |      64 B |          NA |
| Failure_UnregisteredType          | Job-WXJLOL | 10             | Default     |  67.9880 ns |  0.1295 ns | 0.0771 ns |  68.0552 ns |  68.06 |    84.82 |    0.73 | 0.0033 |      56 B |          NA |
|                                   |            |                |             |             |            |           |             |        |          |         |        |           |             |
| Baseline_SimpleParameter          | ShortRun   | 3              | 1           |   0.8078 ns |  0.1682 ns | 0.0092 ns |   0.8168 ns |   0.82 |     1.00 |    0.01 |      - |         - |          NA |
| AdvancedType_Inet_Configure       | ShortRun   | 3              | 1           |  60.0881 ns |  2.5754 ns | 0.1412 ns |  60.1780 ns |  60.18 |    74.39 |    0.75 | 0.0072 |     120 B |          NA |
| AdvancedType_Range_Configure      | ShortRun   | 3              | 1           | 162.3876 ns |  0.9614 ns | 0.0527 ns | 162.4396 ns | 162.45 |   201.05 |    1.98 | 0.0162 |     272 B |          NA |
| AdvancedType_Geometry_Configure   | ShortRun   | 3              | 1           |  15.3013 ns |  1.1346 ns | 0.0622 ns |  15.3599 ns |  15.37 |    18.94 |    0.20 |      - |         - |          NA |
| AdvancedType_RowVersion_Configure | ShortRun   | 3              | 1           |  82.2482 ns |  0.3288 ns | 0.0180 ns |  82.2622 ns |  82.26 |   101.83 |    1.00 | 0.0072 |     120 B |          NA |
| AdvancedType_Null_Configure       | ShortRun   | 3              | 1           |  36.8087 ns |  2.5024 ns | 0.1372 ns |  36.9411 ns |  36.96 |    45.57 |    0.47 | 0.0019 |      32 B |          NA |
| AdvancedType_Cached_Configure     | ShortRun   | 3              | 1           | 122.1089 ns |  3.9802 ns | 0.2182 ns | 122.3241 ns | 122.35 |   151.18 |    1.50 | 0.0143 |     240 B |          NA |
| Coercion_Guid_Write               | ShortRun   | 3              | 1           |  25.2344 ns |  1.4291 ns | 0.0783 ns |  25.2829 ns |  25.28 |    31.24 |    0.32 | 0.0038 |      64 B |          NA |
| Coercion_RowVersion_Write         | ShortRun   | 3              | 1           |  22.9699 ns |  0.6435 ns | 0.0353 ns |  23.0047 ns |  23.01 |    28.44 |    0.28 |      - |         - |          NA |
| Coercion_Json_Write               | ShortRun   | 3              | 1           |  26.0227 ns |  0.6555 ns | 0.0359 ns |  26.0484 ns |  26.05 |    32.22 |    0.32 | 0.0033 |      56 B |          NA |
| Coercion_HStore_Write             | ShortRun   | 3              | 1           | 220.5455 ns |  6.1407 ns | 0.3366 ns | 220.7909 ns | 220.80 |   273.05 |    2.71 | 0.0224 |     376 B |          NA |
| Coercion_Range_Write              | ShortRun   | 3              | 1           | 186.6712 ns | 31.0460 ns | 1.7017 ns | 188.3473 ns | 188.56 |   231.11 |    2.91 | 0.0110 |     184 B |          NA |
| Coercion_TimeSpan_Write           | ShortRun   | 3              | 1           |  19.3770 ns |  0.9289 ns | 0.0509 ns |  19.4227 ns |  19.43 |    23.99 |    0.24 | 0.0029 |      48 B |          NA |
| Coercion_DateTimeOffset_Write     | ShortRun   | 3              | 1           |  16.9530 ns |  1.2710 ns | 0.0697 ns |  17.0015 ns |  17.00 |    20.99 |    0.22 | 0.0038 |      64 B |          NA |
| Coercion_IntArray_Write           | ShortRun   | 3              | 1           |  22.5682 ns |  2.7778 ns | 0.1523 ns |  22.6797 ns |  22.68 |    27.94 |    0.32 |      - |         - |          NA |
| Coercion_StringArray_Write        | ShortRun   | 3              | 1           |  20.8197 ns |  0.3903 ns | 0.0214 ns |  20.8404 ns |  20.84 |    25.78 |    0.25 |      - |         - |          NA |
| Coercion_Guid_Read                | ShortRun   | 3              | 1           |  15.6858 ns |  0.4093 ns | 0.0224 ns |  15.7070 ns |  15.71 |    19.42 |    0.19 | 0.0038 |      64 B |          NA |
| Coercion_RowVersion_Read          | ShortRun   | 3              | 1           |  10.7973 ns |  0.0869 ns | 0.0048 ns |  10.8006 ns |  10.80 |    13.37 |    0.13 |      - |         - |          NA |
| Coercion_Json_Read                | ShortRun   | 3              | 1           | 305.0381 ns |  9.0780 ns | 0.4976 ns | 305.5282 ns | 305.60 |   377.66 |    3.75 | 0.0076 |     128 B |          NA |
| Coercion_HStore_Read              | ShortRun   | 3              | 1           | 777.3600 ns | 29.0580 ns | 1.5928 ns | 778.9257 ns | 779.14 |   962.43 |    9.60 | 0.0887 |    1496 B |          NA |
| Coercion_Range_Read               | ShortRun   | 3              | 1           | 228.8130 ns |  9.1208 ns | 0.4999 ns | 229.2759 ns | 229.32 |   283.29 |    2.83 | 0.0186 |     312 B |          NA |
| Coercion_TimeSpan_Read            | ShortRun   | 3              | 1           |  15.1958 ns |  4.0685 ns | 0.2230 ns |  15.3498 ns |  15.36 |    18.81 |    0.30 | 0.0029 |      48 B |          NA |
| Coercion_DateTimeOffset_Read      | ShortRun   | 3              | 1           |  17.1463 ns |  0.6630 ns | 0.0363 ns |  17.1792 ns |  17.18 |    21.23 |    0.21 | 0.0038 |      64 B |          NA |
| Coercion_IntArray_Read            | ShortRun   | 3              | 1           |  10.5719 ns |  0.1062 ns | 0.0058 ns |  10.5776 ns |  10.58 |    13.09 |    0.13 |      - |         - |          NA |
| Coercion_StringArray_Read         | ShortRun   | 3              | 1           |   8.0739 ns |  0.0451 ns | 0.0025 ns |   8.0757 ns |   8.08 |    10.00 |    0.10 |      - |         - |          NA |
| Complex_JsonParsing               | ShortRun   | 3              | 1           | 473.9829 ns | 22.6985 ns | 1.2442 ns | 475.2103 ns | 475.37 |   586.82 |    5.92 | 0.0086 |     152 B |          NA |
| Complex_HStoreParsing             | ShortRun   | 3              | 1           | 935.9429 ns | 28.1336 ns | 1.5421 ns | 936.9261 ns | 936.95 | 1,158.76 |   11.50 | 0.1230 |    2072 B |          NA |
| Complex_RangeParsing              | ShortRun   | 3              | 1           | 355.7839 ns |  5.5632 ns | 0.3049 ns | 356.0606 ns | 356.09 |   440.48 |    4.34 | 0.0186 |     312 B |          NA |
| ProviderSpecific_PostgreSqlGuid   | ShortRun   | 3              | 1           |  28.4661 ns |  0.5730 ns | 0.0314 ns |  28.4849 ns |  28.49 |    35.24 |    0.35 | 0.0043 |      72 B |          NA |
| ProviderSpecific_SqlServerJson    | ShortRun   | 3              | 1           |  25.7579 ns |  0.3175 ns | 0.0174 ns |  25.7700 ns |  25.77 |    31.89 |    0.31 | 0.0033 |      56 B |          NA |
| ProviderSpecific_MySqlBoolean     | ShortRun   | 3              | 1           |  27.2635 ns |  0.5959 ns | 0.0327 ns |  27.2956 ns |  27.30 |    33.75 |    0.33 | 0.0029 |      48 B |          NA |
| HotPath_MixedCoercion             | ShortRun   | 3              | 1           | 244.5460 ns | 16.9586 ns | 0.9296 ns | 245.4569 ns | 245.57 |   302.76 |    3.14 | 0.0210 |     352 B |          NA |
| HotPath_CachedLookup              | ShortRun   | 3              | 1           | 101.0310 ns |  3.4113 ns | 0.1870 ns | 101.1678 ns | 101.17 |   125.08 |    1.24 | 0.0210 |     352 B |          NA |
| HotPath_AdvancedTypeParameter     | ShortRun   | 3              | 1           |  59.6645 ns |  2.9236 ns | 0.1603 ns |  59.7806 ns |  59.79 |    73.87 |    0.75 | 0.0072 |     120 B |          NA |
| Lookup_GetMapping                 | ShortRun   | 3              | 1           |   9.3318 ns |  7.8785 ns | 0.4318 ns |   9.7014 ns |   9.73 |    11.55 |    0.48 |      - |         - |          NA |
| Lookup_GetConverter               | ShortRun   | 3              | 1           |   6.4784 ns |  0.1521 ns | 0.0083 ns |   6.4844 ns |   6.48 |     8.02 |    0.08 |      - |         - |          NA |
| Coercion_InetConverter            | ShortRun   | 3              | 1           |  82.4039 ns |  2.3518 ns | 0.1289 ns |  82.4919 ns |  82.49 |   102.02 |    1.01 | 0.0114 |     192 B |          NA |
| Coercion_RangeConverter           | ShortRun   | 3              | 1           | 242.4419 ns | 17.0722 ns | 0.9358 ns | 243.2110 ns | 243.26 |   300.16 |    3.11 | 0.0129 |     216 B |          NA |
| Coercion_GeometryConverter        | ShortRun   | 3              | 1           |  16.9940 ns |  2.0647 ns | 0.1132 ns |  17.0723 ns |  17.07 |    21.04 |    0.24 | 0.0038 |      64 B |          NA |
| Failure_UnregisteredType          | ShortRun   | 3              | 1           |  67.3774 ns |  2.6201 ns | 0.1436 ns |  67.5189 ns |  67.54 |    83.42 |    0.83 | 0.0033 |      56 B |          NA |

---

## CrudBenchmarks.Internal.ValueTaskExecutionBenchmarks-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                           | Mean     | Error    | StdDev   | P95      | P99    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------- |---------:|---------:|---------:|---------:|-------:|------:|--------:|-------:|----------:|------------:|
| ExecuteNonQuery_ValueTask        | 352.0 ns | 310.4 ns | 17.01 ns | 368.7 ns | 371.01 |  1.00 |    0.06 | 0.0176 |     296 B |        1.00 |
| ExecuteNonQuery_ValueTask_AsTask | 362.3 ns | 287.8 ns | 15.78 ns | 377.9 ns | 379.97 |  1.03 |    0.06 | 0.0176 |     296 B |        1.00 |

---

## CrudBenchmarks.MaterializedViewPerformanceBenchmarks-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  Job-XOISIN : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

WarmupCount=3  

```
| Method                                      | Job        | InvocationCount | IterationCount | LaunchCount | UnrollFactor | CustomerCount | OrdersPerCustomer | Parallelism | OperationsPerRun | Mean         | Error            | StdDev          | Median      | P95            | P99          | Gen0     | Gen1     | Allocated  |
|-------------------------------------------- |----------- |---------------- |--------------- |------------ |------------- |-------------- |------------------ |------------ |----------------- |-------------:|-----------------:|----------------:|------------:|---------------:|-------------:|---------:|---------:|-----------:|
| **MaterializedView_Pengdows**                   | **Job-XOISIN** | **50**              | **5**              | **Default**     | **1**            | **2000**          | **15**                | **16**          | **64**               |     **338.3 μs** |         **26.77 μs** |         **6.95 μs** |    **337.6 μs** |       **346.3 μs** |       **346.87** |        **-** |        **-** |    **9.53 KB** |
| MaterializedView_Dapper                     | Job-XOISIN | 50              | 5              | Default     | 1            | 2000          | 15                | 16          | 64               |     227.0 μs |         16.50 μs |         4.28 μs |    227.3 μs |       232.1 μs |       232.83 |        - |        - |     4.6 KB |
| MaterializedView_EntityFramework            | Job-XOISIN | 50              | 5              | Default     | 1            | 2000          | 15                | 16          | 64               |     404.6 μs |        200.65 μs |        31.05 μs |    393.3 μs |       442.3 μs |       448.35 |        - |        - |   20.66 KB |
| TableScan_Pengdows                          | Job-XOISIN | 50              | 5              | Default     | 1            | 2000          | 15                | 16          | 64               |     354.2 μs |         22.18 μs |         3.43 μs |    355.0 μs |       357.0 μs |       357.12 |        - |        - |   10.35 KB |
| TableScan_Dapper                            | Job-XOISIN | 50              | 5              | Default     | 1            | 2000          | 15                | 16          | 64               |     282.0 μs |         20.22 μs |         3.13 μs |    282.5 μs |       284.9 μs |       285.10 |        - |        - |    5.53 KB |
| TableScan_EntityFramework                   | Job-XOISIN | 50              | 5              | Default     | 1            | 2000          | 15                | 16          | 64               |     462.1 μs |         31.51 μs |         8.18 μs |    463.2 μs |       472.0 μs |       473.55 |        - |        - |   22.11 KB |
| MaterializedView_Pengdows_Concurrent        | Job-XOISIN | 50              | 5              | Default     | 1            | 2000          | 15                | 16          | 64               |   4,127.3 μs |        892.06 μs |       231.67 μs |  4,157.0 μs |     4,373.7 μs |     4,389.14 |  20.0000 |        - |  578.69 KB |
| MaterializedView_Dapper_Concurrent          | Job-XOISIN | 50              | 5              | Default     | 1            | 2000          | 15                | 16          | 64               |   6,269.4 μs |      7,513.70 μs |     1,951.28 μs |  7,035.1 μs |     8,003.1 μs |     8,023.85 |        - |        - |  262.02 KB |
| MaterializedView_EntityFramework_Concurrent | Job-XOISIN | 50              | 5              | Default     | 1            | 2000          | 15                | 16          | 64               |   6,204.8 μs |      4,812.75 μs |     1,249.86 μs |  6,802.7 μs |     7,179.3 μs |     7,190.80 | 220.0000 |  80.0000 | 3420.71 KB |
| TableScan_Pengdows_Concurrent               | Job-XOISIN | 50              | 5              | Default     | 1            | 2000          | 15                | 16          | 64               |   4,072.3 μs |        709.43 μs |       109.79 μs |  4,077.0 μs |     4,181.1 μs |     4,187.83 |  20.0000 |        - |  630.38 KB |
| TableScan_Dapper_Concurrent                 | Job-XOISIN | 50              | 5              | Default     | 1            | 2000          | 15                | 16          | 64               |   3,476.2 μs |        739.82 μs |       192.13 μs |  3,532.8 μs |     3,673.8 μs |     3,687.35 |  20.0000 |        - |  324.84 KB |
| TableScan_EntityFramework_Concurrent        | Job-XOISIN | 50              | 5              | Default     | 1            | 2000          | 15                | 16          | 64               |   6,284.7 μs |      4,874.88 μs |     1,265.99 μs |  5,775.7 μs |     7,742.1 μs |     7,797.44 | 220.0000 | 100.0000 | 3512.87 KB |
| MaterializedView_Pengdows                   | ShortRun   | Default         | 3              | 1           | 16           | 2000          | 15                | 16          | 64               |     265.0 μs |         46.95 μs |         2.57 μs |    263.9 μs |       267.6 μs |       267.87 |   0.4883 |        - |    9.11 KB |
| MaterializedView_Dapper                     | ShortRun   | Default         | 3              | 1           | 16           | 2000          | 15                | 16          | 64               |     188.8 μs |         16.73 μs |         0.92 μs |    188.6 μs |       189.7 μs |       189.82 |   0.2441 |        - |    4.14 KB |
| MaterializedView_EntityFramework            | ShortRun   | Default         | 3              | 1           | 16           | 2000          | 15                | 16          | 64               |     255.4 μs |         18.84 μs |         1.03 μs |    255.9 μs |       256.1 μs |       256.09 |   0.9766 |        - |   20.16 KB |
| TableScan_Pengdows                          | ShortRun   | Default         | 3              | 1           | 16           | 2000          | 15                | 16          | 64               |     277.2 μs |         37.48 μs |         2.05 μs |    276.9 μs |       279.1 μs |       279.32 |   0.4883 |        - |    9.92 KB |
| TableScan_Dapper                            | ShortRun   | Default         | 3              | 1           | 16           | 2000          | 15                | 16          | 64               |     250.1 μs |         34.25 μs |         1.88 μs |    250.1 μs |       251.8 μs |       251.97 |        - |        - |    5.07 KB |
| TableScan_EntityFramework                   | ShortRun   | Default         | 3              | 1           | 16           | 2000          | 15                | 16          | 64               |     333.0 μs |         61.07 μs |         3.35 μs |    332.4 μs |       336.2 μs |       336.49 |   0.9766 |        - |   21.59 KB |
| MaterializedView_Pengdows_Concurrent        | ShortRun   | Default         | 3              | 1           | 16           | 2000          | 15                | 16          | 64               |   4,411.2 μs |      8,800.39 μs |       482.38 μs |  4,674.3 μs |     4,701.7 μs |     4,704.16 |        - |        - |  577.05 KB |
| MaterializedView_Dapper_Concurrent          | ShortRun   | Default         | 3              | 1           | 16           | 2000          | 15                | 16          | 64               |   2,675.5 μs |      1,081.94 μs |        59.30 μs |  2,683.2 μs |     2,725.8 μs |     2,729.59 |  15.6250 |        - |  266.05 KB |
| MaterializedView_EntityFramework_Concurrent | ShortRun   | Default         | 3              | 1           | 16           | 2000          | 15                | 16          | 64               |  12,385.0 μs |     34,230.09 μs |     1,876.27 μs | 11,902.9 μs |    14,200.0 μs |    14,404.23 | 187.5000 |  62.5000 | 3416.37 KB |
| TableScan_Pengdows_Concurrent               | ShortRun   | Default         | 3              | 1           | 16           | 2000          | 15                | 16          | 64               |   5,092.4 μs |      5,915.34 μs |       324.24 μs |  5,226.0 μs |     5,318.2 μs |     5,326.45 |        - |        - |  635.32 KB |
| TableScan_Dapper_Concurrent                 | ShortRun   | Default         | 3              | 1           | 16           | 2000          | 15                | 16          | 64               |   4,019.4 μs |      2,977.52 μs |       163.21 μs |  4,012.1 μs |     4,168.7 μs |     4,182.63 |  15.6250 |        - |  326.39 KB |
| TableScan_EntityFramework_Concurrent        | ShortRun   | Default         | 3              | 1           | 16           | 2000          | 15                | 16          | 64               |   7,214.0 μs |      9,257.25 μs |       507.42 μs |  7,216.1 μs |     7,669.9 μs |     7,710.26 | 218.7500 |  93.7500 | 3514.96 KB |
| **MaterializedView_Pengdows**                   | **Job-XOISIN** | **50**              | **5**              | **Default**     | **1**            | **5000**          | **15**                | **16**          | **64**               |     **364.9 μs** |         **31.00 μs** |         **4.80 μs** |    **365.8 μs** |       **369.2 μs** |       **369.68** |        **-** |        **-** |    **9.53 KB** |
| MaterializedView_Dapper                     | Job-XOISIN | 50              | 5              | Default     | 1            | 5000          | 15                | 16          | 64               |     247.8 μs |         54.20 μs |         8.39 μs |    245.0 μs |       257.9 μs |       259.25 |        - |        - |    4.54 KB |
| MaterializedView_EntityFramework            | Job-XOISIN | 50              | 5              | Default     | 1            | 5000          | 15                | 16          | 64               |     461.6 μs |        290.24 μs |        75.37 μs |    473.3 μs |       554.5 μs |       569.91 |        - |        - |   20.61 KB |
| TableScan_Pengdows                          | Job-XOISIN | 50              | 5              | Default     | 1            | 5000          | 15                | 16          | 64               |     344.8 μs |         16.53 μs |         4.29 μs |    344.2 μs |       349.0 μs |       349.00 |        - |        - |   10.35 KB |
| TableScan_Dapper                            | Job-XOISIN | 50              | 5              | Default     | 1            | 5000          | 15                | 16          | 64               |     289.8 μs |         13.12 μs |         3.41 μs |    291.5 μs |       291.9 μs |       291.91 |        - |        - |    5.53 KB |
| TableScan_EntityFramework                   | Job-XOISIN | 50              | 5              | Default     | 1            | 5000          | 15                | 16          | 64               |     441.2 μs |         27.10 μs |         7.04 μs |    443.3 μs |       446.6 μs |       446.96 |        - |        - |   22.06 KB |
| MaterializedView_Pengdows_Concurrent        | Job-XOISIN | 50              | 5              | Default     | 1            | 5000          | 15                | 16          | 64               |   3,686.1 μs |         63.85 μs |        16.58 μs |  3,683.3 μs |     3,707.8 μs |     3,711.22 |  20.0000 |        - |  578.41 KB |
| MaterializedView_Dapper_Concurrent          | Job-XOISIN | 50              | 5              | Default     | 1            | 5000          | 15                | 16          | 64               |   2,879.7 μs |        996.00 μs |       258.66 μs |  2,967.8 μs |     3,122.6 μs |     3,129.52 |        - |        - |  265.81 KB |
| MaterializedView_EntityFramework_Concurrent | Job-XOISIN | 50              | 5              | Default     | 1            | 5000          | 15                | 16          | 64               |   6,053.6 μs |      3,667.73 μs |       952.50 μs |  6,156.5 μs |     7,068.6 μs |     7,140.54 | 220.0000 | 100.0000 | 3420.33 KB |
| TableScan_Pengdows_Concurrent               | Job-XOISIN | 50              | 5              | Default     | 1            | 5000          | 15                | 16          | 64               |   4,090.1 μs |      1,170.89 μs |       181.20 μs |  4,012.7 μs |     4,309.7 μs |     4,350.14 |  20.0000 |        - |  630.57 KB |
| TableScan_Dapper_Concurrent                 | Job-XOISIN | 50              | 5              | Default     | 1            | 5000          | 15                | 16          | 64               |   3,323.3 μs |        740.55 μs |       192.32 μs |  3,346.0 μs |     3,539.4 μs |     3,559.39 |  20.0000 |        - |   325.1 KB |
| TableScan_EntityFramework_Concurrent        | Job-XOISIN | 50              | 5              | Default     | 1            | 5000          | 15                | 16          | 64               |   6,503.8 μs |      4,398.17 μs |     1,142.19 μs |  6,474.2 μs |     7,740.3 μs |     7,811.01 | 220.0000 | 100.0000 | 3512.74 KB |
| MaterializedView_Pengdows                   | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 15                | 16          | 64               |     275.2 μs |         72.18 μs |         3.96 μs |    274.4 μs |       279.0 μs |       279.36 |   0.4883 |        - |    9.11 KB |
| MaterializedView_Dapper                     | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 15                | 16          | 64               |     188.4 μs |         58.36 μs |         3.20 μs |    188.1 μs |       191.4 μs |       191.68 |   0.2441 |        - |    4.14 KB |
| MaterializedView_EntityFramework            | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 15                | 16          | 64               |     258.4 μs |          7.46 μs |         0.41 μs |    258.2 μs |       258.8 μs |       258.89 |   0.9766 |        - |   20.16 KB |
| TableScan_Pengdows                          | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 15                | 16          | 64               |     277.1 μs |         21.13 μs |         1.16 μs |    276.9 μs |       278.2 μs |       278.35 |   0.4883 |        - |    9.92 KB |
| TableScan_Dapper                            | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 15                | 16          | 64               |     249.5 μs |         30.60 μs |         1.68 μs |    248.7 μs |       251.1 μs |       251.36 |        - |        - |    5.07 KB |
| TableScan_EntityFramework                   | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 15                | 16          | 64               |     342.8 μs |        170.70 μs |         9.36 μs |    340.2 μs |       351.9 μs |       352.89 |   0.9766 |        - |   21.58 KB |
| MaterializedView_Pengdows_Concurrent        | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 15                | 16          | 64               |   4,963.1 μs |     11,819.11 μs |       647.85 μs |  4,640.4 μs |     5,602.1 μs |     5,687.57 |        - |        - |  601.12 KB |
| MaterializedView_Dapper_Concurrent          | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 15                | 16          | 64               |   2,617.6 μs |        473.32 μs |        25.94 μs |  2,613.5 μs |     2,642.2 μs |     2,644.74 |  15.6250 |        - |  265.91 KB |
| MaterializedView_EntityFramework_Concurrent | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 15                | 16          | 64               |   4,002.2 μs |      1,267.11 μs |        69.45 μs |  3,973.6 μs |     4,070.6 μs |     4,079.21 | 218.7500 |  93.7500 | 3420.66 KB |
| TableScan_Pengdows_Concurrent               | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 15                | 16          | 64               | 660,539.5 μs | 20,730,678.10 μs | 1,136,318.42 μs |  4,920.4 μs | 1,775,874.2 μs | 1,933,292.34 |        - |        - |  631.03 KB |
| TableScan_Dapper_Concurrent                 | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 15                | 16          | 64               |   2,999.4 μs |        410.64 μs |        22.51 μs |  3,007.3 μs |     3,015.9 μs |     3,016.65 |  19.5313 |   3.9063 |  325.49 KB |
| TableScan_EntityFramework_Concurrent        | ShortRun   | Default         | 3              | 1           | 16           | 5000          | 15                | 16          | 64               |   4,833.9 μs |      1,108.41 μs |        60.76 μs |  4,841.8 μs |     4,885.4 μs |     4,889.30 | 226.5625 |  93.7500 | 3513.75 KB |

---

## CrudBenchmarks.ServerExecutionTimeBenchmarks-report-github.md

_Source: 
_Modified: 

```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.101
  [Host]     : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  Job-GEZALO : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2
  ShortRun   : .NET 8.0.23 (8.0.2325.60607), X64 RyuJIT AVX2

UnrollFactor=1  

```
| Method                             | Job        | InvocationCount | IterationCount | LaunchCount | WarmupCount | Parallelism | OperationsPerRun | Mean       | Error       | StdDev   | P95        | P99      | Allocated |
|----------------------------------- |----------- |---------------- |--------------- |------------ |------------ |------------ |----------------- |-----------:|------------:|---------:|-----------:|---------:|----------:|
| SingleRead_Pengdows                | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   332.8 μs |     6.47 μs |  3.85 μs |   337.8 μs |   338.87 |   8.39 KB |
| SingleRead_Dapper                  | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   220.9 μs |     7.20 μs |  4.29 μs |   225.6 μs |   225.65 |    3.6 KB |
| SingleRead_EntityFramework         | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   359.5 μs |    20.98 μs | 12.48 μs |   374.4 μs |   374.44 |  16.51 KB |
| CompositeKeyRead_Pengdows          | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   337.6 μs |    16.70 μs |  8.74 μs |   350.8 μs |   355.20 |   8.81 KB |
| CompositeKeyRead_Dapper            | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   249.3 μs |    14.02 μs |  8.34 μs |   261.9 μs |   264.30 |   4.08 KB |
| CompositeKeyRead_EntityFramework   | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |         NA |          NA |       NA |         NA |       NA |        NA |
| ListRead_Pengdows                  | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   483.5 μs |    49.42 μs | 32.69 μs |   535.2 μs |   546.16 |  16.57 KB |
| ListRead_Dapper                    | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   264.9 μs |     8.58 μs |  5.67 μs |   271.6 μs |   272.12 |  14.87 KB |
| ListRead_EntityFramework           | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   363.3 μs |    22.38 μs | 14.80 μs |   382.0 μs |   387.55 |  33.53 KB |
| Insert_Pengdows                    | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   456.2 μs |     9.30 μs |  5.54 μs |   462.5 μs |   462.89 |   8.06 KB |
| Insert_Dapper                      | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   360.8 μs |     7.35 μs |  4.86 μs |   368.9 μs |   369.25 |   3.93 KB |
| Insert_EntityFramework             | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   368.6 μs |    27.99 μs | 18.52 μs |   395.8 μs |   397.95 |   3.03 KB |
| Update_Pengdows                    | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   452.3 μs |    15.67 μs | 10.36 μs |   466.9 μs |   467.33 |   7.72 KB |
| Update_Dapper                      | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   343.3 μs |     3.55 μs |  2.11 μs |   346.8 μs |   347.14 |   3.63 KB |
| Update_EntityFramework             | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   365.4 μs |     9.81 μs |  6.49 μs |   373.5 μs |   373.90 |   4.64 KB |
| Delete_Pengdows                    | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   892.0 μs |    79.46 μs | 47.28 μs |   933.1 μs |   935.17 |   14.3 KB |
| Delete_Dapper                      | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   645.3 μs |    43.83 μs | 22.92 μs |   676.1 μs |   683.10 |   4.98 KB |
| Delete_EntityFramework             | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   656.8 μs |    49.04 μs | 29.18 μs |   687.5 μs |   690.78 |   3.98 KB |
| ConnectionHoldTime_Pengdows        | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   330.0 μs |    13.20 μs |  7.85 μs |   341.7 μs |   342.59 |   8.42 KB |
| ConnectionHoldTime_Dapper          | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   241.3 μs |    10.48 μs |  6.93 μs |   248.8 μs |   249.62 |   3.79 KB |
| ConnectionHoldTime_EntityFramework | Job-GEZALO | 100             | 10             | Default     | 5           | 16          | 64               |   496.2 μs |    13.44 μs |  8.89 μs |   508.3 μs |   511.01 |  49.92 KB |
| SingleRead_Pengdows                | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   769.8 μs | 1,012.06 μs | 55.47 μs |   820.6 μs |   825.34 |  10.91 KB |
| SingleRead_Dapper                  | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   442.2 μs |   270.81 μs | 14.84 μs |   456.6 μs |   458.21 |   5.29 KB |
| SingleRead_EntityFramework         | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   609.4 μs |   636.48 μs | 34.89 μs |   643.5 μs |   647.48 |  18.38 KB |
| CompositeKeyRead_Pengdows          | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   783.5 μs |   202.92 μs | 11.12 μs |   792.3 μs |   792.89 |  11.51 KB |
| CompositeKeyRead_Dapper            | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   428.1 μs |    60.03 μs |  3.29 μs |   431.4 μs |   431.78 |    5.4 KB |
| CompositeKeyRead_EntityFramework   | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |         NA |          NA |       NA |         NA |       NA |        NA |
| ListRead_Pengdows                  | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   865.7 μs |   694.47 μs | 38.07 μs |   902.6 μs |   906.84 |  18.52 KB |
| ListRead_Dapper                    | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   450.5 μs |   239.28 μs | 13.12 μs |   463.1 μs |   464.52 |  16.33 KB |
| ListRead_EntityFramework           | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   657.9 μs |   442.32 μs | 24.25 μs |   675.7 μs |   676.57 |  35.56 KB |
| Insert_Pengdows                    | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   942.2 μs |   746.85 μs | 40.94 μs |   979.1 μs |   982.37 |  10.87 KB |
| Insert_Dapper                      | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   597.9 μs |   197.51 μs | 10.83 μs |   608.4 μs |   609.58 |   5.38 KB |
| Insert_EntityFramework             | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   614.7 μs |   340.25 μs | 18.65 μs |   630.2 μs |   631.34 |   4.48 KB |
| Update_Pengdows                    | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   900.0 μs |   399.86 μs | 21.92 μs |   919.0 μs |   920.51 |   10.2 KB |
| Update_Dapper                      | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   605.6 μs |   546.76 μs | 29.97 μs |   633.7 μs |   636.51 |   5.09 KB |
| Update_EntityFramework             | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   604.2 μs |   695.67 μs | 38.13 μs |   638.8 μs |   641.89 |   5.95 KB |
| Delete_Pengdows                    | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               | 1,498.3 μs |   421.83 μs | 23.12 μs | 1,519.8 μs | 1,521.89 |  16.93 KB |
| Delete_Dapper                      | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               | 1,004.4 μs |   145.17 μs |  7.96 μs | 1,010.0 μs | 1,010.16 |   6.63 KB |
| Delete_EntityFramework             | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               | 1,040.8 μs |   152.36 μs |  8.35 μs | 1,049.1 μs | 1,050.20 |   6.07 KB |
| ConnectionHoldTime_Pengdows        | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   716.3 μs |   612.04 μs | 33.55 μs |   740.3 μs |   741.31 |  10.21 KB |
| ConnectionHoldTime_Dapper          | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   403.3 μs |   301.66 μs | 16.54 μs |   415.1 μs |   415.63 |   4.96 KB |
| ConnectionHoldTime_EntityFramework | ShortRun   | 1               | 3              | 1           | 3           | 16          | 64               |   808.3 μs |   782.27 μs | 42.88 μs |   844.0 μs |   846.55 |  54.16 KB |

Benchmarks with issues:
  ServerExecutionTimeBenchmarks.CompositeKeyRead_EntityFramework: Job-GEZALO(InvocationCount=100, IterationCount=10, UnrollFactor=1, WarmupCount=5) [Parallelism=16, OperationsPerRun=64]
  ServerExecutionTimeBenchmarks.CompositeKeyRead_EntityFramework: ShortRun(InvocationCount=1, IterationCount=3, LaunchCount=1, UnrollFactor=1, WarmupCount=3) [Parallelism=16, OperationsPerRun=64]

---

