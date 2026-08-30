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
| One context per tenant identifier is created and cached (`ConcurrentDictionary<string, Lazy<IDatabaseContext>>` with `Lazy<T>.ExecutionAndPublication` — the factory runs at most once per key even under concurrent first access). | **Tenant identity itself must be trusted before it reaches the registry.** `GetContext(tenant)` does not authenticate anything; it is a lookup key. Resolving `tenant` from validated authentication/authorization state upstream is the caller's job. |
| Tenant-ID comparison is case-insensitive (`StringComparer.OrdinalIgnoreCase`) end to end — `TenantConnectionResolver`'s configuration dictionary and `TenantContextRegistry`'s context cache use the same comparer, so `"acme"` and `"ACME"` always resolve to exactly one cached context, never two independently governed ones (fixed as CORE-009; this is not an accidental convenience, it's an invariant relied on to prevent duplicate-context/cardinality-evasion bugs). | No other normalization (trimming, casing conventions beyond ordinal case-insensitivity, Unicode normalization) is applied to the tenant identifier. Two strings that differ only by leading/trailing whitespace, for example, are treated as different tenants. |
| An optional `maxTenantCount` cap (`MultiTenantOptions.MaxTenantCount`) is enforced atomically against concurrent admission of *different* new tenants — the count-check and dictionary-add happen under one lock, so two threads racing to admit two different new tenants cannot both slip past the check. | Choosing and sizing the cap for the deployment's actual tenant cardinality; the library defaults to unbounded. |
| Object identity *is* tenant identity for lifecycle events — `ContextCreated`/`ContextRemoved` pass the `IDatabaseContext` instance itself, nothing else (see CORE-011's resolution: this is a deliberate architectural decision, not a missing feature — there is no separate tenant-ID field threaded through the API for a subscriber to correlate against). | A subscriber that needs a human-readable tenant label must derive or capture it itself (e.g. from the same call site that invoked `GetContext(tenantId)`), since the event does not supply one. |
| Each `DatabaseContext`'s own operational logging (connection lifecycle, dialect detection, session settings, etc.) uses the `ILoggerFactory` passed to *that* context's constructor/`CreateAsync`, so per-tenant sinks/scopes work correctly for everything the context itself logs. | **`TypeCoercionHelper`'s diagnostic logging is process-wide, not per-tenant.** It's an `internal static` utility with one static `Logger`, set once via first-caller-wins semantics (`SetLoggerIfUnset`, race-free but not per-context) the first time any `DatabaseContext` is constructed in the process. In a deployment with distinct per-tenant `ILoggerFactory` instances, every tenant *except* the first one to construct has its type-coercion warnings/errors logged through the *first* tenant's logger — misattributed, not lost. If per-tenant attribution of coercion diagnostics specifically matters, don't rely on this logger for it. |

## Context disposal: application shutdown, not live ejection, is the designed path

There is no designed, recommended product feature for ejecting or reconfiguring a tenant while the
application keeps running. The intended way a tenant's `IDatabaseContext` gets disposed is
application shutdown — disposing `ITenantContextRegistry` disposes every context it created. If a
deployment needs a tenant gone or reconfigured, restarting with updated configuration is the
supported path today; there is no live-rotation feature this architecture doc is describing as
recommended.

## `Invalidate`/`InvalidateAll` mechanics — an implemented primitive, described precisely, not endorsed for live use

`TenantContextRegistry.Invalidate`/`InvalidateAll` exist, are real, and their concurrency behavior
is deliberately hardened (CORE-009/010/011) — this section documents exactly what they do so that
if an application does choose to call them outside a shutdown sequence, the caller understands the
guarantees and gaps precisely, not so that doing so is a recommended pattern. It is a two-step,
caller-driven action — there is no polling, no background refresh, and no eventual-consistency
window baked into the registry itself:

1. `TenantConnectionResolver.Register(tenant, newConfig)` replaces the stored configuration
   snapshot for that tenant identifier. This alone has no effect on any already-cached context.
2. `TenantContextRegistry.Invalidate(tenant)` (or `InvalidateAll()`) synchronously removes the
   tenant's entry from the internal `ConcurrentDictionary` and, if a context had actually been
   constructed for it (`Lazy<T>.IsValueCreated`), disposes that context and raises `ContextRemoved`
   — all before `Invalidate` returns. There is no drain phase: `Invalidate` does not wait for
   in-flight operations on that context to complete before disposing it. (`IDatabaseContext.Dispose`
   itself does wait for its own outstanding pool leases to drain before releasing governors and
   owned data sources — see `docs/architecture.md`'s Reader-as-Lease Model and Connection
   Lifecycle Management sections for that contract. `Invalidate` triggers that same
   disposal, it does not add a separate drain step of its own.)

The next `GetContext(tenant)` call after invalidation constructs a brand-new context from
whatever configuration `TenantConnectionResolver` holds at that moment — so rotation is
**immediate from the registry's perspective** (the stale context is gone the instant `Invalidate`
returns), not "eventual" in the sense of a background refresh loop, but also not a coordinated,
zero-downtime handoff: any caller still holding a reference to the disposed context from before
invalidation is on its own once that context's own connections are torn down.

## Concurrency semantics: in-flight requests racing `Invalidate`

Two distinct races are relevant if `Invalidate` is ever called during live operation (again: not a
recommended pattern, but the guarantees below hold regardless), and they have different outcomes:

**Creation racing invalidation (closed).** If `Invalidate`/registry disposal races a concurrent
`GetContext` call that is still in the middle of *constructing* a new context for that same
tenant, the registry does not leak an orphaned, untracked context. `GetContext` re-verifies after
construction that its `Lazy` instance is still the one registered for that key; if a racing
`Invalidate` already evicted it, the just-built context is disposed as an orphan and the call
retries. This is closed with deterministic tests (`TenantTests.cs`:
`Invalidate_RacingWithInFlightCreate_DoesNotLeakOrphanedContext`,
`Dispose_RacingWithInFlightCreate_ThrowsInsteadOfLeakingOrphanedContext`).

**Lookup racing invalidation (a documented, open limitation — not closed).** If a caller's
`GetContext` call hits the fast, already-cached path and receives a live `IDatabaseContext`
reference, there is **no protection** against a concurrent `Invalidate`/`Dispose()` disposing that
exact context immediately afterward. The caller's reference can become a reference to a disposed
context between the moment `GetContext` returns and the moment the caller actually uses it. This
is an inherent consequence of handing out a bare object reference rather than a
reference-counted/leased handle, and closing it fully would require either a leased-context API
shape (a real public-contract change) or resolving the registry's disposal-ownership model further
— deliberately not attempted as of this writing (see CORE-010 in
`docs/planning/future-work.md` for the exact investigation and why a bigger redesign was judged
out of scope rather than half-done). Practically: a caller that holds a tenant context across an
`await` boundary during which a concurrent rotation could plausibly occur should re-resolve via
`GetContext` rather than caching the reference itself, since the registry provides no staleness
signal short of the context throwing `ObjectDisposedException` on its next use.

**Registry-wide disposal.** This is fully closed, not a residual race: the registry's own
`IsDisposed` flag flips atomically at the very start of `Dispose()`/`DisposeAsync()`, before any
individual tenant context is torn down, and `GetContext`'s `ThrowIfDisposed()` check runs at the
top of every loop iteration — so a `GetContext` call arriving concurrently with registry shutdown
reliably observes `ObjectDisposedException` rather than a context that might be mid-disposal.
