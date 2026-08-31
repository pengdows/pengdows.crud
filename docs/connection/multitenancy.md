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

Both methods share the same cache — an already-cached tenant resolves immediately and identically
from either method. For two concurrent `GetContextAsync` calls racing to construct the *same*
not-yet-cached tenant, only one wins and installs its context; the other's redundant,
already-constructed context is disposed as an orphan and that caller transparently receives the
winner's context instead — never a leaked, uncached duplicate (see
[`multitenancy-architecture.md`](./multitenancy-architecture.md#concurrency-semantics-in-flight-requests-racing-invalidate)
for the exact mechanism, including how it differs from `GetContext`'s own dedup). `CancellationToken`
is honored only while a not-yet-cached tenant's context is actually being constructed; it has no
effect once the tenant is cached (there is nothing left to cancel).

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

## Disposal: application shutdown is the intended path

The primary, designed way a tenant's `IDatabaseContext` gets disposed is **application shutdown**
— disposing `ITenantContextRegistry` itself (directly, or via normal DI container teardown) disposes
every context it has created. There is currently no designed, recommended product feature for
*live* tenant ejection or configuration rotation while the application keeps running. If your
process needs a tenant gone or reconfigured, restarting it (or the relevant subset of it) with
updated configuration is the supported path today.

## `Invalidate`/`InvalidateAll`: implemented primitives, not a designed live-rotation feature

`ITenantContextRegistry` does expose `Invalidate(tenant)` and `InvalidateAll()`, and they are real,
tested APIs — CORE-009/010/011 in `docs/planning/future-work.md` hardened their concurrency
behavior. Documenting their mechanics here for completeness, **not** as a recommended workflow:

```csharp
((TenantConnectionResolver)resolver).Register("acme", newConfig); // Register is on the concrete
                                                                    // class, not the interface
registry.Invalidate("acme"); // disposes and evicts the cached context, if one exists
// The next GetContext("acme") call constructs a fresh context from the newly registered config.
```

`InvalidateAll()` does the same for every tenant at once. Invalidation disposes and evicts
synchronously — there is no partial/eventual window, and the next `GetContext` call is what
triggers (re-)creation.

**Calling this during live operation is an application-level choice, not a vetted pattern**, and
it carries a known, accepted residual race: a caller that already obtained a live context via the
registry's fast lookup path has no protection against a concurrent `Invalidate` disposing that
exact context immediately afterward (see `docs/planning/future-work.md`'s CORE-010 entry). That
race is a real gap in the primitive's live-operation safety — one more reason application shutdown,
not live invalidation, is the only currently-designed disposal trigger. If you do call `Invalidate`
outside of a shutdown sequence, you are taking on that risk yourself; treat it as an
application-specific decision, not something this library has designed and hardened for routine
use.

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

### Cross-provider migration: what the mechanism would look like

There is no designed, recommended live-migration *feature* (see "Disposal: application shutdown is
the intended path" above) — but it's worth being precise about what happens mechanically if an
application chose to use `Register`+`Invalidate` for this anyway, since the answer is "nothing
provider-specific breaks, and no stale-cache cleanup step exists to forget":

```csharp
// Tenant "acme" is moving from SQL Server to PostgreSQL.
var newConfig = new DatabaseContextConfiguration
{
    ConnectionString = "Host=acme-pg;Database=acme;...",
    ProviderName = "postgres", // a different DatabaseProviders section key
    DbMode = DbMode.Standard
};

((TenantConnectionResolver)resolver).Register("acme", newConfig);
registry.Invalidate("acme"); // application-level choice, not a vetted pattern — see caveat above

// The next GetContext("acme") call — from the very same shared gateway used by every other
// tenant — constructs a context against PostgreSQL and the gateway emits PostgreSQL SQL for it,
// with zero gateway-side code change and no separate "acme is now Postgres" branch anywhere.
var tenantContext = registry.GetContext("acme");
await gateway.UpsertAsync(order, tenantContext);
```

Because dialect-derived caches are keyed by dialect instance/fingerprint (not by tenant identity),
the old SQL Server-shaped cache entries for "acme" simply stop being referenced once its context is
disposed — there is no stale-cache cleanup step to remember, and no risk of PostgreSQL SQL landing
on the old SQL Server connection or vice versa. This is a statement about what the *caches* do, not
an endorsement of live invalidation as a migration strategy — the same CORE-010 caveat applies.

## Lifecycle events

```csharp
registry.ContextCreated += ctx => logger.LogInformation("Tenant context created: {Name}", ctx.Name);
registry.ContextRemoved += ctx => logger.LogInformation("Tenant context removed: {Name}", ctx.Name);
```

Both events pass the `IDatabaseContext` itself — there is no separate tenant-ID parameter. Since
the context *is* the tenant's identity, a subscriber that needs to know "which tenant" already
knows, because it's the same code path that called `GetContext(tenantId)` in the first place.
`ContextRemoved` fires for `Invalidate`, `InvalidateAll`, **and** registry disposal (which disposes
and raises the event for every context the registry has created) — not just the first two.

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
