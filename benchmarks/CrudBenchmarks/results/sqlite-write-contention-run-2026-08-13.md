> **Correction (2026-08-24):** two claims below are not supported and should not be relied on.
> 1. "Fails = 0" for Dapper/EF is **not verified proof of zero correctness issues.** The
>    correctness artifact `CountFailures` reads from silently returned `0` whenever the artifact
>    file itself was missing — indistinguishable from a genuinely verified zero. This run's own
>    "Note on artifact durability" below confirms the artifact file did not survive
>    BenchmarkDotNet's cleanup, so "Fails: 0" here means "unknown," not "verified." Fixed in
>    `BenchmarkCorrectnessArtifacts.CountFailures` (now returns `int?`, `null` for missing/
>    unreadable artifacts) and the artifacts-directory durability bug that caused the loss (see
>    `BenchmarkCorrectnessArtifactsTests` and the `CRUD_BENCH_ARTIFACTS_DIR` fix in `Program.cs`).
> 2. "Dapper and EF each hit hundreds of ... exceptions ... (caught/retried by the benchmark
>    harness, at real latency cost)" **misdescribes the code.** `WriteStorm_Dapper` and
>    `WriteStorm_EntityFramework` each catch the exception once, record it via `MarkInvalid`, and
>    abandon that transaction — there is no retry loop anywhere in this file. A caught exception
>    means that transaction's 50 writes were never committed, not "eventually applied." A future
>    run with the fixed, durable artifact plus the new Attempted/Committed transaction counters
>    (see `WriteLatencySidecar`'s "Attempted vs. Committed Transactions" table) will show the real
>    Dapper/EF lost-transaction count directly instead of this being inferred from prose.
>
> The core thesis result (pengdows's `SingleWriter` governor throws zero exceptions under this
> workload while Dapper/EF throw hundreds) is unaffected by this correction — only the
> "everything still got applied anyway" characterization is retracted.

## SQLite Write Contention Benchmark — 2026-08-13

`SQLiteWriteContentionBenchmarks` proves two thesis points directly:

- EF Core and Dapper don't protect the connection pool under heavy write contention — with
  SQLite `busy_timeout=10ms`, concurrent writers throw `SQLITE_BUSY`/"database is locked"
  exceptions.
- pengdows.crud degrades safely under the same contention: the `SingleWriter` governor
  serializes writers so no exceptions are thrown, while still completing all writes
  correctly.

Design: **100 concurrent writers × 50 writes per transaction**, `busy_timeout=10ms`, all
three frameworks operating against the same shared-cache in-memory SQLite database.

### Result

| Method | Mean | P95 | P99 | Fails | Exceptions during run | Allocated |
|--------|-----:|----:|----:|------:|----------------------:|----------:|
| WriteStorm_Pengdows | **74.99 ms** | 108.2 ms | 112.17 ms | 0 | **0** | 13.25 MB |
| WriteStorm_Dapper | 1,054.46 ms | 1,054.7 ms | 1,054.77 ms | 0 | **268** | 3.05 MB |
| WriteStorm_EntityFramework | 1,055.40 ms | 1,055.5 ms | 1,055.55 ms | 0 | **348** | 10.39 MB |

**This is a correctness/resilience result, not a raw-speed result.** "Fails = 0" for all
three means every framework's writes were eventually, correctness-wise, all applied — none
of them lost data. The difference is *how* they got there: Dapper and EF each hit hundreds
of "database is locked" exceptions along the way (caught/retried by the benchmark harness,
at real latency cost), while pengdows threw zero. The `SingleWriter` governor serializes
write *tasks* at the application layer before they ever reach SQLite's own lock, so the
contention that produces `SQLITE_BUSY` in the other two frameworks never occurs for
pengdows in the first place.

The `P÷D` = **0.071** (`EF÷P` = **14.074**) time ratio is downstream of that, not an
independent claim that pengdows executes faster in general: under this specific hostile,
valid concurrency workload, pengdows spends no time on contention retries, while Dapper and
EF spend most of their ~1,055 ms paying for exactly that. Don't cite this as "pengdows is
~14x faster than Dapper" in isolation — it's "pengdows avoids the retry storm that
contention otherwise causes," and the 14x is what avoiding it happens to cost the other two
frameworks here.

### pengdows governor behavior during the storm

From the run's `PoolGovernor` snapshot (writer role):

| Metric | Value |
|--------|------:|
| Max Slots | 1 |
| Peak In-Use | 1 |
| Peak Turnstile Queued | 99 |
| Total Acquired | 901 |
| Avg Wait | 58.172 ms |
| Avg Hold | 1.145 ms |
| Slot Timeouts | 0 |
| Turnstile Timeouts | 0 |
| Canceled Waits | 0 |

"Peak Turnstile Queued = 99" confirms the expected shape: 1 writer holds the single write
slot, the other 99 queue at the fairness turnstile rather than contending for the SQLite
file lock directly — exactly the mechanism principle 5 of `docs/positioning/product-thesis.md`
describes.

### Note on artifact durability

The benchmark's own custom writers (`BenchmarkCorrectnessArtifacts`, the per-transaction
`*-tx-latency.md` P50/P95/P99/Max writer) write to a path relative to the benchmark
process's working directory, which is BenchmarkDotNet's isolated per-run build directory —
that directory (and the custom artifacts inside it) gets deleted by BenchmarkDotNet's own
"Artifacts cleanup" step after each run. Neither file was recoverable after this run
completed; the numbers above come from the console log BenchmarkDotNet itself preserves,
which was sufficient for this write-up but does not include the finer per-transaction
P50/P95/P99/Max breakdown the tx-latency writer was designed to produce. Worth fixing in
the benchmark itself if that breakdown is wanted from a future run (write to an
absolute/repo-relative path instead of a relative one).

