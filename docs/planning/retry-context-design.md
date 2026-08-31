# RetryContext Subsystem — Design, Shortcomings, and Comparison (FEAT-001)

**Status: designed, zero implementation.** Nothing described in this document exists in code yet.
Tracked as `FEAT-001` in [`future-work.md`](./future-work.md)'s tracker (section 10); the original
design write-up lives in that same file under "RetryContext Subsystem (Governor-Aware Resilient
Execution)" and is reproduced/expanded here with the shortcomings and comparisons future-work.md's
entry deliberately left out of scope. [`exception-analysis.md`](../exception-analysis.md)'s
"Retry-policy boundary" section points readers here for the full picture; it also shows the
application-level retry loop you'd have to hand-write today, since none of this exists.

Grounded against the current `3.0` branch by direct source inspection, not assumed from the design
prose — each factual claim below about what exists today cites the exact file it was checked
against.

## The problem this is meant to solve

`AnalyzeException`/`DatabaseException.IsTransient` already tell a caller *whether* retrying might
help (deadlock, serialization failure, command timeout → yes; a unique/FK/not-null violation → no
— see `exception-analysis.md`). Nothing in the library acts on that signal. An application that
wants retry-with-backoff has to write its own loop, and the two obvious ways to do that both create
real operational failure modes specific to a governed connection pool:

1. **Connection holding during backoff.** A naive retry loop that wraps `BeginTransaction()` and
   sleeps inside the `catch` before retrying holds the transaction's connection (and, in
   `PreventDatabaseUnload`/`SingleWriter` modes, a `PoolGovernor` permit) for the full sleep
   duration. Under load, this is how a handful of transient deadlocks turns into full pool
   exhaustion.
2. **Thundering herds.** If every caller's retry loop uses the same fixed or lightly-jittered
   backoff, a burst of simultaneous transient failures (a deadlock storm) produces synchronized
   retry waves that hit the database at the same moments, extending or worsening the original
   contention instead of resolving it.

Neither Polly nor a hand-rolled loop can fix this from outside the library, for a concrete,
verifiable reason (not just a design preference):

> **`PoolGovernor` is `internal sealed`** (`pengdows.crud/infrastructure/PoolGovernor.cs`). Its
> `Acquire`/`AcquireAsync`/`TryAcquireAsync`/`WaitForDrainAsync` members — the fairness turnstile,
> the permit-tracking `PoolSlot`, everything a governor-aware retry coordinator would need to
> release-then-reacquire around a backoff sleep — are not reachable from application code, and
> `pengdows.crud.abstractions` exposes no public seam onto them either. This is why `RetryContext`
> has to ship as a first-party feature of `DatabaseContext` itself: an external policy wrapper
> (Polly or otherwise) can retry a delegate, but it has no way to *see* the pool permit its own
> retried call is holding, so it cannot release it before sleeping or coordinate re-admission
> through the fairness turnstile on wake-up. This is the one property no external tool can add.

## The design as currently specified

Everything in this section is prose-only design from `future-work.md`; nothing is built.

### Dual retry modes

- **`ExecuteTransactionalAsync`** — treats an entire caller-supplied delegate as one atomic,
  all-or-nothing unit. On a transient failure (`DatabaseException.IsTransient == true`): roll back
  the current transaction, dispose its connection lease immediately, revert any in-memory audit
  mutations the delegate made, release the `PoolGovernor` slot, sleep with decorrelated exponential
  jitter while holding **zero** connection slots, then reacquire a fresh slot via
  `PoolGovernor.AcquireAsync` (going back through the fairness turnstile, not around it) and start
  the whole delegate over from a fresh transaction.
- **`ExecuteSequentialAsync`** — processes an independent stream/queue of items one at a time. If
  item *K* fails transiently, only *K* retries with backoff; items `1..K-1` stay committed and are
  never re-executed; execution advances to `K+1` once `K` succeeds.

### PoolGovernor slot coordination

Zero connection slots held during backoff sleep; re-admission after backoff goes back through the
same fairness turnstile every other caller uses, so a retry storm can't cut the queue or starve
non-retrying traffic the way an external, pool-blind retry loop could.

### Transient exception classification

No new work needed here — `DatabaseException.IsTransient` (verified current: `pengdows.crud.
abstractions/exceptions/DatabaseException.cs` hierarchy, see CLAUDE.md's Exception Hierarchy
section) already distinguishes `DeadlockException`/`SerializationConflictException`/
`CommandTimeoutException` (retryable) from `UniqueConstraintViolationException`/
`ForeignKeyViolationException` (fail-fast, never retried).

## Shortcomings and open gaps in the design as written

`future-work.md`'s entry states the mechanism at a conceptual level. Building it surfaces several
concrete gaps the prose glosses over — found by tracing the actual primitives the design assumes
exist, not by re-reading the prose more carefully.

