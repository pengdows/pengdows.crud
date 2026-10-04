## SQL Server hydration — 2026-10-04 (REL-007)

Rerun of `SqlServerHydrationHotPathBenchmarks` (SQL Server 2022 container, .NET 10, SqlClient 5.2.2,
`DbMode.SingleConnection` for pengdows and one open connection for Dapper) with a new
`HydrationOnly_DapperAsync` cell. The 2026-08-13 comparison set pengdows' `LoadListAsync` against
Dapper's synchronous `Query`; SqlClient's async read path costs more per row, so the async Dapper
cell is the like-for-like one.

| Rows | pengdows `LoadListAsync` | Dapper `QueryAsync` | Dapper `Query` (sync) | pengdows ÷ Dapper async | Allocated (p / Dapper async) |
|---:|---:|---:|---:|---:|---:|
| 100 | 299.8 µs | 288.6 µs | 256.7 µs | 1.04 | 43.8 / 42.8 KB |
| 1,000 | 884.7 µs | 772.9 µs | 746.1 µs | 1.14 | 451 / 373 KB |
| 5,000 | 3,406.9 µs | 3,074.2 µs | 3,464.2 µs | 1.11 | 2,354 / 1,924 KB |

### Where the difference comes from

Per-row allocations against a live SQL Server 2025 container (1,000 rows, allocation sampling by type):

| Path | `byte[]` | `string` | entity | boxed `double`/`int` |
|---|---:|---:|---:|---:|
| pengdows `LoadListAsync` | 110.9 B | 157.8 B | 63.9 B | — |
| Dapper `QueryAsync` | — | 159.8 B | 72.5 B | 95.9 B |
| raw SqlClient, `ReadAsync`, `CommandBehavior.Default` | 101.2 B | 149.1 B | 73.5 B | — |
| raw SqlClient, `ReadAsync`, `CommandBehavior.SequentialAccess` | — | 155.5 B | 71.3 B | — |
| raw SqlClient, synchronous `Read`, `Default` | — | 156.6 B | 71.3 B | — |

pengdows' own hydration adds nothing per row beyond the entity (typed getters, no boxing; confirmed
on SQLite too). The `byte[]` is SqlClient's: an async read without `SequentialAccess` buffers each
row. Dapper's `QueryAsync` passes `SequentialAccess | SingleResult` and avoids it (it boxes value
columns instead). pengdows executes with `CommandBehavior.Default`.

Using `SequentialAccess` for gateway hydration is a design change (each column must be read once,
in ordinal order; the mapping-failure diagnostics and some provider fallbacks re-read a column), so
it is recorded as a decision (REL-007), not made here.