---

### Raw BDN Output

```
BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 5950X, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.110
  [Host]     : .NET 8.0.29 (8.0.2926.32403), X64 RyuJIT AVX2
  Job-CZPUZH : .NET 8.0.29 (8.0.2926.32403), X64 RyuJIT AVX2

IterationCount=5  WarmupCount=1
```

| Method | Mean | Error | StdDev | P95 | P99 | Fails | Allocated |
|--------|-----:|------:|-------:|----:|----:|------:|----------:|
| WriteStorm_Pengdows | 74.99 ms | 186.878 ms | 28.920 ms | 108.2 ms | 112.17 | 0 | 13.25 MB |
| WriteStorm_Dapper | 1,054.46 ms | 1.430 ms | 0.221 ms | 1,054.7 ms | 1,054.77 | 0 | 3.05 MB |
| WriteStorm_EntityFramework | 1,055.40 ms | 0.922 ms | 0.143 ms | 1,055.5 ms | 1,055.55 | 0 | 10.39 MB |

Total run time: 280.41 sec, 3 benchmarks executed.

Note on pengdows's high StdDev/Error (28.9 ms / 186.9 ms): pengdows's four post-warmup
iterations ranged 46.7–113.2 ms, a real warmup/JIT/OS-scheduling effect on a benchmark this
short and this concurrency-heavy — not noise indicating an unreliable governor. All four
iterations still landed an order of magnitude below Dapper/EF's ~1,055 ms with zero
exceptions in every iteration.

---

## 2026-08-27 investigation — why Dapper/EF converge on ~1,055 ms, and why "Fails" isn't stable

### Why this investigation started

A fresh full-suite re-run on 2026-08-27 reproduced the same shape (pengdows 105.8 ms mean vs.
Dapper/EF ~1,054-1,055 ms) but with a different `Fails` outcome than this file's original
number: this run's own correctness artifact recorded 0 Dapper failures and 456 EntityFramework
failures (`SQLiteWriteContentionBenchmarks-correctness.json`), not the 268/348 split above.
Since the setup is identical to the original run, that alone establishes something worth
documenting explicitly: **the exception count is not a stable, deterministic property of this
workload** — it varies run to run even though the ~1,055 ms mean for Dapper/EF does not. That
asymmetry (latency stable, failure count not) is itself informative and is why the P99/mean gap
— which reproduces every time — is the more defensible number to lead with, not the exception
count.

### The mechanism, confirmed from source, not inferred

`Microsoft.Data.Sqlite`'s `SqliteDataReader.NextResult()` (`dotnet/efcore` repo) retries a
busy/locked statement like this:

```csharp
while (IsBusy(rc = sqlite3_step(stmt)))
{
    if (_command.CommandTimeout != 0
        && (_totalElapsedTime + timer.Elapsed).TotalMilliseconds
           >= _command.CommandTimeout * 1000L)
    {
        break;
    }

    sqlite3_reset(stmt);
    Thread.Sleep(150);
}
```

This benchmark sets `DefaultTimeout=1` (1 second) on the connection string. A maximally
contended statement therefore retries roughly 6-7 times (~900-1,050 ms of `Thread.Sleep`)
before either squeezing through or giving up — which is why Dapper and EF's mean lands so
consistently near 1,055 ms across independent runs: it isn't organic contention timing, it's a
near-deterministic retry budget being nearly fully consumed. Two consequences worth stating
plainly:

1. **`Thread.Sleep(150)` is a real blocking sleep**, reached from `await`-ed code, because
   Microsoft.Data.Sqlite's own docs state its async methods run synchronously. With 100
   concurrent writers, a contended statement doesn't just wait on SQLite's lock — it parks a
   real .NET thread-pool worker thread for up to ~900 ms doing nothing. That's a second,
   independent cost (thread-pool pressure) on top of raw lock-wait time, and it's specific to
   the naive/raw-ADO.NET path — pengdows's writers never reach this retry loop at all, because
   the `SingleWriter` governor means a pengdows writer never contends for SQLite's lock with
   another pengdows writer in the first place.
