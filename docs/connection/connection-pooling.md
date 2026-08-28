# Connection Pooling and Governors

## Pooling defaults
`DatabaseContext` automatically rewrites connection strings for Standard, PreventDatabaseUnload, and SingleWriter modes so sockets and clients stay in a managed pool. `ConnectionPoolingConfiguration.ApplyPoolingDefaults` detects whether the provider supports external pooling, skips raw connection strings (like `:memory:` or a bare file path), and forces `Pooling=true` when the flag is missing. For supported external pools, pengdows.crud also ensures the documented provider minimum. Your own explicit pooling settings (other than turning pooling off) are respected subject to the maximum/minimum policy below, but `Pooling=false` is not: pengdows.crud requires connection pooling for its governor/session-setting model to work, so an explicit `Pooling=false` in the connection string throws `InvalidOperationException` rather than being honored.

## Pool governors
The context creates read and write `PoolGovernor` instances in every mode except `SingleConnection`, issuing `PoolSlot` tokens before any connection is acquired. Each governor waits no longer than `PoolAcquireTimeout` (default 5 seconds via `DatabaseContextConfiguration.PoolAcquireTimeout`) before throwing a `PoolSaturatedException` with queue and slot statistics, so you fail fast instead of saturating the provider pool. Override `MaxConcurrentReads`/`MaxConcurrentWrites` to tune effective limits, and observe the snapshots if you need to correlate hot paths with pool contention.

**The governor and provider pool use the same effective maximum, resolved in this priority order:**

1. `DatabaseContextConfiguration.MaxConcurrentWrites`/`MaxConcurrentReads`, if explicitly set.
2. Otherwise, the connection string's `Max Pool Size` (or the provider's own alias for it), if present.
3. Otherwise, **the dialect's `DefaultMaxPoolSize`** (`SqlDialect.FallbackMaxPoolSize = 100`, inherited by every dialect that doesn't override it — which includes Snowflake, along with most others).

Whatever number that resolves to is then subject to one more hard ceiling: `DatabaseContext.AbsoluteMaxPoolSize = 512`, an internal constant nothing can exceed regardless of configuration — a request above it is silently clamped down.

When `MaxConcurrentReads` or `MaxConcurrentWrites` is explicitly configured and differs from the connection string's `Max Pool Size`, the configuration property wins for both the governor and the rewritten provider connection string. pengdows.crud logs a warning containing both values and the winning value. If the configuration property is absent, the connection-string value wins over the dialect default. `Min Pool Size` has no separate configuration property: enabled pools receive a provider minimum of `1`, or `2` in `PreventDatabaseUnload` mode. An existing connection-string minimum is raised to that floor and clamped to the effective maximum. A read-only writer pool has maximum and minimum `0`.

Setting `MaxConcurrentWrites=0` promotes the context to `ReadOnly` mode, regardless of the requested connection mode. This is equivalent to explicitly selecting a read-only context: the writer governor and provider minimum are both zero, while the reader pool remains enabled.

**Practical implication:** if you're evaluating a claim like "database X behaves correctly under `Standard` mode with no special write handling," check what pool size that evidence actually ran under. With zero explicit configuration, that claim is a claim about behavior at ≤100 concurrent writers (the dialect default), not unbounded concurrency — the governor caps it there whether or not you asked it to. A claim meant to demonstrate genuinely high-concurrency behavior needs to either explicitly raise `MaxConcurrentWrites` (up to 512) or state the tested concurrency level alongside the pool size it ran under, since a green result at N writers against a governor capped at N (or higher) proves less than the same result against a governor capped well below N.

