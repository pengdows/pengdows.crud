## PostgreSQL Equal-Footing Benchmark — 2026-03-15 (fair run)

**All three frameworks use identical Npgsql auto-prepare configuration:**
`MaxAutoPrepare=64;AutoPrepareMinUsages=2` baked into the DataSource for Dapper, EF Core, and pengdows.
This is the publishable equal-footing baseline. See [postgres-run-2026-03-04.md](postgres-run-2026-03-04.md)
for the prior biased run and its methodology note.

### Cross-Framework Ratios

`P÷D` = pengdows Mean ÷ Dapper Mean — values < 1.0 mean pengdows is faster.
`EF÷P` = EF Core Mean ÷ pengdows Mean — values > 1.0 mean EF Core is slower.

#### RecordCount=1

| Scenario | Pengdows | Dapper | EF Core | P÷D | EF÷P |
|----------|----------:|-------:|--------:|----:|-----:|
| ReadSingle | 164.1 μs | 164.7 μs | 254.9 μs | 1.000 | 1.553 |
| ReadList | 137.3 μs | 133.4 μs | 224.7 μs | 1.029 | 1.637 |
| FilteredQuery | 138.9 μs | 137.4 μs | 223.5 μs | 1.011 | 1.609 |
| Aggregate | 205.8 μs | 202.6 μs | 257.6 μs | 1.016 | 1.252 |
| ConnectionHoldTime | 164.0 μs | 164.0 μs | 249.8 μs | 1.000 | 1.524 |
| Create | 326.7 μs | 303.5 μs | 368.5 μs | 1.077 | 1.128 |
| Update | 339.3 μs | 327.5 μs | 387.2 μs | 1.036 | 1.141 |
| DeleteOnly | 267.6 μs | 266.5 μs | 332.0 μs | 1.004 | 1.241 |
| DeleteInsertCycle | 595.3 μs | 609.0 μs | 718.4 μs | 0.978 | 1.207 |

#### RecordCount=10

| Scenario | Pengdows | Dapper | EF Core | P÷D | EF÷P |
|----------|----------:|-------:|--------:|----:|-----:|
| ReadSingle | 1,616.1 μs | 1,570.7 μs | 2,512.0 μs | 1.029 | 1.554 |
| ReadList | 143.6 μs | 140.5 μs | 231.0 μs | 1.022 | 1.609 |
| FilteredQuery | 149.7 μs | 143.5 μs | 232.7 μs | 1.043 | 1.554 |
| Aggregate | 1,926.2 μs | 1,929.6 μs | 2,463.5 μs | 0.998 | 1.279 |
| ConnectionHoldTime | 165.7 μs | 163.9 μs | 260.8 μs | 1.011 | 1.574 |
| Create | 3,025.5 μs | 2,907.4 μs | 3,648.7 μs | 1.041 | 1.206 |
| Update | 3,187.2 μs | 3,119.3 μs | 3,759.1 μs | 1.022 | 1.179 |
| DeleteOnly | 2,667.0 μs | 2,617.6 μs | 3,285.9 μs | 1.019 | 1.232 |
| DeleteInsertCycle | 5,931.3 μs | 6,075.8 μs | 7,439.7 μs | 0.976 | 1.254 |

#### RecordCount=100

| Scenario | Pengdows | Dapper | EF Core | P÷D | EF÷P |
|----------|----------:|-------:|--------:|----:|-----:|
| ReadSingle×100 | 15,666.1 μs | 15,270.2 μs | 24,420.8 μs | 1.026 | 1.559 |
| **ReadList (1 query)** | **203.6 μs** | **185.6 μs** | **293.5 μs** | 1.097 | 1.441 |
| FilteredQuery | 226.3 μs | 210.5 μs | 323.0 μs | 1.075 | 1.428 |
| Aggregate | 19,054.7 μs | 19,312.8 μs | 24,537.7 μs | 0.987 | 1.288 |
| ConnectionHoldTime | 170.1 μs | 180.9 μs | 253.8 μs | 0.940 | 1.493 |
| Create | 30,370.6 μs | 28,862.6 μs | 35,010.8 μs | 1.052 | 1.153 |
| Update | 31,772.5 μs | 31,894.1 μs | 37,135.2 μs | 0.996 | 1.169 |
| DeleteOnly | 25,726.9 μs | 25,139.3 μs | 31,926.4 μs | 1.023 | 1.241 |
| DeleteInsertCycle | 59,136.0 μs | 59,648.0 μs | 73,208.8 μs | 0.991 | 1.238 |

