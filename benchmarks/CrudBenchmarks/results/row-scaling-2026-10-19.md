# Row scaling: fixed cost against cost per row, 2026-10-19

`PostgreSqlRowScalingBenchmarks` and `SqlServerRowScalingBenchmarks` (opt-in), net8.0, one machine (AMD Ryzen 9 5950X,
8 CPUs visible, Docker), PostgreSQL 16 and SQL Server 2022. The equal-footing ReadList and FilteredQuery arms (the same
SQL for every library, a pooled connection opened per call, the same Npgsql auto-prepare settings) at 1, 10, 100, 1,000
and 10,000 rows over a 60,000-row seed. Setup refuses to run a case unless every arm returns exactly `RecordCount`
fully hydrated rows.

Two runs. The second (at `091ac44e` plus the change below) is the one reported: its Dapper arms return Dapper's own
buffered list (`AsList()`) instead of copying it with `ToList()`, a copy the other arms do not make. The 10,000-row gap
below held in both runs (PostgreSQL ReadList pengdows / Dapper 0.753 in run 1, 0.769 in run 2).

## Mean time per query

| Database, query | Library | 1 row | 10 | 100 | 1,000 | 10,000 |
|---|---|---:|---:|---:|---:|---:|
| PostgreSQL ReadList | pengdows | 271.2 us* | 163.6 us | 214.9 us | 529.9 us | 4,383 us |
| | Dapper | 154.4 us | 159.9 us | 212.0 us | 542.2 us | 5,700 us |
| | EF | 246.4 us | 252.7 us | 316.2 us | 681.7 us | 6,671 us |
| PostgreSQL FilteredQuery | pengdows | 165.6 us | 167.8 us | 246.1 us | 691.6 us | 6,404 us |
| | Dapper | 168.4 us | 164.8 us | 238.3 us | 709.6 us | 7,656 us |
| | EF | 257.0 us | 254.5 us | 334.7 us | 809.6 us | 8,251 us |
| SQL Server ReadList | pengdows | 234.3 us | 240.4 us | 346.3 us | 718.1 us | 5,520 us |
| | Dapper | 227.5 us | 236.3 us | 347.6 us | 738.0 us | 6,805 us |
| | EF | 374.2 us | 379.7 us | 505.0 us | 1,068 us | 9,590 us |
| SQL Server FilteredQuery | pengdows | 232.5 us | 257.1 us | 364.8 us | 954.7 us | 7,532 us |
| | Dapper | 230.7 us | 256.3 us | 361.8 us | 921.8 us | 8,715 us |
| | EF | 374.0 us | 406.7 us | 525.7 us | 1,127 us | 10,280 us |

\* Noise: standard deviation 97.7 us in this cell; run 1 measured 157.6 us against Dapper's 155.8 us.

## Cost per row over each interval

(T(1,000) − T(100)) / 900 and (T(10,000) − T(1,000)) / 9,000; allocation per row over 1,000 → 10,000.

| Database, query | pengdows | Dapper | EF |
|---|---|---|---|
| PostgreSQL ReadList | 350 → 428 ns, 214 B | 367 → 573 ns, 308 B | 406 → 666 ns, 404 B |
| PostgreSQL FilteredQuery | 495 → 635 ns, 215 B | 524 → 772 ns, 309 B | 528 → 827 ns, 406 B |
| SQL Server ReadList | 413 → 534 ns, 211 B | 434 → 674 ns, 284 B | 626 → 947 ns, 527 B |
| SQL Server FilteredQuery | 655 → 731 ns, 212 B | 622 → 866 ns, 284 B | 668 → 1,017 ns, 531 B |

Allocated per query at 10,000 rows: pengdows 2.06 to 2.09 MB, Dapper 2.77 to 3.01 MB, EF 3.98 to 5.26 MB. Gen2
collections per 1,000 queries at 10,000 rows: pengdows 31, Dapper 31 to 63, EF 63 to 102.

## What it shows

1. **Up to 1,000 rows pengdows and Dapper are level.** Every cell from 1 to 1,000 rows is within about 4% either way
   (apart from the noisy cell above), on both databases, and the fixed cost per query is the same to within about 7
   microseconds. EF is 1.4 to 1.7 times slower at small sizes.
2. **The per-row cost is level from 100 to 1,000 rows** (within about 6%, either direction) and **diverges above that.**
   From 1,000 to 10,000 rows each extra row costs Dapper 135 to 145 ns more than pengdows, so a 10,000-row query takes
   pengdows 0.77 (PostgreSQL ReadList) to 0.86 (SQL Server FilteredQuery) of Dapper's time.
3. **pengdows allocates less per row: about 213 bytes against Dapper's 284 to 309 and EF's 404 to 531.** At 10,000 rows
   that is 25% to 31% less than Dapper, and Dapper triggers up to twice as many Gen2 collections. The time gap opens
   where the allocation gap starts to cost collections, but which of Dapper's per-row allocations account for it was not
   profiled.
4. **What it does not show:** a faster pengdows at ordinary result sizes. The advantage is specific to large result
   sets, measured on one machine, for two queries over six columns (int, text, int, double, bool, text).

## Related: PostgreSQL equal-footing rerun (`PostgreSqlEqualFootingBenchmarks-20261019-101454`)

Until `091ac44e` the PostgreSQL equal-footing and methodology classes never set Dapper's `MatchNamesWithUnderscores`, so
their Dapper arms left `is_active` and `created_at` unmapped: four columns hydrated against pengdows' six. Every
PostgreSQL pengdows / Dapper read ratio published before 2026-10-19 was measured that way. Rerun with every column
mapped, pengdows / Dapper is 0.940 to 1.067 across all scenarios at 1, 10 and 100 records; ReadList at 100 rows moved from
1.061 (2026-10-17) to 0.996 and FilteredQuery at 100 from 1.031 to 1.003. EF / pengdows is 1.20 to 1.70.