**A second, faster-firing admission gate sits in front of the timeout above.** Each governor also tracks how many callers are queued waiting for a slot and rejects a *new* caller with `PoolSaturatedException` **immediately** — without waiting out `PoolAcquireTimeout` at all — once that count exceeds the cap. This cap **is publicly configurable**, via `DatabaseContextConfiguration.MaxQueuedWrites`/`MaxQueuedReads` (`int?`, validated `>= 0`; `0` disables queueing for that pool entirely, `null` — the default — falls back to the governor's built-in `max(effectiveSlots × 8, 32)`). Under sustained heavy contention you can therefore see two distinct failure latencies for the same exception type: most callers wait out the full timeout, but callers arriving once the queue is already deep enough fail instantly instead.

**This cap also guards the `SingleWriter` turnstile queue, not just slot acquisition.** `EnableSingleWriterFairness`'s reader-fairness gate has its own wait path, separate from the slot-acquisition queue documented above — a stalled writer under sustained read load could otherwise cause unbounded reader pile-up on the turnstile with no circuit breaker. Both queues share the same `MaxQueueDepth`/`MaxQueuedReads`/`MaxQueuedWrites` cap.

## Draining a governor

`PoolGovernor.WaitForDrainAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)` waits until in-use permits reach zero (`TimeoutException` if the optional timeout elapses first). This is a real, already-implemented capability — not currently exposed through `IDatabaseContext` or documented elsewhere — useful for a graceful-shutdown path that wants to wait for in-flight database work to finish before tearing down a context.

## Per-connection prepared-statement cache

`ConnectionLocalState` maintains a per-physical-connection LRU cache of up to 32 prepared-statement "shapes" (keyed on raw `CommandText`, FIFO eviction via a `ConcurrentQueue` once the cap is hit), plus a `PrepareDisabled` flag that permanently turns off `Prepare()` calls for that connection once tripped. This is what backs the MySQL/MariaDB behavior where error 1461/1295 permanently vetoes future prepare attempts on a connection (see the per-database gotchas on the wiki) — the veto is connection-scoped, not process-global, so a fresh connection from the pool gets a clean slate.

## Writer-fairness turnstile limitations

Enabling `EnableSingleWriterFairness` (`SingleWriter` mode only) installs a turnstile semaphore that blocks new reader permits while a writer is waiting to acquire the single write slot. This prevents an unbounded stream of incoming readers from starving a pending writer.

However, the protection is **not retroactive**: readers that were already queued on the semaphore *before* the writer grabbed the turnstile are not displaced and will run first. Under a sustained high-read burst a writer may therefore still wait for a short pre-queued cohort to drain before getting its slot. Starvation is reduced, not eliminated.

To observe this in production, watch `PoolStatisticsSnapshot.TotalTurnstileTimeouts`. If the value climbs, consider reducing `MaxConcurrentReads` or increasing `PoolAcquireTimeout` to give writers more room.

## Duplicate connection-string detection

Two live `DatabaseContext` instances sharing the same connection string in-process is a common accidental-non-singleton bug (see `CLAUDE.md`'s DI Lifetime Rules — `DatabaseContext` must be a singleton). pengdows.crud detects this **regardless of configuration**: by default, constructing a context whose connection string matches another already-live context's logs a warning (`UniqueConnectionStringRegistry`) rather than failing — since a small amount of legitimate overlap is possible and shouldn't hard-crash an app.

Set `DatabaseContextConfiguration.EnforceUniqueConnectionString = true` to upgrade this to a hard `InvalidOperationException` thrown at construction of the second context. The check is all-or-nothing across a context's full set of pool keys (reader and writer pools claimed together, not independently), so a partial collision on just one of them still fails the whole construction.
`InitializePoolGovernors` hashes the writer and reader connection strings to get pooled keys, respects the resolved pool size (including overrides) and the selected `DbMode`, and creates a governor for each pool (except `SingleConnection`, which disables governors entirely). `SingleWriter` mode uses the Standard lifecycle but adjusts the governor so writes serialize with `MaxConcurrentWrites = 1` (and an optional writer-preference turnstile); `PreventDatabaseUnload` retains one permit-backed sentinel for each materially separate pool while application work remains ephemeral. This keeps the governors aware of retained sentinels while still allowing other operations to proceed, and the hashed key ensures each unique connection string gets its own governor scope.
