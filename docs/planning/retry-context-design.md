# RetryContext Subsystem — Design, Shortcomings, and Comparison (FEAT-001)

**Status: designed, zero implementation — and not yet ready to implement.** Nothing described in
this document exists in code yet, and the "Shortcomings" section below identifies a blocking issue
(commit ambiguity) that needs a decided policy before backoff mechanics should be built at all.
Tracked as `FEAT-001` in [`future-work.md`](./future-work.md)'s tracker (section 10); the original
design write-up lives in that same file under "RetryContext Subsystem (Governor-Aware Resilient
Execution)" and is reproduced/expanded here. [`exception-analysis.md`](../exception-analysis.md)'s
"Retry-policy boundary" section points readers here for the full picture; it also shows the
application-level retry loop you'd have to hand-write today, since none of this exists.

Grounded against the current `3.0` branch by direct source inspection, not assumed from the design
prose — each factual claim below about what exists today cites the exact file it was checked
against. This revision folds in a second review pass that caught a genuinely new, serious gap
(commit ambiguity, below) and corrected an overstated claim in the first draft about what an
external tool can and can't do.

## The problem this is meant to solve

`AnalyzeException`/`DatabaseException.IsTransient` already tell a caller *whether* retrying might
help (deadlock, serialization failure, command timeout → yes; a unique/FK/not-null violation → no
— see `exception-analysis.md`). Nothing in the library acts on that signal. An application that
wants retry-with-backoff has to write its own loop, and naive versions of that loop create real
operational failure modes specific to a governed connection pool:

1. **Permit holding and governor slot saturation during backoff.** `PoolGovernor` governs
   *admission* toward the provider pool by limiting concurrent operations (a `SemaphoreSlim`) and
   fast-rejecting excessive queues (`MaxQueueDepth`) — verified:
   `pengdows.crud/infrastructure/PoolGovernor.cs`. Once a caller holds a `PoolSlot`, `PoolGovernor`
   has no control over how long it's held. A naive retry loop that catches a transient exception
   and sleeps *while still holding* an active `ITransactionContext` (and its pinned
   `ITrackedConnection` and its `PoolSlot`) keeps that permit occupied for the whole sleep. Under load,
   this is how a handful of transient deadlocks turns into other, unrelated callers being rejected
   with `PoolSaturatedException` — the governor is doing its job (protecting the provider pool),
   but throughput collapses because permits are pinned by idle, sleeping tasks rather than active
   work.
