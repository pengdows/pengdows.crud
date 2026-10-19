# PostgreSQL mixed read/write storm with Dapper and EF arms, 2026-10-19

`PostgreSqlMixedGovernanceBenchmarks` at `039f8dfe`, net8.0, one machine (AMD Ryzen 9 5950X, 8 CPUs visible, Docker).
PostgreSQL 15 with `max_connections=25` (22 usable). Each batch is 1,500 calls that each hold their connection for
300 ms (`pg_sleep(0.3)`); `ReadPercent` of them read and the rest write. `Callers` is how many run at once: 50 (just
over the 22 usable connections) or 1,000. 9,000 calls per case (6 batches), each case in its own process.

Arms:

- **Dapper/EF uncontrolled**: Npgsql's default pool (100) with nothing in front of it, one pool for reads and writes.
- **Dapper/EF + StormGate**: the same, behind StormGate with 20 permits and a 30 s wait.
- **pengdows Governed**: 20 reader and 20 writer slots, no clamp, 30 s wait. Two pools that together can open 40
  connections against a server that allows 22.
- **pengdows Clamped**: the same sizes with `ClampPoolsToServerConnectionLimit` on. On 2.0.6 that divides the 22 usable
  connections between the roles in advance, 11 and 11; **pengdows Governed11** sets 11 and 11 by hand and measured the
  same in every cell (to within 0.02%), so it is left out of the tables.

Latency is per call, from its start to its end, so it includes queueing for a slot or a permit. Failures are timed
separately. Counts and latency come from each process's own `[MIX]` output (the sidecar files were written to a
directory BenchmarkDotNet deletes; fixed since, see below).

## Failed calls and time per batch

| Arm | 50% reads, 50 callers | 50% reads, 1,000 callers | 90% reads, 50 callers | 90% reads, 1,000 callers |
|---|---|---|---|---|
| Dapper uncontrolled | 96.8%, 0.66 s | 96.8%, 0.67 s | 96.8%, 0.74 s | 96.8%, 0.66 s |
| EF uncontrolled | 96.1%, 0.75 s | 95.0%, 1.05 s | 95.8%, 0.78 s | 95.9%, 0.90 s |
| Dapper + StormGate | **0**, 22.57 s | **0**, 22.57 s | **0**, 22.59 s | **0**, 22.59 s |
| EF + StormGate | **0**, 22.60 s | **0**, 22.60 s | **0**, 22.63 s | **0**, 22.63 s |
| pengdows Governed (20 + 20) | 46.0%, 11.44 s | 49.7%, 11.44 s | 6.0%, 20.47 s | 11.1%, 20.47 s |
| pengdows Clamped (11 + 11) | **0**, 21.06 s | **0**, 20.76 s | **0**, 37.02 s | **0**, 37.03 s |

The uncontrolled arms finish a batch in under a second because nearly every call fails at once.

Every failure is a server refusal of the connection: `PostgresException` (SQLSTATE 53300) for Dapper, and for EF
either `PostgresException` or an `InvalidOperationException` that EF raised over it (4,194 to 7,756 per case; not
investigated further), `TooManyConnectionsException` for pengdows Governed. No arm saw an acquire timeout.

## Latency of the calls that succeeded (P50 / P99)

| Arm | 50% reads, 50 callers | 50% reads, 1,000 callers | 90% reads, 50 callers | 90% reads, 1,000 callers |
|---|---|---|---|---|
| Dapper + StormGate | 0.60 / 0.90 s | 11.44 / 15.05 s | 0.60 / 0.90 s | 11.45 / 15.06 s |
| EF + StormGate | 0.61 / 0.91 s | 11.45 / 15.07 s | 0.61 / 0.91 s | 11.47 / 15.10 s |
| pengdows Clamped (11 + 11) | 0.60 / 1.20 s | 10.53 / 14.44 s | 1.20 / 1.51 s | 16.56 / 27.40 s |
| pengdows Governed (20 + 20) | 0.60 / 0.90 s | 5.72 / 11.26 s | 0.60 / 0.90 s | 9.93 / 15.06 s |
| Dapper uncontrolled (3% of calls) | 0.32 / 0.36 s | 0.39 / 0.71 s | 0.32 / 0.35 s | 0.40 / 0.76 s |

Time to failure, P50 / P99: pengdows Governed 4.2 / 13.2 ms (50 callers) and 122 / 214 ms (1,000 callers); Dapper
uncontrolled 8.9 / 23.7 ms and 278 / 554 ms.

## What it shows

1. **Without admission control the server refuses almost everything.** Dapper and EF on the provider's default pool lost
   95% to 97% of calls in every cell, at 50 callers as much as at 1,000: 50 callers already open more connections than
   the server allows.
2. **Admission control is what fixes it, wherever it lives.** StormGate in front of Dapper or EF, and pengdows with the
   clamp, completed every call in every cell. pengdows does it with no extra component; Dapper and EF need StormGate
   (or an equivalent) added.
3. **Over-subscribed pengdows pools are not protected.** 20 reader and 20 writer slots can open 40 connections against
   22, and the server refused 6% to 50% of calls. The governor bounds connections per role, not the sum: the clamp (or
   pool sizes that fit together) is what makes it safe. Same finding as 2026-10-11.
4. **The clamp's static split costs a read-heavy mix.** At 50% reads the clamp's 11 + 11 used all 22 connections and
   finished each batch about 7% sooner than StormGate's 20 permits (21.1 s against 22.6 s). At 90% reads the 11 reader
   slots were the bottleneck: 37.0 s against 22.6 s, and at 1,000 callers a P99 of 27.4 s against 15.1 s. This is the
   cost POOL-003 is meant to remove; a context that never writes avoids it with `MaxConcurrentWrites = 0`.
5. **Pool exhaustion still hurts the host.** Uncontrolled cases left up to 14,072 sockets in TIME_WAIT; the new drain
   step waited for the count to fall below 2,000 before the next case started its container, which the 2026-10-11 run
   could not do.

## Harness notes

- The sidecar file was written relative to the case's working directory, which BenchmarkDotNet deletes; it now uses
  `CRUD_BENCH_ARTIFACTS_DIR` like the other sidecar writers, and `BenchmarkFairnessTests` checks every writer does.
- `TimeWaitDrain` waited before every case; the largest backlog was 14,072 sockets, drained to 2 (it stops waiting
  below 2,000).
