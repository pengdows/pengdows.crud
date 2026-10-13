# SQLite writer patterns, file-backed WAL: clean run, 2026-10-13

What was run: `SqliteFileWriterBenchmarks`, `SqliteFileWriterStormBenchmarks`, `SqliteFileWriterPoisonBenchmarks` and
`EqualFootingCrudBenchmarks` (shared-cache memory database), net10.0 then net8.0, `dotnet run -c Release --no-build`,
15:21 to 16:04 UTC. Poison was re-run afterwards (its harness counted BenchmarkDotNet's JIT invocation twice; fixed).

- **Library:** `5887f286` plus the uncommitted 2026-10-13 SQLite change (session settings `foreign_keys = ON`, `query_only = OFF`,
  `synchronous = NORMAL` once the database is in WAL; `PRAGMA optimize` at startup).
- **Sources frozen** for the whole run (BenchmarkDotNet builds each class from source when it starts; an earlier run was discarded
  because the library changed while it ran).
- **Machine:** AMD Ryzen 9 5950X 16-Core Processor, 8 logical cores, Linux, ZFS on `/home` (the file database lived in `~/.cache/pengdows-bench-db`; `/tmp` is tmpfs and was not used).
- **Microsoft.Data.Sqlite 8.0.11** (the benchmarks project), bundled e_sqlite3; pooling on (the default).
- **Equal work:** every arm runs the same session settings (foreign keys on, query_only off, synchronous NORMAL): pengdows.crud on every
  checkout, per-operation Dapper on every open (one command), pinned, single-connection and queue arms once.
- No retry-on-exception arm: it recovers from contention, it does not prevent it.

## Cost (sequential; every arm succeeds, so time is comparable) - mean microseconds, net10.0 / net8.0

File database, WAL, `RecordCount=1`:

| | Dapper per-op | Dapper pinned / single | Dapper writer queue | pengdows.crud | pengdows / per-op |
|---|---:|---:|---:|---:|---:|
| write | 10.8 / 11.5 | 6.4 / 6.4 | 10.1 / 10.4 | 11.6 / 11.8 | 1.08 / 1.03 |
| read | 9.7 / 9.9 | 5.0 / 5.2 | | 10.9 / 11.5 | 1.12 / 1.17 |

`RecordCount=100` (100 calls, each its own commit): write 1088 / 1091, pinned 629 / 665, queue 858 / 919, pengdows 1130 / 1188;
read 945 / 996, single connection 499 / 528, pengdows 1070 / 1217. Allocation per call: per-op 2744 B, pinned 1587 B,
queue 1925 B, pengdows 2775 B.

Shared-cache memory database, no WAL, `RecordCount=1`, net10.0 / net8.0 (Dapper arms set `Foreign Keys=True`):

| operation | Dapper per-op | Dapper pinned | pengdows | pengdows / per-op | pengdows / pinned |
|---|---:|---:|---:|---:|---:|
| Create | 26.4 / 27.3 | 7.5 / 7.8 | 30.2 / 31.5 | 1.14 / 1.15 | 4.0 / 4.0 |
| Update | 24.8 / 24.9 | 6.3 / 6.8 | 26.1 / 26.9 | 1.05 / 1.08 | 4.1 / 4.0 |
| DeleteOnly | 26.6 / 26.8 | 6.8 / 6.9 | 33.7 / 33.7 | 1.26 / 1.26 | 5.0 / 4.9 |
| DeleteInsertCycle | 50.1 / 50.6 | 12.0 / 13.1 | 52.4 / 52.7 | 1.05 / 1.04 | 4.4 / 4.0 |
| ReadSingle | 25.6 / 25.1 | 6.7 / 7.2 | 28.5 / 29.2 | 1.12 / 1.16 | 4.3 / 4.1 |
| ReadList | 31.7 / 32.7 | 11.3 / 11.8 | 35.5 / 37.1 | 1.12 / 1.14 | 3.1 / 3.1 |
| FilteredQuery | 35.0 / 35.3 | 13.9 / 14.6 | 39.0 / 39.8 | 1.11 / 1.13 | 2.8 / 2.7 |
| Aggregate | 61.2 / 60.3 | 41.0 / 39.6 | 64.3 / 62.9 | 1.05 / 1.04 | 1.6 / 1.6 |

## Survival (100 writers x one transaction of 50 updates), file database, WAL

Fails = failed transactions. Whole-storm time in milliseconds, net10.0 / net8.0, busy_timeout 10 ms then 5000 ms
(`Default Timeout` 1 s then 6 s):

| arm | Fails | busy 10 | busy 5000 |
|---|---:|---:|---:|
| Dapper per-op | 0 | 441 / 373 | 460 / 621 |
| Dapper pinned writer | 0 | 72 / 72 | 71 / 73 |
| Dapper writer queue | 0 | 71 / 73 | 70 / 72 |
| pengdows.crud | 0 | 76 / 79 | 76 / 79 |
| pengdows.crud defaults | 0 | 76 / 79 | 76 / 80 |

No arm lost a write. The earlier failure counts (`sqlite-write-contention-run-2026-08-13.md`) came from a shared-cache in-memory
database; see the README, "What the cost buys".

## Writers and readers together (20 writers x 20 single-row commits, 16 readers), busy_timeout 10, net8.0 sidecar

| arm | write P50 / P95 / P99 / max (ms) | read P50 / P95 / P99 / max (ms) | reads completed |
|---|---|---|---:|
| Dapper per-op | 0.016 / 0.076 / 150 / 603 | 0.012 / 0.029 / 0.043 / 4.5 | 214,741 |
| Dapper pinned writer | 0.32 / 1.15 / 3.1 / 7.1 | 0.010 / 0.080 / 0.29 / 6.9 | 5,390 |
| Dapper single connection | 0.27 / 0.64 / 0.88 / 4.4 | 0.27 / 0.65 / 0.88 / 4.4 | 335 |
| Dapper writer queue | 0.21 / 1.15 / 3.1 / 4.3 | 0.011 / 0.072 / 0.28 / 9.2 | 2,263 |
| pengdows.crud | 0.45 / 1.29 / 2.1 / 39 | 0.46 / 1.30 / 2.1 / 66 | 337 |

Total time, net10.0 / net8.0: per-op 953 / 832, pinned 67 / 59, single 56 / 56, queue 64 / 57, pengdows 59 / 60. Allocation is not
comparable here: the readers loop until the writers finish, so a slower arm runs more reads.

## Writer's connection dies (fakeDb; 200 transactions, severed during the 100th)

| arm | Fails (net10.0 and net8.0) |
|---|---:|
| Dapper pinned writer, no recovery | 100 |
| Dapper pinned writer with reconnect | 1 |
| pengdows.crud | 1 |

## Caveats

- `synchronous = NORMAL` in WAL means a commit is a write to the OS, not a sync. Disk commit cost needs a `FULL` run; pengdows.crud has no setting for it yet (SQLITE-006).
- One machine, 10 iterations (storm and mixed 5), unpinned processes; the net8.0 and net10.0 columns differ by up to 10 to 25% in the storm and mixed rows.
- The latency sidecars are overwritten by each framework's pass; those above are net8.0's.
- The Dapper arms are this repository's own implementations of the patterns, not the libraries named.
