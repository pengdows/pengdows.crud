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
3. **The server's connection limit, only if you opted in** with `ClampPoolsToServerConnectionLimit`: each role is held to the server's usable limit and, when the two roles share one server and together exceed it, the limit is divided between them. See "Server connection ceiling" below.

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
| no pool size, server allows 22 connections | `ClampPoolsToServerConnectionLimit = true` | 11 | 11 |

This order is pinned by `PoolSizeResolutionOrderTests`, so it cannot drift from the code.

A caller-supplied `Min Pool Size` is clamped to `[0, Max Pool Size]`. In `PreventDatabaseUnload` mode every enabled pool (writer and reader) is raised to a maximum of at least `2` — one permit for the sentinel and one for real work — with a warning when a smaller value was requested, and receives a provider `Min Pool Size` of at least `2`. A read-only writer pool keeps maximum and minimum `0`.

A write size of `0` promotes the context to `ReadOnly` with a warning, as described above: it is equivalent to explicitly selecting a read-only context, with the writer governor forbidden and the reader pool enabled.

## Server connection ceiling (opt-in)

Setting `ClampPoolsToServerConnectionLimit = true` (default `false` on the 2.0.x line, because it can silently shrink an oversized pool) makes the context read the database server's own connection limit while it initializes and never size a pool above it. Each role gets the smaller of the size it asked for (or the provider default of 100 when it asked for nothing) and the server's limit. A clamp is logged as a warning, and so is an unreadable limit when clamping was requested, so a silent no-op cannot hide.

The limit each engine reports:

| Engine | Server limit used |
|---|---|
| PostgreSQL, YugabyteDB | `max_connections` minus `reserved_connections` (PostgreSQL 16+) and `superuser_reserved_connections`. A reserve that cannot be read falls back to its documented default (3 and 0), never to 0 |
| MySQL, MariaDB | `@@max_connections`, lowered by a positive `@@max_user_connections`. No admin reserve is subtracted: the privileged connection is on top of the limit |
| SQL Server | `user connections`; `0` means unlimited, which is treated as unknown |
| CockroachDB, TiDB, Spanner and every other engine | Unknown: pools are left as they are |

**Reads and writes share one server.** The two roles use separate provider pools, and a provider pool's `Max Pool Size` caps its connections whether they are in use or idle. When both roles target the same server and together ask for more than it allows, the server's limit is divided between them in proportion to what each asked for, and each role's governor and provider pool are sized to its share. The two pools therefore never hold more connections than the server allows, and callers beyond a share wait under `PoolAcquireTimeout` (failing with `PoolSaturatedException`) instead of waiting inside the provider for its own timeout. "The same server" is decided from the endpoint the connection strings name (`host[\instance]:port`; a dialect's default port is assumed when none is given): the database, credentials, application name and pool settings are ignored. A read replica on another host is probed on its own short-lived connection and budgeted separately; if it cannot be reached its pool is left alone, never given the primary's number. A pool sized `0` stays forbidden.

`ResourceConnectionHeadroom` (default `0`) leaves that many server connections free for other clients such as monitoring, administrators and other applications. It comes off the server's usable limit, not off a pool size that already fits, and a value that consumes the whole limit throws `ArgumentOutOfRangeException` when the context starts. When the limit is unknown no headroom can be reserved, and a warning says so.

The budget belongs to one `DatabaseContext`. It does not coordinate several contexts that point at the same server, which is one reason a `DatabaseContext` must be a singleton per connection string (see the next section and `EnforceUniqueConnectionString`).

## Writer-fairness turnstile limitations

Enabling `EnableSingleWriterFairness` (`SingleWriter` mode only) installs a turnstile semaphore that blocks new reader permits while a writer is waiting to acquire the single write slot. This prevents an unbounded stream of incoming readers from starving a pending writer.

However, the protection is **not retroactive**: readers that were already queued on the semaphore *before* the writer grabbed the turnstile are not displaced and will run first. Under a sustained high-read burst a writer may therefore still wait for a short pre-queued cohort to drain before getting its slot. Starvation is reduced, not eliminated.

To observe this in production, watch `PoolStatisticsSnapshot.TotalTurnstileTimeouts`. If the value climbs, consider reducing `MaxConcurrentReads` or increasing `PoolAcquireTimeout` to give writers more room.

## Pinned connections keep permits
Two live `DatabaseContext` instances sharing the same connection string in-process is a common accidental-non-singleton bug (`DatabaseContext` must be a singleton): each runs its own admission control, so together they can admit more connections than the provider pool was sized for. Since 2.0.6 pengdows.crud detects it: constructing a context whose connection string matches another live context's logs a warning. Set `DatabaseContextConfiguration.EnforceUniqueConnectionString = true` (default `false`) to make it an `InvalidOperationException` at construction of the second context. The check covers the writer and reader strings together (all or nothing), keys on a hash in which each credential is itself hashed (so strings that differ only in credentials are distinct pools and never collide), and is released when the first context is disposed.

`InitializePoolGovernors` hashes the writer and reader connection strings to get pooled keys, respects the resolved pool size (including overrides) and the selected `DbMode`, and creates a governor for each pool (except `SingleConnection`, which disables governors entirely). `SingleWriter` mode uses the Standard lifecycle but adjusts the governor so writes serialize with `MaxConcurrentWrites = 1` (and an optional writer-preference turnstile); `PreventDatabaseUnload` retains one slot per sentinel from that sentinel's own pool (writer, and reader when a dedicated reader connection string exists or the context is read-only); a repaired sentinel releases the old slot and takes a new one. This keeps the governors aware of the pinned connections while still allowing other operations to proceed, and the hashed key ensures each unique connection string gets its own governor scope.
