# Connection Pooling and Governors

## Pooling defaults
`DatabaseContext` automatically rewrites connection strings for Standard, PreventDatabaseUnload, and SingleWriter modes so sockets and clients stay in a managed pool. `ConnectionPoolingConfiguration.ApplyPoolingDefaults` detects whether the provider supports external pooling, skips raw connection strings (like `:memory:` or a bare file path), and forces `Pooling=true` when the flag is missing. It does not inject a `Min Pool Size` value in `Standard` or `SingleWriter` mode—if you want a minimum, set it explicitly in your connection string. `PreventDatabaseUnload` is the one exception (see below). These defaults respect your existing pooling settings when you have custom values or pooling is intentionally turned off.

## Pool governors
The context creates read and write `PoolGovernor` instances in every mode except `SingleConnection`, issuing `PoolSlot` tokens before any connection is acquired. Each governor waits no longer than `PoolAcquireTimeout` (default 5 seconds via `DatabaseContextConfiguration.PoolAcquireTimeout`) before throwing a `PoolSaturatedException` with queue and slot statistics, so you fail fast instead of saturating the provider pool. Governors need no configuration: each is sized automatically from the connection string, using the provider's maximum-pool-size setting (`Max Pool Size`, `MaxPoolSize`, `Maximum Pool Size` and the provider's other spellings) when it is present and the dialect's default otherwise, and the resolved size is written back into the provider's connection string so the governor and the provider pool always agree. `MaxConcurrentReads`/`MaxConcurrentWrites` are optional overrides for when you want a different limit, and the snapshots let you correlate hot paths with pool contention.

### How a pool's size is decided

The read pool and the write pool are sized separately. Each uses the **first** of these that applies, and the size that wins is used for both the governor and the provider's own pool (it is written into that pool's connection string), so the two always agree.

| Priority | Source | Notes |
|---|---|---|
| 1 (highest) | `MaxConcurrentReads` / `MaxConcurrentWrites` on `DatabaseContextConfiguration` | `0` forbids that pool (see below) |
| 2 | The pool-size setting in that pool's connection string | `Max Pool Size`, `MaxPoolSize`, `Maximum Pool Size` or `MaximumPoolSize`. Reads use `ReadOnlyConnectionString` when you supply one and `ConnectionString` otherwise; writes always use `ConnectionString` |
| 3 (lowest) | The dialect's default | 100 for the engines that have a provider pool |

When configuration and the connection string disagree, configuration wins and a warning names both values and the one in effect.

Then these rules are applied, in this order, to whatever size won:

1. **Absolute ceiling of 512.** No source can ask for more; a larger value is coerced to 512 with a warning.
2. **Mode rules.** `SingleWriter` always has exactly one writer, whatever any source says (a warning is logged if one asked for more). `PreventDatabaseUnload` raises a pool smaller than 2 to 2, one permit for the sentinel and one for real work. `SingleConnection` has no governors at all.
3. **The server's connection limit, only if you opted in** with `ClampPoolsToServerConnectionLimit`: each role is held to the server's usable limit and, when the roles together (or several contexts on one server) can exceed it, they draw on one shared per-server budget at run time. See "Server connection ceiling" below.

Special values:

- `MaxConcurrentReads = 0` or `MaxConcurrentWrites = 0` **forbids** that pool: every acquire throws `PoolForbiddenException`. `MaxConcurrentWrites = 0` also makes the whole context read-only, in every `DbMode`.
- A `0` in a **connection string** is not a forbid: it counts as unset and the next source applies.
- A negative size, in configuration or in a connection string, throws `ArgumentOutOfRangeException`.
- Engines with no provider pool to configure (SQLite and DuckDB run in-process) **ignore** a pool size in the connection string. Their default is unbounded, so only the 512 ceiling applies, and `MaxConcurrentReads`/`MaxConcurrentWrites` still work.

Worked examples (the first four use a PostgreSQL connection string and `DbMode.Standard`):

| Connection string | Configuration | Reads | Writes |
|---|---|---|---|
| no pool size | nothing | 100 | 100 |
| `Maximum Pool Size=30` | nothing | 30 | 30 |
| `Maximum Pool Size=30` | `MaxConcurrentReads = 10` | 10 | 30 |
| `Maximum Pool Size=30` | `MaxConcurrentReads = 10`, `MaxConcurrentWrites = 7` | 10 | 7 |
| `Maximum Pool Size=1000` | nothing | 512 | 512 |
| `Maximum Pool Size=30` | `DbMode.SingleWriter` | 30 | 1 |
| no pool size | `MaxConcurrentWrites = 0` | 100 | forbidden (read-only context) |
| no pool size, server allows 22 connections | `ClampPoolsToServerConnectionLimit = true` | 22 | 22 |

With the clamp on, the two 22s are not a division of the server's 22: both roles draw on one shared per-server budget (see "Server connection ceiling").

This order is pinned by `PoolSizeResolutionOrderTests`, so it cannot drift from the code.

