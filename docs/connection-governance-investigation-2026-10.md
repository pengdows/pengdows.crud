# Connection governance investigation — October 2026

A record of one long working session (2026-10-08, branch `2.0.6`) that started with "did my benchmark run
complete?" and turned into an investigation of what `PoolGovernor` and `StormGate` actually buy you over a
correctly sized provider pool. Written so that it can be picked up cold.

**Read this first:** nothing here is committed. Everything is in the working tree of `pengdows.crud` and
`pengdows.hangfire` on branch `2.0.6` (see [Section 8](#8-state-of-the-working-tree)). Numbers are from
single runs unless a section says otherwise. Anything marked **unverified** was not confirmed against a
primary source or a live server.

---

## 1. The question

The user's thesis, from past production experience: connection storms bring down apps and servers,
and `PoolGovernor` (inside `DatabaseContext`) and its smaller sibling `pengdows.stormgate` exist for that
reason. The goal of the session became *proving that value with benchmarks*, honestly — including finding
out where the claim does not hold.

Where the session ended: the claim holds in a narrower, more precise form than the original thesis
(Section 5).

---

## 2. The starting benchmark run

`benchmarks/CrudBenchmarks`, run 2026-10-07 19:16 → 2026-10-08 ~01:00, 1,200 benchmarks, 5 h 43 m,
exit clean. Log: `bin/Release/net10.0/BenchmarkDotNet.Artifacts/BenchmarkRun-20261007-191644.log`.

Cross-framework time ratios (`P÷D` = pengdows ÷ Dapper, `EF÷P` = EF ÷ pengdows; lower P÷D is better):

| Suite | P÷D (median, range) | EF÷P (median) | Note |
|---|---|---|---|
| PostgreSQL equal-footing | 1.007 (0.968–1.082) | 1.36 | parity with Dapper |
| SQL Server equal-footing | 1.016 (0.939–1.073) | 1.47 | parity |
| DuckDB equal-footing | ~1.00–1.06 | — | no EF arm |
| DuckDB reads | ~1.65–1.75 | — | constant ~150 µs/call overhead; unexplained |
| EqualFootingCrud (database not identified; tens of µs/op) | 1.19 (0.90–1.44) | 3.09 | fixed per-call overhead shows when the DB is nearly free |
| Hydration, no server | ~0.62–0.67 | — | pengdows faster at row mapping |
| SQL Server hydration | ~1.0–1.1 | — | noisy |
| ConnectionPoolProtection | ~1.7–2.1 | ~2.5 | see Section 3, item 1 |

Allocation is about 1.3–1.5× Dapper's and about 10–14× lower than EF's wherever measured.

The ratio tables **exclude rows with failures**. That hid the most important result (next section).

---

## 3. Corrections made along the way (the honesty ledger)

Several early statements were wrong. They are listed because the same mistakes are easy to repeat.

1. **"Pool protection is a 2× slowdown with nothing offsetting it" — wrong.** The `WriteStorm` scenario in
   `ConnectionPoolProtectionBenchmarks` (100 concurrent SQLite writers) failed for Dapper (2,417
   `SqliteException`) and EF (1,956) and succeeded for pengdows (0). Failed rows are dropped from the ratio
   tables, so I never saw it.
2. **"StormGate adds nothing" — wrong, and retracted.** I said it from runs where the pools had already been
   capped below the server limit, which removes the failure StormGate exists to prevent. With default pools
   StormGate was the only thing that kept the app and host alive (Section 4.2).
3. **"Governors are per process" — wrong.** They are per `DatabaseContext`. Two contexts on one connection
   string run independent governors; `UniqueConnectionStringRegistry` only warns (or throws if
   `EnforceUniqueConnectionString`). Reader and writer pools inside one context are separate ADO.NET pools.
4. **Predicted the default-config SQLite arm would fail on the write-queue cap — wrong on 2.0.6.** The
   bounded-by-default queue (`max(slots×8, 32)`) is 3.0 behavior; 2.0.x leaves the queue unbounded unless
   `MaxQueuedReads/Writes` is set. The benchmark's comment about the default of 32 is 3.0-only.
5. **`MySqlDefaultConcurrencyBenchmarks` is not a Dapper/EF comparison.** It has three pengdows-only methods
   and sizes `max_connections` to 3× parallelism so the server is never the limit. The repo comment calling it
   "a real server-engine confirmation" is unsupported.
6. **Oracle formula:** I first cited `SESSIONS = (1.1 × PROCESSES) + 5` (9i–11g). The 19c docs say
   `(1.5 × PROCESSES) + 22`.
7. **"Informix Developer Edition = 25"** is not documented. The documented 25 is *SAP ASE* Developer Edition.
8. **The replica bug (found by the user asking):** the first clamp probed once and applied that one number to
   both reader and writer, so a read replica inherited the primary's limit (in the dangerous direction too).
   Fixed — Section 7.
9. **The two "failing" allocation unit tests were Debug-only artifacts,** not regressions (Section 7.4).
10. **A benchmark filter gotcha produced a mislabelled row:** `Pengdows_Clamped*` also matches
    `Pengdows_ClampedHeadroom`. Use exact names when arms share a prefix.

---

## 4. Experiments and results

### 4.1 SQLite write storm (the strong, proven result)

100 concurrent writers × 50-update transaction, shared-cache in-memory SQLite, no retry on any arm.
`BusyTimeoutMs` is now a `[Params]` axis (10 and 5,000) and a default-config pengdows arm was added.

| | busy_timeout 10 ms | busy_timeout 5,000 ms |
|---|---|---|
| pengdows (benchmark config) | 0 lost, 139 ms | 0 lost, 137 ms |
| pengdows (defaults) | 0 lost, 133 ms | 0 lost, 141 ms |
| Dapper | 408 lost (51%), 1,063 ms | 0 lost, 3,553 ms |
| EF Core | 393 lost (49%), 1,062 ms | 58 lost (7%), 3,462 ms |

Mechanism (from earlier investigation recorded in `benchmarks/CrudBenchmarks/results/sqlite-write-contention-run-2026-08-13.md`):
`Microsoft.Data.Sqlite` retries a busy statement with `Thread.Sleep(150)` bounded by `CommandTimeout`, so
Dapper/EF converge on ~1,055 ms at a 1 s timeout. `SingleWriter` serializes admission, so pengdows never races
for the file lock.

Caveats a skeptic will raise (unaddressed): shared-cache in-memory is not how production SQLite runs (file +
WAL); no retry arm for Dapper/EF; exception counts are unstable run to run (268, 0, 496, 2,417 seen) — lead
with lost transactions and latency, not exception counts. A WAL-file variant and retry arms were deferred.

### 4.2 PostgreSQL connection storm (`max_connections=25`)

**Setup:** 1,000-way, 5,000 ops/batch, default pools unless stated, ~130,000 attempts/arm.

| Configuration | Dapper | EF | pengdows default | pengdows clamp on | StormGate arms |
|---|---|---|---|---|---|
| Default pools (100 > 25) | 69% fail | 65% fail | 71–72% fail | **0 (153 ms)** | **0** (Dapper 171 ms, EF 453 ms) |
| Pool capped at 20, no gate | 0 | 0 | 0 (at pool 20) | — | — |
| Pool capped at 25 (= server limit; 200-op/100-way run) | 78/5,200 | 2/5,200 | 34/5,200 | — | — |

Findings:

- **The failures come from a pool larger than the server limit.** At pool 20 nobody fails, even ungoverned.
  "Trusting the pool isn't safe" is true only when the pool is not sized below the server.
- **pengdows is not protected by default.** The governor sizes from the connection string/config (dialect
  default 100), not from the server, so at defaults it fails like the others.
- **A pool equal to the server limit still leaves residual failures** (anything else holding a slot).
- **The ungoverned arms break the host:** ~10,700 sockets in `TIME_WAIT` after one ungoverned Dapper arm
  (31,701 seen during the pengdows default arm) made Docker unable to bind ports for the next container
  (`address already in use`). That is a connection storm taking down more than the app.
- **The clamp (opt-in) fixes the default-pool case with no hand sizing:** it read the server limit,
  sized pools to 22 (25 − 3 reserved), 0 failures.

### 4.3 SQL Server storm (`user connections=25`, set via `sp_configure` + container restart)

A stock SQL Server allows 32,767 connections and `user connections` reads 0 (unlimited), so nothing can fail
and the probe correctly says "unknown". The benchmark now caps the server (fixed host port so it survives the
restart).

| Arm | Failures (of 130,000) | Mean/batch |
|---|---|---|
| Dapper + StormGate(20) | 0 | 196 ms |
| EF + StormGate(20) | 0 | 823 ms |
| Dapper, no gate | 14,247 (11%) | 162 ms |
| pengdows default | 6,288 (4.8%) | 185 ms |
| pengdows clamp on, headroom 0 | 3,030 (2.3%), re-run 3,196 (all `ConnectionException`) | 189 ms |
| **pengdows clamp on, headroom 2** | **0, 0 (two runs)** | 199 ms |
| EF, no gate | 0 (**unexplained**) | 675 ms |

SQL Server has no admin reserve for the probe to subtract, so a clamp at exactly the limit has no room for
the context's own idle connection(s) in the other role's pool (hypothesis, not directly shown); a headroom of
2 removes it. `ConnectionException` (not `TooManyConnectionsException`) is deliberate: SQL Server logs error
17809 but sends the client nothing recognizable.

### 4.4 Slow-call storm (300 ms calls, 1,500 callers at once, ~20 concurrent)

| Arm | Failures | Time/batch |
|---|---|---|
| Dapper + StormGate (30 s wait) | 0 | 22.6 s |
| EF + StormGate (30 s wait) | 0 | 22.6 s |
| pengdows clamp, **30 s** acquire timeout | 0 | 20.8 s |
| pengdows clamp, **default 5 s** acquire timeout | 19,552 of 30,000 (65%) | 10.2 s |
| default pengdows / Dapper / EF (ungoverned) | `NA` — **setup failed** (Docker port exhaustion), not a result | — |

The 20.8 s vs 22.6 s gap is slot arithmetic (22 vs 20), not efficiency. The 5 s default shedding 65% is
fast-fail admission control working as designed, but it is the setting that decides whether a spike is
survived — a trap if someone enables the clamp and changes nothing else.

### 4.5 The mixed read/write physical-connection gap (red test, by design)

`ServerConnectionCeilingIntegrationTests`: server 25, `MaxConcurrentReads=20`, `MaxConcurrentWrites=20`,
clamp on. A 20-wide read burst leaves **21 idle physical connections**; the following write burst gets
**17/20 `TooManyConnectionsException`** (peak seen 24).

Cause: `Standard` mode uses **separate ADO.NET pools** for reader and writer (different pool key via
`-rw` application-name suffix). Governors count *leases in use*, not idle physical connections the provider
pool keeps. Each pool is individually "sized", the sum is not.

### 4.6 Hangfire

*Public evidence (searched; forum threads, not GitHub issues):* connection spike at server start
([thread 7714](https://discuss.hangfire.io/t/sudden-spike-in-the-number-of-db-connections-after-hangfire-server-starts/7714)),
"each worker maintains a connection" by design
([231](https://discuss.hangfire.io/t/excessive-connection-usage-sqlserver/231)), workers > pool
([4325](https://discuss.hangfire.io/t/timeout-on-connectin-pool/4325)), 30 servers × 80 workers still timing out
([8751](https://discuss.hangfire.io/t/connect-timeout-expired-all-pooled-connections-are-in-use/8751)),
startup failure with pool timeout and recovery problems
([8316](https://discuss.hangfire.io/t/hangfire-exausts-connection-pool-resources-and-hangs/8316)). No report of
a database crash was found, only pool timeouts and connection spikes.

*A list of GitHub issues (#395, #1031, #1835, #2065, #1795, #880, #1307, #1664, #1728, #2493) was pasted by the
user from an outside source. **None of those numbers were verified.*** The stress test's doc comment says so.

*Repro* (`HangfireStartupStormFacts`, real `BackgroundJobServer`, 40 workers = default on 8 cores, Postgres
`max_connections=30`, default pools):

| | Jobs done | Peak server conns | Refused |
|---|---|---|---|
| stock Hangfire.PostgreSql 1.21.1 | 200/200 | 29/30 | 0 |
| pengdows.hangfire, default `DatabaseContext` | 200/200 | 28/30 | **1** (`FetchedJobWatchdog`'s requeue update) |

With 20 ms jobs, both finished with no errors. The effect is small at this scale; neither reproduced a crash.
The refusal surfaced as `DatabaseOperationException` wrapping a 53300 (not `TooManyConnectionsException`) — an
unexplained classification inconsistency on that code path.

**Limitation:** the Hangfire stress project consumes the *published* `pengdows.crud` 2.0.6 package, so it cannot
see any local change until it references the project or a local pack.

### 4.7 Open-loop overload (backpressure)

The earlier storms were **closed-loop** (fixed callers each waiting for their last call), which slows the
offered load whenever the system is slow, so it can never overload anything. This harness offers calls at a
fixed rate above capacity regardless of backlog.

Setup (`PostgreSqlBackpressureBenchmarks`): 20 concurrent, 300 ms calls (capacity 66.7/s), offered 133/s for
20 s, 5 s wait budget where an arm has one, server limit 25, plus a `SELECT 1` probe at 5/s through the same
path. Two valid samples per arm:

| Arm | Served (run 1 / 2) | Failed | Served-call p50 | Peak waiting | Probe p50 |
|---|---|---|---|---|---|
| Pool alone, default `Timeout` 15 s | 2,330 / 2,332 | 336 / 334, after 15 s | 9.0 s | 1,351 / 1,348 | 8.3 / 8.7 s |
| Pool alone, `Timeout=5` | 1,660 / 1,663 | 1,006 / 1,003, after 5 s | 5.3 s | 687 | 5.0 s |
| StormGate (20, 5 s) | 1,660 / 1,662 | 1,006 / 1,004, after 5 s | 5.3 s | 687 | 5.0 s |
| Governor, unbounded queue, 5 s | 1,660 / 1,663 | 1,006 / 1,003, after 5 s | 5.3 s | 688 | 5.0 s |
| **Governor, bounded queue (40)** | 1,371 / 1,376 | 1,295 / 1,290, **at once** | **0.84 / 0.88 s** | **60 / 61** | 0.54 / 0.58 s |

- Goodput is the same everywhere (~66/s): bounding cannot create capacity.
- **A tuned pool, StormGate and an unbounded governor are the same result** (within 3 calls in 1,660).
- **Only the bounded queue changes behavior:** refusals in milliseconds, served calls near
  service time + depth/capacity, backlog ≤ bound + in-service.
- The probe is *not* protected by a bound — it is refused as often as the work (33%; 41–66 admitted across the
  two runs, so that figure is noisy). Protecting an app's other traffic needs separate budgets per workload.
- The expectations were written into the class doc **before** any result existed (class summary).

---

## 5. What the evidence supports, and what it does not

**Supported**

- **Single-writer serialization** for embedded engines (SQLite, DuckDB): a provider pool cannot do this.
- **Bounded-queue backpressure** in `PoolGovernor` (`MaxQueuedReads/Writes`): large, reproducible, and the
  thing a pool lacks.
- **Auto-sizing from the server's limit** (the opt-in clamp): proven live on PostgreSQL 15 and 17 and on a
  capped SQL Server; needs a headroom on engines with no admin reserve.
- **The sum-of-pools problem** is real and reproduced for reader/writer (Section 4.5).

**Not supported**

- That a gate (StormGate, or the governor with an unbounded queue) beats a correctly sized pool with a tuned
  `Timeout`: measured equal.
- That pengdows protects the server *by default* on server databases: at defaults it failed 71%.
- Anything about multiple app instances: each process has its own pool/governor/gate; N pods × a correct pool
  still exceeds the server. Only an explicit per-instance share or an external pooler helps.
- Crash/recovery behavior from the Hangfire reports: not reproduced.

**Unmeasured:** sync callers (Hangfire workers block threads on sync `Open()`), the 3.0 default queue depth
(20×8 = 160 in the overload test), SQL Server and MySQL versions of the overload test, EF under overload,
ungoverned arms under the slow-call storm.

---

## 6. Design decisions

Made, with the reason:

| Decision | Reason |
|---|---|
| Ceiling = min(requested **or** provider default 100, probed server limit, dialect absolute); the provider default applies only when nothing was requested | An explicit request is never capped by the default; "min of four" was refined to this |
| Clamp is **opt-in** (`ClampPoolsToServerConnectionLimit`, default off) on 2.0.x; the user chose "2.0.6 only" | It can silently shrink an oversized pool — a behavior change for a patch line |
| `ResourceConnectionHeadroom` default **0**, absolute only (no percentages); a headroom that consumes the whole server limit is **rejected** at startup | A library should not invent a reservation; silently turning 25−25 into 1 violates the stated one |
| Headroom comes off the **server's usable limit**, not off a request that already fits | Two formulas were offered; this one leaves N slots free for other clients without shrinking small requests |
| PostgreSQL probe: `max_connections − reserved_connections − superuser_reserved_connections`; unreadable superuser reserve assumes **3**, unreadable `reserved_connections` assumes **0** (inferred, logged) | A failed safety probe must not become permission to over-admit |
| MySQL: do **not** subtract the admin connection | The privileged connection is *on top of* `max_connections` (official MySQL page) |
| **Keep separate reader and writer pools.** A shared count applies **only if the reader and writer connection strings resolve to the same server endpoint**; a replica has its own budget | User constraint; physical connections are bounded by a shared count + reclaim, not by merging pools |
| A reader on a different server is probed on its own short-lived non-pooled connection; if that fails the reader is **left unclamped** — never given the primary's number | The unknown case must not borrow the other server's limit |
| `ServerEndpoint` keys on host[\instance]:port, ignoring database/credentials/app name/pool settings; loopback spellings normalize | Server limits are server-wide; reader/writer strings already differ in app name and pool settings |

Agreed in principle, **not built:**

1. A **second semaphore** on top of the per-role governors (a total governor), created only when
   reader max + writer max exceeds the ceiling; acquire the role slot first, then the total slot
   (deadlock-free ordering). The existing `PoolGovernor` already accepts a shared semaphore; the new part is
   two-layer acquire in the hot path (watch `PoolGovernorAllocationTests`).
2. **Reclaim idle connections:** when the server refuses (or before admitting past the physical bound),
   clear the *other* role's pool via the provider and retry once. Provider-specific (`ClearPool`, or Npgsql
   pool statistics where available).
3. A **process-wide, dialect-keyed resource budget:** server endpoint for server databases, **file path for
   SQLite/DuckDB** (which would also give a per-file single writer across contexts — today two contexts on one
   file each have their own `SingleWriter`), no budget for engines with no meaningful limit.
4. A **manual `ServerConnectionLimit` override** for engines that cannot be probed or sit behind a pooler/proxy
   (a probe behind PgBouncer/RDS Proxy answers confidently and wrongly).
5. Treating a **refusal as a signal** (cooldown + jittered re-admission) and consistent classification of
   server-limit refusals.

---

## 7. What was built

All in the working tree (`pengdows.crud`, branch `2.0.6`, base `6982c621`). Test-first throughout; mutation
checks were done on the safety-critical properties (restored afterwards).

### 7.1 Library

| File | Change |
|---|---|
| `pengdows.crud/internal/ConnectionCeiling.cs` | New. Pure min-rule + headroom; `ConnectionCeilingResult { Value, Limiter, HeadroomApplied, WasClamped }` |
| `pengdows.crud/internal/ServerEndpoint.cs` | New. Endpoint key from a connection string builder |
| `pengdows.crud/dialects/SqlDialect.cs` | `ProbeServerConnectionLimitAsync` (never throws) + `ProbeServerConnectionLimitCoreAsync` (default: unknown) + `ParseConnectionCount` |
| `dialects/PostgreSqlDialect.cs` | PG probe (both reserve layers, conservative defaults); Yugabyte inherits it |
| `dialects/MySqlDialect.cs` | `@@max_connections`, lowered by a positive `@@max_user_connections`; MariaDB inherits |
| `dialects/SqlServerDialect.cs` | `sys.configurations` `user connections`; 0 → unknown |
| `dialects/{CockroachDb,Spanner,TiDb}Dialect.cs` | Explicit **unknown** overrides (so they don't inherit a probe that may not apply) |
| `abstractions/.../IDatabaseContextConfiguration.cs` | `ClampPoolsToServerConnectionLimit`, `ResourceConnectionHeadroom` as **default interface members** (existing implementers keep compiling); API baseline +2 lines |
| `configuration/DatabaseContextConfiguration.cs` | The two properties (headroom validated ≥ 0) |
| `tenant/TenantConnectionResolver.cs` | Both cloned for tenants (the existing "every property is preserved" guard now covers them) |
| `DatabaseContext.cs`, `DatabaseContext.Initialization.cs` | Probe on the detection connection before pool sizing; one limit per role; `ReaderTargetsADifferentServer`; `ProbeSeparateServerAsync`; `ApplyServerCeiling`; explicit per-role requests clamped up front |

### 7.2 Tests (all new unless noted)

`ConnectionCeilingTests` (16), `ServerEndpointTests` (10), `PostgreSqlServerConnectionLimitProbeTests` (9),
`ServerConnectionLimitProbeTests` (12), `ServerCeilingConfigurationTests` (4), `ServerCeilingWiringTests`
(14, incl. replica cases), plus the live-server tests below. `TenantConnectionResolverTests` updated.

- **Live (integration project):** `ServerConnectionLimitLiveProbeTests` — PostgreSQL 15 and 17, both give 22
  slots at `max_connections=25`. **Passing.**
- **Intentionally RED:** `ServerConnectionCeilingIntegrationTests` (the Section 4.5 acceptance test) and
  `HangfireStartupStormFacts.PengdowsHangfire_DefaultConfiguration_…` (1 refused watchdog update).

### 7.3 Benchmarks

`PostgreSqlConnectionGovernanceBenchmarks` (modified: shared connection-string seam, 1,000-way/5,000-op load,
`WorkMilliseconds` param, `Pengdows_Governed`/`Clamped`/`ClampedPatient` arms, per-arm disposal),
`SqlServerConnectionGovernanceBenchmarks` (new; capped server, `Clamped` and `ClampedHeadroom` arms),
`SQLiteWriteContentionBenchmarks` (modified: `BusyTimeoutMs` param, `PengdowsDefaults` arm, per-parameter
correctness keys and sidecar names), `OpenLoopLoad.cs` + `PostgreSqlBackpressureBenchmarks` (new). Shape tests in
`CrudBenchmarks.Tests` (76 passing) pin equal-footing so a later edit cannot tilt them.

### 7.4 The two "failing" unit tests

`PoolGovernorAllocationTests.UncontendedAcquireAndRelease_…` (216 B vs 48 B) and
`DataReaderMapperPlanLookupAllocationTests.CachedPlanCall_…` (792 B vs 568 B) failed **only in Debug**, where
async state machines are heap classes. In Release they pass at the budgets the tests promise (504 B vs 408 B).
Fix: `ReleaseBuildFactAttribute` skips with a stated reason when the library under test is not optimized; the
budgets were not loosened. Run unit tests with `-c Release` when touching the governor.

### 7.5 Test status at last full run (scratch copy, identical files to the tree)

`pengdows.crud.Tests` Release: **11,057 / 11,057** on net8.0 and net10.0. Debug: 2 skipped, 0 failed.
After the copy-back, 62 focused tests re-run green on both runtimes. The earlier coverage-gate errors in
output come from running a *filtered* subset against the 83% rule, not from failures.

### 7.6 Engine probe research (docs-backed unless noted)

| Engine | Finding | Probe |
|---|---|---|
| PostgreSQL | `max_connections` typically 100, no stated max; `reserved_connections` 0 (new in 16); `superuser_reserved_connections` 3; each < max − the other ([docs](https://www.postgresql.org/docs/current/runtime-config-connection.html)) | Built |
| MySQL | permits `max_connections` **+ 1** for `CONNECTION_ADMIN`/`SUPER` ([docs](https://dev.mysql.com/doc/refman/8.0/en/too-many-connections.html)); default 151/max 100,000 from [MariaDB docs](https://mariadb.com/docs/server/ref/mdb/system-variables/max_connections) | Built |
| SQL Server | max 32,767; current docs say 0 means that max ([docs](https://learn.microsoft.com/en-ie/sql/database-engine/configure-windows/configure-the-user-connections-server-configuration-option?view=sql-server-2017)); Azure SQL limits are per tier ([docs](https://learn.microsoft.com/en-us/azure/azure-sql/database/resource-limits-logical-server)) | Built (0 → unknown) |
| Oracle | `SESSIONS` default `(1.5 × PROCESSES) + 22`; range 1–65,535; default capped 262,143 at COMPATIBLE ≥ 19; size as users + background + ~10% recursive ([docs](https://docs.oracle.com/en/database/oracle/oracle-database/19/refrn/SESSIONS.html)) | Not built; needs live check |
| TiDB | `max_connections` global/per-instance, default **0 = no limit**, range 0–100,000 ([docs](https://docs.pingcap.com/tidb/stable/system-variables)) | Could inherit the MySQL probe; currently explicit unknown |
| CockroachDB | `server.max_connections_per_gateway` default **−1 (unlimited)**, superusers unaffected ([docs](https://docs.cockroachlabs.com/docs/stable/cluster-settings)) | Could probe via `SHOW CLUSTER SETTING`; currently explicit unknown |
| YugabyteDB | per-node `max_connections`; reserve default not documented on the page ([docs](https://docs.yugabyte.com/stable/develop/quality-of-service/limiting-connections/)) | Inherits PG probe; **unverified** |
| Db2 | `max_connections` −1, AUTOMATIC or 1–64,000; −1 means `max_coordagents` ([IBM](https://www.ibm.com/docs/en/SSHRBY/com.ibm.db2.luw.admin.config.doc/doc/r0003289.html)) | Not built; AUTOMATIC isn't a number |
| SAP ASE | Developer Edition caps the parameter at 25 (range 5–25) per an SAP KBA; official default not found | Not built |
| Aurora/RDS PG | default `LEAST(DBInstanceClassMemory/9531392, 5000)`; AWS advises ≥3 spare for automation ([Aurora](https://docs.aws.amazon.com/AmazonRDS/latest/AuroraUserGuide/AuroraPostgreSQL.Managing.html)); `rds.rds_superuser_reserved_connections` default 2 ([RDS](https://repost.aws/knowledge-center/rds-postgresql-error-connection-slots)); "deprecated from 16" **not confirmed** | Not built (version-aware) |
| InterBase | licence-based: Server 1–unlimited users × 4 connections; Developer 4 users (current docs) ([editions](https://docwiki.embarcadero.com/InterBase/en/InterBase_Editions)) | None queryable |
| Informix, Firebird, HANA, Snowflake, Spanner | No documented queryable limit found (Firebird: community says none; HANA: a HANA 1.0 KBA log shows 65,536) | None |
| SQLite, DuckDB, FlatFile, Access | Embedded; nothing to read | None (correct) |

"Legitimately no limit" ceiling: the user suggested Int16.Max. (The typed number, 16,535, is not Int16.Max, which is
**32,767** and is also SQL Server's documented maximum.) Existing `AbsoluteMaxPoolSize = 512` already clamps
every pool first, so a 32,767 ceiling would never bind unless that cap is raised. Documented per-engine
maximums: SQL Server 32,767, MySQL/MariaDB/TiDB 100,000, Db2 64,000, Oracle 65,535.

---

## 8. State of the working tree

`pengdows.crud` (`2.0.6` @ `6982c621`) — modified: both governance benchmarks, SQLite benchmark, the two
allocation tests, `TenantConnectionResolverTests`, API baseline, the interface/config/context/dialect/tenant
files listed in 7.1. Untracked: `ConnectionCeiling.cs`, `ServerEndpoint.cs`, `OpenLoopLoad.cs`,
`PostgreSqlBackpressureBenchmarks.cs`, `SqlServerConnectionGovernanceBenchmarks.cs`,
`ReleaseBuildFactAttribute.cs`, the new unit/shape/integration test files.

`pengdows.hangfire` (`2.0.6` @ `d090165`) — modified `pengdows.hangfire.stress.tests.csproj` (adds
`Hangfire.PostgreSql` 1.21.1); untracked `HangfireStartupStormFacts.cs`.

A scratch copy of the repo was used while a long benchmark ran (so a half-edited library would not be built
into later arms); the three changed files were copied back and the copy is **identical to the tree**. The
scratch directory is ephemeral and can be deleted.

---

## 9. Open items and decisions still pending

1. **Build the shared count + reclaim** (turns the Section 4.5 test green; makes the headroom guess
   unnecessary). Hot path: keep `PoolGovernorAllocationTests` green in Release.
2. **Default `PoolAcquireTimeout` (5 s) with the clamp on** — change it, or document that it decides
   survive-vs-shed.
3. **Add the documented per-dialect absolutes**, and decide whether to raise the 512 pool cap.
4. **Manual `ServerConnectionLimit` override** (poolers, un-probeable engines).
5. **Rename `Pengdows_Governed`** (it is the default-config arm and fails like the others — the name implies
   protection) and fix stale comments/doc text in the governance benchmarks (pool-100-era measurements,
   "Expected result" lines). Renaming changes benchmark/correctness identities. The user declined to choose
   which expectations to fix when asked, so this is still open.
6. **StormGate depth bound** — the one property that separates a bounded governor from a tuned pool.
7. Run the **3.0 default queue depth** in the overload test; build the **SQL Server and MySQL** versions
   (MySQL with both `MySqlConnector` and `MySql.Data`); add a pool-capped Dapper arm to the slow-call storm
   and re-run the three `NA` arms with a drain between parameter values.
8. **Probes:** TiDB and CockroachDB (docs support them; need a live check), Oracle, ASE, Db2 (only when a
   numeric value is readable), AWS version-aware reserves.
9. **Classification:** why a 53300 on the Hangfire watchdog path arrived as `DatabaseOperationException`.
10. **Per-workload budgets** (Hangfire vs app) so a bounded queue does not refuse the app's own traffic.
11. **Verify** the pasted Hangfire GitHub issue numbers; make the Hangfire stress project use the local
    library; scale the repro to several servers in one process plus app traffic.
12. **Multiple instances:** a configured per-instance share (not solved by anything built).
13. **Commit strategy** — none of this is committed; no branch, no PR.

---

## 10. Reproducing, and gotchas

Run from `benchmarks/CrudBenchmarks`; opt-in benchmarks need `--include-opt-in` (or
`CRUD_BENCH_INCLUDE_OPT_IN=1`) and Docker.

```bash
# one arm (exact name when arms share a prefix; trailing * matches ClampedHeadroom etc.)
dotnet run -c Release --framework net8.0 -- \
  --filter '*PostgreSqlConnectionGovernanceBenchmarks.Pengdows_Clamped*' --include-opt-in

# overload test, one arm
dotnet run -c Release --framework net8.0 -- \
  --filter '*PostgreSqlBackpressureBenchmarks.Pengdows_Governor_BoundedQueue' --include-opt-in
# result line: grep '^\[BACKPRESSURE\]' in the log; sidecar *-backpressure.md under BenchmarkDotNet.Artifacts/results
```

Gotchas that cost time:

- **Run ungoverned arms last and wait for `TIME_WAIT` to drain between arms** (`ss -s | grep timewait`,
  < ~500). One ungoverned storm leaves ~10k+ sockets and the next container fails to start; the BDN table shows
  `NA`, which is a setup failure, not a result. A loop that waits is in the appendix.
- **BenchmarkDotNet invokes a benchmark method more than once** (jitting + measured). A method that builds
  its own pool must dispose it, or the second call runs against a server still holding the first's connections.
- **Correctness fragments are cleared at the start of each run** and `CRUD_BENCH_ARTIFACTS_DIR` is not honored
  by the child processes; read the `Fails` column / log output right after a run, and look for sidecars under
  the working directory's `BenchmarkDotNet.Artifacts/results`.
- **Run unit tests with `-c Release`** for anything touching the governor; Debug skips two allocation guards.
  Filtered runs trip the 83% coverage gate (an artifact).
- A benchmark build and a test build of the library share outputs per configuration; don't edit library code
  while a long benchmark is still launching new arms.
- SQL Server's `user connections` only applies after a restart: the benchmark sets it, restarts the container,
  and uses a **fixed host port** so the connection string survives.

### Appendix: arm runner with socket-drain wait

```bash
#!/bin/bash
# usage: run-arms.sh <ClassPrefix> <arm> [arm...]   (class = <prefix>ConnectionGovernanceBenchmarks)
cls=$1; shift
for arm in "$@"; do
  for i in $(seq 1 60); do
    tw=$(ss -s | sed -n 's/.*timewait \([0-9]*\).*/\1/p' | head -1)
    [ "${tw:-0}" -lt 500 ] && break
    sleep 10
  done
  dotnet run -c Release --framework net8.0 -- \
    --filter "*${cls}ConnectionGovernanceBenchmarks.${arm}*" --include-opt-in > "run-$cls-$arm.log" 2>&1
  grep -aE '^\| (Dapper|EF|Pengdows)_' "run-$cls-$arm.log" | awk '!seen[$0]++'
done
```
