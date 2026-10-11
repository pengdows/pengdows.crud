# PostgreSQL connection-governance storm, 2026-10-11

`PostgreSqlConnectionGovernanceBenchmarks`: server `max_connections=25` (22 usable), 1,000-way concurrency, 5,000
operations per batch at 0 ms and 1,500 at 300 ms (calls hold their connection for 300 ms through `pg_sleep`). Net10.0 and
net8.0 gave identical failure counts; per-case counts below come from each benchmark process's own sidecar file
(`results/PostgreSqlConnectionGovernanceBenchmarks-<pid>-latency.md`, pids from the `[GOV] process` log lines). The table's
`Fails` column sums every correctness fragment in a shared folder and picked up stray fragments from killed and
overlapping runs (it once showed 148,273 failures out of 130,000 attempts), so it is not authoritative.

## Failed operations per arm

| Arm | 0 ms (of 130,000) | 300 ms (of 39,000) | 300 ms batch time |
|---|---|---|---|
| Dapper, uncontrolled | 74,577 to 88,956 (57% to 68%) | 37,392 to 37,416 (95.9%) | ~0.9 s |
| EF, uncontrolled | 86,956 to 93,867 (67% to 72%) | 37,401 to 37,475 (95.9%) | ~0.9 s |
| Dapper + StormGate | 0 | 0 | 22.58 s |
| EF + StormGate | 0 (one run: 2,910, not repeated) | 0 | 22.6 s |
| pengdows, ceiling 20, default 5 s wait | 0 | 21,320 (54.67%) | 10.24 s |
| pengdows, clamp, default 5 s wait | 0 | 29,276 (75.07%) | 10.23 s |
| pengdows, clamp, 30 s wait | 0 | 0 | 41.24 s |
| pengdows, ceiling 20, **30 s wait, no clamp** | 0 | 0 | 22.57 s |
| pengdows, **11 per role by hand, no clamp** (control) | 0 | 29,276 (75.07%) | 10.24 s |
| pengdows, clamp, **writes forbidden**, 30 s wait | 0 | 0 | 22.58 s |

Every pengdows failure is a `PoolSaturatedException` (confirmed from the correctness fragments: 21,320 and 29,276).

## What the arms show

- The pengdows governor and StormGate behave the same when configured the same: 20 slots and a 30 s wait, 22.57 s against
  22.58 s, 0 failures. The gap between them in earlier tables was the 5 s default wait.
- The clamp is exactly its split: 11 per role by hand with no clamp fails the same 29,276 calls as the clamp.
- The split costs a one-sided workload half its capacity (41 s against 22.6 s even with patience), and a context that
  declares it never writes keeps the full reader budget.
- Failure count versus slots is arithmetic: a batch serves `2 x 17 x slots` calls inside two 5 s windows
  (680 with 20 slots, 374 with 11), which reproduces 820 and 1,126 failures per batch exactly.

## Harness findings from this run

- Each case starts its own PostgreSQL container, and a case that follows an uncontrolled storm can fail in setup with
  `Connection reset by peer` (`Dapper_StormGate` at 0 ms failed on every pass). The server in those containers was healthy
  and never saw the connection, so the reset is upstream of PostgreSQL: the storm's sockets in TIME_WAIT fill the
  ephemeral port range that Docker's proxy also needs. The Testcontainers cleanup container (Ryuk) hit the same range and
  was disabled for these runs (`TESTCONTAINERS_RYUK_DISABLED=true`), so a setup that throws leaks its container.
- `BenchmarkHostPort` publishes the PostgreSQL container below the ephemeral range, with a random first candidate.
- Open items: dispose the container when setup throws, wait for TIME_WAIT to drain between cases, and consider connecting
  to the container address directly instead of through the proxy.
