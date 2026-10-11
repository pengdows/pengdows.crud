# A/B: pengdows.crud 2.0.5 vs 2.0.6, tier 1 (no database), 2026-10-11

Method: `benchmarks/run-main-vs-3.0.sh tier1 2` with `v2.0.5` (7ee63f14) against `HEAD` (660c7471), net10.0, 2 rounds,
arms alternating order, the same benchmark code on both (six harness files that use APIs 2.0.5 lacks were excluded from the
baseline arm only: `PoolGovernorAcquireBenchmarks`, `Internal/TypeHandlingBenchmarks`,
`PostgreSqlConnectionGovernanceBenchmarks`, `SentinelHotPathBenchmarks`, `PostgreSqlBackpressureBenchmarks`,
`SqlServerConnectionGovernanceBenchmarks`; none are in tier 1). AMD Ryzen 9 5950X, idle machine, no builds during the run.
Dapper and EF rows are the in-run control: they did not move (within about 3%), so the pengdows deltas are the library's.
The script flagged Dapper drift above 3% for a few rows (`BrokenMapping`, `EqualFootingCrud`); the Dapper-normalized column
agrees with the raw one throughout, and allocation figures are exact bytes per operation and unaffected by drift.

## Result 1: 2.0.6 is faster and allocates far less on the main paths (SQLite in-memory equal footing)

pengdows time and allocation, 2.0.5 to 2.0.6: single read 29.8 to 26.6 us (-10.6%), 6,032 to 3,528 B (-41.5%); create
32.6 to 29.5 us (-9.6%), -44% allocation; update -9.1%, -40%; delete -10%, -28%; filtered query -8%, -34%; list read at 1 row
-6.6%, -39%; aggregate -5.4%, -49%. `BrokenMapping` (SQLite): every pengdows row -8% to -14% time and -25% to -44%
allocation. Hydration-only is unchanged (+0.6% to +2.6%, inside the round spread; allocation +0.0% to +0.6%).

## Result 2: the small-result gap against Dapper has narrowed; the large-result win is unchanged

pengdows time divided by Dapper time, same run:

| operation | rows | 2.0.5 | 2.0.6 |
|---|---|---|---|
| read single | 1 | 1.357 | 1.187 |
| read single (100 separate calls) | 100 | 1.353 | 1.200 |
| list read | 1 | 1.325 | 1.248 |
| list read | 100 | 0.924 | 0.920 |
| filtered query | 1 | 1.338 | 1.215 |
| filtered query | 100 | 0.945 | 0.915 |
| aggregate | 1 | 1.162 | 1.091 |
| create | 1 | 1.432 | 1.306 |
| update | 1 | 1.309 | 1.168 |
| delete | 1 | 1.544 | 1.342 |

The crossover remains: slower than Dapper on a one-row call, faster on a 100-row list or filtered result. Per-call overhead
dominates a ~22 us call on SQLite with no network; it is not visible on PostgreSQL, SQL Server or DuckDB (see the
2026-10-10/11 full-suite runs), where pengdows is within about 5% of Dapper at every size.

## Result 3: two regressions to investigate before 2.0.6 ships

> **Update 2026-10-11:** both were investigated; the Guid regression is fixed (allocation at or below 2.0.5 on every dialect, time within
> 3 to 18%) and the second is diagnosed (`TrackedReader` grew by about ten fields). See `ab-2.0.5-vs-2.0.6-parameter-regressions-2026-10-11.md`.

1. **`BenchmarkGuidParameters`: all seven cases slower and allocating more, every round.** Time +16% to +41% (for example
   Sqlite_String 51.8 to 66.5 ns, Firebird_Binary 77.6 to 109.8 ns, SqlServer_PassThrough 63.0 to 76.6 ns) and
   allocation +14% to +50% (+32 to +64 B per parameter). Small in absolute terms (13 to 32 ns) but on a hot path, and it
   changes the allocation profile of every Guid parameter.
2. **`ApplesToApples ReadSingle_Pengdows_PureMapping`:** +5.2% time and +3.0% allocation (+56 B per call), repeatable.

## Not run

Tier 2 (embedded concurrency, pool protection, DuckDB) and tier 3 (PostgreSQL and SQL Server equal footing) of the A/B have
not been run on the final build.

## Raw summary (`benchmarks/summarize-ab.py`)

# 2.0.5 vs 2.0.6  (rounds: 2 / 2)

Flags: time>5.0% (Dapper-normalized when a control exists) AND larger than the round-to-round spread, tail(P99)>10.0% AND larger than P99 spread, alloc>2.0%. Time deltas untrusted if Dapper drifts >3.0%.

