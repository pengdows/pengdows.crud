# Multi-Tenancy: Architecture and Contract

This is the architectural reference for pengdows.crud's multi-tenancy model — what the library
actually guarantees, what it deliberately leaves to the deployment operator, and the exact
concurrency semantics of tenant identity and rotation. For a getting-started guide (configuration,
DI wiring, request-time usage), see [`multitenancy.md`](./multitenancy.md).

## The model: a tenant selects a complete execution environment

A tenant identifier does not select a row filter, a schema, or a connection-string fragment
applied on top of shared infrastructure. It selects a complete, independently governed
`IDatabaseContext` — its own dialect, its own connection pool (`PoolGovernor` instances for
reader and writer are per-`DatabaseContext` instance fields, never shared or static across
contexts), its own admission control, and its own metrics collector. Two tenants can run against
different database products, different server versions, and different `DbMode`s, because nothing
about tenant isolation depends on them sharing infrastructure.

## What the library enforces vs. what it assumes

This distinction matters for anyone evaluating the isolation guarantee:

| Library-enforced | Deployment/caller responsibility |
|---|---|
| Each tenant's `IDatabaseContext` has independent, non-shared `PoolGovernor` instances — one tenant's connection saturation cannot consume another tenant's admission slots. | **Database-per-tenant is a convention the library assumes, not one it verifies.** Nothing in `TenantContextRegistry`/`TenantConnectionResolver` checks that tenant A's and tenant B's connection strings point at physically distinct databases. If two tenant configurations point at the same database, the library isolates the *connections/admission*, not the *data* — that's an operator configuration error, not something the library detects or prevents. |
| One context per tenant identifier is created and cached (`ConcurrentDictionary<string, TenantContextEntry>`, each entry wrapping a `Lazy<Task<IDatabaseContext>>` with `Lazy<T>.ExecutionAndPublication` — the factory runs at most once per key even under concurrent first access, whether the racing callers are sync `GetContext`/`AcquireLease` or async `GetContextAsync`/`AcquireLeaseAsync`, any mix). | **Tenant identity itself must be trusted before it reaches the registry.** `GetContext(tenant)` does not authenticate anything; it is a lookup key. Resolving `tenant` from validated authentication/authorization state upstream is the caller's job. |
| Tenant-ID comparison is case-insensitive (`StringComparer.OrdinalIgnoreCase`) end to end — `TenantConnectionResolver`'s configuration dictionary and `TenantContextRegistry`'s context cache use the same comparer, so `"acme"` and `"ACME"` always resolve to exactly one cached context, never two independently governed ones (fixed as CORE-009; this is not an accidental convenience, it's an invariant relied on to prevent duplicate-context/cardinality-evasion bugs). | No other normalization (trimming, casing conventions beyond ordinal case-insensitivity, Unicode normalization) is applied to the tenant identifier. Two strings that differ only by leading/trailing whitespace, for example, are treated as different tenants. |
| An optional `maxTenantCount` cap (`MultiTenantOptions.MaxTenantCount`) is enforced atomically against concurrent admission of *different* new tenants — the count-check and dictionary-add happen under one lock, so two threads racing to admit two different new tenants cannot both slip past the check. | Choosing and sizing the cap for the deployment's actual tenant cardinality; the library defaults to unbounded. |
| Object identity *is* tenant identity for lifecycle events — `ContextCreated`/`ContextRemoved` pass the `IDatabaseContext` instance itself, nothing else (see CORE-011's resolution: this is a deliberate architectural decision, not a missing feature — there is no separate tenant-ID field threaded through the API for a subscriber to correlate against). | A subscriber that needs a human-readable tenant label must derive or capture it itself (e.g. from the same call site that invoked `GetContext(tenantId)`), since the event does not supply one. |
| Each `DatabaseContext`'s own operational logging (connection lifecycle, dialect detection, session settings, etc.) uses the `ILoggerFactory` passed to *that* context's constructor/`CreateAsync`, so per-tenant sinks/scopes work correctly for everything the context itself logs. | **`TypeCoercionHelper`'s diagnostic logging is process-wide, not per-tenant.** It's an `internal static` utility with one static `Logger`, set once via first-caller-wins semantics (`SetLoggerIfUnset`, race-free but not per-context) the first time any `DatabaseContext` is constructed in the process. In a deployment with distinct per-tenant `ILoggerFactory` instances, every tenant *except* the first one to construct has its type-coercion warnings/errors logged through the *first* tenant's logger — misattributed, not lost. If per-tenant attribution of coercion diagnostics specifically matters, don't rely on this logger for it. |

## Context disposal: application shutdown, or live rotation via leases

Application shutdown remains the simplest path — disposing `ITenantContextRegistry` disposes every
context it created. But **live ejection/reconfiguration of a tenant while the application keeps
running is a genuinely supported pattern**, not just an accepted-risk primitive, for callers that
follow one rule: anything holding a tenant context across an `await` boundary acquires a lease
(`AcquireLease`/`AcquireLeaseAsync`) instead of caching a bare `GetContext`/`GetContextAsync`
reference. See [`multitenancy.md`](./multitenancy.md#protecting-against-concurrent-rotation-acquirelease-acquireleaseasync)
for the caller-facing contract; this doc covers the mechanism.

## `Invalidate`/`InvalidateAll` mechanics

`TenantContextRegistry.Invalidate`/`InvalidateAll`'s concurrency behavior is deliberately hardened
(CORE-009/010/011, the last now closed via leasing — see below). It is a two-step, caller-driven
action — there is no polling, no background refresh, and no eventual-consistency window baked into
the registry itself:

1. `TenantConnectionResolver.Register(tenant, newConfig)` replaces the stored configuration
   snapshot for that tenant identifier. This alone has no effect on any already-cached context.
2. `TenantContextRegistry.Invalidate(tenant)` (or `InvalidateAll()`) synchronously removes the
   tenant's entry from the internal `ConcurrentDictionary<string, TenantContextEntry>` — this part
   is always immediate, before `Invalidate` returns, and is what makes the *next*
   `GetContext`/`AcquireLease` call for that tenant construct a fresh context right away, never
   observing the superseded entry.
3. Actual **disposal** of the superseded context is a separate, asynchronous step:
   `TenantContextEntry.MarkRemoved` disposes immediately if the entry is idle (no outstanding
   leases) — via `TenantContextRegistry.ScheduleDisposeEntry`, which dispatches the actual
   `context.Dispose()` call onto the thread pool rather than running it inline on the invalidating
   caller's thread. This is a deliberate, empirically-motivated choice, not an incidental
   implementation detail: `IDatabaseContext.Dispose()` can itself block synchronously (its own
   pool-governor drain wait — see `docs/architecture.md`'s Reader-as-Lease Model and Connection
   Lifecycle Management sections), and running that inline on whichever thread happens to call
   `Invalidate`/release the last lease measurably contributed to thread-pool contention once many
   concurrent lease/release/invalidate cycles were in flight — confirmed directly, not assumed.
   If the entry was leased when `Invalidate` ran, disposal is deferred further still, until every
   outstanding lease releases (see "Concurrency semantics" below).

**Practical consequence:** `Invalidate`/`InvalidateAll` never block the calling thread, and evict
from the lookup immediately — but do not assume the superseded context has already finished
disposing by the time `Invalidate` returns, or even by the time a lease's `Dispose()`/`DisposeAsync()`
returns. If you need to observe completion, subscribe to `ContextRemoved`.

## Concurrency semantics: in-flight requests racing `Invalidate`

**Creation racing invalidation (closed).** If `Invalidate`/registry disposal races a concurrent
`GetContext`/`AcquireLease` call that is still in the middle of *constructing* a new context for
that same tenant, the registry does not leak an orphaned, untracked context. The bare-reference
methods (`GetContext`/`GetContextAsync`) re-verify after construction that the `TenantContextEntry`
they resolved is still the one registered for that key; if a racing `Invalidate` already evicted
it, the caller discards its reference and retries — the entry's own `MarkRemoved`/disposal logic
(not the caller) owns tearing down the orphaned context, immediately or once construction is
observed complete. Closed with deterministic tests (`TenantTests.cs`:
`Invalidate_RacingWithInFlightCreate_DoesNotLeakOrphanedContext`,
`Dispose_RacingWithInFlightCreate_ThrowsInsteadOfLeakingOrphanedContext`).

**Concurrent callers racing to construct the same new tenant (closed, true single-flight).**
`GetContext`, `GetContextAsync`, `AcquireLease`, and `AcquireLeaseAsync` all resolve through the
same `TenantContextEntry.LazyContext` — a `Lazy<Task<IDatabaseContext>>` under
`LazyThreadSafetyMode.ExecutionAndPublication`. The factory delegate only has to *start* the async
construction (return a `Task`, non-blocking) rather than block a thread to produce a value
directly, so this single mechanism gives every caller — sync or async, in any mix — genuine
single-flight construction: the underlying connection/dialect-detection work happens exactly once
per tenant generation, regardless of how many callers race for it, with no redundant construction
to discard afterward. (An earlier design awaited construction fully before racing to install the
result, discarding the loser's already-built context as an orphan — that meant N concurrent callers
for a brand-new tenant each paid full connection/detection cost. The unified `Lazy<Task<T>>`
mechanism eliminates that waste entirely; there is no "loser" to construct redundantly.) Closed with
deterministic tests in `TenantContextRegistryAsyncTests.cs`
(`GetContextAsync_CalledConcurrentlyForSameNewTenant_InvokesFactoryExactlyOnce`,
`AcquireLeaseAsync_CalledConcurrentlyForSameNewTenant_InvokesFactoryExactlyOnce`) and
`TenantContextLeaseTests.cs` (`AcquireLease_ConcurrentReleaseAndInvalidateHammer_DisposesExactlyOnce`,
driving the *synchronous* `AcquireLease` at high fan-out).

**Lookup racing invalidation (closed via `AcquireLease`/`AcquireLeaseAsync`).** The bare-reference
methods (`GetContext`/`GetContextAsync`) still carry the original, narrower gap: a caller that hits
the fast, already-cached path and receives a live `IDatabaseContext` reference has no protection
against a concurrent `Invalidate`/`Dispose()` disposing that exact context immediately afterward —
inherent to handing out a bare object reference with no refcount. **This is now closed for callers
who need the guarantee**, via a lease: `TenantContextEntry` carries a CAS-based lease refcount
(`TryAddLease`/`ReleaseLease`, with a reserved `int.MinValue` sentinel meaning "disposal already
committed") so that acquiring a lease and a concurrent `Invalidate` marking the entry removed can
never race into handing back an already-disposed context — whichever side's CAS commits first is
authoritative, and `MarkRemoved` finding an outstanding lease defers disposal to that lease's
eventual release rather than disposing out from under it. This required more than a plain
`Interlocked.Increment` counter: an increment alone cannot distinguish "add a lease to a live
entry" from "resurrect a reference to an entry that already committed to disposal" — the CAS
against the `Dead` sentinel is what makes that distinction safe. Closed with deterministic tests in
`TenantContextLeaseTests.cs` covering lease-protects-context-across-`Invalidate`, multiple
concurrent leases requiring all releases before disposal, and a concurrent
acquire/release/invalidate hammer asserting exactly one disposal per constructed context (the exact
scenario that caught the plain-increment flaw during design review, before it shipped).
`GetContext`/`GetContextAsync` keep their original bare-reference contract unchanged — no lease is
taken, so `Invalidate`'s behavior for callers who don't opt in is exactly as before. Practically: a
caller that holds a tenant context across an `await` boundary during which a concurrent rotation
could plausibly occur should use `AcquireLease`/`AcquireLeaseAsync`, not cache a bare
`GetContext`/`GetContextAsync` reference.

**Registry-wide disposal.** This is fully closed, not a residual race: the registry's own
`IsDisposed` flag flips atomically at the very start of `Dispose()`/`DisposeAsync()`, before any
individual tenant context is torn down, and `GetContext`'s `ThrowIfDisposed()` check runs at the
top of every loop iteration — so a `GetContext` call arriving concurrently with registry shutdown
reliably observes `ObjectDisposedException` rather than a context that might be mid-disposal.
