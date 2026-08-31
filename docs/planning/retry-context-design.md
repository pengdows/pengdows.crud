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

**Third revision note:** the API/ownership shape (shortcoming #3, originally left open) has since
been decided through direct design discussion and is now the specified shape below — an
`IDatabaseContext`-returning `CreateRetryContext(RetryType)` builds a *queue* of `ISqlContainer`s
rather than wrapping an arbitrary caller delegate. That decision resolves shortcoming #2 (audit
mutation composition) outright and half of shortcoming #6 (materialization), but it is a
**narrower** feature than "retry an arbitrary unit of work" — see the new "Deliberate scope
boundary" subsection below. Shortcoming #1 (commit ambiguity) remains blocking and is, if anything,
sharper under this shape: `RetryType.Sequential`'s "remove a command from the queue on success"
mechanic only works if a transient exception reliably means "did not reach the server."

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
| **During active queries** | Limits concurrent active reads/writes; fast-rejects deep queues. | Executes its queued commands within an assigned transaction lease (`RetryType.Transactional`) or one command at a time (`RetryType.Sequential`) — see "The design as currently specified" below for the queue shape. |
| **On transient error** | Unaware of query exceptions — just tracks that a permit is in use. | Rolls back the transaction immediately, freeing server-side locks, and disposes the connection lease. |
| **During backoff sleep** | Holds a slot as in-use if the caller doesn't explicitly release it. | Rolls back and disposes the transaction lease, which returns its permit before `Task.Delay`; zero permits are held during backoff. |
| **On wake-up** | Regulates entry through the semaphore and, for writers, the turnstile. | Starts the next attempt through `BeginTransactionAsync`, whose normal context acquisition re-enters the semaphore/turnstile fairly. |

`PoolGovernor` prevents connection storms in general; `RetryContext`'s job is to make sure *its own*
retries don't defeat that protection by hoarding permits while asleep.

## The design as currently specified

Everything in this section is prose/sketch-only design; nothing is built.

### Shape: an `IDatabaseContext`-returning queue builder, not a delegate wrapper

The originally-sketched shape in `future-work.md` was a delegate wrapper —
`ExecuteTransactionalAsync(async ctx => { ... })`. That has been superseded by a different shape,
worked out directly against the shortcomings below: `CreateRetryContext` returns something that
*is* an `IDatabaseContext`, so every existing `CreateSqlContainer`/`BuildX` call site works against
it completely unchanged — no new delegate-shaped API for callers to learn.

```csharp
IDatabaseContext rc = context.CreateRetryContext(RetryType.Transactional);
var sc = rc.CreateSqlContainer();      // NOT executed yet — appended to rc's internal queue
sc.Query.Append("dbo.sp");
// ... AddParameterWithValue, etc. — same ISqlContainer API as always ...
await rc.StartAsync();                 // runs the queue per RetryType's semantics
```

Calling `rc.CreateSqlContainer()` does **not** execute anything. It appends the resulting
`ISqlContainer`, fully built and parameterized, to an internal queue. Nothing hits the database
until `StartAsync()` runs. This is the mechanism that resolves shortcoming #2: because every
command's SQL text and parameter values are fixed once, at the moment it's added to the queue,
retrying a command means literally re-sending the same already-decided values (presumably via
`ISqlContainer.Clone()` for a fresh `DbCommand` per attempt) — there is no delegate re-invoked, so
there is no shared mutable entity object whose audit fields could be stamped twice, or
preserved-vs-overwritten inconsistently, across attempts. Audit stamping happens exactly once, at
build time.

### Entity CRUD composes for free — but only through Tier 1, and this is not a new decision