2. **Server-side lock contagion.** If a transaction catches a transient deadlock or serialization
   conflict but sleeps *before* calling `Rollback()`, it keeps whatever server-side row/page locks
   it held open on the database engine for the duration of the sleep — extending, not resolving,
   the contention that caused the transient error in the first place. (This is a different
   mechanism from `docs/positioning/product-thesis.md`'s SQLite `busy_timeout`-vs-turnstile
   discussion — that one is about `SQLITE_BUSY` polling on a single file, not server-side row
   locks — but the underlying design principle it argues for, coordinated admission over blind
   retry, is the same one this section is applying to a client-server engine's locks.)
3. **Thundering herds on wake-up.** If every caller's retry loop uses the same fixed or
   lightly-jittered backoff, a burst of simultaneous transient failures produces synchronized retry
   waves that hit the database at the same moments, extending or worsening contention instead of
   resolving it.

### Why this has to be a first-party feature, not an external wrapper — narrowed

The first draft of this document claimed `PoolGovernor` being `internal sealed` made it
*structurally impossible* for an external tool like Polly to avoid failure mode #1. That overstated
it. A carefully-scoped external wrapper **can** be pool-safe today, because `BeginTransactionAsync`
already acquires a governed slot and normal `Commit`/`Rollback`+`Dispose` already releases it — if a
caller scopes a fresh `await using` transaction *inside* the retried delegate, that transaction is
disposed (releasing its slot) during exception unwinding, before the exception ever reaches an outer
Polly policy's own backoff delay:

```csharp
await policy.ExecuteAsync(async () =>
{
    await using var txn = context.BeginTransaction(); // acquires a governed slot
    // ... do work ...
    txn.Commit();
}); // txn already disposed (slot released) here, before Polly's delay runs
```

So the honest claim is narrower than "no external tool can add this property": it's that
`PoolGovernor`'s internals (the fairness turnstile, `PoolSlot`, `WaitForDrainAsync`) are not
reachable from application code (verified: no public seam exists in `pengdows.crud.abstractions`),
so nothing external can *enforce* correct scoping, coordinate re-admission through the same
turnstile as a first-class citizen, or give a caller a guarantee instead of a convention they have
to get right themselves. A first-party `RetryContext` is a differentiated
enforcement/convenience layer that owns the transaction lifecycle by construction — not something
literally impossible to approximate externally, but something no external tool can *guarantee* the
way an in-process feature can.

## The interplay between `PoolGovernor` and `RetryContext`

They address two different layers of the execution lifecycle:

| Dimension | `PoolGovernor` (admission gatekeeper) | `RetryContext` (retry coordinator) |
|---|---|---|
| **Core responsibility** | Protects the database engine and provider pool from unbounded concurrency. | Manages the lifecycle and resilience of one retrying operation across attempts. |
| **During active queries** | Limits concurrent active reads/writes; fast-rejects deep queues. | Executes the caller's delegate within an assigned transaction lease. |
| **On transient error** | Unaware of query exceptions — just tracks that a permit is in use. | Rolls back the transaction immediately, freeing server-side locks, and disposes the connection lease. |
| **During backoff sleep** | Holds a slot as in-use if the caller doesn't explicitly release it. | Rolls back and disposes the transaction lease, which returns its permit before `Task.Delay`; zero permits are held during backoff. |
| **On wake-up** | Regulates entry through the semaphore and, for writers, the turnstile. | Starts the next attempt through `BeginTransactionAsync`, whose normal context acquisition re-enters the semaphore/turnstile fairly. |

`PoolGovernor` prevents connection storms in general; `RetryContext`'s job is to make sure *its own*
retries don't defeat that protection by hoarding permits while asleep.

## The design as currently specified

Everything in this section is prose-only design from `future-work.md`; nothing is built.

### Dual retry modes

- **`ExecuteTransactionalAsync`** — treats an entire caller-supplied delegate as one atomic,
  all-or-nothing unit. On a transient failure (`DatabaseException.IsTransient == true`): roll back
  the current transaction, dispose its transaction/connection lease immediately (returning its
  governor permit), revert any in-memory audit mutations the delegate made, sleep with decorrelated
  exponential jitter while holding **zero** connection slots, then start the next attempt via
  public `BeginTransactionAsync` (whose normal context acquisition goes back through the fairness
  turnstile, not around it) and start
  the whole delegate over from a fresh transaction.
- **`ExecuteSequentialAsync`** — processes an independent stream/queue of items one at a time. If
  item *K* fails transiently, only *K* retries with backoff; items `1..K-1` stay committed and are
  never re-executed; execution advances to `K+1` once `K` succeeds.

### Transient exception classification

No new work needed here — `DatabaseException.IsTransient` (verified:
`pengdows.crud.abstractions/exceptions/DatabaseException.cs` hierarchy, see CLAUDE.md's Exception
Hierarchy section) already distinguishes `DeadlockException`/`SerializationConflictException`/
`CommandTimeoutException` (retryable) from `UniqueConstraintViolationException`/
`ForeignKeyViolationException` (fail-fast, never retried).

## Shortcomings and open gaps — resolve these before implementing

`future-work.md`'s entry states the mechanism at a conceptual level. Building it — and a second
review pass on this document — surfaced concrete gaps the prose doesn't address. **Item 1 is
blocking**: implementing backoff mechanics before deciding a commit-ambiguity policy risks shipping
a feature that silently duplicates writes.

1. **Blocking — commit ambiguity is not addressed.** If `CommitAsync` itself throws a transient
   exception (a timeout, a dropped connection mid-commit), the transaction may have *already
   committed on the server* even though the caller never received confirmation. Blindly retrying
   the delegate in that state can duplicate inserts or re-apply other side effects — this is
   especially sharp for `ExecuteSequentialAsync`, where item *K* may have actually applied before
   its failure became visible to the caller. The design as written has no policy for this at all.
   Before backoff mechanics are implemented, the contract must explicitly choose one of:
   - never retry a failure that occurred during commit (fail closed, surface it to the caller as-is);
   - retry only when the specific provider/exception guarantees the outcome is known (e.g. the
     connection never left the client before failing);
   - require the caller to supply an idempotency key or outcome-verification step for anything
     retried past a commit attempt; or
   - surface an explicit "commit outcome unknown" terminal result distinct from ordinary failure,
     so the caller can decide rather than the library guessing.
2. **The audit-rollback primitive doesn't compose with an arbitrary delegate, and needs an explicit
   guarantee boundary.** The design says a failed attempt reverts in-memory audit mutations "using
   `RestoreAuditSnapshot`," as if this were an existing, generically callable utility. What actually
   exists (verified: `pengdows.crud/BaseTableGateway.Audit.cs`) is `SnapshotAuditFields(TEntity)`/
   `RestoreAuditFields(TEntity, in AuditFieldSnapshot)` — `protected` members of
   `BaseTableGateway<TEntity>`, generic over **one entity type**, called internally by
   `CreateAsync`/`UpdateAsync`/`UpsertAsync` around their own single-entity write. `RetryContext`'s
   stated unit of retry is an arbitrary caller-supplied delegate, which could touch zero, one, or
   many entities across one or several gateways and tables in a single transaction. There is no
   existing hook for "snapshot everything this delegate is about to mutate, across every gateway it
   touches, and restore all of it on failure." The design must not promise automatic rollback for an
   arbitrary delegate — either callers are responsible for making their own entity mutations
   retry-safe, or a new, explicit enlistment/snapshot mechanism gets designed (new surface area, not
   a reuse of what exists today).
3. **API and ownership contract needs to be made concrete.** The most likely shape: a delegate
   receiving the current attempt's `ITransactionContext` and a `CancellationToken`, with
   `RetryContext` itself owning transaction creation, commit, rollback, and disposal around each
   attempt — nested transactions are already forbidden by `ITransactionContext`, so the delegate
   should not be able to (or need to) start its own. The implementation should go through the
   existing public `BeginTransactionAsync` surface for admission — which already performs governed
   acquisition — rather than exposing `PoolGovernor`/`PoolSlot` through `pengdows.crud.abstractions`.
   Still open: is `RetryContext` a static entry point taking an `IDatabaseContext`, an instance
   method on `IDatabaseContext` itself, or a separate object a caller constructs once and reuses?
4. **Backoff parameters are unspecified.** "Decorrelated exponential jitter" names an algorithm
   family (see the AWS Architecture Blog's 2015 "Exponential Backoff and Jitter" post, which coined
   the term for `sleep = min(cap, random_between(base, previous_sleep * 3))`) but the design states
   no base delay, cap, jitter bounds, or maximum attempt count, and doesn't say whether these are
   fixed, configurable per `DatabaseContext`, or configurable per tenant.
5. **No stated interaction with pool-admission exceptions.** `PoolSaturatedException`,
   `ModeContentionException`, and `PoolForbiddenException` (verified:
   `pengdows.crud/exceptions/PoolForbiddenException.cs`, thrown when a pool configured with zero
   capacity — e.g. the write pool on a `ReadOnly`-promoted context — is accessed at all) are
   deliberately *not* `DatabaseException` subclasses (see CLAUDE.md's "Not part of this hierarchy"
   note), specifically so they're never mistaken for an ordinary transient database error.
   `DatabaseException.IsTransient` — the only classification the design currently cites — cannot see
   any of the three by construction. Should exhausting `PoolAcquireTimeout` while trying to
   *reacquire* a slot after backoff itself be retried, with its own distinct policy? The design is
   silent, and `PoolForbiddenException` specifically should probably never be retried at all (it's a
   configuration-level admission rejection, not a transient condition).
6. **`ExecuteSequentialAsync` has no idempotency or input-materialization story.** "Only item K
   retries, `1..K-1` stay committed" is fine when items are independent, but the design doesn't say:
   whether the input sequence must be fully materialized up front (so a retried item K re-reads the
   same value) or may be a one-shot/side-effecting source that can't safely be re-enumerated; or what
   a caller must guarantee if item K's transient failure (e.g. a dropped connection mid-write) leaves
   ambiguous state — did K itself partially apply before the transient error, such that retrying it
   double-applies (the same class of problem as shortcoming #1, at per-item scope)? EF Core's
   execution-strategy docs call out non-idempotent side effects as the caller's problem, in bold
   text, for comparison — this design should do the same, explicitly.
7. **No telemetry/metrics integration decided.** The project has rich built-in observability
   (`DatabaseMetrics`, `AttributionStats`, `ActivitySource("pengdows.crud")` tracing spans, the
   OpenTelemetry adapter — see `docs/metrics.md`, `docs/tracing.md`, `docs/opentelemetry-metrics.md`).
   A retry subsystem is exactly the kind of thing operators need visibility into: attempt counts,
   backoff durations, exhausted-retry rate, and which transient-error category triggered each retry.
   Concretely, it should emit span tags (e.g. `db.retry.attempt`, `db.retry.backoff_ms`,
   `db.retry.transient_reason`) and increment counters in `DatabaseMetrics`, alongside the first
   implementation rather than as a follow-up.
8. **No stated cancellation contract for the backoff sleep itself.** Every other async surface in
   this library threads a `CancellationToken` through and never wraps `OperationCanceledException`
   (a documented, tested invariant — see CLAUDE.md's Exception Hierarchy section and
   `InfrastructureTimeoutExceptionIdentityTests.cs`). The design should state explicitly that
   cancellation during the backoff `Task.Delay` propagates unwrapped, consistent with everything
   else in the library — that's the correct answer by house style, but it isn't written down.
9. **Multi-tenancy interaction unaddressed, including lease lifecycle across backoff.** Each tenant
   gets an independently-governed `DatabaseContext`/`PoolGovernor`
   (`docs/connection/multitenancy-architecture.md`), so `RetryContext` presumably needs to be
   constructed per-context like everything else context-scoped — but the design doesn't say so, or
   whether retry policy (attempt counts, backoff bounds) should be tenant-configurable. Separately:
   if an attempt is running under a leased tenant context (`ITenantContextLease` from
   `AcquireLease`/`AcquireLeaseAsync`, see `docs/connection/multitenancy.md`), holding that lease
   across a backoff sleep blocks live tenant eviction/rotation for the whole sleep; releasing and
   reacquiring the lease across retries needs a decided answer for a tenant invalidated or rotated
   during the sleep (most likely: fail closed with `ObjectDisposedException` on wake-up, matching how
   every other post-invalidation access already behaves).
10. **The two exception-classification systems need an explicit alignment statement.**
    `ISqlDialect.AnalyzeException` (`DbExceptionInfo.IsTransient`/`IsRetryable`) and
    `IDbExceptionTranslator` (which decides which typed `DatabaseException` subclass gets thrown in
    the first place) are two independently-maintained classification systems that can disagree —
    already documented as a known risk in `docs/exception-analysis.md` and in CLAUDE.md's "Adding a
    New Database" checklist. `RetryContext` should state plainly that it binds to
    `DatabaseException.IsTransient` (the thrown-exception-side classification) as its canonical
    trigger, not `AnalyzeException`'s independent signal, and that keeping the two in agreement for
    new dialects remains a separate, already-tracked maintenance concern rather than something
    `RetryContext` itself needs to reconcile at runtime.

None of these are reasons not to build it — they're the actual scope of "build `RetryContext`,"
which is larger than the four bullet points in `future-work.md` suggest, and shortcoming #1
specifically should block starting on backoff mechanics until it has a decided answer.

## Comparison to Polly and other .NET resilience tooling

| Dimension | Polly (`Policy`/`ResiliencePipeline`) | Hand-rolled retry loop | Designed `RetryContext` |
|---|---|---|---|
| **Pool/connection awareness** | None generically — but see the narrowed rationale above: a caller who scopes a fresh transaction *inside* the retried delegate and lets it dispose before the policy's own delay gets correct pool behavior today, by construction of `BeginTransactionAsync`/`Dispose` | Same — correct if the author scopes it right, silently wrong (holds the slot across sleep) if they don't | **Enforced by construction** — the coordinator, not the caller, owns transaction scoping, so the correct pattern isn't optional |
| **Coordinated re-admission through the fairness turnstile** | Not reachable — `PoolGovernor`'s turnstile has no public seam | Same | **Yes** — reacquires through the same turnstile every other caller uses |
| **Backoff algorithm** | Configurable (fixed, linear, exponential, jittered exponential via `Backoff.DecorrelatedJitterBackoffV2`) | Whatever the author writes, often a fixed sleep | Decorrelated exponential jitter (algorithm named, parameters unspecified — see shortcoming #4) |
| **Exception classification** | Caller supplies a predicate (`Policy.Handle<T>(predicate)`) — no built-in database-transient-vs-permanent distinction | Caller supplies whatever check they write | Reuses `DatabaseException.IsTransient`, already correct for every shipped provider's typed exceptions |
| **Commit-ambiguity policy** | Caller's problem entirely | Caller's problem entirely | Not yet decided — **blocking gap, see shortcoming #1** |
| **Audit-field rollback** | Not applicable — no concept of pengdows.crud entities | Not applicable | Only proven for single-entity gateway writes today; not automatic for an arbitrary multi-entity delegate — see shortcoming #2 |

Polly (used correctly, with a transaction scoped inside the retried delegate) is a legitimate,
pool-safe choice today for an application that's willing to get that scoping right itself and
accept that commit-ambiguity and audit-rollback are entirely its own problem to solve. A first-party
`RetryContext` doesn't do something structurally impossible for Polly to approximate — it makes the
correct behavior the only behavior, which is a real, valuable difference at the API-design level
even though it isn't a capability gap in the strict "Polly literally cannot do this" sense the first
draft of this document claimed.

## Comparison to how other DALs handle this

| DAL / ecosystem | Retry story | How it compares to designed `RetryContext` |
|---|---|---|
| **EF Core** (`IExecutionStrategy`, `EnableRetryOnFailure()`) | Provider-specific strategies (`SqlServerRetryingExecutionStrategy`, `NpgsqlRetryingExecutionStrategy`) wrap a delegate with exponential backoff + jitter. EF Core **forbids starting a transaction manually inside a retried operation** — you must call `strategy.ExecuteInTransactionAsync(...)`, which owns the transaction lifecycle itself so a retry can cleanly restart it. This is the closest real-world precedent for `RetryContext`'s "the coordinator owns the transaction around the delegate" shape (shortcoming #3). | Same core idea (coordinator, not caller, owns the transaction-retry boundary), but EF Core's strategy has no pool-admission-controller concept to release/reacquire — `DbContext` doesn't sit on top of anything like `PoolGovernor`. `RetryContext`'s pool-slot coordination is genuinely new relative to this precedent. Note also: EF Core's docs are explicit that non-idempotent operations are the caller's problem — this design should match that clarity (shortcoming #6). |
| **Dapper** | None. No retry, no backoff, no transient classification. Users wrap calls in Polly or hand-rolled loops entirely outside the library. | `RetryContext` would be strictly more capable than "nothing," which is the actual bar here. |
| **jOOQ** (Java) | None built in; documentation points to wrapping calls with an external library. Connection pooling (HikariCP, etc.) is a fully separate concern with no coordination hook for a retry layer. | Same gap this project's own `docs/positioning/dal-taxonomy-and-comparison.md` already notes for jOOQ generally (pooling/admission control left to external tools) — retry is the same story. |
| **Spring / resilience4j** (Java) | `@Retryable` annotations / resilience4j's `Retry` module — generic, Polly-equivalent. No JDBC connection-pool coordination; HikariCP's pool has no retry-aware admission hook either. | Same structural gap as Polly: generic and pool-blind by default, correct only if the caller scopes things right. |
| **Go (`database/sql`, sqlx, GORM)** | No standard retry in `database/sql`. GORM has optional retry plugins that wrap query execution; still pool-blind, since Go's connection pool (`sql.DB`) exposes no admission-control seam a retry plugin could coordinate with. | Same category as Dapper/Polly — a wrapper around a pool it can't see into. |
| **Rust (`sqlx`, Diesel)** | No built-in retry in either. Compile-time query checking (`sqlx`) is orthogonal to runtime resilience. | Not comparable feature-for-feature; neither claims this space. |

**Net position:** no DAL surveyed in this repo's own comparison doc (`dal-taxonomy-and-comparison.md`)
claims pool-admission-aware retry today, and none of them has solved commit ambiguity or
multi-entity audit rollback either — those are open problems for this design to solve, not gaps
relative to prior art. If `RetryContext` is built with shortcomings #1–#10 actually resolved (not
just implemented around), it would be a genuinely differentiated capability. That comparison doc
does not currently list retry/resilience as a pillar at all; if this gets built, it belongs there.

## If you pick this up

Treat it as its own TDD-first effort, not a quick addition — per CLAUDE.md's mandatory-TDD rule,
write the failing test for each behavior before implementing it. Suggested sequencing:

1. **Decide the commit-ambiguity policy first (shortcoming #1).** This is blocking — it changes
   what "retry" is even allowed to mean for `ExecuteTransactionalAsync`.
2. Decide the public API shape and ownership contract (shortcoming #3) — everything else depends on
   it.
3. Resolve the audit-rollback scope question (shortcoming #2) before writing any rollback code — it
   changes what the delegate contract looks like.
4. Pick concrete backoff defaults (shortcoming #4) and make them configurable, not hardcoded.
5. Decide the `PoolSaturatedException`/`ModeContentionException`/`PoolForbiddenException`
   interaction (shortcoming #5) explicitly, even if the decision is "out of scope for v1."
6. Settle `ExecuteSequentialAsync`'s materialization and idempotency contract (shortcoming #6).
7. Add metrics/tracing hooks (shortcoming #7) alongside the first implementation, not as a
   follow-up — this project already has the plumbing for every other execution path; a retry
   subsystem with no visibility into attempt counts or backoff durations would be a real
   observability gap on day one.
8. Verify unwrapped `OperationCanceledException` propagation through the backoff delay
   (shortcoming #8).
9. Address tenant-lease lifecycle and rotation safety for multi-tenant applications
   (shortcoming #9).
10. State the `AnalyzeException`-vs-`DatabaseException.IsTransient` alignment explicitly
    (shortcoming #10).
