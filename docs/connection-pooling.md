# Connection Pooling and Governors

## Pooling defaults
`DatabaseContext` automatically rewrites connection strings for Standard, PreventDatabaseUnload, and SingleWriter modes so sockets and clients stay in a managed pool. `ConnectionPoolingConfiguration.ApplyPoolingDefaults` detects whether the provider supports external pooling, skips raw connection strings (like `:memory:` or a bare file path), and forces `Pooling=true` when the flag is missing. It does not inject a `Min Pool Size` value in `Standard` or `SingleWriter` mode—if you want a minimum, set it explicitly in your connection string. `PreventDatabaseUnload` is the one exception (see below). These defaults respect your existing pooling settings when you have custom values or pooling is intentionally turned off.

## Pool governors
The context creates read and write `PoolGovernor` instances in every mode except `SingleConnection`, issuing `PoolSlot` tokens before any connection is acquired. Each governor waits no longer than `PoolAcquireTimeout` (default 5 seconds via `DatabaseContextConfiguration.PoolAcquireTimeout`) before throwing a `PoolSaturatedException` with queue and slot statistics, so you fail fast instead of saturating the provider pool. Override `MaxConcurrentReads`/`MaxConcurrentWrites` to tune effective limits, and observe the snapshots if you need to correlate hot paths with pool contention.

When `MaxConcurrentReads` or `MaxConcurrentWrites` is explicitly configured and differs from the connection string's `Max Pool Size`, the configuration property wins for both the governor and the rewritten provider connection string, and pengdows.crud logs a warning containing both values and the winning value. If the configuration property is absent, the connection-string value wins over the dialect default.

A caller-supplied `Min Pool Size` is clamped to `[0, Max Pool Size]`. In `PreventDatabaseUnload` mode every enabled pool (writer and reader) is raised to a maximum of at least `2` — one permit for the sentinel and one for real work — with a warning when a smaller value was requested, and receives a provider `Min Pool Size` of at least `2`. A read-only writer pool keeps maximum and minimum `0`.

Setting `MaxConcurrentWrites=0` promotes the context to `ReadOnly` in every `DbMode` (not only `SingleWriter`), with a warning. This is equivalent to explicitly selecting a read-only context: the writer governor is forbidden while the reader pool remains enabled.

## Writer-fairness turnstile limitations

Enabling `EnableSingleWriterFairness` (`SingleWriter` mode only) installs a turnstile semaphore that blocks new reader permits while a writer is waiting to acquire the single write slot. This prevents an unbounded stream of incoming readers from starving a pending writer.

However, the protection is **not retroactive**: readers that were already queued on the semaphore *before* the writer grabbed the turnstile are not displaced and will run first. Under a sustained high-read burst a writer may therefore still wait for a short pre-queued cohort to drain before getting its slot. Starvation is reduced, not eliminated.

To observe this in production, watch `PoolStatisticsSnapshot.TotalTurnstileTimeouts`. If the value climbs, consider reducing `MaxConcurrentReads` or increasing `PoolAcquireTimeout` to give writers more room.

## Pinned connections keep permits
`InitializePoolGovernors` hashes the writer and reader connection strings to get pooled keys, respects the resolved pool size (including overrides) and the selected `DbMode`, and creates a governor for each pool (except `SingleConnection`, which disables governors entirely). `SingleWriter` mode uses the Standard lifecycle but adjusts the governor so writes serialize with `MaxConcurrentWrites = 1` (and an optional writer-preference turnstile); `PreventDatabaseUnload` retains a slot for its sentinel connection via `AttachPinnedSlotIfNeeded`. This keeps the governors aware of the pinned connection while still allowing other operations to proceed, and the hashed key ensures each unique connection string gets its own governor scope.
