# Multi-Tenancy (`AddMultiTenancy`)

pengdows.crud's multi-tenancy model is **context-per-tenant**, not row-level filtering. There is
no `WHERE tenant_id = X` injected into your queries, and there is no separate "tenant ID"
concept threaded through the library's APIs. Each tenant is given its own, independently governed
`IDatabaseContext` — its own connection pool, admission control, dialect, and metrics — and that
`IDatabaseContext` instance *is* the tenant's identity from the library's point of view. Database-
per-tenant (a physically separate database per tenant) is the deployment/provisioning shape this
is designed around, but the library only enforces context isolation — it does not provision
databases or authenticate tenant identity for you. Trusting the `tenant` string passed to
`GetContext` is the caller's responsibility.

This doc is the getting-started guide. For the deeper architectural contract — precisely what
isolation is library-enforced vs. deployment-assumed, tenant-ID case/normalization rules, and the
exact concurrency semantics of rotation and in-flight requests — see
[`multitenancy-architecture.md`](./multitenancy-architecture.md).

This doc covers the standard, configuration-driven path: `AddMultiTenancy`, `MultiTenantOptions`,
`TenantConfiguration`, `ITenantConnectionResolver`, and `ITenantContextRegistry`.

**If you're building a new application and there's any realistic chance it will ever need more
than one tenant, adopt context-per-tenant from the start** — even if you launch with exactly one
tenant. Retrofitting it later is a low-effort change (wrap your existing single connection string
in a one-entry `MultiTenant:Tenants` list and resolve through `ITenantContextRegistry` instead of
a single `IDatabaseContext`), but the earlier you adopt the pattern, the less application code ever
has to be rewritten to stop assuming a single, ambient database connection.

## Configuration shape

```json
{
  "MultiTenant": {
    "ApplicationName": "MyApp",
    "MaxTenantCount": 500,
    "Tenants": [
      {
        "Name": "acme",
        "DatabaseContextConfiguration": {
          "ConnectionString": "Server=acme-db;Database=acme;...",
          "ProviderName": "sqlserver",
          "DbMode": "Standard"
        }
      },
      {
        "Name": "globex",
        "DatabaseContextConfiguration": {
          "ConnectionString": "Host=globex-db;Database=globex;...",
          "ProviderName": "postgres",
          "DbMode": "Standard"
        }
      }
    ]
  }
}
```

Each `TenantConfiguration.Name` is the tenant identifier you'll pass to `GetContext`. Each
tenant's `DatabaseContextConfiguration` is a full, ordinary `IDatabaseContextConfiguration` —
tenants can use different providers, different connection strings, different `DbMode`s, and
different server versions; nothing requires them to share an engine. `ProviderName` here is the
**provider-loading section key**, not necessarily an ADO.NET invariant name — see
[`dynamic-provider-loading.md`](./dynamic-provider-loading.md#di-registration-key-vs-providername--the-gotcha)
for the exact contract if you're loading providers dynamically alongside multi-tenancy (call
`AddDbProviderLoading` before `AddMultiTenancy` so the keyed factories exist when tenants resolve
against them).

## DI registration

```csharp
services.AddMultiTenancy(configuration);
```

This single call, reading the `MultiTenant` configuration section, registers:

- `IOptions<MultiTenantOptions>` (via `Configure<T>`)
- `ITenantConnectionResolver` as a singleton (`TenantConnectionResolver`), pre-populated with every
  tenant listed in configuration
- `IDatabaseContextFactory` as a singleton (`TryAddSingleton<IDatabaseContextFactory,
  DefaultDatabaseContextFactory>()` — see "Replacing the context factory" below)
- `ITenantContextRegistry` as a singleton (`TenantContextRegistry`), wired to the resolver and
  factory above, with `MultiTenantOptions.MaxTenantCount` already threaded through as its
  cardinality cap

## Custom resolver (no `AddMultiTenancy`)

`AddMultiTenancy` covers the common case — a static tenant list expressible as configuration. If
your tenant list is dynamic (loaded from a control-plane database, provisioned by a separate
onboarding system, too large for `appsettings.json`), implement `ITenantConnectionResolver`
yourself and wire the pieces `AddMultiTenancy` would otherwise assemble for you by hand:

```csharp
public interface ITenantConnectionResolver
{
    IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant);
}
```

