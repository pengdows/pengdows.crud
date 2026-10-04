## SQLite methodology benchmark — 2026-10-04 (REL-006)

`SqliteMethodologyBenchmarks` (opt-in) is the SQLite counterpart of `PostgreSqlMethodologyBenchmarks`'
framework-API cells: each framework's raw-SQL path against the same statement, and each framework's
own API. Shared-cache in-memory database kept alive by a sentinel; every framework opens and closes
its connection per operation. One job (3 warmups, 10 iterations): the PostgreSQL class's pinning,
server-GC and latency jobs are about a database in another process. .NET 10, 20 operations per
invocation; times are per operation.

```
dotnet run -c Release -f net10.0 -- --include-opt-in --filter '*SqliteMethodology*'
```

| Category | Method | Mean | Ratio to Dapper | Allocated |
|---|---|---:|---:|---:|
| ReadSingle | Dapper | 21.24 µs | 1.00 | 2.61 KB |
| ReadSingle | pengdows `LoadSingleAsync` (reused container) | 25.43 µs | 1.20 | 3.76 KB |
| ReadSingle | EF Core `FromSqlRaw`, pooled context | 58.00 µs | 2.73 | 18.43 KB |
| ReadSingle | EF Core `FromSqlRaw`, new context | 104.05 µs | 4.90 | 53.78 KB |
| ReadSingle-Api | pengdows `RetrieveOneAsync` | 28.32 µs | (1.33) | 4.71 KB |
| ReadSingle-Api | EF Core compiled query, pooled | 29.27 µs | (1.38) | 7.32 KB |
| ReadSingle-Api | EF Core LINQ, pooled | 52.66 µs | (2.48) | 11.44 KB |
| Create | Dapper | 21.69 µs | 1.00 | 3.72 KB |
| Create | EF Core `ExecuteSqlRaw`, pooled context | 27.61 µs | 1.27 | 8.44 KB |
| Create | pengdows `BuildCreate` + execute | 28.53 µs | 1.32 | 5.98 KB |
| Create | EF Core `ExecuteSqlRaw`, new context | 76.29 µs | 3.52 | 43.62 KB |
| Create-Api | pengdows `CreateAsync` (returns the generated id) | 40.69 µs | (1.88) | 6.74 KB |
| Create-Api | EF Core `Add` + `SaveChangesAsync`, pooled | 57.34 µs | (2.64) | 17.14 KB |

Ratios in parentheses compare an API cell with the Dapper row of the matching raw-SQL category;
the workloads differ (the API cells also read the key back).

Findings:
- Own API: `RetrieveOneAsync` is the fastest single-row read API measured (EF Core's compiled query
  is 3% slower, LINQ 86% slower); `CreateAsync` is 29% faster than `SaveChanges`.
- Raw SQL: pengdows is 20% (read) and 32% (create) over Dapper, consistent with the 2026-10-03
  equal-footing run; pooled EF Core raw SQL is close to pengdows on create (27.6 vs 28.5 µs).
- `CreateAsync` costs about 12 µs more than `BuildCreate` + execute on the same statement shape
  (SQLite 3.35+ uses one `INSERT ... RETURNING`, so it is not a second round trip). Tracked as PERF-016.
