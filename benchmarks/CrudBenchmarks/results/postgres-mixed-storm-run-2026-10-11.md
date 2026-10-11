# PostgreSQL mixed read/write storm, 2026-10-11

> The "Clamped" rows below ran the shared per-server budget of POOL-003 phase 1, on the local branch
> `pool-003-shared-budget`. 2.0.6 keeps the static division (the `Governed11` rows), which is why phase 1 was withdrawn.
> On 2.0.6 (commit `9992c4f9`, same benchmark, net10.0) the `Clamped` arm is that static division and was re-run: **0 failed
> of 9,000 at both mixes, 20.77 s at 50% reads and 37.03 s at 90%**, identical to `Governed11` (20.77 s and 37.03 s).

`PostgreSqlMixedGovernanceBenchmarks` (new), against `cdf00222` (POOL-003 phase 1), net10.0: PostgreSQL 15 with
`max_connections=25` (22 usable), 1,000-way concurrency, 1,500 calls per batch that each hold their connection for 300 ms
(`pg_sleep(0.3)`). `ReadPercent` of the calls read through the reader pool and the rest write through the writer pool.
Every arm waits 30 s for a slot, as StormGate does, so a failure here is not the 5 s default wait. Each arm ran in its own
process; the failure counts are the benchmark's own per-process `[MIX]` log lines, 9,000 attempts each (6 batches), and the
times are BenchmarkDotNet's mean per batch.

| ReadPercent | Arm | Sizes | Failed | Batch time |
|---:|---|---|---:|---:|
| 50 | Governed, no clamp | 20 / 20, provider pools 20 / 20 | 5,182 (57.6%) | 11.44 s |
| 50 | Governed11, no clamp (the old static division by hand) | 11 / 11 | **0** | 20.77 s |
| 50 | Clamped (shared per-server budget of 22) | 20 / 20, provider pools 20 / 20 | 4,059 (45.1%) | 12.11 s |
| 90 | Governed, no clamp | 20 / 20 | 1,305 (14.5%) | 20.47 s |
| 90 | Governed11, no clamp | 11 / 11 | **0** | 37.03 s |
| 90 | Clamped (shared per-server budget of 22) | 20 / 20 | 700 (7.8%) | 20.79 s |

Every failure in every arm is a `TooManyConnectionsException`: the server refused the connection (SQLSTATE 53300), not a
timeout and not a `PoolSaturatedException`.

## What it shows

1. **The shared budget alone does not keep the server from refusing connections.** The clamped arm, which bounds the
   connections in use at 22, still has 45% refusals at 50% reads and 7.8% at 90%. Fewer than the unclamped arm, not zero.
2. **The static division did keep the server from refusing: 0 failures at both mixes.** The two provider pools were
   capped at 11 each, so their physical connections, idle or not, could never total more than 22.
3. **The trade is exactly speed against safety.** At 90% reads the shared budget runs the batch in 20.8 s against 37.0 s,
   the gain the design was for, and the static division is the only arm with no refusals.
4. **The cause is the gap already recorded as unsolved:** a permit count bounds connections in use, but each provider pool
   keeps its own idle connections open. After a burst on one role the other role's pool still holds idle connections while
   this role opens new ones, and the server sees the sum (up to 20 + 20 = 40 against 25). The red integration test
   `ReadBurstThenWriteBurst_NeverExceedsTheServersConnectionCeiling` is the same failure at small scale.

## Consequence for POOL-003 phase 1

Phase 1 replaced the static division with per-role sizes plus the shared budget. Measured here, that is faster on a
skewed mix and unsafe on any mix: it trades away the property the clamp exists for. It needs the idle-reclaim step (or
the reactive form: on a refusal, clear the other role's idle connections and retry) before it can replace the division.
Not part of this run: Dapper and EF arms in the mixed shape.