| class | method | params | base med | new med | d med | d vs Dapper | d P95 | d P99 | base B/op | new B/op | d alloc | d gen0/1k | round spread | flags |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ApplesToApplesDapperBenchmarks | ReadSingle_Dapper_NewConnection | RecordCount=1 | 19.419us | 19.498us | +0.4% | n/a | +0.5% | +0.5% | 2311 | 2311 | +0.0% | +0.00 | ±3.6% |  |
| ApplesToApplesDapperBenchmarks | ReadSingle_Dapper_NewConnection | RecordCount=100 | 1.976ms | 1.944ms | -1.6% | n/a | -1.4% | -1.4% | 231201 | 231198 | -0.0% | +0.98 | ±0.5% |  |
| ApplesToApplesDapperBenchmarks | ReadSingle_Dapper_PureMapping | RecordCount=1 | 4.897us | 4.846us | -1.0% | n/a | -0.5% | -0.5% | 1839 | 1839 | +0.0% | +0.00 | ±1.4% |  |
| ApplesToApplesDapperBenchmarks | ReadSingle_Dapper_PureMapping | RecordCount=100 | 478.203us | 474.627us | -0.7% | n/a | +0.7% | +0.8% | 183994 | 183994 | +0.0% | +0.00 | ±3.4% |  |
| ApplesToApplesDapperBenchmarks | ReadSingle_Pengdows_ProductionStandard | RecordCount=1 | 26.947us | 24.353us | -9.6% | -9.3% | -9.3% | -9.2% | 5655 | 3151 | -44.3% | -0.15 | ±2.0% | time- repro |
| ApplesToApplesDapperBenchmarks | ReadSingle_Pengdows_ProductionStandard | RecordCount=100 | 2.685ms | 2.405ms | -10.4% | -9.3% | -10.8% | -11.0% | 565601 | 315201 | -44.3% | -15.62 | ±1.0% | time- repro |
| ApplesToApplesDapperBenchmarks | ReadSingle_Pengdows_PureMapping | RecordCount=1 | 5.053us | 5.315us | +5.2% | +5.5% | +5.1% | +5.0% | 1871 | 1927 | +3.0% | +0.01 | ±1.3% | TIME+ ALLOC+ repro |
| ApplesToApplesDapperBenchmarks | ReadSingle_Pengdows_PureMapping | RecordCount=100 | 515.442us | 514.183us | -0.2% | +0.9% | -0.3% | -0.3% | 187195 | 192795 | +3.0% | +0.00 | ±3.0% | ALLOC+ |
| BenchmarkGuidParameters | DuckDb_String |  | 77.0ns | 90.9ns | +18.0% | n/a | +17.4% | +17.4% | 216 | 248 | +14.8% | +0.00 | ±5.1% | TIME+ TAIL+ ALLOC+ repro |
| BenchmarkGuidParameters | Firebird_Binary |  | 77.6ns | 109.8ns | +41.4% | n/a | +41.0% | +41.0% | 128 | 192 | +50.0% | +0.00 | ±15.0% | TIME+ TAIL+ ALLOC+ repro |
| BenchmarkGuidParameters | Firebird_String |  | 76.5ns | 106.0ns | +38.6% | n/a | +38.7% | +38.8% | 184 | 248 | +34.8% | +0.00 | ±6.3% | TIME+ TAIL+ ALLOC+ repro |
| BenchmarkGuidParameters | Oracle_String |  | 77.8ns | 90.4ns | +16.2% | n/a | +16.4% | +16.4% | 216 | 248 | +14.8% | +0.00 | ±3.9% | TIME+ TAIL+ ALLOC+ repro |
| BenchmarkGuidParameters | SqlServer_PassThrough |  | 63.0ns | 76.6ns | +21.6% | n/a | +22.0% | +22.0% | 120 | 152 | +26.7% | +0.00 | ±2.8% | TIME+ TAIL+ ALLOC+ repro |
| BenchmarkGuidParameters | Sqlite_1k_GuidParams |  | 62.438us | 78.007us | +24.9% | n/a | +24.7% | +24.7% | 232000 | 264000 | +13.8% | +1.95 | ±3.1% | TIME+ TAIL+ ALLOC+ repro |
| BenchmarkGuidParameters | Sqlite_String |  | 51.8ns | 66.5ns | +28.3% | n/a | +28.2% | +28.2% | 152 | 184 | +21.1% | +0.00 | ±13.2% | TIME+ TAIL+ ALLOC+ repro |
| BrokenMappingBenchmarks | AggregateCount_Dapper |  | 7.508us | 7.733us | +3.0% | n/a | +2.9% | +2.9% | 1824 | 1824 | +0.0% | +0.00 | ±2.1% | DAPPER-DRIFT |
| BrokenMappingBenchmarks | AggregateCount_EntityFramework |  | 7.086us | 7.155us | +1.0% | +1.7% | +0.9% | +0.9% | 1640 | 1640 | +0.0% | +0.00 | ±1.0% |  |
| BrokenMappingBenchmarks | AggregateCount_Pengdows |  | 32.755us | 29.773us | -9.1% | -8.5% | -9.0% | -9.1% | 6728 | 3856 | -42.7% | -0.15 | ±4.7% | time- repro |
| BrokenMappingBenchmarks | BatchRead_Dapper |  | 63.251us | 65.544us | +3.6% | n/a | +4.3% | +4.4% | 25440 | 25440 | +0.0% | +0.00 | ±3.1% | DAPPER-DRIFT |
| BrokenMappingBenchmarks | BatchRead_EntityFramework |  | 841.806us | 851.298us | +1.1% | +1.8% | +1.1% | +1.1% | 292271 | 292272 | +0.0% | +0.00 | ±5.7% |  |
| BrokenMappingBenchmarks | BatchRead_Pengdows |  | 321.164us | 279.576us | -12.9% | -12.3% | -13.3% | -13.3% | 76258 | 47617 | -37.6% | -1.95 | ±5.6% | time- repro |
| BrokenMappingBenchmarks | Create_Dapper |  | 8.368us | 8.171us | -2.3% | n/a | -2.3% | -2.3% | 6808 | 6808 | +0.0% | +0.00 | ±1.8% |  |
| BrokenMappingBenchmarks | Create_EntityFramework |  | 29.624us | 29.754us | +0.4% | +1.1% | -0.1% | -0.1% | 14328 | 14328 | +0.0% | +0.00 | ±0.9% |  |
| BrokenMappingBenchmarks | Create_Pengdows |  | 33.201us | 30.198us | -9.0% | -8.4% | -8.7% | -8.6% | 12200 | 8984 | -26.4% | -0.17 | ±2.5% | time- repro |
| BrokenMappingBenchmarks | DeleteByKeyword_Dapper |  | 46.828us | 42.973us | -8.2% | n/a | -8.4% | -8.4% | 11992 | 8752 | -27.0% | -0.18 | ±3.7% | DAPPER-DRIFT |
| BrokenMappingBenchmarks | DeleteByKeyword_EntityFramework |  | 69.508us | 65.341us | -6.0% | -5.3% | -6.0% | -6.0% | 18072 | 14832 | -17.9% | -0.12 | ±4.6% | time- repro |
| BrokenMappingBenchmarks | DeleteByKeyword_Pengdows |  | 67.583us | 61.466us | -9.1% | -8.4% | -9.1% | -9.1% | 16840 | 10536 | -37.4% | -0.37 | ±2.6% | time- repro |
| BrokenMappingBenchmarks | Delete_Dapper |  | 41.887us | 37.837us | -9.7% | n/a | -9.2% | -9.2% | 11864 | 8624 | -27.3% | -0.18 | ±3.0% | DAPPER-DRIFT |
| BrokenMappingBenchmarks | Delete_EntityFramework |  | 64.126us | 60.481us | -5.7% | -5.0% | -5.8% | -5.8% | 17848 | 14608 | -18.2% | -0.12 | ±0.9% | time- repro |
| BrokenMappingBenchmarks | Delete_Pengdows |  | 63.666us | 55.050us | -13.5% | -12.9% | -13.5% | -13.4% | 16664 | 10384 | -37.7% | -0.37 | ±3.4% | time- repro |
| BrokenMappingBenchmarks | FilterByKeyword_Dapper |  | 26.220us | 25.905us | -1.2% | n/a | -1.1% | -1.1% | 9792 | 9792 | +0.0% | +0.00 | ±4.5% |  |
| BrokenMappingBenchmarks | FilterByKeyword_EntityFramework |  | 79.706us | 80.850us | +1.4% | +2.2% | +3.0% | +3.2% | 23600 | 23601 | +0.0% | +0.00 | ±2.2% |  |
| BrokenMappingBenchmarks | FilterByKeyword_Pengdows |  | 81.717us | 79.053us | -3.3% | -2.6% | -3.3% | -3.3% | 21057 | 18192 | -13.6% | -0.24 | ±2.3% |  |
| BrokenMappingBenchmarks | FilteredQuery_Dapper |  | 21.288us | 20.609us | -3.2% | n/a | -2.8% | -2.8% | 4560 | 4560 | +0.0% | +0.00 | ±6.0% | DAPPER-DRIFT |
| BrokenMappingBenchmarks | FilteredQuery_EntityFramework |  | 90.875us | 90.592us | -0.3% | +0.4% | +0.0% | +0.0% | 24945 | 24945 | +0.0% | +0.00 | ±1.4% |  |
| BrokenMappingBenchmarks | FilteredQuery_Pengdows |  | 54.582us | 50.376us | -7.7% | -7.1% | -8.3% | -8.3% | 10216 | 7384 | -27.7% | -0.18 | ±4.1% | time- repro |
| BrokenMappingBenchmarks | ReadAll_Dapper |  | 41.357us | 42.494us | +2.7% | n/a | +3.0% | +3.0% | 15784 | 15784 | +0.0% | +0.00 | ±3.2% |  |
| BrokenMappingBenchmarks | ReadAll_EntityFramework |  | 75.454us | 75.886us | +0.6% | +1.3% | +3.1% | +3.3% | 22233 | 22233 | +0.0% | +0.00 | ±1.9% |  |
| BrokenMappingBenchmarks | ReadAll_Pengdows |  | 121.403us | 120.861us | -0.4% | +0.3% | -0.1% | -0.1% | 32873 | 30352 | -7.7% | -0.24 | ±4.3% |  |
| BrokenMappingBenchmarks | ReadList_Dapper |  | 37.665us | 38.999us | +3.5% | n/a | +4.1% | +4.2% | 10808 | 10808 | +0.0% | +0.00 | ±4.7% | DAPPER-DRIFT |
| BrokenMappingBenchmarks | ReadList_EntityFramework |  | 91.344us | 89.443us | -2.1% | -1.4% | -1.4% | -1.3% | 24929 | 24929 | +0.0% | +0.00 | ±4.5% |  |
| BrokenMappingBenchmarks | ReadList_Pengdows |  | 95.706us | 93.783us | -2.0% | -1.3% | -1.8% | -1.8% | 22033 | 19200 | -12.9% | -0.12 | ±2.7% |  |
| BrokenMappingBenchmarks | ReadSingle_Dapper |  | 6.286us | 6.443us | +2.5% | n/a | +2.8% | +2.8% | 2576 | 2576 | +0.0% | +0.00 | ±6.2% |  |
| BrokenMappingBenchmarks | ReadSingle_EntityFramework |  | 83.431us | 83.977us | +0.7% | +1.4% | +1.6% | +1.7% | 29209 | 29209 | +0.0% | +0.00 | ±2.5% |  |
| BrokenMappingBenchmarks | ReadSingle_Pengdows |  | 31.577us | 27.865us | -11.8% | -11.1% | -11.4% | -11.5% | 7592 | 4728 | -37.7% | -0.15 | ±6.2% | time- repro |
| BrokenMappingBenchmarks | ReadWithKeyword_Dapper |  | 9.828us | 9.972us | +1.5% | n/a | +1.6% | +1.7% | 2856 | 2856 | +0.0% | +0.00 | ±0.1% |  |
| BrokenMappingBenchmarks | ReadWithKeyword_EntityFramework |  | 79.889us | 79.351us | -0.7% | +0.0% | -0.3% | -0.2% | 23601 | 23601 | +0.0% | +0.00 | ±1.1% |  |
| BrokenMappingBenchmarks | ReadWithKeyword_Pengdows |  | 35.844us | 32.736us | -8.7% | -8.0% | -8.7% | -8.7% | 7848 | 4984 | -36.5% | -0.18 | ±6.5% | time- repro |
| BrokenMappingBenchmarks | UpdateKeyword_Dapper |  | 4.269us | 4.336us | +1.6% | n/a | +1.0% | +0.9% | 2104 | 2104 | +0.0% | +0.00 | ±4.8% |  |
| BrokenMappingBenchmarks | UpdateKeyword_EntityFramework |  | 24.474us | 24.892us | +1.7% | +2.4% | +1.4% | +1.4% | 8328 | 8328 | +0.0% | +0.00 | ±4.7% |  |
| BrokenMappingBenchmarks | UpdateKeyword_Pengdows |  | 27.120us | 23.382us | -13.8% | -13.2% | -13.7% | -13.7% | 6936 | 3880 | -44.1% | -0.18 | ±6.1% | time- repro |
| BrokenMappingBenchmarks | Update_Dapper |  | 7.830us | 7.494us | -4.3% | n/a | -3.7% | -3.6% | 7968 | 7968 | +0.0% | +0.00 | ±1.2% | DAPPER-DRIFT |
| BrokenMappingBenchmarks | Update_EntityFramework |  | 53.110us | 53.357us | +0.5% | +1.2% | +1.2% | +1.3% | 21248 | 21248 | +0.0% | +0.00 | ±2.0% |  |
| BrokenMappingBenchmarks | Update_Pengdows |  | 32.845us | 29.198us | -11.1% | -10.5% | -10.9% | -10.8% | 13352 | 10136 | -24.1% | -0.21 | ±3.4% | time- repro |
| BrokenMappingBenchmarks | Upsert_Dapper |  | 11.123us | 11.339us | +1.9% | n/a | +1.3% | +1.2% | 12104 | 12104 | +0.0% | +0.00 | ±3.4% |  |
| BrokenMappingBenchmarks | Upsert_EntityFramework |  | 33.186us | 33.220us | +0.1% | +0.8% | +0.2% | +0.2% | 21448 | 21448 | +0.0% | +0.00 | ±3.3% |  |
| BrokenMappingBenchmarks | Upsert_Pengdows |  | 38.113us | 33.136us | -13.1% | -12.4% | -13.1% | -13.0% | 18592 | 14824 | -20.3% | -0.24 | ±3.5% | time- repro |
| EqualFootingCrudBenchmarks | Aggregate_Dapper | RecordCount=1 | 55.849us | 56.271us | +0.8% | n/a | +0.8% | +0.8% | 1344 | 1344 | +0.0% | +0.00 | ±5.2% |  |
| EqualFootingCrudBenchmarks | Aggregate_Dapper | RecordCount=100 | 5.607ms | 5.669ms | +1.1% | n/a | +1.0% | +1.0% | 127285 | 127288 | +0.0% | +0.00 | ±1.1% |  |
| EqualFootingCrudBenchmarks | Aggregate_EntityFramework | RecordCount=1 | 106.357us | 105.510us | -0.8% | -1.8% | +0.0% | +0.2% | 41713 | 41712 | -0.0% | +0.00 | ±4.4% |  |
| EqualFootingCrudBenchmarks | Aggregate_EntityFramework | RecordCount=100 | 10.622ms | 10.616ms | -0.1% | -1.7% | +0.0% | +0.0% | 4164078 | 4164115 | +0.0% | -7.81 | ±1.9% |  |
| EqualFootingCrudBenchmarks | Aggregate_Pengdows | RecordCount=1 | 64.900us | 61.391us | -5.4% | -6.4% | -5.6% | -5.7% | 4936 | 2496 | -49.4% | -0.12 | ±3.8% | time- repro |
| EqualFootingCrudBenchmarks | Aggregate_Pengdows | RecordCount=100 | 6.474ms | 6.161ms | -4.8% | -6.4% | -4.7% | -4.7% | 486485 | 242488 | -50.2% | -15.62 | ±4.0% | time- repro |
| EqualFootingCrudBenchmarks | Breakdown_BuildVsExecute_Dapper | RecordCount=1 | 22.001us | 22.211us | +1.0% | n/a | +1.1% | +1.2% | 2776 | 2776 | +0.0% | +0.00 | ±2.0% |  |
| EqualFootingCrudBenchmarks | Breakdown_BuildVsExecute_Dapper | RecordCount=100 | 2.188ms | 2.193ms | +0.2% | n/a | +1.1% | +1.2% | 265735 | 265732 | -0.0% | +0.00 | ±3.5% |  |
| EqualFootingCrudBenchmarks | Breakdown_BuildVsExecute_EntityFramework | RecordCount=1 | 105.248us | 106.029us | +0.7% | -0.3% | +0.8% | +0.8% | 55467 | 55467 | +0.0% | +0.12 | ±1.4% |  |
| EqualFootingCrudBenchmarks | Breakdown_BuildVsExecute_EntityFramework | RecordCount=100 | 10.572ms | 10.792ms | +2.1% | +0.4% | +2.3% | +2.3% | 5534815 | 5534826 | +0.0% | +0.00 | ±2.3% |  |
| EqualFootingCrudBenchmarks | Breakdown_BuildVsExecute_Pengdows | RecordCount=1 | 32.843us | 28.570us | -13.0% | -13.9% | -12.6% | -12.6% | 7296 | 4328 | -40.7% | -0.18 | ±2.5% | time- repro |
| EqualFootingCrudBenchmarks | Breakdown_BuildVsExecute_Pengdows | RecordCount=100 | 3.183ms | 2.860ms | -10.1% | -11.6% | -10.1% | -10.1% | 717744 | 420936 | -41.4% | -15.62 | ±2.0% | time- repro |
| EqualFootingCrudBenchmarks | ConnectionHoldTime_Dapper | RecordCount=1 | 21.935us | 22.211us | +1.3% | n/a | +1.1% | +1.0% | 2768 | 2768 | +0.0% | +0.00 | ±0.8% |  |
| EqualFootingCrudBenchmarks | ConnectionHoldTime_Dapper | RecordCount=100 | 22.035us | 21.901us | -0.6% | n/a | -0.7% | -0.7% | 2768 | 2768 | +0.0% | +0.00 | ±3.7% |  |
| EqualFootingCrudBenchmarks | ConnectionHoldTime_EntityFramework | RecordCount=1 | 106.107us | 106.873us | +0.7% | -0.3% | +1.2% | +1.2% | 55459 | 55460 | +0.0% | +0.00 | ±2.7% |  |
| EqualFootingCrudBenchmarks | ConnectionHoldTime_EntityFramework | RecordCount=100 | 105.890us | 106.263us | +0.4% | -1.3% | +0.2% | +0.2% | 55459 | 55459 | +0.0% | +0.24 | ±2.5% |  |
| EqualFootingCrudBenchmarks | ConnectionHoldTime_Pengdows | RecordCount=1 | 29.285us | 26.933us | -8.0% | -9.0% | -7.0% | -7.0% | 6072 | 3568 | -41.2% | -0.15 | ±3.3% | time- repro |
| EqualFootingCrudBenchmarks | ConnectionHoldTime_Pengdows | RecordCount=100 | 29.663us | 26.482us | -10.7% | -12.2% | -10.4% | -10.2% | 6072 | 3568 | -41.2% | -0.15 | ±0.9% | time- repro |
| EqualFootingCrudBenchmarks | Create_Dapper | RecordCount=1 | 22.781us | 22.575us | -0.9% | n/a | -0.1% | -0.0% | 3784 | 3784 | +0.0% | +0.00 | ±4.4% |  |
| EqualFootingCrudBenchmarks | Create_Dapper | RecordCount=100 | 2.222ms | 2.279ms | +2.6% | n/a | +2.5% | +2.4% | 379199 | 379200 | +0.0% | +0.00 | ±2.2% |  |
| EqualFootingCrudBenchmarks | Create_EntityFramework | RecordCount=1 | 77.052us | 77.119us | +0.1% | -1.0% | -0.5% | -0.7% | 46298 | 46297 | -0.0% | +0.00 | ±4.1% |  |
| EqualFootingCrudBenchmarks | Create_EntityFramework | RecordCount=100 | 7.649ms | 7.673ms | +0.3% | -1.3% | +0.7% | +0.7% | 4630464 | 4630467 | +0.0% | +0.00 | ±4.8% |  |
| EqualFootingCrudBenchmarks | Create_Pengdows | RecordCount=1 | 32.615us | 29.472us | -9.6% | -10.6% | -10.2% | -10.3% | 8808 | 4896 | -44.4% | -0.21 | ±2.0% | time- repro |
| EqualFootingCrudBenchmarks | Create_Pengdows | RecordCount=100 | 3.238ms | 2.801ms | -13.5% | -14.9% | -13.5% | -13.6% | 881608 | 490400 | -44.4% | -21.48 | ±4.9% | time- repro |
| EqualFootingCrudBenchmarks | DeleteInsertCycle_Dapper | RecordCount=1 | 43.209us | 43.443us | +0.5% | n/a | +1.0% | +1.1% | 5800 | 5800 | +0.0% | +0.00 | ±5.1% |  |
| EqualFootingCrudBenchmarks | DeleteInsertCycle_Dapper | RecordCount=100 | 4.268ms | 4.275ms | +0.2% | n/a | -0.3% | -0.3% | 580085 | 580088 | +0.0% | +0.00 | ±3.9% |  |
| EqualFootingCrudBenchmarks | DeleteInsertCycle_EntityFramework | RecordCount=1 | 148.278us | 150.087us | +1.2% | +0.2% | +1.7% | +1.9% | 89970 | 89969 | -0.0% | +0.00 | ±0.6% |  |
| EqualFootingCrudBenchmarks | DeleteInsertCycle_EntityFramework | RecordCount=100 | 14.977ms | 15.022ms | +0.3% | -1.3% | +0.1% | +0.1% | 8996920 | 8996925 | +0.0% | +0.00 | ±3.3% |  |
| EqualFootingCrudBenchmarks | DeleteInsertCycle_Pengdows | RecordCount=1 | 56.015us | 49.906us | -10.9% | -11.8% | -10.9% | -10.9% | 12248 | 6744 | -44.9% | -0.31 | ±4.5% | time- repro |
| EqualFootingCrudBenchmarks | DeleteInsertCycle_Pengdows | RecordCount=100 | 5.354ms | 4.943ms | -7.7% | -9.2% | -7.9% | -7.9% | 1224884 | 674487 | -44.9% | -31.25 | ±2.9% | time- repro |
| EqualFootingCrudBenchmarks | DeleteOnly_Dapper | RecordCount=1 | 22.797us | 23.577us | +3.4% | n/a | +1.5% | +1.4% | 5224 | 5224 | +0.0% | +0.00 | ±4.7% | DAPPER-DRIFT |
| EqualFootingCrudBenchmarks | DeleteOnly_Dapper | RecordCount=100 | 2.273ms | 2.338ms | +2.9% | n/a | +2.7% | +2.8% | 522479 | 522480 | +0.0% | +0.00 | ±2.7% |  |
| EqualFootingCrudBenchmarks | DeleteOnly_EntityFramework | RecordCount=1 | 79.733us | 82.824us | +3.9% | +2.8% | +4.1% | +4.1% | 44545 | 44545 | +0.0% | +0.00 | ±5.0% |  |
| EqualFootingCrudBenchmarks | DeleteOnly_EntityFramework | RecordCount=100 | 8.136ms | 8.104ms | -0.4% | -2.0% | +0.1% | +0.2% | 4454544 | 4454550 | +0.0% | +0.00 | ±2.9% |  |
| EqualFootingCrudBenchmarks | DeleteOnly_Pengdows | RecordCount=1 | 35.190us | 31.643us | -10.1% | -11.0% | -9.8% | -9.7% | 9976 | 7144 | -28.4% | -0.18 | ±2.8% | time- repro |
| EqualFootingCrudBenchmarks | DeleteOnly_Pengdows | RecordCount=100 | 3.516ms | 3.141ms | -10.7% | -12.1% | -10.4% | -10.4% | 997679 | 714480 | -28.4% | -19.53 | ±4.5% | time- repro |
| EqualFootingCrudBenchmarks | FilteredQuery_Dapper | RecordCount=1 | 30.555us | 30.993us | +1.4% | n/a | +2.5% | +2.7% | 4400 | 4400 | +0.0% | +0.00 | ±1.7% |  |
| EqualFootingCrudBenchmarks | FilteredQuery_Dapper | RecordCount=100 | 135.542us | 136.679us | +0.8% | n/a | +0.9% | +0.8% | 34848 | 34848 | +0.0% | +0.00 | ±3.1% |  |
| EqualFootingCrudBenchmarks | FilteredQuery_EntityFramework | RecordCount=1 | 108.852us | 108.880us | +0.0% | -1.0% | +0.6% | +0.6% | 56899 | 56900 | +0.0% | +0.00 | ±3.5% |  |
| EqualFootingCrudBenchmarks | FilteredQuery_EntityFramework | RecordCount=100 | 201.216us | 200.532us | -0.3% | -2.0% | -0.2% | -0.2% | 96067 | 96068 | +0.0% | -0.24 | ±2.5% |  |
| EqualFootingCrudBenchmarks | FilteredQuery_Pengdows | RecordCount=1 | 40.886us | 37.654us | -7.9% | -8.9% | -7.5% | -7.4% | 7392 | 4888 | -33.9% | -0.18 | ±1.3% | time- repro |
| EqualFootingCrudBenchmarks | FilteredQuery_Pengdows | RecordCount=100 | 128.069us | 125.038us | -2.4% | -4.0% | -2.8% | -2.8% | 27544 | 25040 | -9.1% | +0.00 | ±1.3% |  |
| EqualFootingCrudBenchmarks | ReadList_Dapper | RecordCount=1 | 28.225us | 27.970us | -0.9% | n/a | +0.3% | +0.4% | 3360 | 3360 | +0.0% | +0.00 | ±2.3% |  |
| EqualFootingCrudBenchmarks | ReadList_Dapper | RecordCount=100 | 119.681us | 126.853us | +6.0% | n/a | +5.9% | +5.9% | 33360 | 33360 | +0.0% | +0.00 | ±12.2% | DAPPER-DRIFT |
| EqualFootingCrudBenchmarks | ReadList_EntityFramework | RecordCount=1 | 104.706us | 105.455us | +0.7% | -0.3% | +0.9% | +0.9% | 55195 | 55195 | +0.0% | +0.24 | ±3.2% |  |
| EqualFootingCrudBenchmarks | ReadList_EntityFramework | RecordCount=100 | 186.334us | 189.232us | +1.6% | -0.1% | +2.0% | +2.1% | 93915 | 93915 | +0.0% | +0.00 | ±1.6% |  |
| EqualFootingCrudBenchmarks | ReadList_Pengdows | RecordCount=1 | 37.385us | 34.904us | -6.6% | -7.6% | -6.8% | -6.9% | 6480 | 3976 | -38.6% | -0.18 | ±1.8% | time- repro |
| EqualFootingCrudBenchmarks | ReadList_Pengdows | RecordCount=100 | 110.606us | 116.763us | +5.6% | +3.8% | +5.7% | +5.8% | 26184 | 23680 | -9.6% | -0.12 | ±11.5% |  |
| EqualFootingCrudBenchmarks | ReadList_Pengdows_Native | RecordCount=1 | 36.505us | 34.302us | -6.0% | -7.0% | -5.6% | -5.6% | 6488 | 3984 | -38.6% | -0.18 | ±1.1% | time- repro |
| EqualFootingCrudBenchmarks | ReadList_Pengdows_Native | RecordCount=100 | 110.120us | 109.266us | -0.8% | -2.4% | -0.7% | -0.7% | 26984 | 24480 | -9.3% | -0.24 | ±3.2% |  |
| EqualFootingCrudBenchmarks | ReadSingle_Dapper | RecordCount=1 | 21.934us | 22.408us | +2.2% | n/a | +2.3% | +2.2% | 2728 | 2728 | +0.0% | +0.00 | ±2.8% |  |
| EqualFootingCrudBenchmarks | ReadSingle_Dapper | RecordCount=100 | 2.189ms | 2.226ms | +1.7% | n/a | +1.9% | +2.0% | 265687 | 265688 | +0.0% | +0.00 | ±2.8% |  |
| EqualFootingCrudBenchmarks | ReadSingle_EntityFramework | RecordCount=1 | 105.361us | 105.642us | +0.3% | -0.8% | +0.9% | +0.8% | 55419 | 55419 | +0.0% | +0.12 | ±1.7% |  |
| EqualFootingCrudBenchmarks | ReadSingle_EntityFramework | RecordCount=100 | 10.455ms | 10.513ms | +0.6% | -1.1% | +0.2% | +0.2% | 5534767 | 5534778 | +0.0% | +0.00 | ±1.7% |  |
| EqualFootingCrudBenchmarks | ReadSingle_Pengdows | RecordCount=1 | 29.769us | 26.601us | -10.6% | -11.6% | -10.7% | -10.7% | 6032 | 3528 | -41.5% | -0.15 | ±4.5% | time- repro |
| EqualFootingCrudBenchmarks | ReadSingle_Pengdows | RecordCount=100 | 2.962ms | 2.671ms | -9.8% | -11.3% | -9.7% | -9.7% | 596087 | 345687 | -42.0% | -15.62 | ±4.3% | time- repro |
| EqualFootingCrudBenchmarks | ReadSingle_Pengdows_Native | RecordCount=1 | 29.783us | 26.631us | -10.6% | -11.5% | -10.3% | -10.2% | 6040 | 3536 | -41.5% | -0.15 | ±1.1% | time- repro |
| EqualFootingCrudBenchmarks | ReadSingle_Pengdows_Native | RecordCount=100 | 2.910ms | 2.683ms | -7.8% | -9.3% | -7.5% | -7.4% | 596887 | 346488 | -42.0% | -15.62 | ±2.1% | time- repro |
| EqualFootingCrudBenchmarks | Update_Dapper | RecordCount=1 | 21.020us | 21.414us | +1.9% | n/a | +2.9% | +3.3% | 4120 | 4120 | +0.0% | +0.00 | ±4.9% |  |
| EqualFootingCrudBenchmarks | Update_Dapper | RecordCount=100 | 2.095ms | 2.136ms | +2.0% | n/a | +2.0% | +2.0% | 412079 | 412080 | +0.0% | +0.00 | ±2.6% |  |
| EqualFootingCrudBenchmarks | Update_EntityFramework | RecordCount=1 | 76.115us | 76.800us | +0.9% | -0.1% | +1.4% | +1.4% | 46769 | 46769 | -0.0% | +0.00 | ±4.3% |  |
| EqualFootingCrudBenchmarks | Update_EntityFramework | RecordCount=100 | 7.453ms | 7.591ms | +1.9% | +0.2% | +1.8% | +1.8% | 4676942 | 4676947 | +0.0% | +0.00 | ±5.1% |  |
| EqualFootingCrudBenchmarks | Update_Pengdows | RecordCount=1 | 27.516us | 25.016us | -9.1% | -10.0% | -7.7% | -7.8% | 7104 | 4280 | -39.8% | -0.15 | ±4.6% | time- repro |
| EqualFootingCrudBenchmarks | Update_Pengdows | RecordCount=100 | 2.724ms | 2.531ms | -7.1% | -8.6% | -6.7% | -6.7% | 710478 | 428079 | -39.7% | -15.62 | ±1.4% | time- repro |
| HydrationHotPathBenchmarks | HydrationOnly_Dapper | RowCount=100 | 127.308us | 126.596us | -0.6% | n/a | -0.3% | -0.3% | 41352 | 41352 | +0.0% | +0.00 | ±6.5% |  |
| HydrationHotPathBenchmarks | HydrationOnly_Dapper | RowCount=1000 | 1.161ms | 1.167ms | +0.5% | n/a | +0.6% | +0.6% | 401370 | 401370 | +0.0% | +0.00 | ±4.5% |  |
| HydrationHotPathBenchmarks | HydrationOnly_Dapper | RowCount=5000 | 5.829ms | 5.876ms | +0.8% | n/a | +0.9% | +0.9% | 2084136 | 2084139 | +0.0% | +0.00 | ±1.5% |  |
| HydrationHotPathBenchmarks | HydrationOnly_Pengdows | RowCount=100 | 82.412us | 83.074us | +0.8% | +1.4% | +0.6% | +0.5% | 21472 | 21600 | +0.6% | +0.00 | ±1.1% |  |
| HydrationHotPathBenchmarks | HydrationOnly_Pengdows | RowCount=1000 | 720.564us | 739.409us | +2.6% | +2.1% | +2.3% | +2.2% | 201489 | 201617 | +0.1% | +0.00 | ±3.0% |  |
| HydrationHotPathBenchmarks | HydrationOnly_Pengdows | RowCount=5000 | 3.606ms | 3.693ms | +2.4% | +1.6% | +2.3% | +2.3% | 1084252 | 1084381 | +0.0% | +0.00 | ±3.8% |  |

