# main (2.0.5) vs 3.0 — tier 1 (SQLite/DuckDB/embedded + incidental SQL Server hydration)

Same harness (3.0's, with `BASELINE_2X` guard), library swapped. 2 rounds per arm, alternating order
(main, 3.0, 3.0, main). BenchmarkDotNet class-defined jobs, net8.0 runtime 8.0.31.

## How to read this
- **Bytes/op is exact and trustworthy** (Dapper/EF allocate identically to the byte in both arms).
- **Time/tail flags are raised only when the delta exceeds the measured round-to-round spread**
  (column "round spread"); small-iteration storms (4-5 iterations) are inherently noisy.
- TAIL is over per-iteration means (~max of 10), not per-operation latency.
- The 3.0 arm sets `MaxQueuedWrites` in the two write-storm benchmarks; `main` has no such option, so
  storm rows are not identical configs.
- An unrelated mariadb container was running on the host throughout.

## Environment
date: 2026-09-20T14:17:13-05:00
tier: tier1  job:   rounds: 2
base: main = c8835796fae19de858c786bf092bd1d1a58c1b4a
new:  3.0 = 9e2304a62063c8d7e83fbb2031224954005939ee
sdk(repo global.json): 10.0.112  runtime: Microsoft.NETCore.App 8.0.31 [/usr/lib/dotnet/shared/Microsoft.NETCore.App]
cpu:  AMD Ryzen 9 5950X 16-Core Processor x8
governor: n/a
loadavg-at-start: 0.49 1.22 0.98

# main vs 3.0  (rounds: 2 / 2)

Flags: time>5.0% (Dapper-normalized when a control exists) AND larger than the round-to-round spread, tail(P99)>10.0% AND larger than P99 spread, alloc>2.0%. Time deltas untrusted if Dapper drifts >3.0%.

| class | method | params | base med | new med | d med | d vs Dapper | d P95 | d P99 | base B/op | new B/op | d alloc | d gen0/1k | round spread | flags |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ApplesToApplesDapperBenchmarks | ReadSingle_Dapper_NewConnection | RecordCount=1 | 22.186us | 21.984us | -0.9% | n/a | +0.4% | +0.5% | 2311 | 2311 | +0.0% | +0.00 | ±5.8% |  |
| ApplesToApplesDapperBenchmarks | ReadSingle_Dapper_NewConnection | RecordCount=100 | 2.190ms | 2.229ms | +1.8% | n/a | +5.0% | +5.0% | 231200 | 231200 | +0.0% | +0.00 | ±3.9% |  |
| ApplesToApplesDapperBenchmarks | ReadSingle_Dapper_PureMapping | RecordCount=1 | 5.402us | 5.545us | +2.6% | n/a | +2.1% | +2.1% | 1839 | 1839 | +0.0% | +0.00 | ±3.1% |  |
| ApplesToApplesDapperBenchmarks | ReadSingle_Dapper_PureMapping | RecordCount=100 | 531.745us | 555.310us | +4.4% | n/a | +8.5% | +9.2% | 183994 | 183994 | +0.0% | +0.00 | ±2.3% | DAPPER-DRIFT |
| ApplesToApplesDapperBenchmarks | ReadSingle_Pengdows_ProductionStandard | RecordCount=1 | 30.941us | 31.488us | +1.8% | +0.9% | +7.2% | +8.1% | 5647 | 5695 | +0.9% | +0.02 | ±1.6% |  |
| ApplesToApplesDapperBenchmarks | ReadSingle_Pengdows_ProductionStandard | RecordCount=100 | 3.169ms | 3.112ms | -1.8% | -4.7% | -1.9% | -1.9% | 564800 | 569600 | +0.8% | +0.00 | ±3.6% |  |
| ApplesToApplesDapperBenchmarks | ReadSingle_Pengdows_PureMapping | RecordCount=1 | 5.860us | 6.025us | +2.8% | +1.9% | +3.1% | +3.1% | 1871 | 1975 | +5.6% | +0.01 | ±2.7% | ALLOC+ |
| ApplesToApplesDapperBenchmarks | ReadSingle_Pengdows_PureMapping | RecordCount=100 | 594.743us | 588.642us | -1.0% | -4.0% | -2.3% | -2.4% | 187194 | 197594 | +5.6% | +0.98 | ±8.8% | ALLOC+ |
| BenchmarkGuidParameters | DuckDb_String |  | 92.0ns | 110.4ns | +20.0% | n/a | +35.5% | +36.9% | 216 | 216 | +0.0% | +0.00 | ±4.0% | TIME+ TAIL+ repro |
| BenchmarkGuidParameters | Firebird_Binary |  | 81.0ns | 94.9ns | +17.2% | n/a | +17.2% | +17.2% | 128 | 160 | +25.0% | +0.00 | ±6.3% | TIME+ TAIL+ ALLOC+ repro |
| BenchmarkGuidParameters | Firebird_String |  | 86.2ns | 98.4ns | +14.2% | n/a | +14.5% | +14.6% | 184 | 216 | +17.4% | +0.00 | ±9.1% | TIME+ TAIL+ ALLOC+ repro |
| BenchmarkGuidParameters | Oracle_String |  | 89.7ns | 113.3ns | +26.3% | n/a | +26.0% | +25.9% | 216 | 216 | +0.0% | +0.00 | ±3.6% | TIME+ TAIL+ repro |
| BenchmarkGuidParameters | SqlServer_PassThrough |  | 70.0ns | 72.8ns | +4.1% | n/a | +5.2% | +5.3% | 120 | 120 | +0.0% | +0.00 | ±4.0% |  |
| BenchmarkGuidParameters | Sqlite_1k_GuidParams |  | 88.179us | 90.023us | +2.1% | n/a | +3.3% | +3.4% | 232000 | 232000 | +0.0% | +0.00 | ±4.5% |  |
| BenchmarkGuidParameters | Sqlite_String |  | 74.1ns | 71.7ns | -3.2% | n/a | -3.2% | -3.2% | 152 | 152 | +0.0% | +0.00 | ±13.1% |  |
| BrokenMappingBenchmarks | AggregateCount_Dapper |  | 8.051us | 8.258us | +2.6% | n/a | +2.5% | +2.5% | 1848 | 1848 | +0.0% | +0.00 | ±2.1% |  |
| BrokenMappingBenchmarks | AggregateCount_EntityFramework |  | 7.561us | 7.829us | +3.5% | +0.6% | +3.1% | +3.1% | 1640 | 1640 | +0.0% | +0.00 | ±0.6% |  |
| BrokenMappingBenchmarks | AggregateCount_Pengdows |  | 37.636us | 39.184us | +4.1% | +1.1% | +4.2% | +4.3% | 6784 | 6840 | +0.8% | +0.00 | ±3.4% |  |
| BrokenMappingBenchmarks | BatchRead_Dapper |  | 70.288us | 70.761us | +0.7% | n/a | +0.1% | +0.0% | 25440 | 25440 | +0.0% | +0.00 | ±3.9% |  |
| BrokenMappingBenchmarks | BatchRead_EntityFramework |  | 1.763ms | 1.761ms | -0.1% | -3.0% | -0.0% | +0.0% | 388276 | 388276 | +0.0% | +0.00 | ±1.5% |  |
| BrokenMappingBenchmarks | BatchRead_Pengdows |  | 353.314us | 367.831us | +4.1% | +1.1% | +4.4% | +4.4% | 76178 | 76738 | +0.7% | +0.00 | ±5.2% |  |
| BrokenMappingBenchmarks | Create_Dapper |  | 9.167us | 9.355us | +2.1% | n/a | +2.4% | +2.4% | 6808 | 6808 | +0.0% | +0.00 | ±6.4% |  |
| BrokenMappingBenchmarks | Create_EntityFramework |  | 33.479us | 34.995us | +4.5% | +1.5% | +22.5% | +23.8% | 14304 | 14304 | +0.0% | +0.00 | ±5.7% |  |
| BrokenMappingBenchmarks | Create_Pengdows |  | 38.265us | 41.101us | +7.4% | +4.3% | +8.8% | +8.9% | 12232 | 12256 | +0.2% | +0.06 | ±2.6% |  |
| BrokenMappingBenchmarks | DeleteByKeyword_Dapper |  | 51.962us | 55.157us | +6.1% | n/a | +5.8% | +5.8% | 12024 | 12048 | +0.2% | +0.00 | ±4.1% | DAPPER-DRIFT |
| BrokenMappingBenchmarks | DeleteByKeyword_EntityFramework |  | 82.380us | 82.361us | -0.0% | -2.9% | +0.3% | +0.3% | 18080 | 18105 | +0.1% | +0.00 | ±4.0% |  |
| BrokenMappingBenchmarks | DeleteByKeyword_Pengdows |  | 77.652us | 79.408us | +2.3% | -0.7% | +2.0% | +2.0% | 16968 | 17017 | +0.3% | +0.00 | ±3.6% |  |
| BrokenMappingBenchmarks | Delete_Dapper |  | 48.631us | 51.439us | +5.8% | n/a | +6.9% | +7.1% | 11896 | 11920 | +0.2% | -0.03 | ±4.8% | DAPPER-DRIFT |
| BrokenMappingBenchmarks | Delete_EntityFramework |  | 75.059us | 77.903us | +3.8% | +0.8% | +4.2% | +4.3% | 17856 | 17881 | +0.1% | +0.00 | ±3.8% |  |
| BrokenMappingBenchmarks | Delete_Pengdows |  | 72.557us | 78.374us | +8.0% | +4.9% | +9.5% | +9.8% | 16792 | 16840 | +0.3% | +0.00 | ±0.5% |  |
| BrokenMappingBenchmarks | FilterByKeyword_Dapper |  | 30.636us | 29.830us | -2.6% | n/a | -2.7% | -2.7% | 9792 | 9792 | +0.0% | +0.02 | ±1.2% |  |
| BrokenMappingBenchmarks | FilterByKeyword_EntityFramework |  | 148.257us | 150.089us | +1.2% | -1.7% | +0.9% | +0.9% | 27817 | 27817 | +0.0% | +0.00 | ±0.5% |  |
| BrokenMappingBenchmarks | FilterByKeyword_Pengdows |  | 91.749us | 96.861us | +5.6% | +2.6% | +5.5% | +5.5% | 21112 | 21168 | +0.3% | +0.00 | ±3.5% |  |
| BrokenMappingBenchmarks | FilteredQuery_Dapper |  | 22.029us | 24.948us | +13.2% | n/a | +24.8% | +24.9% | 4560 | 4560 | +0.0% | +0.00 | ±18.0% | DAPPER-DRIFT |
| BrokenMappingBenchmarks | FilteredQuery_EntityFramework |  | 158.987us | 169.251us | +6.5% | +3.4% | +8.9% | +9.1% | 29161 | 29161 | +0.0% | -0.24 | ±9.8% |  |
| BrokenMappingBenchmarks | FilteredQuery_Pengdows |  | 61.489us | 62.270us | +1.3% | -1.6% | +1.4% | +1.4% | 10272 | 10328 | +0.5% | +0.00 | ±1.6% |  |
| BrokenMappingBenchmarks | ReadAll_Dapper |  | 47.626us | 47.685us | +0.1% | n/a | +0.1% | +0.1% | 15784 | 15784 | +0.0% | +0.00 | ±5.0% |  |
| BrokenMappingBenchmarks | ReadAll_EntityFramework |  | 141.540us | 143.283us | +1.2% | -1.7% | +1.1% | +1.1% | 26449 | 26449 | +0.0% | +0.00 | ±0.8% |  |
| BrokenMappingBenchmarks | ReadAll_Pengdows |  | 141.851us | 145.804us | +2.8% | -0.1% | +2.8% | +2.8% | 32865 | 32921 | +0.2% | +0.00 | ±4.0% |  |
| BrokenMappingBenchmarks | ReadList_Dapper |  | 42.004us | 43.278us | +3.0% | n/a | +3.8% | +3.9% | 10808 | 10808 | +0.0% | +0.00 | ±1.4% | DAPPER-DRIFT |
| BrokenMappingBenchmarks | ReadList_EntityFramework |  | 157.929us | 163.476us | +3.5% | +0.6% | +3.8% | +3.8% | 29145 | 29145 | +0.0% | -0.24 | ±4.2% |  |
| BrokenMappingBenchmarks | ReadList_Pengdows |  | 108.457us | 116.315us | +7.2% | +4.2% | +6.0% | +6.0% | 22088 | 22145 | +0.3% | +0.00 | ±2.2% |  |
| BrokenMappingBenchmarks | ReadSingle_Dapper |  | 7.055us | 7.126us | +1.0% | n/a | +1.7% | +1.7% | 2576 | 2576 | +0.0% | +0.00 | ±2.7% |  |
| BrokenMappingBenchmarks | ReadSingle_EntityFramework |  | 176.915us | 178.737us | +1.0% | -1.9% | +1.3% | +1.3% | 38809 | 38809 | +0.0% | +0.00 | ±1.4% |  |
| BrokenMappingBenchmarks | ReadSingle_Pengdows |  | 36.627us | 36.879us | +0.7% | -2.2% | +0.9% | +1.0% | 7648 | 7704 | +0.7% | +0.00 | ±5.6% |  |
| BrokenMappingBenchmarks | ReadWithKeyword_Dapper |  | 10.586us | 10.753us | +1.6% | n/a | +1.7% | +1.8% | 2856 | 2856 | +0.0% | +0.00 | ±3.9% |  |
| BrokenMappingBenchmarks | ReadWithKeyword_EntityFramework |  | 146.651us | 149.361us | +1.8% | -1.1% | +1.5% | +1.4% | 27817 | 27817 | +0.0% | +0.00 | ±1.3% |  |
| BrokenMappingBenchmarks | ReadWithKeyword_Pengdows |  | 41.958us | 41.816us | -0.3% | -3.2% | -1.3% | -1.4% | 7904 | 7960 | +0.7% | +0.00 | ±2.9% |  |
| BrokenMappingBenchmarks | UpdateKeyword_Dapper |  | 4.621us | 4.759us | +3.0% | n/a | +3.1% | +3.1% | 2104 | 2104 | +0.0% | +0.00 | ±0.7% |  |
| BrokenMappingBenchmarks | UpdateKeyword_EntityFramework |  | 28.139us | 27.735us | -1.4% | -4.3% | -1.6% | -1.7% | 8304 | 8304 | +0.0% | +0.00 | ±3.6% |  |
| BrokenMappingBenchmarks | UpdateKeyword_Pengdows |  | 30.595us | 31.323us | +2.4% | -0.5% | +2.2% | +2.1% | 7032 | 7056 | +0.3% | -0.02 | ±2.8% |  |
| BrokenMappingBenchmarks | Update_Dapper |  | 8.402us | 8.691us | +3.4% | n/a | +6.0% | +6.3% | 7968 | 7968 | +0.0% | +0.00 | ±2.9% | DAPPER-DRIFT |
| BrokenMappingBenchmarks | Update_EntityFramework |  | 110.580us | 110.231us | -0.3% | -3.2% | +1.3% | +1.5% | 24152 | 24152 | +0.0% | +0.00 | ±2.4% |  |
| BrokenMappingBenchmarks | Update_Pengdows |  | 37.678us | 40.442us | +7.3% | +4.3% | +11.2% | +11.5% | 13384 | 13408 | +0.2% | +0.00 | ±8.1% |  |
| BrokenMappingBenchmarks | Upsert_Dapper |  | 12.443us | 12.692us | +2.0% | n/a | +2.4% | +2.5% | 12104 | 12104 | +0.0% | +0.00 | ±5.0% |  |
| BrokenMappingBenchmarks | Upsert_EntityFramework |  | 38.784us | 38.757us | -0.1% | -2.9% | -0.5% | -0.5% | 21424 | 21424 | +0.0% | +0.00 | ±4.4% |  |
| BrokenMappingBenchmarks | Upsert_Pengdows |  | 42.934us | 43.947us | +2.4% | -0.6% | +1.7% | +1.7% | 18624 | 18649 | +0.1% | +0.00 | ±2.3% |  |
| EqualFootingCrudBenchmarks | Aggregate_Dapper | RecordCount=1 | 57.153us | 60.420us | +5.7% | n/a | +10.5% | +11.5% | 1344 | 1344 | +0.0% | -0.03 | ±3.6% | DAPPER-DRIFT TAIL+ repro |
| EqualFootingCrudBenchmarks | Aggregate_Dapper | RecordCount=100 | 5.725ms | 5.847ms | +2.1% | n/a | +2.1% | +2.1% | 127283 | 127283 | +0.0% | +0.00 | ±1.2% |  |
| EqualFootingCrudBenchmarks | Aggregate_EntityFramework | RecordCount=1 | 116.218us | 122.495us | +5.4% | +4.4% | +8.6% | +9.0% | 42904 | 42904 | +0.0% | -0.06 | ±3.0% |  |
| EqualFootingCrudBenchmarks | Aggregate_EntityFramework | RecordCount=100 | 11.779ms | 11.909ms | +1.1% | -1.8% | +0.8% | +0.7% | 4283291 | 4283291 | +0.0% | +0.00 | ±1.7% |  |
| EqualFootingCrudBenchmarks | Aggregate_Pengdows | RecordCount=1 | 67.299us | 69.104us | +2.7% | +1.7% | +3.3% | +3.4% | 4928 | 4976 | +1.0% | +0.00 | ±2.4% |  |
| EqualFootingCrudBenchmarks | Aggregate_Pengdows | RecordCount=100 | 6.671ms | 6.846ms | +2.6% | -0.3% | +1.3% | +1.3% | 485683 | 490483 | +1.0% | +0.00 | ±2.7% |  |
| EqualFootingCrudBenchmarks | Breakdown_BuildVsExecute_Dapper | RecordCount=1 | 24.681us | 23.854us | -3.3% | n/a | -9.7% | -10.2% | 2776 | 2776 | +0.0% | +0.00 | ±8.5% | DAPPER-DRIFT |
| EqualFootingCrudBenchmarks | Breakdown_BuildVsExecute_Dapper | RecordCount=100 | 2.298ms | 2.440ms | +6.2% | n/a | +6.8% | +6.8% | 265733 | 265733 | +0.0% | +0.00 | ±10.5% | DAPPER-DRIFT |
| EqualFootingCrudBenchmarks | Breakdown_BuildVsExecute_EntityFramework | RecordCount=1 | 127.976us | 129.573us | +1.2% | +0.2% | +1.3% | +1.3% | 59116 | 59116 | +0.0% | +0.00 | ±1.7% |  |
| EqualFootingCrudBenchmarks | Breakdown_BuildVsExecute_EntityFramework | RecordCount=100 | 12.654ms | 12.747ms | +0.7% | -2.1% | +0.9% | +0.9% | 5899587 | 5899587 | +0.0% | +0.00 | ±3.6% |  |
| EqualFootingCrudBenchmarks | Breakdown_BuildVsExecute_Pengdows | RecordCount=1 | 37.286us | 38.082us | +2.1% | +1.1% | +1.9% | +1.9% | 7320 | 7553 | +3.2% | +0.00 | ±0.4% | ALLOC+ |
| EqualFootingCrudBenchmarks | Breakdown_BuildVsExecute_Pengdows | RecordCount=100 | 3.689ms | 3.867ms | +4.8% | +1.8% | +5.4% | +5.6% | 720152 | 743437 | +3.2% | -5.86 | ±1.8% | ALLOC+ |
| EqualFootingCrudBenchmarks | ConnectionHoldTime_Dapper | RecordCount=1 | 23.705us | 23.703us | -0.0% | n/a | +0.6% | +0.8% | 2768 | 2768 | +0.0% | +0.00 | ±1.5% |  |
| EqualFootingCrudBenchmarks | ConnectionHoldTime_Dapper | RecordCount=100 | 23.790us | 23.827us | +0.2% | n/a | +0.1% | +0.2% | 2768 | 2768 | +0.0% | +0.00 | ±3.1% |  |
| EqualFootingCrudBenchmarks | ConnectionHoldTime_EntityFramework | RecordCount=1 | 128.214us | 129.075us | +0.7% | -0.3% | +1.5% | +1.5% | 59108 | 59108 | +0.0% | +0.00 | ±1.1% |  |
| EqualFootingCrudBenchmarks | ConnectionHoldTime_EntityFramework | RecordCount=100 | 126.822us | 128.321us | +1.2% | -1.7% | +1.3% | +1.4% | 59108 | 59108 | +0.0% | +0.00 | ±2.3% |  |
| EqualFootingCrudBenchmarks | ConnectionHoldTime_Pengdows | RecordCount=1 | 33.528us | 33.640us | +0.3% | -0.7% | +1.1% | +1.1% | 6064 | 6112 | +0.8% | +0.00 | ±1.5% |  |
| EqualFootingCrudBenchmarks | ConnectionHoldTime_Pengdows | RecordCount=100 | 32.849us | 33.200us | +1.1% | -1.8% | +0.5% | +0.4% | 6064 | 6112 | +0.8% | +0.00 | ±5.3% |  |
| EqualFootingCrudBenchmarks | Create_Dapper | RecordCount=1 | 24.716us | 25.160us | +1.8% | n/a | +1.6% | +1.6% | 3784 | 3784 | +0.0% | +0.00 | ±3.0% |  |
| EqualFootingCrudBenchmarks | Create_Dapper | RecordCount=100 | 2.419ms | 2.635ms | +8.9% | n/a | +20.3% | +20.2% | 379197 | 379197 | +0.0% | +0.00 | ±4.8% | DAPPER-DRIFT |
| EqualFootingCrudBenchmarks | Create_EntityFramework | RecordCount=1 | 91.305us | 91.914us | +0.7% | -0.3% | -2.0% | -2.1% | 47489 | 47489 | +0.0% | +0.00 | ±2.2% |  |
| EqualFootingCrudBenchmarks | Create_EntityFramework | RecordCount=100 | 9.041ms | 9.300ms | +2.9% | -0.1% | +4.5% | +4.6% | 4749653 | 4749653 | +0.0% | +0.00 | ±1.8% |  |
| EqualFootingCrudBenchmarks | Create_Pengdows | RecordCount=1 | 38.121us | 41.118us | +7.9% | +6.8% | +7.7% | +7.7% | 8960 | 9121 | +1.8% | +0.00 | ±1.6% | TIME+ repro |
| EqualFootingCrudBenchmarks | Create_Pengdows | RecordCount=100 | 3.834ms | 4.030ms | +5.1% | +2.1% | +5.2% | +5.1% | 896797 | 912899 | +1.8% | -3.91 | ±0.1% |  |
| EqualFootingCrudBenchmarks | DeleteInsertCycle_Dapper | RecordCount=1 | 45.063us | 46.624us | +3.5% | n/a | +2.9% | +2.9% | 5800 | 5800 | +0.0% | +0.00 | ±8.5% | DAPPER-DRIFT |
| EqualFootingCrudBenchmarks | DeleteInsertCycle_Dapper | RecordCount=100 | 4.549ms | 4.684ms | +3.0% | n/a | +2.8% | +2.7% | 580083 | 580083 | +0.0% | +0.00 | ±4.7% |  |
| EqualFootingCrudBenchmarks | DeleteInsertCycle_EntityFramework | RecordCount=1 | 176.639us | 179.868us | +1.8% | +0.8% | +1.5% | +1.6% | 92353 | 92353 | +0.0% | +0.00 | ±1.8% |  |
| EqualFootingCrudBenchmarks | DeleteInsertCycle_EntityFramework | RecordCount=100 | 17.568ms | 17.849ms | +1.6% | -1.3% | +2.4% | +2.5% | 9235308 | 9235308 | +0.0% | +0.00 | ±1.5% |  |
| EqualFootingCrudBenchmarks | DeleteInsertCycle_Pengdows | RecordCount=1 | 61.621us | 64.174us | +4.1% | +3.1% | +21.5% | +25.0% | 12312 | 12344 | +0.3% | +0.00 | ±4.5% |  |
| EqualFootingCrudBenchmarks | DeleteInsertCycle_Pengdows | RecordCount=100 | 5.963ms | 6.143ms | +3.0% | +0.1% | +0.5% | +0.0% | 1231283 | 1234483 | +0.3% | +0.00 | ±2.3% |  |
| EqualFootingCrudBenchmarks | DeleteOnly_Dapper | RecordCount=1 | 25.318us | 25.285us | -0.1% | n/a | -5.1% | -5.1% | 5328 | 5328 | +0.0% | +0.00 | ±10.0% |  |
| EqualFootingCrudBenchmarks | DeleteOnly_Dapper | RecordCount=100 | 2.520ms | 2.565ms | +1.8% | n/a | +1.8% | +1.8% | 532877 | 532877 | +0.0% | +0.00 | ±1.3% |  |
| EqualFootingCrudBenchmarks | DeleteOnly_EntityFramework | RecordCount=1 | 95.169us | 96.216us | +1.1% | +0.1% | +0.3% | +0.3% | 45864 | 45864 | +0.0% | +0.00 | ±3.6% |  |
| EqualFootingCrudBenchmarks | DeleteOnly_EntityFramework | RecordCount=100 | 9.579ms | 9.547ms | -0.3% | -3.2% | -0.4% | -0.5% | 4586536 | 4586513 | -0.0% | +7.81 | ±2.5% |  |
| EqualFootingCrudBenchmarks | DeleteOnly_Pengdows | RecordCount=1 | 40.669us | 40.491us | -0.4% | -1.4% | -3.3% | -3.7% | 10168 | 10264 | +0.9% | +0.06 | ±4.3% |  |
| EqualFootingCrudBenchmarks | DeleteOnly_Pengdows | RecordCount=100 | 3.863ms | 4.043ms | +4.7% | +1.7% | +5.1% | +5.2% | 1016877 | 1026498 | +0.9% | -3.91 | ±0.8% |  |
| EqualFootingCrudBenchmarks | FilteredQuery_Dapper | RecordCount=1 | 32.497us | 33.544us | +3.2% | n/a | +3.4% | +3.4% | 4400 | 4400 | +0.0% | +0.00 | ±9.2% | DAPPER-DRIFT |
| EqualFootingCrudBenchmarks | FilteredQuery_Dapper | RecordCount=100 | 150.915us | 153.989us | +2.0% | n/a | +2.0% | +1.9% | 34848 | 34848 | +0.0% | +0.00 | ±2.7% |  |
| EqualFootingCrudBenchmarks | FilteredQuery_EntityFramework | RecordCount=1 | 128.955us | 133.796us | +3.8% | +2.7% | +4.4% | +4.5% | 60499 | 60500 | +0.0% | +0.00 | ±3.2% |  |
| EqualFootingCrudBenchmarks | FilteredQuery_EntityFramework | RecordCount=100 | 236.890us | 239.478us | +1.1% | -1.8% | +0.4% | +0.3% | 99667 | 99669 | +0.0% | +0.00 | ±5.0% |  |
| EqualFootingCrudBenchmarks | FilteredQuery_Pengdows | RecordCount=1 | 45.004us | 45.087us | +0.2% | -0.8% | +0.2% | +0.2% | 7384 | 7432 | +0.7% | +0.00 | ±1.5% |  |
| EqualFootingCrudBenchmarks | FilteredQuery_Pengdows | RecordCount=100 | 145.603us | 147.127us | +1.0% | -1.8% | +1.3% | +1.4% | 27536 | 27584 | +0.2% | +0.00 | ±2.9% |  |
| EqualFootingCrudBenchmarks | ReadList_Dapper | RecordCount=1 | 29.808us | 30.010us | +0.7% | n/a | -0.3% | -0.3% | 3360 | 3360 | +0.0% | +0.00 | ±0.9% |  |
| EqualFootingCrudBenchmarks | ReadList_Dapper | RecordCount=100 | 133.898us | 133.733us | -0.1% | n/a | +0.1% | +0.2% | 33360 | 33360 | +0.0% | +0.00 | ±2.8% |  |
| EqualFootingCrudBenchmarks | ReadList_EntityFramework | RecordCount=1 | 126.736us | 128.727us | +1.6% | +0.6% | +1.8% | +1.7% | 58796 | 58796 | +0.0% | +0.00 | ±2.9% |  |
| EqualFootingCrudBenchmarks | ReadList_EntityFramework | RecordCount=100 | 221.925us | 221.413us | -0.2% | -3.1% | -0.8% | -1.1% | 97513 | 97514 | +0.0% | -0.24 | ±1.5% |  |
| EqualFootingCrudBenchmarks | ReadList_Pengdows | RecordCount=1 | 41.773us | 41.777us | +0.0% | -1.0% | -0.5% | -0.6% | 6472 | 6520 | +0.7% | +0.00 | ±3.4% |  |
| EqualFootingCrudBenchmarks | ReadList_Pengdows | RecordCount=100 | 128.858us | 133.008us | +3.2% | +0.3% | +4.0% | +4.4% | 26176 | 26224 | +0.2% | +0.00 | ±6.5% |  |
| EqualFootingCrudBenchmarks | ReadList_Pengdows_Native | RecordCount=1 | 43.311us | 41.554us | -4.1% | -5.0% | -8.0% | -9.1% | 6480 | 6528 | +0.7% | +0.00 | ±7.4% | within-noise |
| EqualFootingCrudBenchmarks | ReadList_Pengdows_Native | RecordCount=100 | 128.284us | 132.628us | +3.4% | +0.4% | +3.6% | +3.6% | 26976 | 27024 | +0.2% | +0.00 | ±3.3% |  |
| EqualFootingCrudBenchmarks | ReadSingle_Dapper | RecordCount=1 | 23.872us | 23.645us | -1.0% | n/a | -6.8% | -7.5% | 2728 | 2728 | +0.0% | +0.00 | ±4.2% |  |
| EqualFootingCrudBenchmarks | ReadSingle_Dapper | RecordCount=100 | 2.345ms | 2.414ms | +3.0% | n/a | +2.8% | +2.8% | 265685 | 265685 | +0.0% | +0.00 | ±3.1% |  |
| EqualFootingCrudBenchmarks | ReadSingle_EntityFramework | RecordCount=1 | 129.487us | 128.868us | -0.5% | -1.5% | -1.1% | -1.2% | 59069 | 59069 | +0.0% | +0.00 | ±0.8% |  |
| EqualFootingCrudBenchmarks | ReadSingle_EntityFramework | RecordCount=100 | 12.823ms | 12.800ms | -0.2% | -3.0% | -0.7% | -0.8% | 5899584 | 5899539 | -0.0% | +0.00 | ±1.7% |  |
| EqualFootingCrudBenchmarks | ReadSingle_Pengdows | RecordCount=1 | 33.217us | 33.146us | -0.2% | -1.2% | -0.6% | -0.7% | 6024 | 6072 | +0.8% | +0.00 | ±3.6% |  |
| EqualFootingCrudBenchmarks | ReadSingle_Pengdows | RecordCount=100 | 3.218ms | 3.420ms | +6.3% | +3.2% | +5.4% | +5.2% | 595285 | 600093 | +0.8% | -1.95 | ±1.4% |  |
| EqualFootingCrudBenchmarks | ReadSingle_Pengdows_Native | RecordCount=1 | 33.887us | 33.329us | -1.6% | -2.6% | -11.5% | -13.4% | 6032 | 6080 | +0.8% | +0.03 | ±8.6% |  |
| EqualFootingCrudBenchmarks | ReadSingle_Pengdows_Native | RecordCount=100 | 3.201ms | 3.367ms | +5.2% | +2.2% | +5.0% | +5.0% | 596085 | 600882 | +0.8% | +0.00 | ±3.9% |  |
| EqualFootingCrudBenchmarks | Update_Dapper | RecordCount=1 | 22.958us | 22.938us | -0.1% | n/a | -0.3% | -0.2% | 4120 | 4120 | +0.0% | +0.00 | ±5.2% |  |
| EqualFootingCrudBenchmarks | Update_Dapper | RecordCount=100 | 2.265ms | 2.324ms | +2.6% | n/a | +2.6% | +2.5% | 412077 | 412077 | +0.0% | +0.00 | ±2.6% |  |
| EqualFootingCrudBenchmarks | Update_EntityFramework | RecordCount=1 | 89.086us | 91.629us | +2.9% | +1.8% | -1.1% | -1.5% | 47960 | 47960 | +0.0% | +0.00 | ±8.2% |  |
| EqualFootingCrudBenchmarks | Update_EntityFramework | RecordCount=100 | 8.957ms | 8.948ms | -0.1% | -2.9% | -0.3% | -0.4% | 4796133 | 4796133 | +0.0% | +0.00 | ±1.1% |  |
| EqualFootingCrudBenchmarks | Update_Pengdows | RecordCount=1 | 31.068us | 32.324us | +4.0% | +3.0% | +4.2% | +4.2% | 7136 | 7152 | +0.2% | +0.05 | ±8.2% |  |
| EqualFootingCrudBenchmarks | Update_Pengdows | RecordCount=100 | 3.102ms | 3.065ms | -1.2% | -4.0% | -1.4% | -1.4% | 713677 | 715277 | +0.2% | +0.00 | ±4.8% |  |
| HydrationHotPathBenchmarks | HydrationOnly_Dapper | RowCount=100 | 141.644us | 140.758us | -0.6% | n/a | -1.6% | -1.7% | 41352 | 41352 | +0.0% | +0.00 | ±3.4% |  |
| HydrationHotPathBenchmarks | HydrationOnly_Dapper | RowCount=1000 | 1.319ms | 1.299ms | -1.6% | n/a | -1.9% | -2.0% | 401369 | 401369 | +0.0% | +0.00 | ±4.3% |  |
| HydrationHotPathBenchmarks | HydrationOnly_Dapper | RowCount=5000 | 6.552ms | 6.666ms | +1.7% | n/a | +1.6% | +1.5% | 2084134 | 2084134 | +0.0% | +0.00 | ±2.7% |  |
| HydrationHotPathBenchmarks | HydrationOnly_Pengdows | RowCount=100 | 94.440us | 96.020us | +1.7% | +2.3% | +1.3% | +1.3% | 21472 | 21576 | +0.5% | +0.00 | ±2.4% |  |
| HydrationHotPathBenchmarks | HydrationOnly_Pengdows | RowCount=1000 | 843.151us | 843.015us | -0.0% | +1.6% | -0.8% | -1.3% | 201489 | 201593 | +0.1% | +0.00 | ±4.2% |  |
| HydrationHotPathBenchmarks | HydrationOnly_Pengdows | RowCount=5000 | 4.130ms | 4.157ms | +0.7% | -1.1% | +0.6% | +0.6% | 1084254 | 1084358 | +0.0% | +0.00 | ±2.5% |  |
| SqlServerHydrationHotPathBenchmarks | HydrationOnly_Dapper | RowCount=100 | 279.722us | 277.943us | -0.6% | n/a | -0.3% | -0.0% | 41364 | 41363 | -0.0% | +0.24 | ±0.5% |  |
| SqlServerHydrationHotPathBenchmarks | HydrationOnly_Dapper | RowCount=1000 | 815.655us | 848.105us | +4.0% | n/a | -17.1% | -21.0% | 379820 | 379907 | +0.0% | -1.46 | ±7.1% | DAPPER-DRIFT |
| SqlServerHydrationHotPathBenchmarks | HydrationOnly_Dapper | RowCount=5000 | 3.112ms | 3.252ms | +4.5% | n/a | +6.9% | +7.4% | 1966729 | 1966681 | -0.0% | +0.00 | ±1.7% | DAPPER-DRIFT |
| SqlServerHydrationHotPathBenchmarks | HydrationOnly_Pengdows | RowCount=100 | 345.647us | 332.307us | -3.9% | -3.2% | -10.3% | -11.6% | 44504 | 44646 | +0.3% | -0.24 | ±10.2% |  |
| SqlServerHydrationHotPathBenchmarks | HydrationOnly_Pengdows | RowCount=1000 | 1.012ms | 1.035ms | +2.3% | -1.6% | +1.4% | +1.2% | 461601 | 461692 | +0.0% | +0.00 | ±3.2% |  |
| SqlServerHydrationHotPathBenchmarks | HydrationOnly_Pengdows | RowCount=5000 | 4.033ms | 4.158ms | +3.1% | -1.4% | +4.7% | +4.6% | 2410166 | 2410694 | +0.0% | -7.81 | ±3.8% |  |

## Validity
- Dapper control drift exceeded threshold for: ApplesToApplesDapperBenchmarks[RecordCount=100], BrokenMappingBenchmarks[], EqualFootingCrudBenchmarks[RecordCount=1], EqualFootingCrudBenchmarks[RecordCount=100], SqlServerHydrationHotPathBenchmarks[RowCount=1000], SqlServerHydrationHotPathBenchmarks[RowCount=5000]

## Flagged (investigate; `repro` = same direction in every paired round)
- ApplesToApplesDapperBenchmarks.ReadSingle_Pengdows_PureMapping [RecordCount=1]: ALLOC+
- ApplesToApplesDapperBenchmarks.ReadSingle_Pengdows_PureMapping [RecordCount=100]: ALLOC+
- BenchmarkGuidParameters.DuckDb_String []: TIME+ TAIL+ repro
- BenchmarkGuidParameters.Firebird_Binary []: TIME+ TAIL+ ALLOC+ repro
- BenchmarkGuidParameters.Firebird_String []: TIME+ TAIL+ ALLOC+ repro
- BenchmarkGuidParameters.Oracle_String []: TIME+ TAIL+ repro
- EqualFootingCrudBenchmarks.Aggregate_Dapper [RecordCount=1]: DAPPER-DRIFT TAIL+ repro
- EqualFootingCrudBenchmarks.Breakdown_BuildVsExecute_Pengdows [RecordCount=1]: ALLOC+
- EqualFootingCrudBenchmarks.Breakdown_BuildVsExecute_Pengdows [RecordCount=100]: ALLOC+
- EqualFootingCrudBenchmarks.Create_Pengdows [RecordCount=1]: TIME+ repro
