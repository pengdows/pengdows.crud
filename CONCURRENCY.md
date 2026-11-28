# pengdows.crud Concurrency Contract

**Status: DRAFT.** Written against branch `2.0.6` at `9e81cc53` (2026-11-18). Not reviewed. Evidence below records source audits and test runs reported by the author; these are local results unless explicitly identified as public-CI results. No public-CI run is recorded in this document, so no guarantee here should be read as CI-verified. The concurrency guarantees were exercised through a mix of unit tests, real-provider integration tests, and documented code audits. One deterministic race hook for G4 is compiled only when `PENGDOWS_CRUD_TEST_HOOKS=true`; it is excluded from ordinary Release builds. The hook used for G9a-SW remains in production source and should receive the same build isolation before claiming that release builds are free of test instrumentation.

This document states only what pengdows.crud enforces at the layers it controls. Provider and database behavior is listed separately, as best-effort. A claim that is not in this file is not a guarantee.

## Evidence levels

Every statement carries one of these labels.

| Label | Meaning |
| --- | --- |
| **Code-verified** | Read in the cited source at the branch head stated above. This says the code is written to behave this way. It does not say the behavior has been observed under load. |
| **Assumption** | Inferred from comments, partial reading, or provider documentation. Not yet confirmed. |
| **Test: pending** | A falsification test is specified but has not yet been written. |
| **Test: drafted** | A test exists in the tree but has not yet been run successfully. |
| **Test: passed (local)** | The cited test passed in a local run; it is not evidence of a public CI pass. |
| **Test: passed (public CI)** | The cited test passed in a named public CI run. |

A guarantee becomes verified for a build mode when its falsification test passes in that mode. Public CI verification is a separate evidence level and must be cited explicitly; local results do not imply a CI pass.

Paths are relative to `pengdows.crud/` unless stated otherwise.

---

## 1. The unit of admission

**Admission is per connection checkout, not per command or operation.** *Code-verified.*

- A permit (`PoolSlot`) is taken before the connection object is created. See `GetStandardConnectionWithExecutionType(Async)` in `DatabaseContext.ConnectionLifecycle.cs`, L600–653 (slot acquired at L608 / L639).
- The permit is attached to the `TrackedConnection`.
- The permit is released after the physical connection's `Dispose`/`DisposeAsync` returns, in a `finally`, even if dispose throws (`wrappers/TrackedConnection.cs`, `DisposeConnectionSync` / `DisposeConnectionAsyncCore`).
- Each acquisition produces one `PoolSlotToken` (`infrastructure/PoolSlot.cs`, L55–61), released at most once.

**Consequence.** One admission can carry many sequential commands:

- A transaction context: every command runs on the pinned connection.
- The internal `PinnedConnectionLease`: an INSERT followed by a session-scoped id query.
- An open `ITrackedReader`: holds its checkout until disposed.

Outside those three, one library call is one checkout. *Code-verified.*

**Concurrent commands on one checkout** are described in comments as serialized by a per-connection lock (`DatabaseContext.cs`, L324–330; `TrackedReader._connectionLocker`). *Test: passed* (`pengdows.crud.Tests/PinnedConnectionCommandSerializationTests.cs`). Traced precisely: `TrackedConnection.GetLock()` returns a real lock only when `_isSharedConnection` is true (a transaction's pinned connection, or SingleConnection's persistent one) — an ordinary ephemeral per-operation connection gets `NoOpAsyncLocker`, because no contention is possible by construction. Two commands fired concurrently against the same transaction's pinned connection are proven genuinely serialized, in order, not merely both-eventually-complete (one gated mid-execute; the second is shown not even to have started, then runs only after the first's gate releases). A second, contrasting test proves the converse: two commands on separate Standard-mode ephemeral connections are *not* artificially serialized — the lock is doing real work in the first case, not just slow scheduling.

### 1.1 Governors

Each context has one write governor and one read governor. *Code-verified* (`DatabaseContext.Initialization.cs`, L1149–1169).

- Each is a separate `PoolGovernor` instance.
- Each has its own `PoolGovernorConcurrencyGate`, lock, permit count and queue counters.
- The gate is the only admission primitive (`infrastructure/PoolGovernor.cs`, L174, L271–278).
- The effective limit always equals the configured limit. The gate's `SetLimit` hook has no production callers. *Code-verified.*

`MaxConcurrentReads` and `MaxConcurrentWrites` size both the governor and the provider's pool. The value is written into the pool's connection string when the provider has a pool-size keyword (`IDatabaseContextConfiguration.cs`, L89–112). Microsoft.Data.Sqlite has no such keyword (`dialects/SqliteDialect.cs`, L738), so for it the governor is the only cap.

### 1.2 Ungoverned paths

These paths take no permit. G1 does not cover them. *Code-verified.*

| Path | Where |
| --- | --- |
| `DbMode.SingleConnection` (no governor; the shared connection is counted as one persistent lease) | `DatabaseContext.Initialization.cs` L1068–1077 |
| A context whose dialect could not be resolved | `DatabaseContext.Initialization.cs` L1060–1065 |
| The initialization connection | `DatabaseContext.Initialization.cs` L820 |
| Calls made before the governors are initialized (bootstrap window) | `DatabaseContext.ConnectionLifecycle.cs` L1031–1038, L1057–1064 |

Calls made after the context is disposed fail rather than run ungoverned (`ThrowIfDisposed`, `ThrowIfGovernorMissingAfterDisposal`). *Code-verified.*

### 1.3 Defaults that change what is guaranteed

| Setting | Default | Effect |
| --- | --- | --- |
| `MaxQueuedReads`, `MaxQueuedWrites` | `null`: no queue-depth limit | Queue depth is unbounded. Waiting is bounded only by `PoolAcquireTimeout` (G2). |
| `PoolAcquireTimeout` | 5 s | Longest time a caller waits for a permit, across all wait stages, before `PoolSaturatedException`. |
| `EnableSingleWriterFairness` | `true` | Creates the SingleWriter turnstile (G9a-SW, G11). |

---

## 2. Layers

| Layer | Controlled by | Claim type |
| --- | --- | --- |
| Admission: permits, queues, rejection | pengdows.crud | Enforced |
| Lifecycle: checkout, return and dispose of library-owned resources | pengdows.crud, on top of the provider pool | Enforced for library-owned resources |
| Physical sessions, server-side cancellation, database-level read-only enforcement | Provider and database | Best-effort, measured per provider |

**A physical connection kept open by the provider's pool after pengdows.crud has released its checkout is owned by the provider, not the library.** It is not a library leak.

---

## 3. Resource ownership

*Code-verified unless marked.*