---

### Key Findings

**pengdows vs Dapper (equal auto-prepare):**
- **Reads:** at parity — within ±3% for ReadSingle across all record counts; within ±10% for
  FilteredQuery and ReadList. Neither framework dominates; differences are within run-to-run noise.
- **Writes:** at parity — Create ~5% slower (SQL generation overhead), Update and Delete within 2%.
- **Cost:** pengdows allocates ~2x more heap per operation (5.9 KB vs 3.1 KB per ReadSingle N=1).
  This is the price of type-safe SQL generation, named parameters, and mapped entities.

**pengdows vs EF Core:**
- Consistently 1.2–1.6x faster across all scenarios.
- EF Core allocates 8–10x more heap per read operation.

**The architectural argument (ReadList vs ReadSingle×100):**
- `ReadList` at N=100: **204 μs** — one query, stream all rows.
- `ReadSingle×100` at N=100: **15,666 μs** — 100 round-trips.
- **77x difference.** This is a query design argument, not a framework argument.
  All three frameworks show the same pattern; pengdows and Dapper are within 10% of each other in both cases.

**What the prior (biased) run showed:**
The March 4/10 results showing 12–28% pengdows read advantage were entirely due to pengdows having
`MaxAutoPrepare=64` while Dapper used the default (`MaxAutoPrepare=0`, disabled). With equal
configuration, that advantage disappears. The true performance story is **parity with Dapper**.

---

### Raw BDN Output

```
BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.104
  [Host]     : .NET 8.0.25 (8.0.2526.11203), X64 RyuJIT AVX2
  Job-TECOJV : .NET 8.0.25 (8.0.2526.11203), X64 RyuJIT AVX2

IterationCount=10  WarmupCount=3
```