```csharp
// 1. A keyed DbProviderFactory for every ADO.NET provider your tenants use — same requirement
//    as the configuration-driven path, just without AddDbProviderLoading doing it for you.
services.AddKeyedSingleton<DbProviderFactory>("postgres", NpgsqlFactory.Instance);
services.AddKeyedSingleton<DbProviderFactory>("sqlserver", SqlClientFactory.Instance);

// 2. Your resolver, as a singleton.
services.AddSingleton<ITenantConnectionResolver, MyCustomTenantResolver>();

// 3. AddMultiTenancy registers both of these for you; a custom setup registers them directly.
//    TenantContextRegistry's constructor also takes IServiceProvider and ILoggerFactory, but
//    those are supplied automatically by the container — nothing extra to register for them.
services.AddSingleton<IDatabaseContextFactory, DefaultDatabaseContextFactory>();
services.AddSingleton<ITenantContextRegistry, TenantContextRegistry>();
```

From here, request-time usage (`ITenantContextRegistry.GetContext(tenantId)`, singleton-gateway
calls) is identical to the `AddMultiTenancy` path — the registry doesn't know or care which kind of
resolver it's backed by. See
[`docs/examples/CustomTenantResolver-example.cs`](../examples/CustomTenantResolver-example.cs) for
a complete, worked example: a resolver that loads tenant rows from a control-plane database via
plain ADO.NET, plus the two-step `Register`-then-`Invalidate` pattern for picking up a single
tenant's changed configuration.

## Non-blocking context creation (`GetContextAsync`)

`GetContext` is synchronous — for a not-yet-cached tenant, the calling thread blocks for the
duration of connection/dialect-detection work. `GetContextAsync(tenant, cancellationToken)` does
the same lookup-or-create job without blocking the caller's thread on a not-yet-cached tenant:

```csharp
var tenantContext = await registry.GetContextAsync(tenantId, cancellationToken);
```

Both methods share one cache and one construction mechanism — an already-cached tenant resolves
immediately and identically from either method, and concurrent callers racing to construct the
*same* not-yet-cached tenant (any mix of `GetContext` and `GetContextAsync`) genuinely single-flight:
the underlying connection/dialect-detection work happens exactly once, and every caller converges
on the same resulting context — never duplicate construction, never a leaked orphan. This matters
in a multi-tenant webservice specifically because a burst of concurrent first-requests for a
brand-new tenant is a real, common pattern; single-flighting it avoids paying connection/detection
cost N times for N simultaneous requests. `CancellationToken` is honored only for *your own* wait —
if another caller is already constructing the same not-yet-cached tenant, cancelling your token
stops your wait without cancelling that shared construction for whoever else is still waiting on it.

## Protecting against concurrent rotation (`AcquireLease`/`AcquireLeaseAsync`)

`GetContext`/`GetContextAsync` return a bare `IDatabaseContext` reference with no protection
against a concurrent `Invalidate`/`InvalidateAll` disposing that exact context immediately after —
fine for the common case (resolve, then immediately use it in the same synchronous/async flow), but
not if you hold the reference across an `await` boundary or otherwise can't guarantee your usage
completes atomically with respect to a concurrent rotation. `AcquireLease`/`AcquireLeaseAsync` close
that gap:

```csharp
using var lease = registry.AcquireLease(tenantId);
// or: await using var lease = await registry.AcquireLeaseAsync(tenantId, cancellationToken);

await gateway.RetrieveOneAsync(orderId, lease.Context);
// lease.Context is guaranteed not to be disposed by a concurrent Invalidate/InvalidateAll until
// this lease itself is disposed, however many awaits happen in between.
```

The lease is a reference count on the tenant's context: `Invalidate` on an actively-leased tenant
still removes it from the registry's lookup immediately (so the *next* `GetContext`/`AcquireLease`
call gets a fresh context right away), but defers actually disposing the superseded context until
every outstanding lease on it has been released. Multiple concurrent leases on the same tenant are
independent — the context is only disposed once the *last* one releases. Dispose the lease as soon
as you're done; holding it longer than necessary delays a concurrent rotation's actual cleanup.