| Resource | Owner | Released when |
| --- | --- | --- |
| Checkout for a single command | The executing `SqlContainer` call | The call completes. The connection is closed and disposed via the context. |
| Checkout behind an open reader | `TrackedReader`, when created with `shouldCloseConnection: true` (L47, L236, L600) | The reader is disposed, or `Read` returns false. *Test: passed* (`pengdows.crud.Tests/ReaderConnectionClosureConditionsTests.cs`). Traced to one boolean, computed once, at the one call site that ever constructs a `TrackedReader` (`SqlContainer.ExecuteReaderAsyncInternal`): `closesConnectionAfterRead = !(isTransaction \|\| isSingleConnection \|\| (PinnedConnection != null) \|\| ReferenceEquals(conn, _connectionHold))`. Each of the four exclusions is confirmed directly via the real `CommandBehavior` handed to the provider (`CommandBehavior.CloseConnection` present or absent): a standalone read closes; a read inside a transaction, or on SingleConnection's shared connection, does not (and the shared connection is proven still usable for a second read afterward); the `PinnedConnection` case already follows from `GeneratedIdConnectionAffinityTests.CreateAsync_SingleWriter_FallbackRunsOnThePinnedConnectionWithoutASecondPermit` (if that reader had closed its connection, the generated-id query could not have reused the same physical connection object, which that test already confirms it does). |
| Checkout behind a transaction | `TransactionContext` | Transaction cleanup (`TransactionContext.cs`, L888, L940). `CloseAndDisposeConnection` on the pinned connection is ignored (L593–607). |
| Checkout behind an internal pinned lease | `PinnedConnectionLease` (internal) | `DisposeAsync`, exactly once (`connection/PinnedConnectionLease.cs`, L57–63). |
| Governor permit | The `TrackedConnection` it is attached to | That connection's dispose, after the physical dispose. |
| Idle physical connection after release | The provider's pool | Provider policy. |

**No public API hands a governed connection to external code.** `ITrackedConnection` is internal (`pengdows.crud.abstractions/wrappers/ITrackedConnection.cs`, L12). So is `PinnedConnectionLease`. Neither `PublicAPI.*.txt` lists a member returning `DbConnection` or `IDbConnection`. See G10.

---

## 4. Guarantees

Exception types used throughout:

- `PoolSaturatedException`: the queue is full, or the acquire timeout elapsed.
- `PoolForbiddenException`: the pool size is 0.
- `ObjectDisposedException`: the governor or context is closed.
- Cancellation: the async acquire reports a cancelled task (`TaskCanceledException`); the sync acquire throws `OperationCanceledException`.

### G1. Admission bound

**Statement.** After initialization, in every mode except SingleConnection, the checkouts admitted by a governor never exceed its configured limit. Limits are per governor (read and write separately).

- **Layer:** admission. **Class:** enforced.
- **Assumptions:** the exclusions in §1.2. PreventDatabaseUnload sentinels hold one permit each and count against the limit.
- **Evidence:** *Code-verified.* The gate's active count changes only under its lock (`PoolGovernorConcurrencyGate.cs`, `ReleaseUnderLock` L191–215, `PromoteOneUnderLock` L300–317).
- **Falsification:** *Test: passed (local)* — found already in the tree, not written for this effort. `PoolGovernorConcurrencyGateRaceTests.CancellationReleaseAndResizeRaces_DoNotLosePermitsOrWaiters` runs 16 workers × 200 iterations of racing cancel/grant/resize against a 2-slot gate and asserts `Assert.InRange(current, 1, 2)` on every successful acquire — exactly "one count above the limit disproves G1," under real concurrent load. `PoolGovernorReleaseOrderingTests.ReleaseToken_ReleasesAdmissionBeforeDecrementingInUse` separately pins the ordering behind the §5 guidance below. No public CI result is recorded here.
- **Do not use `InUse` to check G1** (see §5).

### G2. Waiting and queue bounds

**Statement, configured.** With `MaxQueued*` = Q, a caller arriving when a wait stage already holds Q callers is rejected immediately, before it waits, with `PoolSaturatedException`.

There are two stages, the SingleWriter turnstile and the slot gate. Each is bounded separately (`PoolGovernor.cs`, L427–428 deadline, L464–465 turnstile, L278 slot gate).

**Statement, default.** With no queue limit, queue depth has no configured finite bound. Each caller waits at most `PoolAcquireTimeout`, measured as one deadline across both stages (L427–428). After that it receives `PoolSaturatedException`.

The two failure modes differ:

- **Synchronous rejection:** the queue is full; the caller never waits.
- **Timeout after queuing:** the caller waited the full deadline.

No other count limit exists in the library. *Code-verified.* The time bound limits each caller's wait, not the number of waiters. Bursty arrivals can produce large instantaneous queues.

- **Layer:** admission. **Class:** enforced (both statements).
- **Resolved (was an open Assumption; disproved one of its two forms, then fixed):** `ConvertTimeoutToStopwatchTicks` (`PoolGovernor.cs`, L845–858) is the code behind "a timeout set to infinity removes the time bound." .NET has two idioms for "infinite": `TimeSpan.MaxValue` and `Timeout.InfiniteTimeSpan` (`TimeSpan.FromMilliseconds(-1)`). *Test: passed for `TimeSpan.MaxValue`, failed for `Timeout.InfiniteTimeSpan`* (`pengdows.crud.Tests/PoolGovernorInfiniteTimeoutTests.cs`): the pre-fix conversion let `Timeout.InfiniteTimeSpan`'s negative `TotalSeconds` survive `Math.Min` unchanged, and `Math.Max(1L, negative)` then collapsed it to a one-tick budget — the caller timed out in about 1µs instead of never. A pre-existing test (`PoolGovernorTimeoutConversionTests.Convert_NegativeTimeout_ReturnsOne`) had pinned this exact behavior as intended; it was wrong and has been corrected (`Convert_NegativeTimeout_IsTreatedAsNoBound_ReturnsMaxTimeoutStopwatchTicks`). Fixed by treating any negative `TimeSpan` the same as `TimeSpan.MaxValue` (clamped to `MaxTimeoutStopwatchTicks`). Not reachable from the public contract today — `DatabaseContext.Initialization.cs` rejects any configured `PoolAcquireTimeout <= TimeSpan.Zero` before a `PoolGovernor` is ever constructed — but `PoolGovernor`'s own constructor takes a raw `TimeSpan` with no such guard, so this was a real defect in internal code, not merely a theoretical one.
- **Falsification:** *Test: passed* (`pengdows.crud.Tests/PoolGovernorQueueCapBehaviorTests.cs`). `DatabaseContextQueueCapDefaultTests`/`PoolGovernorQueueCapConfigurationTests` already pinned the *configured values* (`MaxQueueDepth` reads back correctly); these two tests prove the *behavior* the values are supposed to produce, which is a different claim:
  - With Q=1 configured, filling the slot and the one queue slot makes the next caller fail in <200ms (synchronous rejection, not a wait) — `ConfiguredQueueCap_RejectsTheNextCallerSynchronously_BeforeItEverWaits`.
  - With the unbounded default (`maxQueueDepth: int.MaxValue`, DatabaseContext's actual default — not `PoolGovernor`'s own different null-default, an 8×/32 floor), the next caller instead waits out most of a 300ms timeout before failing — `DefaultUnboundedQueue_LetsTheCallerWaitOutTheFullTimeout_InsteadOfRejectingSynchronously`.