| Method | RecordCount | Mean | Error | StdDev | P95 | Ratio | Gen0 | Allocated | Alloc Ratio |
|--------|-------------|-----:|------:|-------:|----:|------:|-----:|----------:|------------:|
| **Create_Pengdows** | **1** | **326.7 μs** | **12.97 μs** | **8.58 μs** | **339.6 μs** | **1.99** | **0.4883** | **8.67 KB** | **1.48** |
| Create_Dapper | 1 | 303.5 μs | 3.64 μs | 1.90 μs | 306.1 μs | 1.85 | - | 4.42 KB | 0.75 |
| Create_EntityFramework | 1 | 368.5 μs | 10.62 μs | 6.32 μs | 376.8 μs | 2.25 | 1.9531 | 39.65 KB | 6.76 |
| **ReadSingle_Pengdows** | **1** | **164.1 μs** | **1.92 μs** | **1.00 μs** | **165.3 μs** | **1.00** | **0.2441** | **5.87 KB** | **1.00** |
| ReadSingle_Dapper | 1 | 164.7 μs | 3.56 μs | 2.12 μs | 167.4 μs | 1.00 | - | 3.13 KB | 0.53 |
| ReadSingle_EntityFramework | 1 | 254.9 μs | 3.64 μs | 2.16 μs | 257.4 μs | 1.55 | 2.9297 | 50.79 KB | 8.66 |
| ReadList_Pengdows | 1 | 137.3 μs | 2.25 μs | 1.49 μs | 139.4 μs | 0.84 | 0.2441 | 6.03 KB | 1.03 |
| ReadList_Dapper | 1 | 133.4 μs | 2.69 μs | 1.60 μs | 135.5 μs | 0.81 | - | 3.52 KB | 0.60 |
| ReadList_EntityFramework | 1 | 224.7 μs | 4.90 μs | 3.24 μs | 229.7 μs | 1.37 | 2.9297 | 50.2 KB | 8.56 |
| Update_Pengdows | 1 | 339.3 μs | 13.65 μs | 9.03 μs | 350.2 μs | 2.07 | - | 6.07 KB | 1.03 |
| Update_Dapper | 1 | 327.5 μs | 6.35 μs | 4.20 μs | 334.6 μs | 2.00 | - | 3.15 KB | 0.54 |
| Update_EntityFramework | 1 | 387.2 μs | 18.39 μs | 12.16 μs | 407.2 μs | 2.36 | 1.9531 | 37.06 KB | 6.32 |
| DeleteOnly_Pengdows | 1 | 267.6 μs | 5.05 μs | 3.00 μs | 271.3 μs | 1.63 | - | 7.09 KB | 1.21 |
| DeleteOnly_Dapper | 1 | 266.5 μs | 5.43 μs | 3.59 μs | 270.1 μs | 1.62 | - | 3.3 KB | 0.56 |
| DeleteOnly_EntityFramework | 1 | 332.0 μs | 6.03 μs | 3.99 μs | 337.1 μs | 2.02 | 1.9531 | 37.23 KB | 6.35 |
| DeleteInsertCycle_Pengdows | 1 | 595.3 μs | 6.91 μs | 4.11 μs | 601.2 μs | 3.63 | - | 10.62 KB | 1.81 |
| DeleteInsertCycle_Dapper | 1 | 608.9 μs | 10.63 μs | 6.32 μs | 618.5 μs | 3.71 | - | 6.92 KB | 1.18 |
| DeleteInsertCycle_EntityFramework | 1 | 718.4 μs | 14.81 μs | 9.79 μs | 731.7 μs | 4.38 | 3.9063 | 76.03 KB | 12.96 |
| FilteredQuery_Pengdows | 1 | 138.9 μs | 2.41 μs | 1.60 μs | 141.3 μs | 0.85 | 0.2441 | 6.25 KB | 1.07 |
| FilteredQuery_Dapper | 1 | 137.4 μs | 3.67 μs | 2.43 μs | 141.1 μs | 0.84 | 0.2441 | 4.07 KB | 0.69 |
| FilteredQuery_EntityFramework | 1 | 223.5 μs | 5.67 μs | 3.75 μs | 228.7 μs | 1.36 | 2.9297 | 51.75 KB | 8.82 |
| Aggregate_Pengdows | 1 | 205.8 μs | 5.20 μs | 3.44 μs | 210.5 μs | 1.25 | - | 5.45 KB | 0.93 |
| Aggregate_Dapper | 1 | 202.6 μs | 5.35 μs | 3.54 μs | 207.9 μs | 1.23 | - | 2.26 KB | 0.38 |
| Aggregate_EntityFramework | 1 | 257.6 μs | 6.73 μs | 4.01 μs | 262.6 μs | 1.57 | 1.9531 | 34.17 KB | 5.83 |
| ConnectionHoldTime_Pengdows | 1 | 164.0 μs | 1.63 μs | 0.85 μs | 164.9 μs | 1.00 | 0.2441 | 5.91 KB | 1.01 |
| ConnectionHoldTime_Dapper | 1 | 164.0 μs | 2.25 μs | 1.49 μs | 166.0 μs | 1.00 | - | 3.17 KB | 0.54 |
| ConnectionHoldTime_EntityFramework | 1 | 249.8 μs | 2.27 μs | 1.19 μs | 250.8 μs | 1.52 | 2.9297 | 50.83 KB | 8.67 |
| | | | | | | | | | |
| **Create_Pengdows** | **10** | **3,025.5 μs** | **77.65 μs** | **51.36 μs** | **3,105.3 μs** | **1.87** | **3.9063** | **80.4 KB** | **1.52** |
| Create_Dapper | 10 | 2,907.4 μs | 75.23 μs | 49.76 μs | 2,989.9 μs | 1.80 | - | 37.9 KB | 0.72 |
| Create_EntityFramework | 10 | 3,648.7 μs | 77.19 μs | 40.37 μs | 3,690.0 μs | 2.26 | 23.4375 | 390.24 KB | 7.39 |
| **ReadSingle_Pengdows** | **10** | **1,616.1 μs** | **50.58 μs** | **33.46 μs** | **1,669.3 μs** | **1.00** | | **52.83 KB** | **1.00** |
| ReadSingle_Dapper | 10 | 1,570.7 μs | 9.83 μs | 5.14 μs | 1,577.7 μs | 0.97 | - | 25.01 KB | 0.47 |
| ReadSingle_EntityFramework | 10 | 2,512.0 μs | 27.75 μs | 14.51 μs | 2,530.8 μs | 1.55 | 23.4375 | 501.64 KB | 9.49 |
| ReadList_Pengdows | 10 | 143.6 μs | 1.93 μs | 1.28 μs | 145.4 μs | 0.09 | 0.2441 | 7.81 KB | 0.15 |
| ReadList_Dapper | 10 | 140.5 μs | 5.80 μs | 3.84 μs | 145.7 μs | 0.09 | 0.2441 | 5.3 KB | 0.10 |
| ReadList_EntityFramework | 10 | 231.0 μs | 8.18 μs | 5.41 μs | 239.5 μs | 0.14 | 2.9297 | 53.68 KB | 1.02 |
| Update_Pengdows | 10 | 3,187.2 μs | 105.05 μs | 69.48 μs | 3,298.6 μs | 1.97 | - | 54.73 KB | 1.04 |
| Update_Dapper | 10 | 3,119.3 μs | 41.66 μs | 24.79 μs | 3,160.0 μs | 1.93 | - | 25.17 KB | 0.48 |
| Update_EntityFramework | 10 | 3,759.1 μs | 90.74 μs | 54.00 μs | 3,853.6 μs | 2.33 | 15.6250 | 364.36 KB | 6.90 |
| DeleteOnly_Pengdows | 10 | 2,667.0 μs | 54.89 μs | 36.30 μs | 2,720.7 μs | 1.65 | 3.9063 | 64.41 KB | 1.22 |
| DeleteOnly_Dapper | 10 | 2,617.6 μs | 50.04 μs | 33.10 μs | 2,670.9 μs | 1.62 | - | 26.31 KB | 0.50 |
| DeleteOnly_EntityFramework | 10 | 3,285.9 μs | 80.06 μs | 52.96 μs | 3,364.9 μs | 2.03 | 15.6250 | 365.78 KB | 6.92 |
| DeleteInsertCycle_Pengdows | 10 | 5,931.3 μs | 136.28 μs | 81.10 μs | 6,040.9 μs | 3.67 | - | 100.15 KB | 1.90 |
| DeleteInsertCycle_Dapper | 10 | 6,075.8 μs | 190.19 μs | 125.80 μs | 6,241.7 μs | 3.76 | - | 62.83 KB | 1.19 |
| DeleteInsertCycle_EntityFramework | 10 | 7,439.7 μs | 351.48 μs | 232.48 μs | 7,783.9 μs | 4.61 | 31.2500 | 753.97 KB | 14.27 |
| FilteredQuery_Pengdows | 10 | 149.7 μs | 1.92 μs | 1.01 μs | 150.6 μs | 0.09 | 0.4883 | 8.03 KB | 0.15 |
| FilteredQuery_Dapper | 10 | 143.5 μs | 3.91 μs | 2.59 μs | 147.1 μs | 0.09 | 0.2441 | 5.85 KB | 0.11 |
| FilteredQuery_EntityFramework | 10 | 232.7 μs | 4.94 μs | 2.94 μs | 237.5 μs | 0.14 | 2.9297 | 55.22 KB | 1.05 |
| Aggregate_Pengdows | 10 | 1,926.2 μs | 24.94 μs | 16.49 μs | 1,949.1 μs | 1.19 | - | 48.49 KB | 0.92 |
| Aggregate_Dapper | 10 | 1,929.6 μs | 80.46 μs | 53.22 μs | 2,008.1 μs | 1.19 | - | 16.26 KB | 0.31 |
| Aggregate_EntityFramework | 10 | 2,463.5 μs | 53.25 μs | 35.22 μs | 2,517.0 μs | 1.52 | 15.6250 | 335.18 KB | 6.34 |
| ConnectionHoldTime_Pengdows | 10 | 165.7 μs | 4.70 μs | 3.11 μs | 170.3 μs | 0.10 | 0.2441 | 5.91 KB | 0.11 |
| ConnectionHoldTime_Dapper | 10 | 163.9 μs | 3.51 μs | 2.32 μs | 166.9 μs | 0.10 | - | 3.17 KB | 0.06 |
| ConnectionHoldTime_EntityFramework | 10 | 260.8 μs | 4.88 μs | 3.23 μs | 265.3 μs | 0.16 | 2.9297 | 50.83 KB | 0.96 |
| | | | | | | | | | |
| **Create_Pengdows** | **100** | **30,370.6 μs** | **418.12 μs** | **276.56 μs** | **30,747.7 μs** | **1.94** | **31.2500** | **798.57 KB** | **1.528** |
| Create_Dapper | 100 | 28,862.6 μs | 619.05 μs | 409.47 μs | 29,447.6 μs | 1.84 | - | 373.36 KB | 0.714 |
| Create_EntityFramework | 100 | 35,010.8 μs | 421.35 μs | 250.74 μs | 35,413.4 μs | 2.24 | 214.2857 | 3,896.83 KB | 7.456 |
| **ReadSingle_Pengdows** | **100** | **15,666.1 μs** | **323.69 μs** | **192.62 μs** | **15,909.6 μs** | **1.00** | **31.2500** | **522.61 KB** | **1.000** |
| ReadSingle_Dapper | 100 | 15,270.2 μs | 325.52 μs | 215.31 μs | 15,531.2 μs | 0.97 | - | 243.91 KB | 0.467 |
| ReadSingle_EntityFramework | 100 | 24,420.8 μs | 203.37 μs | 134.52 μs | 24,600.7 μs | 1.56 | 281.2500 | 5,010.27 KB | 9.587 |
| ReadList_Pengdows | 100 | 203.6 μs | 5.24 μs | 3.12 μs | 208.8 μs | 0.01 | 0.9766 | 25.35 KB | 0.049 |
| ReadList_Dapper | 100 | 185.6 μs | 3.41 μs | 2.25 μs | 188.7 μs | 0.01 | 0.9766 | 22.84 KB | 0.044 |
| ReadList_EntityFramework | 100 | 293.5 μs | 5.92 μs | 3.53 μs | 297.2 μs | 0.02 | 4.8828 | 88.1 KB | 0.169 |
| Update_Pengdows | 100 | 31,772.5 μs | 285.93 μs | 189.12 μs | 32,051.8 μs | 2.03 | 31.2500 | 541.46 KB | 1.036 |
| Update_Dapper | 100 | 31,894.1 μs | 817.09 μs | 540.46 μs | 32,691.1 μs | 2.04 | - | 245.35 KB | 0.469 |
| Update_EntityFramework | 100 | 37,135.2 μs | 857.93 μs | 567.46 μs | 37,884.5 μs | 2.37 | 214.2857 | 3,637.57 KB | 6.960 |
| DeleteOnly_Pengdows | 100 | 25,726.9 μs | 291.49 μs | 173.46 μs | 25,959.5 μs | 1.64 | 31.2500 | 637.61 KB | 1.220 |
| DeleteOnly_Dapper | 100 | 25,139.3 μs | 203.63 μs | 134.69 μs | 25,297.5 μs | 1.60 | - | 256.55 KB | 0.491 |
| DeleteOnly_EntityFramework | 100 | 31,926.4 μs | 330.26 μs | 218.44 μs | 32,216.5 μs | 2.04 | 187.5000 | 3,651.56 KB | 6.987 |
| DeleteInsertCycle_Pengdows | 100 | 59,136.0 μs | 1,142.21 μs | 755.50 μs | 60,330.7 μs | 3.78 | - | 995.56 KB | 1.905 |
| DeleteInsertCycle_Dapper | 100 | 59,648.0 μs | 1,429.68 μs | 850.78 μs | 60,989.6 μs | 3.81 | - | 622.07 KB | 1.190 |
| DeleteInsertCycle_EntityFramework | 100 | 73,208.8 μs | 4,324.94 μs | 2,573.70 μs | 77,556.0 μs | 4.67 | 375.0000 | 7,533.46 KB | 14.415 |
| FilteredQuery_Pengdows | 100 | 226.3 μs | 7.15 μs | 4.73 μs | 233.2 μs | 0.01 | 1.4648 | 25.86 KB | 0.049 |
| FilteredQuery_Dapper | 100 | 210.5 μs | 2.56 μs | 1.34 μs | 211.8 μs | 0.01 | 0.9766 | 23.67 KB | 0.045 |
| FilteredQuery_EntityFramework | 100 | 323.0 μs | 6.92 μs | 3.62 μs | 328.0 μs | 0.02 | 4.8828 | 89.87 KB | 0.172 |
| Aggregate_Pengdows | 100 | 19,054.7 μs | 435.28 μs | 287.91 μs | 19,526.9 μs | 1.22 | - | 479.04 KB | 0.917 |
| Aggregate_Dapper | 100 | 19,312.8 μs | 567.41 μs | 375.31 μs | 19,872.9 μs | 1.23 | - | 156.35 KB | 0.299 |
| Aggregate_EntityFramework | 100 | 24,537.7 μs | 133.47 μs | 88.28 μs | 24,687.0 μs | 1.57 | 187.5000 | 3,345.09 KB | 6.401 |
| ConnectionHoldTime_Pengdows | 100 | 170.1 μs | 6.78 μs | 4.48 μs | 175.1 μs | 0.01 | 0.2441 | 5.91 KB | 0.011 |
| ConnectionHoldTime_Dapper | 100 | 180.9 μs | 30.52 μs | 20.19 μs | 213.8 μs | 0.01 | - | 3.17 KB | 0.006 |
| ConnectionHoldTime_EntityFramework | 100 | 253.8 μs | 5.28 μs | 3.50 μs | 258.8 μs | 0.02 | 2.9297 | 50.83 KB | 0.097 |