A caller-supplied `Min Pool Size` is clamped to `[0, Max Pool Size]`. In `PreventDatabaseUnload` mode every enabled pool (writer and reader) is raised to a maximum of at least `2` — one permit for the sentinel and one for real work — with a warning when a smaller value was requested, and receives a provider `Min Pool Size` of at least `2`. A read-only writer pool keeps maximum and minimum `0`.

A write size of `0` promotes the context to `ReadOnly` with a warning, as described above: it is equivalent to explicitly selecting a read-only context, with the writer governor forbidden and the reader pool enabled.

### How a governor admits callers

Every governor admits callers through one primitive, `PoolGovernorConcurrencyGate`. A caller that finds a free slot takes it without locking. A caller that does not joins a queue and is handed a slot in arrival order: admission is strictly first-in, first-out, so a caller already waiting is never overtaken by a newer one. A blocking (synchronous) wait allocates nothing once warm, and spins briefly before it parks the thread; an asynchronous wait awaits without holding a thread.

Strict FIFO has a price, and it is worth knowing where it shows. Handing a slot to a parked thread costs that thread's wake-up, so a tight loop that acquires, holds for almost no time and releases again is slower than a primitive that lets the releasing thread take its own slot back. Measured against `SemaphoreSlim` (16 blocking callers, no time between acquisitions): on one slot the gate is far slower at a zero-length hold and 1.8× slower at 5 µs; on 1 to 8 slots it is 1.1 to 1.5× slower at 10 to 50 µs, 1.07 to 1.10× at 100 µs, and level from about 10 ms or at 64 slots and up. A database round trip is not that short: against PostgreSQL, 32 threads queuing for 2 or 8 slots through blocking `BeginTransaction` ran within -4% to +6% of 2.0.5, and allocated about 36% less. Asynchronous waits, and the maximum pool size (512 slots), matched or beat `SemaphoreSlim` at every hold measured. Only a workload that holds a slot for a few microseconds, with callers queuing behind it on a small pool, pays the difference.

`PoolAcquireTimeout` accepts any positive value, so `TimeSpan.MaxValue` is a valid way to say "wait as long as it takes". A timeout longer than about 49 days, which is more than a timer can hold, is treated as no timeout at all: the caller waits until a slot is granted or it cancels.

## Server connection ceiling (opt-in)

Setting `ClampPoolsToServerConnectionLimit = true` (default `false` on the 2.0.x line, because it can silently shrink an oversized pool) makes the context read the database server's own connection limit while it initializes and never size a pool above it. Each role gets the smaller of the size it asked for (or the provider default of 100 when it asked for nothing) and the server's limit. A clamp is logged as a warning, and so is an unreadable limit when clamping was requested, so a silent no-op cannot hide.

The limit each engine reports:

| Engine | Server limit used |
|---|---|
| PostgreSQL, YugabyteDB | `max_connections` minus `reserved_connections` (PostgreSQL 16+) and `superuser_reserved_connections`. A reserve that cannot be read falls back to its documented default (3 and 0), never to 0 |
| MySQL, MariaDB | `@@max_connections`, lowered by a positive `@@max_user_connections`. No admin reserve is subtracted: the privileged connection is on top of the limit |
| SQL Server | `user connections`; `0` means unlimited, which is treated as unknown |
| CockroachDB, TiDB, Spanner and every other engine | Unknown: pools are left as they are |

**Reads and writes share one server, and so do contexts.** Each role keeps the size it asked for, held on its own to the server's usable limit, and each role's governor and provider pool are sized to it. The roles are not divided in advance. Instead every governor that points at one server registers with a **per-server budget**, a process-wide counter keyed by the server's endpoint (`host[\instance]:port`; a dialect's default port is assumed when none is given). The database, credentials, application name and pool settings are ignored, so contexts on different databases of one server share one budget, which is right because `max_connections` belongs to the server. A caller takes its role's local slot first and then a server permit under the same `PoolAcquireTimeout`; at the budget it waits in order and fails with `PoolSaturatedException` instead of reaching a server that would refuse it. Capacity therefore slides with demand: a read-heavy phase can use nearly the whole budget and a write burst takes permits back as readers release them. Registered contexts take the smallest usable limit any of them probed.