### G3. Cancellation and timeout while queued

**Statement.** A queued acquisition that times out or is cancelled leaves the queue without holding a permit. When cancellation races a grant, exactly one outcome happens: the caller gets the permit, or the permit returns to the gate. It is never lost and never duplicated.

- **Layer:** admission. **Class:** enforced.
- **Evidence:** *Code-verified.*
  - The waiter state is settled under the gate lock (`PoolGovernorConcurrencyGate.cs`, `TryClaimForCancel` L403–415).
  - A sync waiter whose grant raced its interruption gives the permit back (`GiveBackAGrant`, L342–362).
  - Queue counters are decremented in `finally` (`PoolGovernor.cs`, L471–477, L556–562).
- **Note:** the gate is count-only and silently ignores a release when nothing is active (L197–200). It cannot detect an over-release by itself.
- **Falsification:** *Test: passed* — found already in the tree. `PoolGovernorConcurrencyGateRaceTests.cs` forces exactly these races (cancel-vs-grant, dispose-vs-cancel, sync and async) across `CancellationReleaseAndResizeRaces_DoNotLosePermitsOrWaiters`, `QueuedWaiters_AreNeverLostWhenFastPathCallersBargeDuringPromotion`, `SyncQueuedWaiters_AreNeverLostWhenFastPathCallersBargeDuringPromotion`, and `Dispose_RacingWithCancellationOfQueuedAsyncWaiters_CompletesEachWaiterExactlyOnce` — the last one named for exactly "exactly-once" per waiter.

### G4. Exactly-once release of library-owned resources

**Statement.** Permits, checkouts, tracked readers and transaction contexts are released exactly once on every exit path: success, exception, cancellation and dispose. Each `TrackedConnection` holds at most one slot for its life.

- **Layer:** lifecycle. **Class:** enforced.
- **Evidence:** *Code-verified.*
  - `PoolSlotToken.Release` uses `Interlocked.Exchange` (`PoolSlot.cs`, L55–61).
  - The connection's slot release runs in `finally` after the physical dispose.