This is what makes live tenant ejection/rotation a genuinely supported pattern rather than an
accepted-risk primitive: any code path that holds a tenant context across an await (a long-running
operation, a stream, a background job) should acquire a lease for it; anywhere else, the simpler
`GetContext`/`GetContextAsync` remain the right default.

## Request-time resolution and singleton-gateway usage

`ITenantContextRegistry` is the entry point at request time. Resolve the tenant's context and pass
it explicitly into whatever singleton gateway you already have — gateways are stateless and
context-agnostic per call, so one gateway instance serves every tenant:

```csharp
public class OrderService
{
    private readonly ITenantContextRegistry _registry;
    private readonly ITableGateway<Order, long> _orders; // singleton, constructed once

    public OrderService(ITenantContextRegistry registry, ITableGateway<Order, long> orders)
    {
        _registry = registry;
        _orders = orders;
    }

    public async Task<Order?> GetOrderAsync(string tenantId, long orderId)
    {
        var tenantContext = _registry.GetContext(tenantId); // trusted tenant id -> that tenant's context
        return await _orders.RetrieveOneAsync(orderId, tenantContext);
    }
}
```

`tenantId` here must already be a **trusted** value — resolved from authentication/authorization
upstream, not taken directly from unvalidated user input. `GetContext` does not authenticate
anything; it looks up (or lazily creates) the `IDatabaseContext` registered under that string.

## Disposal: application shutdown, or live rotation via `Invalidate` + leases

Application shutdown remains the simplest way a tenant's `IDatabaseContext` gets disposed —
disposing `ITenantContextRegistry` itself (directly, or via normal DI container teardown) disposes
every context it has created. But **live tenant ejection/rotation while the application keeps
running is now a genuinely supported pattern**, not just an accepted-risk primitive, provided any
code path that holds a tenant context across an `await` uses `AcquireLease`/`AcquireLeaseAsync`
(see above) rather than caching a bare `GetContext`/`GetContextAsync` reference.

## `Invalidate`/`InvalidateAll`: the live-rotation recipe

```csharp
((TenantConnectionResolver)resolver).Register("acme", newConfig); // Register is on the concrete
                                                                    // class, not the interface
registry.Invalidate("acme"); // evicts the cached context immediately; disposes it once idle
// The next GetContext("acme")/AcquireLease("acme") call constructs a fresh context from the
// newly registered config, right away — it does not wait for the superseded context to finish
// disposing.
```