`TableGateway<TEntity,TRowID>`/`PrimaryKeyTableGateway<TEntity>` are already written against
`IDatabaseContext` — the constructor takes one, and most Tier-2/3 methods accept an optional
per-call `IDatabaseContext? db` override (see CLAUDE.md's "Common Test Patterns"). Since `rc`
satisfies that same interface, any existing gateway — constructed against `rc` directly, or an
existing singleton gateway called with `rc` passed as the per-call `db` — gets fully-formed,
audit-stamped, correctly-parameterized entity CRUD SQL added to the queue with **zero new code
needed in `TableGateway` itself**. This resolves the "does the queue accept entity CRUD" question
that shortcoming #3 originally left open, and it resolves in the affirmative for exactly one half of
the three-tier API, not by an arbitrary restriction but as a direct consequence of the tiers'
existing contracts:

- **Tier 1 (`BuildCreate`/`BuildUpdateAsync`/`BuildUpsert`/`BuildDelete`) composes with no friction
  at all.** These already do nothing but call `_context.CreateSqlContainer()` and build SQL —
  "generation only, nothing sent to the database, you decide what happens to the container" is
  *already* their documented contract. Pointed at `rc`, that's exactly "append this to the queue and
  don't execute it yet."
- **Tier 3 (`CreateAsync`/`UpdateAsync`/`UpsertAsync`/`DeleteAsync`) is out of scope for the queue,
  by construction, not by restriction.** Their contract is "build *and* execute in one call, return
  a real result now" — a `bool` success flag, or a resolved DB-generated `Id`. That promise is
  fundamentally incompatible with deferred, possibly-repeated execution: there is no way to honor
  "return a real result now" without either lying about it (returning success before anything has
  run) or actually executing immediately (defeating the whole point of queuing). A caller who wants
  entity CRUD in a retry queue uses Tier 1 against `rc`, the same way anyone composing custom SQL or
  reusing a container across contexts already does today.

This also sharpens the scope boundary below: even a **write**'s output isn't available inside the
same retry unit, not just a read's. A DB-generated `[Id(false)]` value from a queued `BuildCreate`
isn't known until `StartAsync()` actually runs that command — so it can't be used as an FK value
for a later command in the same queue, regardless of which tier built it.

Worth noting as a byproduct: this is a genuinely new justification for the existing Tier-1/Tier-3
split, alongside the two already documented elsewhere (composability — inspect/modify a container
before executing it — and `Clone()`/`Clone(IDatabaseContext)` template reuse across dialects and
tenants, see `docs/sql-container-templates.md`). "Build something now and hand it to something else
that decides when, and how many times, it actually runs" is a third reason, and `RetryContext` is
the first concrete design that actually needs it.

### Dual retry modes, both queue-driven

- **`RetryType.Transactional`** — the whole queue runs inside **one transaction**, all-or-nothing.
  On a transient failure (`DatabaseException.IsTransient == true`) at any point: roll back, dispose
  the transaction/connection lease immediately (returning its governor permit), sleep with
  decorrelated exponential jitter while holding **zero** connection slots, then retry the **entire
  queue from the first command**, in a fresh transaction opened via the normal public
  `BeginTransactionAsync` (so re-admission goes back through the real fairness turnstile, not
  around it).
- **`RetryType.Sequential`** — commands execute **one at a time**. A command that succeeds is
  **removed from the queue** and never re-executed. A command that fails transiently retries (with
  backoff, holding zero slots between attempts) up to the configured retry budget. The loop
  terminates on exactly one of three conditions:
  a. the queue is empty — every command eventually succeeded;
  b. an unrecoverable (non-transient) exception — stop immediately, whatever remains in the queue
     never runs;
  c. the retry budget is exhausted for the command currently being retried — stop.

  Open questions this raises, not yet decided (see shortcoming #3 below): does each command commit
  in its own transaction as it succeeds (matching "items `1..K-1` stay committed and are never
  re-executed" from the original phrasing), or does the whole queue share one long-lived
  transaction with a cursor advancing through it? And on termination via (b) or (c), what does the
  caller get back — which commands succeeded, which one failed and why, and which never ran?
  Neither is specified yet.

### Deliberate scope boundary: no intermediate reads *or generated-value writes* inside one retry unit

Because the entire queue must be built *before* `StartAsync()` runs, nothing has executed yet for
calling code to react to. This shape cannot express "read a value, branch on it, then decide what
to write" within a single retry unit — e.g. "check the balance, only enqueue the debit if it's
sufficient." That pattern covers a large fraction of real transactional code (see every worked
example in this project's own docs: retrieve-then-conditionally-update). The same limitation extends
to writes, not just reads: a DB-generated `[Id(false)]` value from an earlier queued command isn't
known until `StartAsync()` actually runs it, so it can't be threaded into a later command in the
same queue as an FK value either (see "Entity CRUD composes for free" above). `RetryContext` as
specified here is **declarative-batch retry over a fully-known-up-front sequence of commands**, not
a general-purpose "retry an arbitrary unit of work" wrapper. That's a legitimate, useful scope
(batch imports, a multi-step insert/update chain whose values are all known before it starts) — but
it's narrower than `future-work.md`'s original "arbitrary delegate" framing, and that framing needs
updating to match once this shape is adopted, so a reader doesn't expect read-then-branch (or
write-then-use-generated-value) support that was never built.

### Transient exception classification

No new work needed here — `DatabaseException.IsTransient` (verified:
`pengdows.crud.abstractions/exceptions/DatabaseException.cs` hierarchy, see CLAUDE.md's Exception
Hierarchy section) already distinguishes `DeadlockException`/`SerializationConflictException`/
`CommandTimeoutException` (retryable) from `UniqueConstraintViolationException`/
`ForeignKeyViolationException` (fail-fast, never retried). This is also the exact boundary
`RetryType.Sequential`'s condition (b) above depends on: "unrecoverable" means
`IsTransient == false`.

## Shortcomings and open gaps — resolve these before implementing

`future-work.md`'s entry states the mechanism at a conceptual level. Building it — and a second
review pass on this document — surfaced concrete gaps the prose doesn't address. **Item 1 is
blocking**: implementing backoff mechanics before deciding a commit-ambiguity policy risks shipping
a feature that silently duplicates writes.

1. **Blocking — commit ambiguity is not addressed, and the queue shape makes it concrete rather
   than abstract.** If a command's execution (or, for `RetryType.Transactional`, the final
   `CommitAsync`) throws a transient exception — a timeout, a dropped connection — the write may
   have *already landed on the server* even though the caller never received confirmation. For
   `RetryType.Sequential`, whose entire mechanism is "remove a command from the queue on success,
   keep and retry it on failure," this is no longer a background risk: the removal decision **is**
   the commit-ambiguity decision. If a transient exception doesn't reliably mean "did not reach the
   server," retrying a kept command can double-apply it. The design as written has no policy for
   this at all. Before backoff mechanics are implemented, the contract must explicitly choose one of:
   - never retry a failure that occurred during commit (fail closed, surface it to the caller as-is);
   - retry only when the specific provider/exception guarantees the outcome is known (e.g. the
     connection never left the client before failing);
   - require the caller to supply an idempotency key or outcome-verification step for anything
     retried past a commit attempt; or
   - surface an explicit "commit outcome unknown" terminal result distinct from ordinary failure,
     so the caller can decide rather than the library guessing.
2. **RESOLVED by the queue shape — audit-rollback no longer needs a generic snapshot/restore
   mechanism.** The original concern: `RestoreAuditSnapshot` was described as if it were an
   existing, generically callable utility, when what actually exists (verified:
   `pengdows.crud/BaseTableGateway.Audit.cs`) is `SnapshotAuditFields(TEntity)`/
   `RestoreAuditFields(TEntity, in AuditFieldSnapshot)` — `protected`, generic over **one entity
   type**, called internally by `CreateAsync`/`UpdateAsync`/`UpsertAsync` around their own
   single-entity write. That concern assumed an arbitrary caller delegate re-invoked per attempt,
   which could touch any number of entities across any number of gateways, with no existing hook to
   snapshot/restore all of it. Under the queue shape, there is no delegate re-invoked at all: every
   command's parameter values (including any audit-stamped ones) are fixed once, when the command is
   built and appended to the queue, before `StartAsync()` ever runs. A retry just re-sends the same
   already-decided values — there is no shared mutable entity state that could be double-stamped or
   inconsistently preserved across attempts. No new snapshot/restore surface area is needed.
3. **RESOLVED (shape) — `CreateRetryContext(RetryType)` returns an `IDatabaseContext`; open
   sub-questions remain.** `RetryContext` is not a delegate wrapper or a static entry point — it's
   an `IDatabaseContext` obtained via `context.CreateRetryContext(RetryType)`, so every existing
   `CreateSqlContainer`/`BuildX` call site works against it unchanged; `CreateSqlContainer`/`BuildX`
   append to an internal queue instead of executing, and `StartAsync()` runs the queue per the
   `RetryType`'s semantics (see "The design as currently specified" above). Nested transactions are
   still forbidden by `ITransactionContext`, and admission still goes through the existing public
   `BeginTransactionAsync` surface — `PoolGovernor`/`PoolSlot` stay unexposed in
   `pengdows.crud.abstractions`. **Also resolved, as a direct consequence rather than a separate
   decision:** the queue accepts entity CRUD via Tier-1 `BuildCreate`/`BuildUpdateAsync`/`BuildUpsert`/
   `BuildDelete` from `TableGateway`/`PrimaryKeyTableGateway` — pointing an existing or new gateway at
   `rc` (constructor or per-call `db` parameter) gets fully-formed, audit-stamped SQL into the queue
   for free, since those methods already do nothing but call `_context.CreateSqlContainer()`. Tier-3
   convenience methods (`CreateAsync`/`UpdateAsync`/`UpsertAsync`/`DeleteAsync`) are out of scope for
   the queue, because their "execute now, return a real result" contract cannot be honored under
   deferred execution — see "Entity CRUD composes for free" above. What the shape decision does *not*
   yet answer:
   - Is retry of a single command implemented via `ISqlContainer.Clone()` (a fresh `DbCommand` per
     attempt, reusing the already-bound parameter values) or some other mechanism?
   - `RetryType.Sequential`'s transaction boundary — one commit per successful command, or one
     shared transaction with a cursor? (See "Dual retry modes" above.)
   - What return shape reports partial progress when `Sequential` stops on an unrecoverable error or
     exhausted retry budget — succeeded/failed/never-ran per command?
   - This shape is deliberately narrower than "retry an arbitrary unit of work" — see "Deliberate
     scope boundary" above. `future-work.md`'s original delegate-shaped description needs updating
     to match once this is adopted, so it doesn't advertise read-then-branch support that isn't
     part of this design.
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
6. **`RetryType.Sequential`'s materialization question is RESOLVED by the queue shape; the
   idempotency half is not (it's shortcoming #1).** The original concern was whether the input
   sequence must be fully materialized up front or could be a one-shot/side-effecting source unsafe
   to re-enumerate. Under the queue shape, materialization isn't optional — the queue is, by
   construction, a fully-built list of `ISqlContainer`s before `StartAsync()` ever runs; there's no
   ambiguity to resolve. What remains open is exactly shortcoming #1's question restated per-item:
   if command *K*'s transient failure leaves ambiguous state (did it partially apply before the
   error?), retrying it can double-apply — the same commit-ambiguity policy decision, just scoped to
   one queue entry instead of one whole transaction. EF Core's execution-strategy docs call out
   non-idempotent side effects as the caller's problem, in bold text, for comparison — this design
   should do the same, explicitly, once #1 is decided.
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
| **Audit-field rollback** | Not applicable — no concept of pengdows.crud entities | Not applicable | Not a rollback problem under the queue shape at all — commands are fully built (audit fields stamped once) before `StartAsync()` runs, so there's no shared mutable state to restore — see shortcoming #2 |
| **Intermediate reads/generated-value writes affecting later commands, within one retry unit** | Fully supported — the retried delegate can read, branch, write, and use a just-generated ID freely | Same | **Not supported** — the queue must be fully built before execution starts, so neither a read result nor a DB-generated `Id` from an earlier queued command is available to a later one; see "Deliberate scope boundary" above |

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
| **EF Core** (`IExecutionStrategy`, `EnableRetryOnFailure()`) | Provider-specific strategies (`SqlServerRetryingExecutionStrategy`, `NpgsqlRetryingExecutionStrategy`) wrap a delegate with exponential backoff + jitter. EF Core **forbids starting a transaction manually inside a retried operation** — you must call `strategy.ExecuteInTransactionAsync(...)`, which owns the transaction lifecycle itself so a retry can cleanly restart it. EF Core's shape is delegate-based and supports intermediate reads-then-writes; `RetryContext`'s queue shape (see above) does not, in exchange for resolving the audit/entity-mutation composition problem EF Core doesn't have to deal with in the first place. | Same core idea (coordinator, not caller, owns the transaction-retry boundary), but EF Core's strategy has no pool-admission-controller concept to release/reacquire — `DbContext` doesn't sit on top of anything like `PoolGovernor`. `RetryContext`'s pool-slot coordination is genuinely new relative to this precedent. Note also: EF Core's docs are explicit that non-idempotent operations are the caller's problem — this design should match that clarity (shortcoming #6). |
| **Dapper** | None. No retry, no backoff, no transient classification. Users wrap calls in Polly or hand-rolled loops entirely outside the library. | `RetryContext` would be strictly more capable than "nothing," which is the actual bar here. |
| **jOOQ** (Java) | None built in; documentation points to wrapping calls with an external library. Connection pooling (HikariCP, etc.) is a fully separate concern with no coordination hook for a retry layer. | Same gap this project's own `docs/positioning/dal-taxonomy-and-comparison.md` already notes for jOOQ generally (pooling/admission control left to external tools) — retry is the same story. |
| **Spring / resilience4j** (Java) | `@Retryable` annotations / resilience4j's `Retry` module — generic, Polly-equivalent. No JDBC connection-pool coordination; HikariCP's pool has no retry-aware admission hook either. | Same structural gap as Polly: generic and pool-blind by default, correct only if the caller scopes things right. |
| **Go (`database/sql`, sqlx, GORM)** | No standard retry in `database/sql`. GORM has optional retry plugins that wrap query execution; still pool-blind, since Go's connection pool (`sql.DB`) exposes no admission-control seam a retry plugin could coordinate with. | Same category as Dapper/Polly — a wrapper around a pool it can't see into. |
| **Rust (`sqlx`, Diesel)** | No built-in retry in either. Compile-time query checking (`sqlx`) is orthogonal to runtime resilience. | Not comparable feature-for-feature; neither claims this space. |

**Net position:** no DAL surveyed in this repo's own comparison doc (`dal-taxonomy-and-comparison.md`)
claims pool-admission-aware retry today, and none of them has solved commit ambiguity either — that
remains an open problem for this design to solve, not a gap relative to prior art. (Multi-entity
audit rollback, the other originally-open problem, is now sidestepped rather than solved — the
queue shape has no delegate re-invocation for it to apply to, at the cost of losing the
read-then-branch expressiveness EF Core's delegate shape has.) If `RetryContext` is built with
shortcoming #1 actually resolved (not just implemented around), it would be a genuinely
differentiated capability. That comparison doc does not currently list retry/resilience as a pillar
at all; if this gets built, it belongs there.

## If you pick this up

Treat it as its own TDD-first effort, not a quick addition — per CLAUDE.md's mandatory-TDD rule,
write the failing test for each behavior before implementing it. Shortcomings #2 and #3 (shape,
including the Tier-1-only entity-CRUD question) are now decided in prose above; suggested sequencing
for what's left:

1. **Decide the commit-ambiguity policy first (shortcoming #1).** Still blocking — for
   `RetryType.Sequential` it's literally the "remove from queue on success" decision rule; for
   `RetryType.Transactional` it governs whether a transient failure during the final commit is ever
   safe to retry.
2. Close `RetryType.Sequential`'s two remaining open sub-questions from shortcoming #3: per-command
   commit vs. one shared transaction with a cursor, and the partial-progress return shape on early
   termination.
3. Decide whether single-command retry is implemented via `ISqlContainer.Clone()` or some other
   mechanism (the other remaining shortcoming #3 sub-question).
4. Update `future-work.md`'s FEAT-001 entry to match the queue shape (it currently still describes
   the superseded delegate-wrapper framing) so the two documents don't disagree about what's being
   built.
5. Pick concrete backoff defaults (shortcoming #4) and make them configurable, not hardcoded.
6. Decide the `PoolSaturatedException`/`ModeContentionException`/`PoolForbiddenException`
   interaction (shortcoming #5) explicitly, even if the decision is "out of scope for v1."
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
