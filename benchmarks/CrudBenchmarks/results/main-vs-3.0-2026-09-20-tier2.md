# main (2.0.5) vs 3.0 — tier 2 (embedded concurrency, pool protection, DuckDB)

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
date: 2026-09-20T15:54:15-05:00
tier: tier2  job:   rounds: 2
base: main = c8835796fae19de858c786bf092bd1d1a58c1b4a
new:  3.0 = 9e2304a62063c8d7e83fbb2031224954005939ee
sdk(repo global.json): 10.0.112  runtime: Microsoft.NETCore.App 8.0.31 [/usr/lib/dotnet/shared/Microsoft.NETCore.App]
cpu:  AMD Ryzen 9 5950X 16-Core Processor x8
governor: n/a
loadavg-at-start: 0.39 1.23 1.86

# main vs 3.0  (rounds: 2 / 2)

Flags: time>5.0% (Dapper-normalized when a control exists) AND larger than the round-to-round spread, tail(P99)>10.0% AND larger than P99 spread, alloc>2.0%. Time deltas untrusted if Dapper drifts >3.0%.

| class | method | params | base med | new med | d med | d vs Dapper | d P95 | d P99 | base B/op | new B/op | d alloc | d gen0/1k | round spread | flags |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ConnectionPoolProtectionBenchmarks | ConnectionHoldTime_Dapper |  | 2.413ms | 2.365ms | -2.0% | n/a | -1.9% | -1.8% | 316077 | 316077 | +0.0% | +0.00 | ±5.6% |  |
| ConnectionPoolProtectionBenchmarks | ConnectionHoldTime_EntityFramework |  | 13.069ms | 12.933ms | -1.0% | -2.1% | -0.6% | -0.6% | 5782808 | 5782854 | +0.0% | +0.00 | ±0.9% |  |
| ConnectionPoolProtectionBenchmarks | ConnectionHoldTime_Pengdows |  | 5.940ms | 5.910ms | -0.5% | -1.5% | -2.0% | -2.1% | 1201712 | 1208901 | +0.6% | +7.81 | ±6.7% |  |
| ConnectionPoolProtectionBenchmarks | PoolExhaustion_Dapper |  | 2.019ms | 2.038ms | +0.9% | n/a | +1.4% | +1.4% | 169552 | 169533 | -0.0% | +0.00 | ±3.9% |  |
| ConnectionPoolProtectionBenchmarks | PoolExhaustion_EntityFramework |  | 3.096ms | 3.073ms | -0.7% | -1.8% | -2.6% | -2.8% | 2902135 | 2902059 | -0.0% | +0.00 | ±5.1% |  |
| ConnectionPoolProtectionBenchmarks | PoolExhaustion_Pengdows |  | 4.115ms | 4.214ms | +2.4% | +1.4% | +4.8% | +5.0% | 602747 | 606380 | +0.6% | +0.00 | ±3.2% |  |
| ConnectionPoolProtectionBenchmarks | SustainedPressure_Dapper |  | 2.324ms | 2.448ms | +5.3% | n/a | +5.3% | +5.3% | 312005 | 312005 | +0.0% | +0.00 | ±2.9% | DAPPER-DRIFT |
| ConnectionPoolProtectionBenchmarks | SustainedPressure_EntityFramework |  | 12.900ms | 12.965ms | +0.5% | -0.5% | +0.7% | +0.7% | 5778782 | 5778782 | +0.0% | +0.00 | ±1.5% |  |
| ConnectionPoolProtectionBenchmarks | SustainedPressure_Pengdows |  | 5.909ms | 5.892ms | -0.3% | -1.3% | -0.5% | -0.6% | 1197640 | 1204834 | +0.6% | +3.91 | ±2.3% |  |
| ConnectionPoolProtectionBenchmarks | WriteStorm_Dapper |  | 1.057s | 1.057s | +0.0% | n/a | -0.0% | -0.0% | 3423516 | 2953296 | -13.7% | +0.00 | ±0.1% |  |
| ConnectionPoolProtectionBenchmarks | WriteStorm_EntityFramework |  | 1.056s | 1.057s | +0.1% | -1.0% | -0.0% | -0.0% | 12016096 | 13600212 | +13.2% | +0.00 | ±0.0% | ALLOC+ |
| ConnectionPoolProtectionBenchmarks | WriteStorm_Pengdows |  | 147.767ms | 130.138ms | -11.9% | -12.8% | +8.1% | +9.4% | 15601456 | 15648656 | +0.3% | +0.00 | ±59.8% | within-noise |
| DuckDbEqualFootingBenchmarks | Aggregate_Dapper | RecordCount=1 | 303.201us | 303.095us | -0.0% | n/a | +0.0% | +0.0% | 3193 | 3193 | +0.0% | +0.00 | ±0.5% |  |
| DuckDbEqualFootingBenchmarks | Aggregate_Dapper | RecordCount=100 | 30.797ms | 30.246ms | -1.8% | n/a | -2.2% | -2.3% | 312114 | 312114 | +0.0% | +0.00 | ±1.7% |  |
| DuckDbEqualFootingBenchmarks | Aggregate_Pengdows | RecordCount=1 | 311.089us | 313.080us | +0.6% | +0.2% | +0.6% | +0.5% | 5585 | 5633 | +0.9% | +0.00 | ±1.1% |  |
| DuckDbEqualFootingBenchmarks | Aggregate_Pengdows | RecordCount=100 | 31.207ms | 31.291ms | +0.3% | +0.2% | +0.7% | +0.6% | 551349 | 556149 | +0.9% | +0.00 | ±1.2% |  |
| DuckDbEqualFootingBenchmarks | Breakdown_BuildVsExecute_Dapper | RecordCount=1 | 391.105us | 391.419us | +0.1% | n/a | -0.3% | -0.4% | 5153 | 5153 | +0.0% | +0.00 | ±0.9% |  |
| DuckDbEqualFootingBenchmarks | Breakdown_BuildVsExecute_Dapper | RecordCount=100 | 39.669ms | 38.968ms | -1.8% | n/a | -2.8% | -2.8% | 503433 | 503433 | +0.0% | +0.00 | ±2.2% |  |
| DuckDbEqualFootingBenchmarks | Breakdown_BuildVsExecute_Pengdows | RecordCount=1 | 402.914us | 411.198us | +2.1% | +1.6% | +2.0% | +1.9% | 7985 | 8203 | +2.7% | +0.49 | ±1.1% | ALLOC+ |
| DuckDbEqualFootingBenchmarks | Breakdown_BuildVsExecute_Pengdows | RecordCount=100 | 41.038ms | 40.850ms | -0.5% | -0.5% | -1.8% | -1.8% | 786667 | 808392 | +2.8% | +0.00 | ±2.6% | ALLOC+ |
| DuckDbEqualFootingBenchmarks | ConnectionHoldTime_Dapper | RecordCount=1 | 395.921us | 392.828us | -0.8% | n/a | -0.5% | -0.5% | 5145 | 5145 | +0.0% | +0.00 | ±0.8% |  |
| DuckDbEqualFootingBenchmarks | ConnectionHoldTime_Dapper | RecordCount=100 | 388.805us | 391.143us | +0.6% | n/a | -1.3% | -1.3% | 5145 | 5145 | +0.0% | +0.00 | ±2.8% |  |
| DuckDbEqualFootingBenchmarks | ConnectionHoldTime_Pengdows | RecordCount=1 | 397.571us | 401.114us | +0.9% | +0.4% | +0.2% | +0.1% | 6729 | 6777 | +0.7% | +0.00 | ±1.9% |  |
| DuckDbEqualFootingBenchmarks | ConnectionHoldTime_Pengdows | RecordCount=100 | 403.121us | 402.925us | -0.0% | -0.1% | +0.4% | +0.3% | 6729 | 6777 | +0.7% | +0.00 | ±1.0% |  |
| DuckDbEqualFootingBenchmarks | Create_Dapper | RecordCount=1 | 639.528us | 646.104us | +1.0% | n/a | +1.1% | +1.5% | 3929 | 3929 | +0.0% | +0.00 | ±1.5% |  |
| DuckDbEqualFootingBenchmarks | Create_Dapper | RecordCount=100 | 64.036ms | 63.836ms | -0.3% | n/a | +0.5% | +0.7% | 393042 | 393042 | +0.0% | +0.00 | ±2.5% |  |
| DuckDbEqualFootingBenchmarks | Create_Pengdows | RecordCount=1 | 649.594us | 657.473us | +1.2% | +0.8% | +0.7% | +0.6% | 8257 | 8507 | +3.0% | +0.00 | ±1.2% | ALLOC+ |
| DuckDbEqualFootingBenchmarks | Create_Pengdows | RecordCount=100 | 65.100ms | 65.875ms | +1.2% | +1.2% | +0.6% | +0.6% | 825842 | 850900 | +3.0% | +0.00 | ±0.3% | ALLOC+ |
| DuckDbEqualFootingBenchmarks | Delete_Dapper | RecordCount=1 | 1.239ms | 1.244ms | +0.5% | n/a | -0.3% | -0.7% | 6835 | 6835 | +0.0% | +0.00 | ±1.8% |  |
| DuckDbEqualFootingBenchmarks | Delete_Dapper | RecordCount=100 | 123.526ms | 123.991ms | +0.4% | n/a | +1.2% | +1.4% | 683612 | 683612 | +0.0% | +0.00 | ±0.7% |  |
| DuckDbEqualFootingBenchmarks | Delete_Pengdows | RecordCount=1 | 1.259ms | 1.261ms | +0.1% | -0.3% | +0.2% | +0.2% | 13507 | 13775 | +2.0% | +0.00 | ±0.7% |  |
| DuckDbEqualFootingBenchmarks | Delete_Pengdows | RecordCount=100 | 125.464ms | 125.921ms | +0.4% | +0.3% | +2.6% | +3.2% | 1350812 | 1377728 | +2.0% | +0.00 | ±0.5% |  |
| DuckDbEqualFootingBenchmarks | FilteredQuery_Dapper | RecordCount=1 | 484.734us | 490.298us | +1.1% | n/a | +2.2% | +2.1% | 5649 | 5649 | +0.0% | +0.00 | ±1.7% |  |
| DuckDbEqualFootingBenchmarks | FilteredQuery_Dapper | RecordCount=100 | 509.522us | 510.989us | +0.3% | n/a | +0.3% | +0.2% | 36905 | 36905 | +0.0% | +0.00 | ±0.5% |  |
| DuckDbEqualFootingBenchmarks | FilteredQuery_Pengdows | RecordCount=1 | 472.894us | 473.532us | +0.1% | -0.3% | +0.1% | +0.3% | 7473 | 7521 | +0.6% | +0.00 | ±0.9% |  |
| DuckDbEqualFootingBenchmarks | FilteredQuery_Pengdows | RecordCount=100 | 501.423us | 509.148us | +1.5% | +1.5% | +1.3% | +1.4% | 19722 | 19770 | +0.2% | +0.00 | ±0.9% |  |
| DuckDbEqualFootingBenchmarks | ReadList_Dapper | RecordCount=1 | 430.747us | 434.244us | +0.8% | n/a | +1.2% | +1.5% | 5401 | 5401 | +0.0% | +0.00 | ±2.1% |  |
| DuckDbEqualFootingBenchmarks | ReadList_Dapper | RecordCount=100 | 452.019us | 457.503us | +1.2% | n/a | +1.5% | +1.6% | 36193 | 36193 | +0.0% | +0.00 | ±2.7% |  |
| DuckDbEqualFootingBenchmarks | ReadList_Pengdows | RecordCount=1 | 440.114us | 444.904us | +1.1% | +0.6% | +0.9% | +1.1% | 7281 | 7329 | +0.7% | +0.00 | ±0.7% |  |
| DuckDbEqualFootingBenchmarks | ReadList_Pengdows | RecordCount=100 | 465.622us | 476.046us | +2.2% | +2.2% | +2.0% | +1.9% | 19065 | 19113 | +0.3% | +0.00 | ±0.8% |  |
| DuckDbEqualFootingBenchmarks | ReadList_Pengdows_SingleConnection | RecordCount=1 | 354.472us | 357.080us | +0.7% | +0.3% | +1.1% | +1.1% | 2993 | 3097 | +3.5% | +0.00 | ±1.1% | ALLOC+ |
| DuckDbEqualFootingBenchmarks | ReadList_Pengdows_SingleConnection | RecordCount=100 | 383.488us | 388.151us | +1.2% | +1.2% | +1.0% | +1.0% | 14777 | 14881 | +0.7% | +0.00 | ±0.2% |  |
| DuckDbEqualFootingBenchmarks | ReadSingle_Dapper | RecordCount=1 | 391.603us | 394.626us | +0.8% | n/a | +2.3% | +2.5% | 5105 | 5105 | +0.0% | +0.00 | ±0.7% |  |
| DuckDbEqualFootingBenchmarks | ReadSingle_Dapper | RecordCount=100 | 38.955ms | 39.216ms | +0.7% | n/a | +1.1% | +1.1% | 503385 | 503385 | +0.0% | +0.00 | ±0.5% |  |
| DuckDbEqualFootingBenchmarks | ReadSingle_Pengdows | RecordCount=1 | 400.380us | 400.528us | +0.0% | -0.4% | -0.6% | -0.7% | 6689 | 6737 | +0.7% | +0.00 | ±0.4% |  |
| DuckDbEqualFootingBenchmarks | ReadSingle_Pengdows | RecordCount=100 | 39.824ms | 40.162ms | +0.8% | +0.8% | +0.5% | +0.5% | 661801 | 666601 | +0.7% | +0.00 | ±1.4% |  |
| DuckDbEqualFootingBenchmarks | ReadSingle_Pengdows_SingleConnection | RecordCount=1 | 301.811us | 302.150us | +0.1% | -0.3% | +1.7% | +2.0% | 2361 | 2465 | +4.4% | +0.00 | ±1.0% | ALLOC+ |
| DuckDbEqualFootingBenchmarks | ReadSingle_Pengdows_SingleConnection | RecordCount=100 | 30.452ms | 30.280ms | -0.6% | -0.6% | -0.3% | -0.1% | 228922 | 239322 | +4.5% | +0.00 | ±2.5% | ALLOC+ |
| DuckDbEqualFootingBenchmarks | Update_Dapper | RecordCount=1 | 735.654us | 739.748us | +0.6% | n/a | +0.8% | +0.8% | 3881 | 3881 | +0.0% | +0.00 | ±1.4% |  |
| DuckDbEqualFootingBenchmarks | Update_Dapper | RecordCount=100 | 74.853ms | 75.568ms | +1.0% | n/a | +0.6% | +0.6% | 388266 | 388266 | +0.0% | +0.00 | ±2.9% |  |
| DuckDbEqualFootingBenchmarks | Update_Pengdows | RecordCount=1 | 738.725us | 749.563us | +1.5% | +1.0% | +1.4% | +1.5% | 5961 | 5977 | +0.3% | +0.00 | ±0.1% |  |
| DuckDbEqualFootingBenchmarks | Update_Pengdows | RecordCount=100 | 74.624ms | 75.476ms | +1.1% | +1.1% | +1.1% | +1.0% | 596266 | 597866 | +0.3% | +0.00 | ±2.2% |  |
| DuckDbReadBenchmarks | ReadList_Dapper | RecordCount=1 | 267.169us | 268.001us | +0.3% | n/a | -0.7% | -0.7% | 3000 | 3000 | +0.0% | +0.00 | ±2.7% |  |
| DuckDbReadBenchmarks | ReadList_Dapper | RecordCount=10 | 274.361us | 269.591us | -1.7% | n/a | -3.9% | -4.0% | 3000 | 3000 | +0.0% | +0.00 | ±5.7% |  |
| DuckDbReadBenchmarks | ReadList_Dapper | RecordCount=100 | 267.204us | 267.723us | +0.2% | n/a | +0.0% | +0.0% | 3000 | 3000 | +0.0% | +0.00 | ±1.1% |  |
| DuckDbReadBenchmarks | ReadList_Pengdows | RecordCount=1 | 438.816us | 437.188us | -0.4% | -0.9% | -0.3% | -0.2% | 8369 | 8425 | +0.7% | +0.00 | ±1.2% |  |
| DuckDbReadBenchmarks | ReadList_Pengdows | RecordCount=10 | 440.120us | 440.896us | +0.2% | +1.0% | +1.1% | +1.3% | 9473 | 9529 | +0.6% | +0.00 | ±0.9% |  |
| DuckDbReadBenchmarks | ReadList_Pengdows | RecordCount=100 | 461.841us | 467.073us | +1.1% | +1.0% | -0.8% | -1.3% | 20162 | 20217 | +0.3% | +0.00 | ±2.1% |  |
| DuckDbReadBenchmarks | ReadSingle_Dapper | RecordCount=1 | 242.982us | 244.778us | +0.7% | n/a | +0.8% | +0.7% | 3048 | 3048 | +0.0% | +0.00 | ±1.8% |  |
| DuckDbReadBenchmarks | ReadSingle_Dapper | RecordCount=10 | 2.458ms | 2.460ms | +0.1% | n/a | -0.6% | -0.8% | 29836 | 29836 | +0.0% | +0.00 | ±1.6% |  |
| DuckDbReadBenchmarks | ReadSingle_Dapper | RecordCount=100 | 24.621ms | 24.624ms | +0.0% | n/a | +0.7% | +1.0% | 297704 | 297704 | +0.0% | +0.00 | ±0.4% |  |
| DuckDbReadBenchmarks | ReadSingle_Pengdows | RecordCount=1 | 395.230us | 400.397us | +1.3% | +0.8% | +0.9% | +0.5% | 7649 | 7705 | +0.7% | +0.00 | ±1.1% |  |
| DuckDbReadBenchmarks | ReadSingle_Pengdows | RecordCount=10 | 3.925ms | 3.983ms | +1.5% | +2.3% | +1.3% | +1.4% | 75846 | 76406 | +0.7% | +0.00 | ±0.9% |  |
| DuckDbReadBenchmarks | ReadSingle_Pengdows | RecordCount=100 | 39.580ms | 40.215ms | +1.6% | +1.5% | +2.5% | +2.6% | 757819 | 763419 | +0.7% | +0.00 | ±1.4% |  |
| SQLiteWriteContentionBenchmarks | WriteStorm_Dapper |  | 1.063s | 1.062s | -0.0% | n/a | +0.1% | +0.1% | 3850212 | 3891072 | +1.1% | +0.00 | ±0.6% |  |
| SQLiteWriteContentionBenchmarks | WriteStorm_EntityFramework |  | 1.061s | 1.061s | +0.0% | +0.1% | +0.2% | +0.2% | 13052288 | 12501512 | -4.2% | +0.00 | ±0.2% |  |
| SQLiteWriteContentionBenchmarks | WriteStorm_Pengdows |  | 94.859ms | 117.452ms | +23.8% | +23.8% | +29.6% | +28.9% | 13888620 | 13935652 | +0.3% | +0.00 | ±36.1% | within-noise |
| SqliteConcurrencyBenchmark | Dapper_Concurrency |  | 62.644ms | 63.695ms | +1.7% | n/a | +1.9% | +2.0% | 196616 | 196616 | +0.0% | +0.00 | ±3.7% |  |
| SqliteConcurrencyBenchmark | EFCore_Concurrency |  | 95.753ms | 103.423ms | +8.0% | +6.2% | -0.8% | -1.5% | 5247392 | 5247392 | +0.0% | +0.00 | ±5.9% | TIME+ repro |
| SqliteConcurrencyBenchmark | Pengdows_Concurrency |  | 25.285ms | 26.199ms | +3.6% | +1.9% | +12.6% | +13.4% | 705208 | 736432 | +4.4% | +0.00 | ±5.0% | TAIL+ ALLOC+ repro |