`InvalidateAll()` does the same for every tenant at once. Eviction from the registry's lookup is
always synchronous and immediate — the very next `GetContext`/`AcquireLease` call for that tenant
never sees the superseded entry. **Actual disposal of the superseded context is asynchronous**:
`Invalidate` never blocks the calling thread waiting for it, and if the entry was leased when
`Invalidate` ran, disposal is deferred until every outstanding lease releases (see "Protecting
against concurrent rotation" above). Even for an idle (no-lease) tenant, the underlying
`DatabaseContext.Dispose()` call — which can itself block synchronously on its own pool-governor
drain wait — is dispatched off the calling thread rather than run inline; confirmed empirically
that this matters under real concurrent load, not just as a theoretical nicety (running it inline
measurably contributed to thread-pool contention under many concurrent lease/release/invalidate
cycles). Don't assume a tenant's context is already disposed the instant `Invalidate`/lease-`Dispose`
returns — if you need to observe completion, subscribe to `ContextRemoved`.

**The one residual gap, by design, not oversight:** `GetContext`/`GetContextAsync`'s bare-reference
callers still have no protection against a concurrent `Invalidate` disposing the exact context they
just received — that's precisely what `AcquireLease`/`AcquireLeaseAsync` exist to close. For the
common "resolve, then immediately use in the same flow" pattern, the plain bare-reference methods
remain simpler and are still the right default.

## Heterogeneous providers behind one shared gateway

Nothing about the context-per-tenant model requires tenants to share a database engine, a server
version, or even a deployment topology. This is proven against real servers, not just asserted:
`MultiTenantDialectVersionTests.SharedGateway_TwoRealMySqlVersions_GeneratesCorrectSqlPerTenant`
(`pengdows.crud.IntegrationTests/Core/`) runs two real MySQL containers straddling the 8.0.20
upsert-syntax threshold (`MySqlDialect.UpsertIncomingAlias`), constructs **one** `TableGateway<TenantEntity, long>`
instance, and calls `BuildUpsert`/`UpsertAsync` against each tenant's context — the legacy-version
tenant gets `VALUES(col)` syntax, the newer-version tenant gets the aliased `incoming.col` form, and
both execute successfully against their own real server:

```csharp
var gateway = new TableGateway<TenantEntity, long>(anyInitialContext); // one instance, any tenant

await gateway.UpsertAsync(entityForTenantA, tenantAContext); // MySQL 8.0.19 -> legacy VALUES(col)
await gateway.UpsertAsync(entityForTenantB, tenantBContext); // MySQL 8.0.33 -> incoming.col alias
```

The same pattern extends to entirely different engines: tenant A on PostgreSQL, tenant B on MySQL,
tenant C on SQL Server, all served by the same gateway instance — each call simply passes that
tenant's own `IDatabaseContext`, and the gateway asks that context's dialect for the correct SQL
shape every time. Multi-engine and multi-version operation is something the architecture happens
to support as a consequence of context-per-tenant isolation — it is not what makes tenants isolated
from each other in the first place (that's the independent `PoolGovernor`/admission/metrics
separation covered in
[`multitenancy-architecture.md`](./multitenancy-architecture.md#the-model-a-tenant-selects-a-complete-execution-environment)).
A single-engine, single-version fleet gets exactly the same isolation guarantees.

### Dialect-capability cache isolation

A singleton `TableGateway<TEntity,TRowID>` caches several dialect-derived artifacts internally so
repeated calls across many tenants don't repeatedly rebuild the same SQL/parameter-binding logic.
Two tenants on different engines or different versions of the same engine never share a cache entry
that would produce wrong SQL for either:

- **Compiled parameter binders** (`_insertBinders`/`_upsertBinders`/`_updateBinders`) — keyed by
  **dialect instance** (`ConditionalWeakTable<ISqlDialect, ...>`), because the compiled binder
  closes over the actual dialect instance and its live `DbProviderFactory` (and, for Firebird, its
  `GuidStorageMode`) — properties a coarse version fingerprint can't fully capture. Reclaimed
  automatically once a tenant's `DatabaseContext`/dialect is no longer referenced.
- **Pure SQL-text templates** (`_templatesByDialect`) — keyed by a **fingerprint string**
  (`dialect.GetCacheFingerprint()`, effectively `DatabaseType + ParsedVersion`), not by instance —
  every property this cache depends on is either constant per `DatabaseType` or a pure function of
  `ParsedVersion`, so many tenants standardized on the identical engine+version correctly share one
  cache entry instead of accumulating a redundant copy per tenant.
- **Pre-built container templates** (`_containersByDialect`) — instance-keyed like the binder
  caches, for the same reason (bakes real `DbParameter` construction).

The comment trail in `TableGateway.Core.cs` (search `_templatesByDialect`) states the actual bug
this design prevents: a cache keyed only by the coarse `SupportedDatabase` enum would let whichever
tenant's dialect built the entry first silently dictate SQL for every other same-enum tenant, even
when a dialect property is legitimately version-gated — exactly the MySQL 8.0.19-vs-8.0.33 scenario
the integration test above exercises.

### Cross-provider migration: what the mechanism looks like

It's worth being precise about what happens mechanically when an application uses `Register`+
`Invalidate` to migrate a live tenant to a different provider: nothing provider-specific breaks,
and no stale-cache cleanup step exists to forget.

```csharp
// Tenant "acme" is moving from SQL Server to PostgreSQL.
var newConfig = new DatabaseContextConfiguration
{
    ConnectionString = "Host=acme-pg;Database=acme;...",
    ProviderName = "postgres", // a different DatabaseProviders section key
    DbMode = DbMode.Standard
};

((TenantConnectionResolver)resolver).Register("acme", newConfig);
registry.Invalidate("acme"); // safe under concurrent use — see the live-rotation sections above

// The next GetContext("acme") call — from the very same shared gateway used by every other
// tenant — constructs a context against PostgreSQL and the gateway emits PostgreSQL SQL for it,
// with zero gateway-side code change and no separate "acme is now Postgres" branch anywhere.
var tenantContext = registry.GetContext("acme");
await gateway.UpsertAsync(order, tenantContext);
```

Because dialect-derived caches are keyed by dialect instance/fingerprint (not by tenant identity),
the old SQL Server-shaped cache entries for "acme" simply stop being referenced once its context is
disposed — there is no stale-cache cleanup step to remember, and no risk of PostgreSQL SQL landing
on the old SQL Server connection or vice versa. Any code path that might still be mid-flight against
the old context when this migration runs should hold an `AcquireLease` rather than a bare
`GetContext` reference, so it finishes safely against the old provider instead of racing disposal.

## Lifecycle events

```csharp
registry.ContextCreated += ctx => logger.LogInformation("Tenant context created: {Name}", ctx.Name);
registry.ContextRemoved += ctx => logger.LogInformation("Tenant context removed: {Name}", ctx.Name);
```

Both events pass the `IDatabaseContext` itself — there is no separate tenant-ID parameter. Since
the context *is* the tenant's identity, a subscriber that needs to know "which tenant" already
knows, because it's the same code path that called `GetContext(tenantId)` in the first place.
`ContextRemoved` fires for `Invalidate`, `InvalidateAll`, a leased context's last `AcquireLease`
release finding its tenant already invalidated, **and** registry disposal (which disposes and
raises the event for every context the registry has created) — not just the first two. For
`Invalidate`/`InvalidateAll`/lease-release, the event fires asynchronously relative to the call
that triggered it (see "Disposal" above) — don't assume it has already fired by the time
`Invalidate` or a lease's `Dispose`/`DisposeAsync` returns.

## Application-name composition

`TenantConnectionResolver.Register(MultiTenantOptions)` composes each tenant's effective
`ApplicationName` as `"{MultiTenantOptions.ApplicationName}:{TenantName}"` before constructing that
tenant's `DatabaseContextConfiguration` — but only when that tenant's own
`DatabaseContextConfiguration.ApplicationName` wasn't already set explicitly; an
already-populated per-tenant `ApplicationName` is left untouched. With the example above, tenant
`acme` (which doesn't set its own `ApplicationName`) connects with application name `MyApp:acme`.
This is purely a connection-string/session-level `ApplicationName` attribute (visible to the
database server, e.g. in `pg_stat_activity` or SQL Server's `sys.dm_exec_sessions`) — it is not
re-exposed as a queryable property on the constructed `IDatabaseContext` itself.

## Tenant cardinality (`MaxTenantCount`)

See [`dynamic-provider-loading.md`](./dynamic-provider-loading.md#tenant-cardinality-cap-maxtenantcount)
— `MultiTenantOptions.MaxTenantCount` binds from the same `MultiTenant` configuration section and
is threaded through to `TenantContextRegistry`'s cardinality cap. Leave it unset for an unbounded
registry (the default); set it for any long-lived process serving many distinct, dynamically
discovered tenants, to bound worst-case connection-pool growth.

## Failure and retry behavior

If constructing a tenant's context throws (a bad connection string, an unreachable database, a
factory that isn't registered), `GetContext` propagates that exception to the caller and does
**not** cache a broken context — the failed attempt is evicted internally, so a later `GetContext`
call for the same tenant starts a fresh construction attempt rather than repeatedly returning (or
throwing from) a stale, half-created instance. There is no built-in retry/backoff inside
`GetContext` itself — a transient failure (e.g. the database was briefly unreachable) surfaces
immediately, and retrying is the caller's responsibility, exactly like an ordinary failed
`DatabaseContext` construction anywhere else in the library. If a `ContextCreated` subscriber
throws after a context was otherwise constructed successfully, the just-created context is
disposed and the subscriber's exception propagates from `GetContext` — the context never becomes
visible to callers or gets cached in that case either, so a later `GetContext` call reliably starts
over rather than returning a context registered subscribers never actually got notified about.

## Replacing the context factory

`IDatabaseContextFactory` is registered via `TryAddSingleton`, so an application can supply its own
implementation by registering it **before** calling `AddMultiTenancy` — `TenantContextRegistry`
will use it for every context it creates from then on, while tenant resolution and registry
lifecycle rules (caching, invalidation, events, `MaxTenantCount`) stay exactly as documented above.
A custom factory must return a new, independently owned context on each call — `TenantContextRegistry`
already caches and disposes contexts per its own lifecycle; a factory that decorates or caches
contexts itself would double-cache and is not a supported pattern.