2. Whether a given writer's retry loop finishes *just inside* the 1-second window (success) or
   *just outside* it (a caught `SqliteException`, recorded as a failure) plausibly comes down to
   thread-pool scheduling variance under load — which would explain why the failure count
   differs run to run while the mean/P99 latency does not.

### Instrumentation added to test this directly, and a real bug found while adding it

Added per-transaction success/failure latency tracking to the Dapper and EF paths (previously
only pengdows recorded this), bucketed into a 150 ms histogram — if the mechanism above is
right, Dapper/EF latencies should cluster near multiples of 150 ms rather than spread smoothly.
Also added `ThreadPool.GetAvailableThreads()` sampling during the storm as independent evidence
of thread-pool pressure, separate from the latency shape.

While wiring this up, found and fixed a real bug in the benchmark's own existing
Attempted/Committed instrumentation (separate from the artifact-durability issue already
documented above): each `[Benchmark]` method in this class runs in its own BenchmarkDotNet-
spawned process with a fresh instance, so `_attemptedTransactions`/`_committedTransactions`
only ever have one framework's data in any given process. The original code wrote a single
shared filename with `File.WriteAllText`, so whichever process's `Cleanup()` ran (or completed)
last silently overwrote the others — this run's leftover file showed `Pengdows: 800/800` next
to `Dapper: 0/0` and `EntityFramework: 0/0`, which is not "zero transactions attempted," it's
"this framework's data was never the last write, or its process didn't complete `Cleanup()`."
Fixed by writing one file per framework
(`SQLiteWriteContentionBenchmarks-{Framework}-tx-latency.md`) instead of one shared file, so a
framework's absence from the results is now visible as a missing file rather than a misleading
zero.

### Empirical result

The instrumented re-run confirms the mechanism for Dapper with remarkable precision, refutes
the thread-pool-starvation half of the hypothesis, and shows EF Core has a second, additional
timeout layer beyond Microsoft.Data.Sqlite's own.

**Dapper's failed transactions cluster in an ~2ms-wide band around 1,050 ms** (448 samples:
P50 1051.15 ms, P99 1052.85 ms, Max 1053.30 ms) — essentially a single spike, exactly where
`Thread.Sleep(150)` × 7 retries (1,050 ms) crossing the 1,000 ms `CommandTimeout` predicts.
Dapper's *committed* transactions (352 samples) spread almost uniformly across every 150 ms
bucket from 0 to 1,050 ms — the "race to get the lock, however many retries it takes and
happens to succeed" shape.

**EF Core's failures split into two clusters**: 279 at ~1,050 ms (same as Dapper) and 73 at
~1,500 ms, with a matching small tail in its committed transactions out to 1,500 ms. That's a
second retry/timeout mechanism specific to EF Core layered on top of the driver's own — not
investigated further here, but real and worth knowing if anyone chases EF-specific latency
tuning later.

**Thread-pool starvation is refuted, not confirmed.** Available worker threads dropped from
32,767 to a minimum of 32,667 during the storm — a drop of exactly 100, one thread per
concurrent writer, nothing more. `Thread.Sleep(150)` is real and blocking, but this machine's
thread pool has far more headroom than 100 blocked threads can dent. The latency cost is real;
it is not a starvation cascade.

**Failure count is genuinely unstable, latency is not.** This run: Dapper lost 448/800
attempted transactions, EF lost 352/800 — the *opposite* ranking from the original
2026-08-13 run (268 Dapper / 348 EF exceptions). Don't cite "Dapper fails more than EF" or
vice versa as a stable claim; cite the ~1,050 ms retry-wall latency instead, which reproduces
every time.

Full histograms: `SQLiteWriteContentionBenchmarks-Dapper-tx-latency.md` and
`SQLiteWriteContentionBenchmarks-EntityFramework-tx-latency.md` in
`BenchmarkDotNet.Artifacts/results/`.

### The more authoritative pengdows-side number: the governor's own telemetry

BenchmarkDotNet's `WriteStorm_Pengdows` mean is noisy by nature (only 5 post-warmup
iterations, with real warmup/JIT/OS-scheduling swings — see the note on StdDev above and in
the original write-up). `pengdows.crud`'s own `PoolGovernor` telemetry, captured via
`IDatabaseContext.Metrics` across every single write-slot acquisition during the run (not
just 5 outer samples), gives a far larger and steadier picture of the same benchmark
(`SQLiteWriteContentionBenchmarks-pengdows-metrics.md`, writer role, 2026-08-27 run):