1. **The audit-rollback primitive doesn't compose with an arbitrary delegate.** The design says a
   failed attempt reverts in-memory audit mutations "using `RestoreAuditSnapshot`," as if this were
   an existing, generically callable utility. What actually exists (verified:
   `pengdows.crud/BaseTableGateway.Audit.cs`) is `SnapshotAuditFields(TEntity)`/
   `RestoreAuditFields(TEntity, in AuditFieldSnapshot)` — `protected` members of
   `BaseTableGateway<TEntity>`, generic over **one entity type**, called internally by
   `CreateAsync`/`UpdateAsync`/`UpsertAsync` around their own single-entity write. `RetryContext`'s
   stated unit of retry is an arbitrary caller-supplied delegate, which could touch zero, one, or
   many entities across one or several gateways and tables in a single transaction. There is no
   existing hook for "snapshot everything this delegate is about to mutate, across every gateway it
   touches, and restore all of it on failure" — that's new surface area to design (most plausibly:
   the delegate itself is responsible for its own audit-safe retry-ability, and `RetryContext` only
   guarantees the transaction/connection/pool-slot lifecycle, not audit-field rollback for
   arbitrary multi-entity work). The current wording overstates what's reusable as-is.
2. **No decided public API shape.** Is `RetryContext` a static entry point taking an
   `IDatabaseContext`, an instance method on `IDatabaseContext` itself, or a separate object a
   caller constructs once and reuses? How does the delegate get a transaction/connection to work
   with — is one handed in as a parameter (and if so, is starting a *nested* transaction inside it
   forbidden the way EF Core's `IExecutionStrategy` forbids manual transactions inside a retried
   operation — see comparison table below), or does the delegate call `context.BeginTransactionAsync`
   itself each attempt? None of this is specified.
3. **Backoff parameters are unspecified.** "Decorrelated exponential jitter" names an algorithm
   family (see the AWS Architecture Blog's 2015 "Exponential Backoff and Jitter" post, which coined
   the term for `sleep = min(cap, random_between(base, previous_sleep * 3))`) but the design states
   no base delay, cap, jitter bounds, or maximum attempt count. Any of these could be wrong for a
   given deployment's timeout budgets without being wrong in principle.
4. **No stated interaction with pool-admission timeouts.** `PoolSaturatedException` and
   `ModeContentionException` are deliberately *not* `DatabaseException` subclasses (see CLAUDE.md's
   "Not part of this hierarchy" note) specifically so they're never mistaken for an ordinary
   transient database error. `DatabaseException.IsTransient` — the only classification the design
   currently cites — cannot see either of them by construction. Should exhausting `PoolAcquireTimeout`
   while trying to *reacquire* a slot after backoff itself be retried, distinctly, with its own
   policy? The design is silent.
5. **`ExecuteSequentialAsync` has no idempotency story.** "Only item K retries, 1..K-1 stay
   committed" is fine when items are independent, but says nothing about what a caller must
   guarantee if item K's transient failure (e.g., a dropped connection mid-write) leaves ambiguous
   state — did K itself partially apply before the transient error, such that retrying it
   double-applies? This is a caller responsibility either way, but the design doesn't say so
   explicitly, unlike (for comparison) EF Core's execution-strategy docs, which call out
   non-idempotent side effects as the caller's problem in bold text.
6. **No telemetry/metrics integration decided.** The project has rich built-in observability
   (`DatabaseMetrics`, `AttributionStats`, OpenTelemetry histograms/spans — see `docs/metrics.md`,
   `docs/opentelemetry-metrics.md`). A retry subsystem is exactly the kind of thing operators need
   visibility into (attempt counts, backoff durations, exhausted-retry rate) but none of that is
   mentioned in the design.
7. **No stated cancellation contract for the backoff sleep itself.** Every other async surface in
   this library threads a `CancellationToken` through and never wraps `OperationCanceledException`
   (a documented, tested invariant — see CLAUDE.md's Exception Hierarchy section and
   `InfrastructureTimeoutExceptionIdentityTests.cs`). The design doesn't say whether a cancellation
   during the backoff `Task.Delay` propagates unwrapped (consistent with everything else in the
   library) or is expected to behave some other way — it should be the former, but that's an
   inference from house style, not something the design states.
8. **Multi-tenancy interaction unaddressed.** Each tenant gets an independently-governed
   `DatabaseContext`/`PoolGovernor` (see `docs/connection/multitenancy-architecture.md`). Presumably
   `RetryContext` would be constructed per-context like everything else context-scoped, but the
   design doesn't say so, and doesn't address whether retry *policy* (attempt counts, backoff
   bounds) should be tenant-configurable.

None of these are reasons not to build it — they're the actual scope of "build `RetryContext`,"
which is larger than the four bullet points in `future-work.md` suggest. Treat this list as the
starting checklist for whoever picks this up as a TDD-first effort.

## Comparison to Polly and other .NET resilience tooling