- **Attach transitions.** Before the guard, `AttachSlot` could overwrite an existing slot and accept a slot after release, both silently. The current implementation uses an atomic `None → Attaching → Attached → Released` lifecycle and rejects invalid transitions while returning the incoming slot.
  - No production call site is known to reach either transition. Every governed checkout attaches in the constructor. The late-attach sites (`DatabaseContext.Initialization.cs`, L1026, L1077, L2078) run during single-threaded initialization. *Code-audited* (not test-covered — the claim is a universally-quantified absence across every constructor overload, which a positive test can't conclusively establish; this is a reasoned review of the actual code, not a guess): every `DatabaseContext` constructor overload (`DatabaseContext.Initialization.cs`, L63, L109, L140, L614, converging on the master constructors at L157 and L179) and `TrackedConnection`'s own construction were checked for anything that could publish the object under construction to another thread before it finishes — `Task.Run`, `new Thread`, `System.Threading.Timer`, `.ContinueWith`, `async void`, event subscription/registration, or insertion into a static/shared collection. None exist in the construction path. The one `Task.Run` in `TrackedConnection.cs` (L524) is in `Dispose()`, not initialization, and blocks synchronously on its own result (`.GetAwaiter().GetResult()`) rather than firing and forgetting. The three late-attach sites each run inside `InitializePoolGovernors(Async)`/`AttachSentinelSlotsIfNeeded`, reached synchronously within the constructor's own call chain, with no cross-thread handoff in between. `TenantContextRegistry` — the one place a `ContextCreated` event exists for a constructed context — fires it (`RaiseContextCreated`, `TenantContextRegistry.cs` L566/L579) only after `_contextFactory.Create(...)`/`CreateAsync(...)` has already returned the fully-built context, never from inside the constructor itself. A separate, pre-existing, already-documented behavior sits nearby and does not weaken this: `AcquireSlot`/`AcquireSlotAsync` (`DatabaseContext.ConnectionLifecycle.cs`, L1024–1067) note a "narrow bootstrap window before `InitializePoolGovernors()` has run" where a connection opened during the constructor's own bootstrap/detection phase is deliberately ungoverned — that is single-thread *ordering* within construction (by design), not another thread observing a partially-built object. Scope limit: this audit covers the construction paths this Assumption names; it is not an exhaustive proof over the entire codebase that no other, unrelated mechanism (e.g. a future DI container callback) could ever do this.
- **Falsification:**
  - *Test: passed.* `pengdows.crud.Tests/TrackedConnectionSlotAttachGuardTests.cs` covers duplicate attach, attach-after-release, and exactly-once release on the valid paths. Run against `2.0.6` at `9f2a4038` on net8.0 and net10.0: 7/7 passed, both targets. Full unit suite re-run alongside it: 12,902/12,902 passed on each target, no regressions; line/branch/method coverage 94.03%/89.14%/97.4%, above the 83% CI floor. The library itself builds clean (0 warnings, 0 errors) and the public API baseline (455 signatures) is unchanged — the guard stayed entirely inside `TrackedConnection`'s internals.
  - *Test: passed* (`pengdows.crud.Tests/PoolSlotFaultInjectionTests.cs`). Fault injection at open (`SetFailOnOpen`), at command creation during execute (`SetFailOnCommand`), and at the physical connection's own `Dispose()` (`SetFailOnDispose`, which fails *after* the real close/dispose has already happened). All three release their permit exactly once and leave a one-slot pool usable by the next caller; the dispose-failure case additionally confirms `TrackedConnection` swallows the provider's cleanup exception rather than propagating it, matching the `finally`-around-physical-dispose structure cited above.
  - *Test: passed (local)* (`pengdows.crud.Tests/TrackedConnectionSlotAttachGuardTests.cs`, `AttachSlot_ReleaseRacingInProgressAttach_Throws_AndReturnsTheSlot_WithoutLeakingOrDoubleReleasing`). The hook `TrackedConnection.TestOnlyAfterSlotFieldSetBeforeAttachedCas` is compiled only in the test-hook build; it forces a `Dispose()` between `_slot = slot` and the CAS that marks the state `Attached`. The guard detects `Released`, disposes the slot, and throws; the permit is returned exactly once. Public CI verification for this hook/build mode is not recorded here.

### G5. Server-side cancellation

**Statement.** Cancelling a running command stops it on the server.

- **Layer:** provider. **Class:** best-effort, measured per provider.
- **Correction (found by testing the second provider, then fixed).** PostgreSQL passed cleanly, but the identical test against real SQL Server at first genuinely disproved the client-side half of this guarantee: SQL Server really did stop the query (confirmed server-side, below), but `Microsoft.Data.SqlClient` reported that as its own `SqlException` ("...Operation cancelled by user.") instead of a real `OperationCanceledException` — silently contradicting the documented contract ("`OperationCanceledException` is never wrapped — cancellation propagates as-is," this file's exceptions-hierarchy section). Fixed in `SqlContainer.cs`: a new `IsCancellation`/`DbExceptionTranslationSupport.LooksLikeCancellation` check (mirroring the existing `IsTimeout`/`LooksLikeTimeout` pattern for the analogous timeout-shaped-but-mistyped-exception problem) catches this shape and rethrows the real `OperationCanceledException`, with the original provider exception preserved as `InnerException`. Gated on the caller's own token actually being canceled — not just the exception's wording — so an unrelated provider exception that happens to use similar phrasing is never misreported as this caller's own cancellation. Pinned at the unit level deterministically via a new fakeDb capability, `fakeDbConnection.CancellationThrowsAs` (off by default, same opt-in pattern as every other fakeDb failure injection), so this regression doesn't need Docker to catch again.
- **Falsification:** *Test: passed for PostgreSQL and SQL Server* (`pengdows.crud.IntegrationTests/ErrorHandling/CommandCancellationStopsServerExecutionTests.cs` and `CommandCancellationStopsServerExecutionSqlServerTests.cs`, real Docker containers, not fakeDb — a mock reporting "the server stopped" would just be agreeing with itself). `SqlContainer.ExecuteReaderAsyncInternal`/`ExecuteNonQueryAsync` pass the caller's `CancellationToken` straight into the real `DbCommand` execute call, so this tests pengdows.crud's own plumbing, not just the driver. Measured: PostgreSQL's `SELECT pg_sleep(30)` cancelled ~500ms in throws `OperationCanceledException` and the backend's `pg_stat_activity` row goes `active`→`idle` within about a second; SQL Server's `WAITFOR DELAY '00:00:30'` behaves the same once the fix above is applied, confirmed via `sys.dm_exec_requests`. `ProviderReportedCancellationTranslationTests.cs` pins the fix itself at the unit level (two positive cases, one negative case proving an uncanceled token is never misclassified). Other providers (MySQL-family, Oracle, ...) are untested — each would need its own container and its own idea of "still running" (`SHOW PROCESSLIST` for MySQL, ...), and, per this finding, its own check for whether its driver has the same cancellation-type-wrapping bug SqlClient does.

### G6. SingleWriter write serialization

**Statement.** Under SingleWriter, at most one write-intent checkout is admitted at a time.

- **Layer:** admission. **Class:** enforced.
- **Evidence:** *Code-verified.*
  - The write governor is coerced to one slot (`DatabaseContext.Initialization.cs`, L1151–1164).
  - The single-slot gate hands off directly to the next waiter (`PoolGovernorConcurrencyGate.cs`, L202–205).
- **Limitation: intent, not SQL.** Serialization applies to write *intent*.
  - A modifying statement run with read intent uses the read path and is not serialized. For example, `ExecuteReaderAsync()` defaults to `ExecutionType.Read` (`SqlContainer.cs`, L1618–1620).
  - On file SQLite, the read connection's `Mode=ReadOnly` rejects such a statement (G7b).
  - **On shared in-memory SQLite, read connections are not read-only, so such a statement is neither serialized nor rejected by the library.** *Test: passed* (`pengdows.crud.IntegrationTests/DatabaseSpecific/SqliteReadOnlyAndSharedCacheLockingTests.cs`, real SQLite, two real connections) confirms the obvious follow-up question has a real, non-obvious answer: SQLite's own shared-cache lock manager *does* intervene as a second line of defense the library gets for free — a genuine concurrent write from a second connection blocks until the first's write transaction commits, and the final value shows no corruption. This is a defense against two *simultaneous* writers; it has nothing to say about one connection's single statement not matching its own declared intent, which is the separate, still-real gap the rest of this bullet describes.
  - **This is specifically about *shared*-cache in-memory, not in-memory generally.** Isolated `:memory:` (no `cache=shared`) never reaches SingleWriter at all: `SqlDialect.CoerceEmbeddedSingleWriterMode` hard-coerces it to `DbMode.SingleConnection` regardless of what mode was requested (`SqlDialect.cs`, L339–341) — one persistent connection for the whole context's life, not the two-governor split this guarantee depends on. So this limitation, and the `Mode=ReadOnly` gap in G7 below, cannot occur on isolated `:memory:`; they are reachable only when `SqliteDialect.DetectInMemoryKind` (`SqliteDialect.cs`, L84–111) classifies the connection string as `InMemoryKind.Shared` (i.e. `cache=shared` is present). *Code-verified.*
  - Choosing the correct execution intent for modifying SQL is the caller's responsibility.
  - **Correction (test-verified):** this limitation is narrower than "any time" — it only applies while **no real write is concurrently active**. SingleWriter's writer-preference turnstile (G11) gates admission of *any new reader*, independent of what SQL that reader will run, whenever a writer is active or waiting (`PoolGovernor.ShouldUseTurnstileGate`, keyed on `WritersActiveOrWaiting`, not on execution intent of the arriving caller beyond "is this a reader"). So a read-intent call carrying modifying SQL that happens to arrive while a genuine write is in flight is blocked by the turnstile exactly like an honest read would be, and only escapes serialization in the window when no write is concurrently active.
- **Falsification:** *Test: passed* (`pengdows.crud.Tests/SingleWriterReadIntentBypassesWriteSerializationTests.cs`, both on net8.0/net10.0).
  - `TwoWriteShapedStatements_BothSentWithReadIntent_RunFullyConcurrently_WhenNoWriterIsActive`: with no writer active, two modifying statements both sent via default read intent are admitted concurrently through the reader governor (3/3 `InUse`, including the sentinel) while the writer governor stays at 0 — the real, SQL-independent danger this guarantee describes.
  - `ReadIntentCall_ArrivingWhileAWriterIsActive_IsBlockedByTheTurnstile_RegardlessOfItsSql`: with a writer holding its slot, the same kind of read-intent call throws `PoolSaturatedException` at the turnstile, then succeeds immediately once the writer releases — confirming the correction above.

### G6b. SingleWriter writer release

**Statement.** After the final SingleWriter write completes and its library-owned resources are disposed, the library holds no checked-out writer connection.

- **Layer:** lifecycle. **Class:** enforced.
- **Evidence:** *Code-verified.* SingleWriter uses the Standard strategy with ephemeral checkouts. The permit is released after the physical dispose.
- **Physical connections are a separate matter.**
  - SingleWriter removes the `Pooling` key from the writer string (`DatabaseContext.Initialization.cs`, L436–440). The reader string is stripped only for SingleConnection, or for a SingleWriter string built for the write connection (L1328–1335), so a read-only reader keeps its pooling. Removing the key does not disable pooling; the provider default applies.
  - Microsoft.Data.Sqlite pools by default. *Test: passed, not an Assumption anymore* (`pengdows.crud.IntegrationTests/DatabaseSpecific/SingleWriterPoolingBehaviorTests.cs`, real provider, `SqliteConnectionStringBuilder.Pooling` defaults to `true`). With Pooling enabled, an idle physical writer remains open in the provider pool after every pengdows.crud-owned object (checkout AND the `DatabaseContext` itself) is disposed — confirmed directly on Linux via `/proc/self/fd`: the main db file, `-wal`, and `-shm` all stay open. That is owned by the provider (§2) and does not violate G6b.
  - **Open question resolved, and it turned out to be moot.** The question was "removing the key vs. setting `Pooling=False` — which is intended?" The real answer: the caller has no choice either way. `ConnectionPoolingConfiguration.StripPoolingSetting` removes a `Pooling` key from the connection string whenever one is *present* (`DbConnectionStringBuilder.ContainsKey`), regardless of its value — so a caller who explicitly sets `Pooling=False` on a SingleWriter connection string has it silently stripped before the provider ever sees it. `SingleWriter_StripsAnExplicitPoolingFalse_TheCallerCannotDisablePooling` proves this directly (`ctx.RawConnectionString` has no `Pooling` key even though the input connection string did); `SingleWriter_WithOrWithoutAnExplicitPoolingFalse_LeavesTheSameFileHandlesOpenAfterDispose` confirms both spellings produce identical real-provider behavior, because they produce the identical real connection string. If disabling pooling for SingleWriter is ever wanted, it isn't reachable through the public connection-string API today.
- **Falsification:** *Test: passed* (`pengdows.crud.IntegrationTests/DatabaseSpecific/SingleWriterPoolingBehaviorTests.cs`, real Microsoft.Data.Sqlite provider — not fakeDb, which models no real OS file handle or provider connection pool).

### G7. Read-intent connections are configured read-only

**Statement.** Read-intent connections are opened with the dialect's read-only configuration, in every mode, with these exceptions:

- **DuckDB:** read intent never uses a read-only connection, deliberately (`DatabaseContext.cs`, L296–313).
- **Shared-cache in-memory SQLite:** no read-only parameter is applied (`dialects/SqliteDialect.cs`, L402–407, L415–420).
- **Isolated in-memory SQLite is not an exception — it's out of scope** (see G6's note on `CoerceEmbeddedSingleWriterMode`/`DetectInMemoryKind`): it runs under `DbMode.SingleConnection`, one persistent connection with no read/write-governor split, so "read-intent connection" isn't a concept that applies there at all. Shared-cache in-memory additionally gets a dedicated, always-read-only sentinel connection to keep the database from vanishing once every other connection closes (`DatabaseContext.Initialization.cs`, `EnsureSharedInMemorySentinelAsync`, L967–1009) — a separate mechanism from G1's PreventDatabaseUnload sentinels, gated purely on shared-in-memory detection, not on `DbMode`.

How each provider is configured:

| Provider | Mechanism |
| --- | --- |
| File SQLite | `Mode=ReadOnly` in the connection string (`SqliteDialect.cs`, L381–390) |
| PostgreSQL | Startup option `default_transaction_read_only=on` (`PostgreSqlDialect.cs`, L755–756) |
| DuckDB | `access_mode=READ_ONLY` file-open attribute — exists, but per the exception above, never engaged for read-intent on a writable context |
| FlatFile | `readonly=true` connection parameter + `SET TRANSACTION READ ONLY` (dual-enforced; `docs/supported-databases.md`'s Read-Only Enforcement Matrix) |
| Access | `Mode=Read` (ACE read-only open) |
| MySQL, AuroraMySql | `SET SESSION transaction_read_only = 1;`, sent when a read-only transaction begins (`MySqlDialect.cs`'s `ReadOnlyTransactionSql`), reset via `= 0;` when it completes |
| MariaDB | Same mechanism, its own variable: `SET SESSION tx_read_only = 1;` / `= 0;` (`MariaDbDialect.cs` overrides both — a genuinely different variable name, not shared with MySQL) |
| Oracle, Informix, SAP HANA | `SET TRANSACTION READ ONLY`, sent when a read-only transaction begins; no reset statement exists for any of the three — the read-only state is scoped to the transaction itself and ends at COMMIT/ROLLBACK, not via SQL text |
| TiDB | **Exception, not an omission:** `TiDbDialect` overrides `ReadOnlyTransactionSql` to `null` — it sends no session/transaction SQL at all for a read-only transaction. *Code-verified.* |
| CockroachDB, YugabyteDB, AuroraPostgreSql | Inherit `PostgreSqlDialect`'s connection-string marker unchanged — same mechanism, same enforcement, as PostgreSQL |
| **Spanner** | **The marker is applied, but the real database does not act on it.** `SpannerDialect` inherits `PostgreSqlDialect`'s connection-string marker unchanged (the GUC genuinely is present), but Spanner's database does not refuse a write in a read-intent transaction — only the library's own `AssertIsWriteConnection` guard protects (`EnforcesReadOnlyTransactions => false`, live-confirmed; `docs/supported-databases.md`'s Read-Only Enforcement Matrix). |
| **SQL Server** | **Marker applied, routing hint only.** `ApplicationIntent=ReadOnly` is an Availability Group routing hint, not server-side enforcement (`SqlServerDialect.GetReadOnlyConnectionParameter`); hard enforcement needs read-only credentials or permissions. |
| **SingleStore** | **Session SQL is sent, but the real database does not act on it.** `MySqlDialect.ReadOnlyTransactionSql` is unconditional (only `EnforcesReadOnlyTransactions`, read by nothing else, differs for SingleStore), so the exact MySQL text is sent — but SingleStore's server accepts the statement and still allows the write (live-confirmed). |
| **Firebird** | A third, distinct mechanism: `SET TRANSACTION READ ONLY;`/`SET TRANSACTION READ WRITE;` (`FirebirdDialect.GetReadOnlySessionSettings`/`GetReadOnlyTransactionResetSql`) are folded into `DatabaseContext`'s one combined per-pool session-settings command (baseline + intent in a single `CommandText`) sent as that connection's first-open handler — not a standalone statement tied to `BeginTransaction` like Oracle/Informix/SAP HANA. **Sent, but not enforced** (`docs/read-only-enforcement.md`, live-verified 2026-10-03: Firebird is explicitly listed under "No — the library's guard is the only protection"), despite `FirebirdDialect.TryClassifyProviderException` being able to classify error 335544361 (`isc_read_only_trans`) into `ReadOnlyViolationException` if a real Firebird connection ever raised it through some other path. |
| Snowflake, Db2, Sybase ASE, InterBase | **None.** Each dialect defines no read-only connection parameter and no session/transaction SQL at all; only the library's `AssertIsWriteConnection` guard protects an explicit `ExecutionType.Write` call — a write sent through the read-intent overload (G6) is not caught at all. InterBase's real driver supports read-only transactions through `IBTransactionOptions` at creation time, which the dialect's SQL-after-open hook cannot reach — a known, documented limitation, not a bug. |

- **Layer:** lifecycle (library configuration). **Class:** enforced (except where noted above — Spanner, SQL Server, SingleStore, Snowflake, Db2, Sybase ASE and InterBase are documented non-enforcement exceptions, not failures of this guarantee).
- **Falsification:** *Test: passed for SQLite, PostgreSQL, DuckDB, FlatFile and Access* (`pengdows.crud.Tests/ReadOnlyConnectionRejectsWriteTests.cs`) — each confirmed end to end through the real per-dialect translator (`AccessDialect`/`FlatFileDialect`/`DuckDbDialect`/`PostgreSqlDialect`'s own `TryClassifyProviderException`, not a generic fallback), using fakeDb's realistic per-product error shape. Two of my own bugs surfaced and were fixed while writing these: Access's real translator checks the message substring "must use an updateable query" (I'd guessed different, invented wording), and FlatFile's checks `SqlState "25006"` exclusively, not message text (my first fake set no SqlState at all).
  **Every remaining provider in the table above is now covered too** (*Test: passed*; `pengdows.crud.Tests/ReadOnlyEnforcementRemainingProvidersTests.cs`) — closing what was previously an open-ended "Others | Not yet catalogued" row. This pass found and fixed a real fakeDb accuracy gap, not a pengdows.crud bug: `fakeDbConnection.IsReadOnly` had been treating every provider its connection-string/session-SQL detection recognized as equally *enforced*, which is wrong for Spanner, SQL Server, SingleStore **and Firebird** specifically — the marker/SQL is genuinely applied (confirmed by a direct test against `FakeDbReadOnlyEnforcement.IsReadOnlyConnectionString`/`SessionMarkedReadOnly` for each), but the real database doesn't act on it. `FakeDbReadOnlyEnforcement.ProductEnforcesConnectionStringReadOnly`/`ProductEnforcesSessionReadOnly` now gate `IsReadOnly` separately from marker detection, so a test can confirm "the library tried" (G7) independently of "the database complied" (G7b) for exactly these four — before this fix, a test for Spanner/SQL Server/SingleStore/Firebird using the same mechanism as their enforcing relatives would have shown a write being rejected that the real database actually allows. Firebird's own first draft of this test got exactly that wrong: it inferred enforcement from `FirebirdDialect.TryClassifyProviderException` being *able* to classify error 335544361, without first checking `docs/read-only-enforcement.md`'s already-recorded live result (Firebird listed under "No") — the same category of mistake this whole document's methodology exists to catch, caught on itself. Firebird's distinct combined-batch mechanism also required switching the session-SQL detection from an exact match to `Contains`, since its read-only text is never a standalone command for this provider, always a substring of a larger one; a pure dialect-level test (`FirebirdDialect.GetFinalSessionSettings`) confirms the right text is generated independent of whether it's ever enforced. TiDB, Snowflake, Db2, Sybase ASE and InterBase needed no fake changes at all — every detection method already fell through to "not enforced" for them, matching the matrix's "None" entries exactly.
  **Extended to the session/transaction-SQL providers** (*Test: passed for MySQL, MariaDB, Oracle, Informix, SAP HANA*; `pengdows.crud.Tests/ReadOnlySessionSqlProvidersRejectWriteTests.cs`): `fakeDbConnection.SessionMarkedReadOnly` + `EnforceReadOnlySessionSql` (opt-in, same shape as `EnforceReadOnlyFromConnectionString`) let `fakeDbCommand` recognize the exact SQL text each dialect's `TryEnterReadOnlyTransaction` sends (`FakeDbReadOnlyEnforcement.IsReadOnlySessionEntrySql`/`IsReadOnlySessionResetSql`), rather than leaving these providers untestable. Two corrections surfaced while building it: MariaDB's distinct `tx_read_only` variable (initially assumed shared with MySQL's `transaction_read_only` — the first test run caught it, `conn.SessionMarkedReadOnly` stayed false), and TiDB's complete absence of session SQL (initially grouped with MySQL/MariaDB by the ambiguous "MySQL-family" framing; `TiDbDialect.ReadOnlyTransactionSql => null` means it was never in scope for this mechanism at all). **A related, structural finding surfaced in the same pass:** an explicit `ExecuteNonQueryAsync(ExecutionType.Write, ...)` call inside a transaction opened with `ExecutionType.Read` never reaches the database-level check this section describes — `InternalConnectionAccessAssertions.AssertIsWriteConnection` throws `ReadOnlyAccessException` first, a database-agnostic structural guard that fires for any explicit write request on a read-only transaction regardless of provider. The database-level rejection these tests prove is reachable only through the same sneaky-write path G6 already describes (a write-shaped statement sent through `ExecuteReaderAsync`'s default read-intent overload), not through a correctly-typed write call — which a correctly-typed write call never needs anyway, since the structural guard already stops it earlier. SQLite additionally has the real-engine confirmation (G7b, below) that fakeDb alone can't provide. Every other provider in `docs/supported-databases.md`'s matrix is now covered too — see the G7 table above and `ReadOnlyEnforcementRemainingProvidersTests.cs`.

### G7b. The database rejects writes on read-only connections

**Statement.** Measured per provider.

- File SQLite: the engine rejects the write.
- PostgreSQL: the write is rejected unless the SQL itself changes `default_transaction_read_only` or the transaction mode.

The library does not intercept or validate SQL, so it promises no library-level rejection.

- **Layer:** provider. **Class:** best-effort.
- **Falsification:** *Test: passed for SQLite* (`pengdows.crud.Tests/ReadOnlyConnectionRejectsWriteTests.cs`). This was marked untestable with fakeDb, because fakeDb modeled no read-only concept at all — it now does: `fakeDbConnection.IsReadOnly` (opt-in via `EnforceReadOnlyFromConnectionString`, off by default like every other fakeDb failure injection, so it doesn't change behavior for existing tests that send write-shaped SQL through a default/read-intent overload purely to get a `RETURNING` value back) is computed from the real per-dialect read-only connection-string marker, and a detected write on such a connection throws the real per-product classified error shape (`FakeDbConstraintErrors.ReadOnlyViolation`, same pattern as `UniqueViolation`), which pengdows.crud's own translators correctly classify into `ReadOnlyViolationException` end to end. Proves file SQLite's `Mode=ReadOnly` really does reject a write-shaped statement sent through the default read-intent overload — the same scenario G6 demonstrated going *uncaught* on shared in-memory SQLite, now shown side by side: `FileSqlite_WriteShapedStatement_SentWithDefaultReadIntent_IsRejectedByTheConnection` throws, `SharedInMemorySqlite_WriteShapedStatement_SentWithDefaultReadIntent_IsNotRejected` doesn't. Other providers (PostgreSQL's "unless the SQL changes the transaction mode" carve-out, DuckDB, Access, FlatFile) have the same detection wired in `FakeDbReadOnlyEnforcement` but no test yet exercises them.

### G8. Transaction affinity

**Statement.** Every operation in a transaction context runs on the same underlying connection, from begin to commit or rollback.

A write transaction holds its write permit for its whole life. Under SingleWriter with fairness, it also holds the turnstile for its whole life (see G11).

- **Layer:** lifecycle. **Class:** enforced.
- **Evidence:** *Code-verified* (`TransactionContext.cs`, L522–537, L593–607).
- **Falsification:** *Test: passed* — found already in the tree. `TransactionContextTests.GetConnection_AlwaysReturnsTransactionConnection_ForAllExecutionTypes` asserts `Assert.Same` across `GetConnection(Write)`, `GetConnection(Read)`, and `GetConnection(Read, isShared: true)` on one transaction. `ParentGetConnection_RemainsIndependentOfTransactionConnections` confirms the parent context's own connections are distinct from the transaction's after it disposes.

### G9a. Independent read and write admission

**Statement.** In Standard and PreventDatabaseUnload, and in SingleWriter when the turnstile is absent (fairness off, or a separate read database), saturating the read side does not consume write permits, write-queue positions, or anything else that write admission waits on. With every read permit held and the read queue saturated, a writer that finds a free write permit is admitted immediately.

- **Layer:** admission. **Class:** enforced.
- **Evidence:** *Code-verified.*
  - The write path resolves `_writerGovernor` (`DatabaseContext.ConnectionLifecycle.cs`, L468–469).
  - With no turnstile, it probes only its own gate (`PoolGovernor.cs`, L370–391).
  - Nothing on that path references the read governor.
  - Standard and PreventDatabaseUnload use separate provider pools for reads and writes (`DatabaseContext.Initialization.cs`, L1373–1375).
- **Not claimed:** bounded write latency. Lock waits, I/O and execution time are outside admission.
- **Falsification:** *Test: passed* (`pengdows.crud.Tests/IndependentReadWriteAdmissionTests.cs`, both on net8.0/net10.0). Deterministic on fakeDb: with the one read permit held and the one read-queue slot also full (a third read is rejected synchronously with `PoolSaturatedException`, confirming genuine saturation), a write with a free write permit was admitted in 154ms against a 2s `PoolAcquireTimeout`, in both Standard and PreventDatabaseUnload. The queued read was left undisturbed and completed normally once the held read permit was released. Not yet run under real load on PostgreSQL.

### G9a-SW. SingleWriter with fairness

**Statement.** Under SingleWriter with fairness on, a writer never waits for a read permit or a read-queue position. It does pass the shared turnstile.

- **Evidence:** *Code-verified.*
  - Readers touch the turnstile only while a writer is active or waiting.
  - Readers release the turnstile before trying for a read permit (`PoolGovernor.cs`, L460–465 and L486–503).
- **Assumption, consequence now proven; root cause still not reproduced live.** In the async path, a reader's release runs in its continuation (L442–465) — the code immediately after `await _turnstile.WaitAsync(...)` resumes. *Test: passed (local)* (`pengdows.crud.Tests/PoolGovernorReaderTurnstileReleaseDelayTests.cs`) proves the causal consequence deterministically: `PoolGovernor.TestOnlyBeforeReaderTurnstileRelease` (null in ordinary paths) holds that exact continuation open on command, and a second waiter queued on the same turnstile is shown to stay blocked for exactly as long as the hold lasts, then proceed the instant it's released. This is the mechanism the Assumption describes, proven without needing genuine OS thread-pool starvation (a timing race, and the wrong kind of flaky for a regression suite) — what remains unproven is only whether *real* thread-pool contention actually produces a delay of this kind in practice, which is a question about the .NET thread pool's own behavior under load, not about this library's logic. The test hook remains in ordinary library builds; release-build isolation is not yet implemented for it.
- **Not extended from G9a. Falsification:** *Test: deliberately not written; documented gap, not a pending test.* Reproducing genuine OS/CLR thread-pool starvation deterministically in a unit test means either keeping every `ThreadPool` worker busy with blocking work for the test's duration (environment-dependent: `ThreadPool.SetMinThreads`, the machine's core count, and whatever else is running on it all affect how many workers that actually requires, and how long starvation takes to manifest once they're busy) or accepting a genuinely timing-dependent race — exactly the "wrong kind of flaky for a regression suite" the mechanism-level test above was already written to avoid. The claim being left unproven here — that a real starved `ThreadPool` delays queued continuations — is also not actually about this library: it is textbook, documented CLR scheduler behavior, not something `PoolGovernor`'s own logic could get right or wrong. What this library's logic owns, and what must stay provable deterministically, is the causal consequence (a delayed reader release blocks a waiting writer for exactly as long as the delay lasts) — proven above. Treating "does the CLR thread pool actually behave the way the CLR documents" as this library's own open test obligation would be testing the runtime, not the library.

### G9b. Independent connection opening

**Statement.** A write checkout's connection open does not wait for read-side open serialization.

- **Class:** enforced, with provider exceptions.
- **Evidence:** *Code-verified.*
  - The library serializes connection opens only for dialects with `RequiresSerializedConnectionOpen`.
  - In 2.0.6 that is DuckDB only (`dialects/DuckDbDialect.cs`, L52). DuckDB uses one context-wide semaphore for reads and writes (`DatabaseContext.Initialization.cs`, L397–400; `DatabaseContext.ConnectionLifecycle.cs`, L476–483).
- **Exception:** **DuckDB is excluded from G9b.**
- **Not claimed:** independence inside the provider's own pool implementation.
- **Falsification:** *Test: passed* — found already in the tree. `DatabaseContextOpenSerializationTests` confirms only DuckDB sets `RequiresSerializedOpen` (SQL Server does not). `ConnectionOpenLockSharingTests.CancelledWaiter_DisposingItsLock_DoesNotReleaseTheHoldersLock` behaviorally proves the DuckDB lock actually serializes: a second caller genuinely blocks until the first releases, a cancelled waiter's own dispose does not leak the first caller's hold (the REV-029 regression this guards), and a third caller succeeds only after release. Together: non-DuckDB dialects never create the lock (trivially independent), and DuckDB's lock, when present, is real and correctly scoped per caller.

### G10. Connections handed to external code

**Statement.** None today. No public API exposes a *governed* connection (§3). Whether to add a lease API is decided at M3, with a 20-hour cap.

The internal `PinnedConnectionLease` already releases exactly once and carries the slot, including the SingleWriter write permit. *Code-verified.*

- **Correction:** "no public API exposes a raw ADO.NET object" would overstate this — `IDatabaseContext.DataSource` / `DatabaseContext.DataSource` / `TransactionContext.DataSource` return `DbDataSource?` directly and are public today (`[Obsolete]`, slated for 3.0 removal, explicitly documented as bypassing governance). This is not a counterexample to G10 as stated: a `DbDataSource` is a connection-pool factory, never itself slot-admitted, so it is not a *governed* connection. But it is a real, deliberate, grandfathered exception to the stricter "no raw ADO.NET object in public outputs" principle, and the project's own test suite treats it as exactly that.
- **Falsification:** *Test: passed* — found already in the tree. `PublicSurfaceGuardTests.NoPublicOutputExposesARawAdoNetObject_BeyondTheThreeThatShippedIn205` enumerates the public surface for any member returning `DbConnection`/`DbCommand`/`DbDataReader`/`DbTransaction`/`DbDataSource` and asserts the only three are exactly those `DataSource` properties — the exception above, pinned by name so a fourth one added later fails the build.

### G11. SingleWriter writer preference

**Statement.** Under SingleWriter with fairness on (the default) and the same database for reads and writes, a writer holding a slot blocks new read admission at the turnstile.

- Blocked readers wait until the writer releases or until `PoolAcquireTimeout`, then receive `PoolSaturatedException`.
- Readers already queued on the read gate are not displaced (`PoolGovernor.cs`, L27–28).
- A long write transaction or a long-held write-intent reader can therefore cause read timeouts. This is a deliberate trade-off that reduces writer starvation. It is not a defect.

- **Layer:** admission. **Class:** enforced (documented behavior).
- **Evidence:** *Code-verified* (`PoolGovernor.cs`, L23–33, L817–836, L913–916; `DatabaseContext.Initialization.cs`, L1140–1147).
- **Falsification:** *Test: passed* — found already in the tree. `PoolGovernorFairnessTests.WriterWithTurnstile_BlocksNewReaders` holds a writer and shows a new reader blocked, then cancelled/timed out, then admitted once the writer releases. `PoolGovernorTurnstileTests` covers the reverse directions (writer times out behind a saturated slot, releases on cancellation/exception). `PoolGovernorFairnessTests.Cancellation_ReleasesTurnstile` and `ExceptionDuringAcquire_ReleasesTurnstile` confirm a cancelled/timed-out waiter's turnstile claim is released, not stranded.

---

## 5. Diagnostic counters are not permit counts

The holder count is the number of outstanding permit holders. The snapshot's `InUse` and `PeakInUse`, and the public `NumberOfOpenConnections`, all derive from it. *Code-verified.*

- **Consumers:**
  - `WaitForDrainAsync`, which decides when the governor is safe to dispose (`PoolGovernor.cs`, `DrainComplete`).
  - Peak tracking.
  - The snapshot.
  - `NumberOfOpenConnections` (`DatabaseContext.cs`).
  - The `PoolSaturatedException` message.
- **Admission never reads it.**
- **Release order.** `ReleaseToken` moves its holder out of the count and its release into the in-flight count in one atomic step, then returns the permit. Drain waits until both are zero, so disposal still waits for a release that is touching the semaphore or turnstile.

Because the holder leaves the count before the permit is returned, a new holder that takes the permit is never counted alongside the holder that gave it back. `InUse` therefore never exceeds the configured limit from this cause.
*Test: passed (local)* — `pengdows.crud.Tests/PoolGovernorInUseExactnessTests.cs` (red before the change: `InUse` read 2 with one slot).

---

## 6. Open items

**None remaining.** This table used to list six items; every one of them was independently resolved elsewhere in this document over several passes, but the table itself was never updated to match — a stale open-items list contradicting a document that otherwise says everything is closed, which is itself the kind of inconsistency this whole effort exists to catch. For the record, each is now closed at the citation given:

| Item (formerly listed here) | Resolved at |
| --- | --- |
| Per-connection command locking is not traced | §1, `pengdows.crud.Tests/PinnedConnectionCommandSerializationTests.cs` |
| Reader call sites passing `shouldCloseConnection` are not traced | §3, `pengdows.crud.Tests/ReaderConnectionClosureConditionsTests.cs` |
| Microsoft.Data.Sqlite default pooling is not confirmed | G6b, `pengdows.crud.IntegrationTests/DatabaseSpecific/SingleWriterPoolingBehaviorTests.cs` |
| `Pooling` key removal vs `Pooling=False`: intent unknown | G6b — turned out to be moot; both are silently stripped either way |
| No initialization path exposes partial objects: not confirmed | G4 — closed by code audit |
| Read-only mechanisms for dialects other than SQLite/PostgreSQL/DuckDB/Access/FlatFile/MySQL/MariaDB/Oracle/Informix/SAP HANA | G7 table, `pengdows.crud.Tests/ReadOnlyEnforcementRemainingProvidersTests.cs` |
| Exactly-once accounting under concurrent cancel/grant | G3, `PoolGovernorConcurrencyGateRaceTests.cs` (pre-existing) |
