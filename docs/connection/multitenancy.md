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

## Registration, update, and invalidation (rotation) flow

To rotate a tenant onto new configuration (a new connection string, a different provider, a
password change) at runtime:

1. **Re-register** the tenant's configuration. `Register` is on the *concrete*
   `TenantConnectionResolver` class, not on the `ITenantConnectionResolver` interface —
   `AddMultiTenancy` registers the concrete instance under the interface type, so either resolve
   `TenantConnectionResolver` directly from DI, or cast:
   ```csharp
   ((TenantConnectionResolver)resolver).Register("acme", newConfig);
   ```
2. **Invalidate** the stale cached context so the registry stops handing it out:
   ```csharp
   registry.Invalidate("acme");
   ```
3. The next `GetContext("acme")` call creates a fresh `IDatabaseContext` using the newly
   registered configuration.

`InvalidateAll()` does the same for every tenant at once (e.g. during a coordinated
configuration reload). There is no partial/eventual rotation window — invalidation disposes and
evicts synchronously, and the next `GetContext` call is what triggers (re-)creation. A `GetContext`
call already in flight when `Invalidate` runs concurrently is protected against returning an
orphaned, undisposed context (see `docs/planning/future-work.md`'s CORE-010 entry for the exact,
narrower race that remains — a caller that already obtained a live context via the registry's fast
lookup path has no protection against a concurrent `Invalidate` disposing it immediately
afterward; this is a documented, accepted limitation, not an oversight).

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
tenant's `DatabaseContextConfiguration`. With the example above, tenant `acme` connects with
application name `MyApp:acme`. This is purely a connection-string/session-level `ApplicationName`
attribute (visible to the database server, e.g. in `pg_stat_activity` or SQL Server's
`sys.dm_exec_sessions`) — it is not re-exposed as a queryable property on the constructed
`IDatabaseContext` itself.

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