| Dimension | Polly (`Policy`/`ResiliencePipeline`) | Hand-rolled retry loop | Designed `RetryContext` |
|---|---|---|---|
| **Pool/connection awareness** | None — generic delegate wrapper, no concept of a connection or pool permit | None, unless the author hand-codes it | **First-class** — releases the `PoolGovernor` permit before sleeping, reacquires through the fairness turnstile on wake-up |
| **Backoff algorithm** | Configurable (fixed, linear, exponential, jittered exponential via `Backoff.DecorrelatedJitterBackoffV2`) | Whatever the author writes, often a fixed sleep | Decorrelated exponential jitter (algorithm named, parameters unspecified — see shortcomings above) |
| **Exception classification** | Caller supplies a predicate (`Policy.Handle<T>(predicate)`) — no built-in database-transient-vs-permanent distinction | Caller supplies whatever check they write | Reuses `DatabaseException.IsTransient`, already correct for every shipped provider's typed exceptions |
| **Transaction-aware rollback** | None — the wrapped delegate owns its own transaction lifecycle entirely | None built in | Rolls back the transaction and disposes the connection lease itself as part of the retry step (audit-field rollback scope is a real, unresolved gap — see shortcoming #1) |
| **Can this be added externally today?** | Yes, and it's the closest thing to what exists in practice — but see the `PoolGovernor` visibility argument above for the one thing it structurally can't do | Yes, same limitation | N/A — has to ship in-process |

Polly remains a fine choice for retrying *non-database* operations, or for a caller who accepts
that their retry loop can't see or coordinate with the ADO.NET connection pool. It is not a
substitute for a pool-aware coordinator for exactly the reason the original design states: it
cannot see what it doesn't have a handle to.

## Comparison to how other DALs handle this

| DAL / ecosystem | Retry story | How it compares to designed `RetryContext` |
|---|---|---|
| **EF Core** (`IExecutionStrategy`, `EnableRetryOnFailure()`) | Provider-specific strategies (`SqlServerRetryingExecutionStrategy`, `NpgsqlRetryingExecutionStrategy`) wrap a delegate with exponential backoff + jitter. Crucially, EF Core **forbids starting a transaction manually inside a retried operation** — you must call `strategy.ExecuteInTransactionAsync(...)`, which owns the transaction lifecycle itself so a retry can cleanly restart it. This is the closest real-world precedent for `RetryContext`'s "the delegate is a full atomic unit, the coordinator owns the transaction around it" shape. | Same core idea (coordinator, not caller, owns the transaction-retry boundary), but EF Core's strategy has **no pool-admission-controller concept to release/reacquire** — `DbContext` doesn't sit on top of anything like `PoolGovernor`. `RetryContext`'s pool-slot coordination is genuinely new relative to this precedent, not a reimplementation of it. |
| **Dapper** | None. No retry, no backoff, no transient classification. Users wrap calls in Polly or hand-rolled loops entirely outside the library. | `RetryContext` would be strictly more capable than "nothing," which is the actual bar here. |
| **jOOQ** (Java) | None built in; documentation points to wrapping calls with an external library. Connection pooling (HikariCP, etc.) is a fully separate concern with no coordination hook for a retry layer. | Same gap this project's own `docs/positioning/dal-taxonomy-and-comparison.md` already notes for jOOQ generally (pooling/admission control left to external tools) — retry is the same story. |
| **Spring / resilience4j** (Java) | `@Retryable` annotations / resilience4j's `Retry` module — generic, Polly-equivalent. No JDBC connection-pool coordination; HikariCP's pool has no retry-aware admission hook either. | Same structural gap as Polly: generic and pool-blind by design. |
| **Go (`database/sql`, sqlx, GORM)** | No standard retry in `database/sql`. GORM has optional retry plugins that wrap query execution; still pool-blind, since Go's connection pool (`sql.DB`) exposes no admission-control seam a retry plugin could coordinate with. | Same category as Dapper/Polly — a wrapper around a pool it can't see into. |
| **Rust (`sqlx`, Diesel)** | No built-in retry in either. Compile-time query checking (`sqlx`) is orthogonal to runtime resilience. | Not comparable feature-for-feature; neither claims this space. |

**Net position:** no DAL surveyed in this repo's own comparison doc (`dal-taxonomy-and-comparison.md`)
claims pool-admission-aware retry today. If `RetryContext` is built as designed — and the
shortcomings above are actually resolved, not just implemented around — it would be a genuinely
differentiated capability, not a reimplementation of an existing pattern from another ecosystem.
That comparison doc does not currently list retry/resilience as a pillar at all; if this gets built,
it belongs there.

## If you pick this up

Treat it as its own TDD-first effort, not a quick addition — per CLAUDE.md's mandatory-TDD rule,
write the failing test for each behavior before implementing it. Suggested sequencing based on the
gaps above:

1. Decide the public API shape first (item 2) — everything else depends on it.
2. Resolve the audit-rollback scope question (item 1) before writing any rollback code — it changes
   what the delegate contract even looks like.
3. Pick concrete backoff defaults (item 3) and make them configurable, not hardcoded.
4. Decide the `PoolSaturatedException`/`ModeContentionException` interaction (item 4) explicitly,
   even if the decision is "out of scope for v1."
5. Add metrics/tracing hooks (item 6) alongside the first implementation, not as a follow-up — this
   project already has the plumbing (`DatabaseMetrics`, `ActivitySource("pengdows.crud")` spans,
   the OpenTelemetry adapter) for every other execution path; a retry subsystem with no visibility
   into attempt counts or backoff durations would be a real observability gap on day one.