## Missing from one arm (not compared)
- none

## Validity
- Dapper control drift exceeded threshold for: BrokenMappingBenchmarks[], EqualFootingCrudBenchmarks[RecordCount=1], EqualFootingCrudBenchmarks[RecordCount=100]

## Flagged (investigate; `repro` = same direction in every paired round)
- ApplesToApplesDapperBenchmarks.ReadSingle_Pengdows_ProductionStandard [RecordCount=1]: time- repro
- ApplesToApplesDapperBenchmarks.ReadSingle_Pengdows_ProductionStandard [RecordCount=100]: time- repro
- ApplesToApplesDapperBenchmarks.ReadSingle_Pengdows_PureMapping [RecordCount=1]: TIME+ ALLOC+ repro
- ApplesToApplesDapperBenchmarks.ReadSingle_Pengdows_PureMapping [RecordCount=100]: ALLOC+
- BenchmarkGuidParameters.DuckDb_String []: TIME+ TAIL+ ALLOC+ repro
- BenchmarkGuidParameters.Firebird_Binary []: TIME+ TAIL+ ALLOC+ repro
- BenchmarkGuidParameters.Firebird_String []: TIME+ TAIL+ ALLOC+ repro
- BenchmarkGuidParameters.Oracle_String []: TIME+ TAIL+ ALLOC+ repro
- BenchmarkGuidParameters.SqlServer_PassThrough []: TIME+ TAIL+ ALLOC+ repro
- BenchmarkGuidParameters.Sqlite_1k_GuidParams []: TIME+ TAIL+ ALLOC+ repro
- BenchmarkGuidParameters.Sqlite_String []: TIME+ TAIL+ ALLOC+ repro
- BrokenMappingBenchmarks.AggregateCount_Pengdows []: time- repro
- BrokenMappingBenchmarks.BatchRead_Pengdows []: time- repro
- BrokenMappingBenchmarks.Create_Pengdows []: time- repro
- BrokenMappingBenchmarks.DeleteByKeyword_EntityFramework []: time- repro
- BrokenMappingBenchmarks.DeleteByKeyword_Pengdows []: time- repro
- BrokenMappingBenchmarks.Delete_EntityFramework []: time- repro
- BrokenMappingBenchmarks.Delete_Pengdows []: time- repro
- BrokenMappingBenchmarks.FilteredQuery_Pengdows []: time- repro
- BrokenMappingBenchmarks.ReadSingle_Pengdows []: time- repro
- BrokenMappingBenchmarks.ReadWithKeyword_Pengdows []: time- repro
- BrokenMappingBenchmarks.UpdateKeyword_Pengdows []: time- repro
- BrokenMappingBenchmarks.Update_Pengdows []: time- repro
- BrokenMappingBenchmarks.Upsert_Pengdows []: time- repro
- EqualFootingCrudBenchmarks.Aggregate_Pengdows [RecordCount=1]: time- repro
- EqualFootingCrudBenchmarks.Aggregate_Pengdows [RecordCount=100]: time- repro
- EqualFootingCrudBenchmarks.Breakdown_BuildVsExecute_Pengdows [RecordCount=1]: time- repro
- EqualFootingCrudBenchmarks.Breakdown_BuildVsExecute_Pengdows [RecordCount=100]: time- repro
- EqualFootingCrudBenchmarks.ConnectionHoldTime_Pengdows [RecordCount=1]: time- repro
- EqualFootingCrudBenchmarks.ConnectionHoldTime_Pengdows [RecordCount=100]: time- repro
- EqualFootingCrudBenchmarks.Create_Pengdows [RecordCount=1]: time- repro
- EqualFootingCrudBenchmarks.Create_Pengdows [RecordCount=100]: time- repro
- EqualFootingCrudBenchmarks.DeleteInsertCycle_Pengdows [RecordCount=1]: time- repro
- EqualFootingCrudBenchmarks.DeleteInsertCycle_Pengdows [RecordCount=100]: time- repro
- EqualFootingCrudBenchmarks.DeleteOnly_Pengdows [RecordCount=1]: time- repro
- EqualFootingCrudBenchmarks.DeleteOnly_Pengdows [RecordCount=100]: time- repro
- EqualFootingCrudBenchmarks.FilteredQuery_Pengdows [RecordCount=1]: time- repro
- EqualFootingCrudBenchmarks.ReadList_Pengdows [RecordCount=1]: time- repro
- EqualFootingCrudBenchmarks.ReadList_Pengdows_Native [RecordCount=1]: time- repro
- EqualFootingCrudBenchmarks.ReadSingle_Pengdows [RecordCount=1]: time- repro
- EqualFootingCrudBenchmarks.ReadSingle_Pengdows [RecordCount=100]: time- repro
- EqualFootingCrudBenchmarks.ReadSingle_Pengdows_Native [RecordCount=1]: time- repro
- EqualFootingCrudBenchmarks.ReadSingle_Pengdows_Native [RecordCount=100]: time- repro
- EqualFootingCrudBenchmarks.Update_Pengdows [RecordCount=1]: time- repro
- EqualFootingCrudBenchmarks.Update_Pengdows [RecordCount=100]: time- repro