## Validity
- Dapper control drift exceeded threshold for: ConnectionPoolProtectionBenchmarks[]

## Flagged (investigate; `repro` = same direction in every paired round)
- ConnectionPoolProtectionBenchmarks.WriteStorm_EntityFramework []: ALLOC+
- DuckDbEqualFootingBenchmarks.Breakdown_BuildVsExecute_Pengdows [RecordCount=1]: ALLOC+
- DuckDbEqualFootingBenchmarks.Breakdown_BuildVsExecute_Pengdows [RecordCount=100]: ALLOC+
- DuckDbEqualFootingBenchmarks.Create_Pengdows [RecordCount=1]: ALLOC+
- DuckDbEqualFootingBenchmarks.Create_Pengdows [RecordCount=100]: ALLOC+
- DuckDbEqualFootingBenchmarks.ReadList_Pengdows_SingleConnection [RecordCount=1]: ALLOC+
- DuckDbEqualFootingBenchmarks.ReadSingle_Pengdows_SingleConnection [RecordCount=1]: ALLOC+
- DuckDbEqualFootingBenchmarks.ReadSingle_Pengdows_SingleConnection [RecordCount=100]: ALLOC+
- SqliteConcurrencyBenchmark.EFCore_Concurrency []: TIME+ repro
- SqliteConcurrencyBenchmark.Pengdows_Concurrency []: TAIL+ ALLOC+ repro
