## SQLite equal-footing and hydration run — 2026-10-03

Commit 784ca27b (2.0.6, after REV-057 `BoundedCache` and REV-058 SQLite decimal binding), .NET 10, Release,
8 cores. Command:

```
dotnet run -c Release -f net10.0 -- --filter "*EqualFootingCrudBenchmarks*" "*HydrationHotPathBenchmarks*"
```

The filter also matched `SqlServerHydrationHotPathBenchmarks`, included below.

`P÷D` = pengdows Mean ÷ Dapper Mean (below 1.0, pengdows is faster). `EF÷P` = EF Core Mean ÷ pengdows Mean.

### EqualFootingCrudBenchmarks (shared in-memory SQLite, connection opened and closed per operation)

RecordCount = 1:

| Scenario | Pengdows | Dapper | EF Core | P÷D | EF÷P |
|----------|---------:|-------:|--------:|----:|-----:|
| Aggregate | 65.60 us | 56.01 us | 106.04 us | 1.171 | 1.616 |
| Create | 33.83 us | 22.38 us | 76.92 us | 1.512 | 2.273 |
| DeleteInsertCycle | 56.44 us | 42.53 us | 147.99 us | 1.327 | 2.622 |
| DeleteOnly | 35.46 us | 23.10 us | 79.38 us | 1.535 | 2.238 |
| FilteredQuery | 41.98 us | 30.30 us | 110.09 us | 1.386 | 2.622 |
| ReadList | 37.68 us | 27.19 us | 103.26 us | 1.386 | 2.740 |
| ReadSingle | 31.01 us | 21.99 us | 104.54 us | 1.410 | 3.372 |
| Update | 28.32 us | 21.42 us | 75.97 us | 1.322 | 2.682 |

RecordCount = 100 (the single-row scenarios loop 100 times; ReadList/FilteredQuery return 100 rows):

| Scenario | Pengdows | Dapper | EF Core | P÷D | EF÷P |
|----------|---------:|-------:|--------:|----:|-----:|
| Aggregate | 6,525.45 us | 5,548.87 us | 10,627.54 us | 1.176 | 1.629 |
| Create | 3,438.02 us | 2,203.54 us | 7,397.60 us | 1.560 | 2.152 |
| DeleteInsertCycle | 5,555.44 us | 4,170.95 us | 14,745.10 us | 1.332 | 2.654 |
| DeleteOnly | 3,697.23 us | 2,416.64 us | 8,082.35 us | 1.530 | 2.186 |
| **FilteredQuery** | **126.79 us** | 132.89 us | 199.77 us | **0.954** | 1.576 |
| **ReadList** | **113.25 us** | 120.75 us | 187.47 us | **0.938** | 1.655 |
| ReadSingle | 3,060.69 us | 2,132.67 us | 10,500.90 us | 1.435 | 3.431 |
| Update | 2,885.26 us | 2,093.19 us | 7,572.28 us | 1.378 | 2.624 |

Compared with run-2026-03-04 (same benchmark): Create 1.58 → 1.51, ReadSingle 1.51 → 1.41, Update
1.42 → 1.32.

### HydrationHotPathBenchmarks (SQLite, connection held open on both sides)

| RowCount | Pengdows | Dapper | P÷D | Dapper alloc ÷ pengdows |
|---------:|---------:|-------:|----:|------------------------:|
| 100 | 85.47 us | 126.27 us | 0.677 | 1.90 |
| 1,000 | 758.28 us | 1,154.72 us | 0.657 | 1.99 |
| 5,000 | 3,707.86 us | 5,720.26 us | 0.648 | 1.92 |

2026-08-13: 0.664 / 0.642 / 0.645. Unchanged.

### SqlServerHydrationHotPathBenchmarks

| RowCount | Pengdows | Dapper | P÷D | 2026-08-13 P÷D |
|---------:|---------:|-------:|----:|---------------:|
| 100 | 301.45 us | 251.80 us | 1.197 | 1.184 |
| 1,000 | 916.42 us | 751.32 us | 1.220 | 1.258 |
| 5,000 | 3,496.33 us | 3,415.46 us | 1.024 | 1.025 |

Dapper allocates 0.82–0.92 of pengdows here (pengdows allocates more), the reverse of SQLite.

### Reading

- On SQLite, pengdows maps rows faster than Dapper (about 35%, with half the allocations); with 100 rows per
  query it is ahead even with the connection opened per operation.
- Each operation costs a fixed 7–13 us more than Dapper (connection lease, governance, metrics). On in-memory
  SQLite, where Dapper's whole operation is about 22 us, that is 1.3–1.5x; on PostgreSQL
  (PostgreSqlMethodologyBenchmarks, 2026-10-02) the same overhead is 4–7%.
- On SQL Server, hydration is still 2–22% behind Dapper and allocates more; not yet investigated.