| Metric | Value |
|---|---|
| Total write-slot acquisitions | 901 |
| Peak turnstile queued | 99 |
| Avg wait (queued for the write slot) | 51.866 ms |
| Avg hold (executing the 50 writes once granted) | 1.047 ms |
| Commands executed | 40,901 |
| Avg command latency | 0.005 ms |
| Slot timeouts / turnstile timeouts / canceled waits | 0 / 0 / 0 |

This is the more defensible number to cite for pengdows's own side of this benchmark: the
actual database work is trivial (5 μs/command, ~1 ms to run all 50 writes once holding the
slot); essentially all of a pengdows writer's time under 100-way contention is spent waiting
in an orderly, zero-timeout queue, averaging 52 ms. That is a steadier and more precise claim
than "BenchmarkDotNet mean ≈ 75-107 ms, high variance" — the variance lives in the outer
process/JIT/OS layer that BenchmarkDotNet measures, not in the governor's own behavior, which
this telemetry shows is consistent. There is no equivalent internal-telemetry number for
Dapper/EF, since neither ever touches an `IDatabaseContext` — their side of the comparison
rests on the histogram evidence above instead.

## 2026-08-27 follow-up — the correctness-artifact fix surfaced a bigger problem: three different answers for the same benchmark

Fixing the two artifact bugs above (documented in the section below this one) produced a
`Fails` column that finally matched independently-tracked ground truth exactly. That did
**not** settle what the "real number" is — it revealed there isn't a single stable one:

| Run | Dapper lost/failed | EF lost/failed | Measurement path |
|---|---:|---:|---|
| 2026-08-13 | 268 | 348 | Scraped from BenchmarkDotNet's preserved console log (the correctness artifact itself was lost — see "Note on artifact durability" above) |
| 2026-08-27, before the fix | 0 | 0 | `CorrectnessColumn`, silently broken by the parameter-key mismatch bug — this row is not evidence of anything |
| 2026-08-27, after the fix | 496 | 409 | `CorrectnessColumn`, cross-checked byte-for-byte against the independently-coded tx-latency Attempted/Committed counters |

The first and third rows were measured through genuinely different code paths (console-log
scraping vs. a structured, cross-verified artifact) and cannot be assumed comparable —
either could be closer to the "true" rate for this exact configuration, and the honest
position is that neither run's specific percentage should be treated as a fixed property of
Dapper or EF. What both rows agree on: pengdows lost zero, and the competitors lost a large,
double-digit-percent share of attempted transactions, in both measurements. **Loss rate
varies (33-62% observed so far); zero has not varied across any run on record.** That's the
number that belongs in citable, load-bearing claims — see the reframed section in
`docs/positioning/product-thesis.md`.

## 2026-08-27 follow-up — the `busy_timeout=10ms` objection, answered with a paired run

The obvious response to the numbers above is "you configured a system to fail — set a sane
`busy_timeout` and the comparison disappears." Rather than argue the point, this was tested
directly: the same benchmark, same 800-attempted-transaction workload, run a second time
with `BusyTimeoutMs` temporarily changed from 10 to 5000 (and `DefaultTimeout`/
`CommandTimeout` scaled from 1s to 6s to match — Microsoft.Data.Sqlite's own retry loop is
bounded by `CommandTimeout`, not the `busy_timeout` PRAGMA, so raising one without the other
would not actually test anything). This was a manual, one-off change to a hardcoded
constant, reverted immediately after capturing the results below — `BusyTimeoutMs` is not
yet a permanent `[Params]` fixture in this benchmark class, which is a real gap worth
closing so this comparison doesn't require hand-editing source to reproduce.

| `busy_timeout` / `CommandTimeout` | pengdows | Dapper | EF Core |
|---|---:|---:|---:|
| 10ms / 1s | 0 lost, 106.6 ms mean | 496 lost (62%), 1,055 ms mean | 409 lost (51%), 1,055 ms mean |
| 5000ms / 6s | 0 lost, 106.6 ms mean | 4 lost (0.5%), 4,272 ms mean | 57 lost (7%), 3,885 ms mean |

(Fails column cross-checked against the independent tx-latency files at 5000ms too: Dapper
4/4, EF 57/57 — the fix holds under a second configuration, not just the one it was found
under.)

pengdows's row is identical across both settings, within measurement noise — 106.6 ms
either way, zero lost either way. Dapper and EF are not: raising the timeout traded most of
their failures for a ~4x latency increase (1,055 ms → 3,885-4,272 ms), and even six seconds
of patience wasn't enough to reach zero failures for either one. This is the pair worth
citing together, not the 10ms row alone: at the hostile setting, competitors throw; at the
sane setting, they mostly complete but pay a real latency tax and still aren't fully
reliable; pengdows is unaffected by the setting either way, because `SingleWriter`'s
admission control means its writers never depend on `busy_timeout` resolving contention in
the first place — there is no contention for it to resolve.