The budget **engages only when it can bind**: while the registered governor sizes together fit the limit (SQL Server's unlimited default against two pools of 100) it is never consulted beyond one volatile read, and it never counts anything. Once the sizes together exceed the limit it engages for good and adopts connections already open, so a sentinel taken at start-up counts. A read replica on another host is probed on its own short-lived connection and gets its own budget; if it cannot be reached its pool is left alone, never given the primary's number. A pool sized `0` stays forbidden and registers nothing.

Three different counts are in play: a role's local governor lease, a server permit, and a connection the provider has opened. The budget counts the middle one, so it bounds connections **in use**. Idle connections that a provider pool keeps open are not counted, so after a read burst the reader pool can still hold idle connections while the writer opens new ones. Until the planned idle-reclaim step exists (POOL-003, `docs/FUTURE_WORK.md`), this is a strong guard on connections in use, not an absolute bound on physical connections; the provider's own idle pruning (for Npgsql `Connection Idle Lifetime` and `Connection Pruning Interval`) closes the gap and needs no code.

`ResourceConnectionHeadroom` (default `0`) leaves that many server connections free for other clients such as monitoring, administrators and other applications. It comes off the server's usable limit, not off a pool size that already fits, and a value that consumes the whole limit throws `ArgumentOutOfRangeException` when the context starts. When the limit is unknown no headroom can be reserved, and a warning says so.

The budget is shared by every context in the process that points at the same server. A `DatabaseContext` must still be a singleton per connection string (see the next section and `EnforceUniqueConnectionString`).

### Why the roles are not divided in advance

The first design of the clamp divided the server's usable connections between the reader and the writer by what each asked for, not by what each used. Against a server that allows 22, both roles configured at 20 gave 11 and 11, so a context that only reads had 11 reader slots and 11 it never used. That design was measured before it shipped (PostgreSQL, `max_connections=25`, 1,000 concurrent callers, 300 ms calls; `benchmarks/CrudBenchmarks/results/postgres-connection-governance-run-2026-10-11.md`), and the result is why the per-server budget above replaced it:

| Configuration measured | Reader slots | Acquire wait | Failed calls | Time |
|---|---|---|---|---|
| Static division, both roles configured | 11 | 5 s (default) | 75.07% | 10.2 s |
| 11 per role set by hand, no clamp | 11 | 5 s | 75.07% | 10.2 s |
| Static division, both roles configured | 11 | 30 s | 0 | 41.2 s |
| Static division, `MaxConcurrentWrites = 0` | 20 | 30 s | 0 | 22.6 s |
| No clamp, 20 slots | 20 | 30 s | 0 | 22.6 s |

- **The division was the whole cost:** setting 11 per role by hand failed exactly the same calls.
- **Saying so avoided it:** `MaxConcurrentWrites = 0` makes the context read-only, the writer pool is forbidden, and the reader keeps its full request (bounded by the server's limit). That still holds, and it is still the cheapest setting for a context that never writes. The shared budget makes the undeclared case pay nothing for it as well.
- **The default wait decides whether a spike is survived.** Failures in a spike are callers that waited `PoolAcquireTimeout` (5 s) for a slot and were turned away with `PoolSaturatedException`; the server refused nothing. A batch serves about `2 x 17 x slots` calls per two 5 s windows, so with 20 slots a 1,500-call burst loses more than half and with a 30 s wait it loses none. A context expecting large spikes should size the wait with the slots, whatever the clamp setting. Declaring the context read-only does not change this.
- **No server refusal.** Every pengdows arm kept the server from refusing a single connection. Dapper and EF without any gate failed 57% to 72% of fast calls and 95.9% of slow calls against the same server and left about 34,000 sockets in TIME_WAIT on the host; behind a gate of 20 permits and a 30 s wait they failed none.

The shared-budget behavior itself has unit and fakeDb coverage; a mixed read/write storm against a real server is still to be measured (POOL-003, `docs/FUTURE_WORK.md`).

## Writer-fairness turnstile limitations

Enabling `EnableSingleWriterFairness` (`SingleWriter` mode only) installs a turnstile semaphore that blocks new reader permits while a writer is waiting to acquire the single write slot. This prevents an unbounded stream of incoming readers from starving a pending writer.

However, the protection is **not retroactive**: readers that were already queued on the semaphore *before* the writer grabbed the turnstile are not displaced and will run first. Under a sustained high-read burst a writer may therefore still wait for a short pre-queued cohort to drain before getting its slot. Starvation is reduced, not eliminated.

To observe this in production, watch `PoolStatisticsSnapshot.TotalTurnstileTimeouts`. If the value climbs, consider reducing `MaxConcurrentReads` or increasing `PoolAcquireTimeout` to give writers more room.

## Pinned connections keep permits
Two live `DatabaseContext` instances sharing the same connection string in-process is a common accidental-non-singleton bug (`DatabaseContext` must be a singleton): each runs its own admission control, so together they can admit more connections than the provider pool was sized for. Since 2.0.6 pengdows.crud detects it: constructing a context whose connection string matches another live context's logs a warning. Set `DatabaseContextConfiguration.EnforceUniqueConnectionString = true` (default `false`) to make it an `InvalidOperationException` at construction of the second context. The check covers the writer and reader strings together (all or nothing), keys on a hash in which each credential is itself hashed (so strings that differ only in credentials are distinct pools and never collide), and is released when the first context is disposed.

`InitializePoolGovernors` hashes the writer and reader connection strings to get pooled keys, respects the resolved pool size (including overrides) and the selected `DbMode`, and creates a governor for each pool (except `SingleConnection`, which disables governors entirely). `SingleWriter` mode uses the Standard lifecycle but adjusts the governor so writes serialize with `MaxConcurrentWrites = 1` (and an optional writer-preference turnstile); `PreventDatabaseUnload` retains one slot per sentinel from that sentinel's own pool (writer, and reader when a dedicated reader connection string exists or the context is read-only); a repaired sentinel releases the old slot and takes a new one. This keeps the governors aware of the pinned connections while still allowing other operations to proceed, and the hashed key ensures each unique connection string gets its own governor scope.
