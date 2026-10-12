# A/B: pengdows.crud 2.0.5 vs 2.0.6, tiers 2 and 3 (real databases), 2026-10-11

Method: `benchmarks/run-main-vs-3.0.sh tier2 2` then `tier3 2`, `v2.0.5` (7ee63f14) against `HEAD` (2ec8e488), net10.0,
2 rounds, arms alternating order, same benchmark code on both (seven harness files that use APIs 2.0.5 lacks excluded from the
baseline arm only). AMD Ryzen 9 5950X, no builds during the run. Tier 2 = embedded (DuckDB, SQLite, pool protection);
tier 3 = containers (PostgreSQL, SQL Server). Summaries: `benchmarks/summarize-ab.py`.

## Tier 3 (PostgreSQL, SQL Server)

- **PostgreSQL:** no regression anywhere. pengdows time is within the round spread of 2.0.5 (-1.6% to +2.8%), and tracks the
  Dapper/EF controls; allocation is down 28% to 52% on every row (one list read at 100 rows -4.2% time, reproduced).
  The 4 to 7 ns per-parameter cost in `ab-2.0.5-vs-2.0.6-parameter-regressions-2026-10-11.md` is invisible next to a network round trip.
- **SQL Server:** 2.0.6 is 25% to 51% faster per operation (e.g. read single 437 to 224 us, create 686 to 470 us, aggregate
  519 to 310 us) with 28% to 44% less allocation, reproduced across rounds. The Dapper control for three parameter sets
  drifted over 3%, but the Dapper-normalized column agrees with the raw one. The likely source is the 2.0.6 session-settings
  change (settings skipped at compatibility level 90 and above, so fewer statements per new connection); this was not isolated
  by a separate run.
- Flags: none worse than noise. One EF row (`Aggregate_EntityFramework`, 1 row, +12.8%) is within-noise on the control.

## Tier 2 (embedded and pool protection)

- DuckDB: time +0.1% to +4.5% across the board, Dapper moves +0% to +4% in the same run, so the Dapper-normalized
  deltas are -1.4% to +2.5%. Allocation down 7% to 47% on every standard-mode row.
- DuckDB `*_SingleConnection` rows: allocation up 4% to 6% (+129 B for a one-row read, 2,353 to 2,482 B; +12.9 KB over 100
  single reads), time within noise. This is the one allocation regression in the A/B. It is on the single-connection path
  only and was not changed in this pass.
- Pool protection (`ConnectionPoolProtectionBenchmarks`): `ConnectionHoldTime`, `SustainedPressure` and `PoolExhaustion`
  pengdows rows -15% to -22% time and -48% to -49% allocation (reproduced).
- `WriteStorm_Pengdows` +18% median / +29% P99 on one benchmark with a round spread of 24.5%, flagged NOT-REPRO. SQLite
  `WriteStorm_PengdowsDefaults` BusyTimeoutMs=10 +34% flagged repro, but the same benchmark at 5000 ms is +12% and within
  noise, the other pengdows write-storm rows are +4% to +5% inside spreads of 35% to 50%, and allocation is -24%. A write storm
  is a contention measurement (milliseconds spent waiting for a file lock), not CPU cost; treated as noise, not a regression.
- `SqliteConcurrencyBenchmark`: Dapper moved -66% between arms in the same run (control unusable); pengdows -12%, EF -15%.

## Verdict

No time regression attributable to 2.0.6 on any real database. One small allocation regression on the DuckDB
single-connection path (+4% to +6%) remains.
