# Multi-Tenancy and Dynamic Provider Loading

pengdows.crud's multi-tenancy model is **context-per-tenant**, not query filtering — each
tenant gets its own `DatabaseContext` wrapping its own `DbProviderFactory` and connection
string. There is no `WHERE tenant_id = X` injection; tenants can be on entirely different
database engines. This doc covers the two pieces that make dynamic, per-tenant provider
resolution work: registering `DbProviderFactory` instances (statically or from configuration),
and mapping tenant identifiers to configs/contexts via `ITenantContextRegistry`.

## 1. Register a keyed `DbProviderFactory` per provider

Factories are resolved through standard DI **keyed services**, keyed by provider name (e.g.
`"Sqlite"`, `"Postgres"`). The library does not pre-register any real provider for you — only
`pengdows.crud.fakeDb` ships pre-wired, and that's for unit tests only. Register each provider
you use:

```csharp
services.AddKeyedSingleton<DbProviderFactory>("Sqlite", Microsoft.Data.Sqlite.SqliteFactory.Instance);
services.AddKeyedSingleton<DbProviderFactory>("Postgres", Npgsql.NpgsqlFactory.Instance);
```

The key must equal the `ProviderName` in each tenant's `DatabaseContextConfiguration` (step 2):
`TenantContextRegistry` resolves the factory with
`GetKeyedService<DbProviderFactory>(config.ProviderName)`.

### Loading providers from configuration

2.0.6 has no public API for loading providers from configuration. `DbProviderLoader`
(`pengdows.crud/configuration/DbProviderLoader.cs`) exists, but its only constructor is
`internal` (it takes an `IConfiguration` and an `ILogger<DbProviderLoader>`), so application code
cannot construct it, and DI cannot activate it either. If you need provider selection driven by
configuration, resolve the factory yourself and register it as a keyed singleton — for example
from a `"DatabaseProviders"` section of your own shape:

```json
"DatabaseProviders": {
  "Postgres": { "AssemblyName": "Npgsql", "FactoryType": "Npgsql.NpgsqlFactory" },
  "Sqlite":   { "AssemblyName": "Microsoft.Data.Sqlite", "FactoryType": "Microsoft.Data.Sqlite.SqliteFactory" }
}
```

```csharp
using System.Data.Common;
using System.Reflection;

foreach (var section in configuration.GetSection("DatabaseProviders").GetChildren())
{
    var assembly = Assembly.Load(section["AssemblyName"]!);
    var type = assembly.GetType(section["FactoryType"]!, throwOnError: true)!;
    var factory = (DbProviderFactory)(
        type.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
        ?? type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!);

    // Key = the name tenants use as their ProviderName
    services.AddKeyedSingleton<DbProviderFactory>(section.Key, factory);
}
```

(For reference, the internal `DbProviderLoader` follows the same pattern: it loads the assembly
from `AssemblyPath` — restricted to the application base directory — or `AssemblyName`, reflects
a public static `Instance` property or field off `FactoryType`, otherwise falls back to
`DbProviderFactories.GetFactory(ProviderName)`, and registers the result as a keyed singleton
under the configuration section key plus via `DbProviderFactories.RegisterFactory`.)

## 2. Register tenants and wire up multi-tenancy

`AddMultiTenancy` (`pengdows.crud/tenant/TenantServiceCollectionExtensions.cs`) binds a
`"MultiTenant"` configuration section into `MultiTenantOptions`, populates
`ITenantConnectionResolver` (tenant name → `IDatabaseContextConfiguration`), and registers
`ITenantContextRegistry`:

```json
"MultiTenant": {
  "Tenants": [
    { "Name": "TenantA", "DatabaseContextConfiguration": {
        "ConnectionString": "Host=db1;Database=tenant_a", "ProviderName": "Postgres", "DbMode": "Standard" } },
    { "Name": "TenantB", "DatabaseContextConfiguration": {
        "ConnectionString": "Data Source=tenantb.db", "ProviderName": "Sqlite", "DbMode": "SingleWriter" } }
  ]
}
```

```csharp
services.AddLogging();
// keyed factories from step 1 must be registered first (or alongside)
services.AddMultiTenancy(configuration);
```

## 3. Resolve a tenant's context at request time

`ITenantContextRegistry.GetContext(tenant)` (`pengdows.crud/tenant/TenantContextRegistry.cs`)
lazily creates and caches one `DatabaseContext` per tenant, thread-safely
(`ConcurrentDictionary<string, TenantContextEntry>`, each entry wrapping a `Lazy<IDatabaseContext>`
plus the lease refcount used by `AcquireLease` — see §4). Internally, `CreateDatabaseContext`
resolves the keyed factory by the tenant's `ProviderName` and hands it to `IDatabaseContextFactory`:

```csharp
var factory = _serviceProvider.GetKeyedService<DbProviderFactory>(config.ProviderName)
              ?? throw new InvalidOperationException($"No factory registered for '{config.ProviderName}'.");
var context = _contextFactory.Create(config, factory, _loggerFactory);
```

Usage in application code:

```csharp
var tenantCtx = registry.GetContext(tenantId);       // resolved per-request
var order = await gateway.RetrieveOneAsync(orderId, tenantCtx);
await gateway.CreateAsync(newOrder, tenantCtx);
```

Since `ProviderName` can differ per tenant, this is how a single deployment serves tenants on
different database engines with zero tenant-aware branching in gateway code.

## 4. Protecting against concurrent rotation (`AcquireLease`)

`GetContext` returns a bare `IDatabaseContext` reference with no protection against a concurrent
`Invalidate`/`InvalidateAll` disposing that exact context immediately after — fine for the common
case (resolve, then immediately use it), but not if you hold the reference across any later point
where a concurrent invalidation could race your usage. `AcquireLease` closes that gap with a
reference-counted lease:

```csharp
using var lease = registry.AcquireLease(tenantId);
await gateway.RetrieveOneAsync(orderId, lease.Context);
// lease.Context is guaranteed not to be disposed by a concurrent Invalidate/InvalidateAll until
// this lease itself is disposed.
```

`Invalidate` on an actively-leased tenant still removes it from the registry's lookup immediately
(so the *next* `GetContext`/`AcquireLease` call gets a fresh context right away), but defers
actually disposing the superseded context until every outstanding lease on it has been released.
Multiple concurrent leases on the same tenant are independent — the context is only disposed once
the *last* one releases. Actual disposal is dispatched onto the thread pool rather than run inline
on the releasing/invalidating thread (`DatabaseContext.Dispose()` can itself block on its own
pool-governor drain wait), so it can lag slightly behind the call that triggered it — don't assume
disposal has already happened by the time `Invalidate`/a lease's `Dispose()` returns; subscribe to
`ContextRemoved` if you need to observe completion.

**Note:** `AcquireLease` is synchronous only in this version — there is no `AcquireLeaseAsync` or
`GetContextAsync`, because 2.0's `IDatabaseContextFactory` has no async `CreateAsync` overload
(that's a separate, later feature). A newer tenant-context API with those async variants exists in
later versions — see the `pengdows.crud` (current) repo's `docs/connection/multitenancy.md` if
you're on a newer version.

## 5. Lifecycle management

- `registry.Invalidate(tenant)` — evicts and disposes one tenant's cached context (e.g. after
  rotating its connection string: call `Register(tenant, newConfig)` on the concrete
  `TenantConnectionResolver` — `ITenantConnectionResolver` itself only exposes
  `GetDatabaseContextConfiguration` — then `Invalidate`).
- `registry.InvalidateAll()` — evicts every cached context.
- Optional `maxTenantCount` constructor argument (not settable through `AddMultiTenancy`, which
  registers the registry with its defaults; construct `TenantContextRegistry` yourself to use it) caps concurrently cached contexts, to guard
  against connection-pool exhaustion with many tenants — `GetContext`/`AcquireLease` throw
  `InvalidOperationException` for a genuinely new tenant once the cap is reached. Admission is
  atomic against the dictionary check-and-add, so concurrent distinct tenants can never exceed
  the cap even under a race.
- `ContextCreated` / `ContextRemoved` events (`Action<IDatabaseContext>`) fire on create/dispose.
- Tenant lookup is case-insensitive (`"acme"`/`"ACME"` resolve to the same cached context),
  matching `ITenantConnectionResolver`'s own case-insensitive comparer.

**Register `ITenantContextRegistry` as a singleton** (`AddMultiTenancy` already does) — same
lifetime rule as `DatabaseContext` and `TableGateway<T,TId>`.

## Reference

- End-to-end example: `pengdows.crud.Tests/MultitenantIntegrationTests.cs` (uses `fakeDb`,
  shows keyed-factory + `AddMultiTenancy` + `GetContext` wiring for two SQLite tenants under
  different `DbMode`s).
- Lease semantics and concurrency guarantees: `pengdows.crud.Tests/TenantContextLeaseTests.cs`.
- Simpler DI-only pattern without the registry (a hand-rolled keyed `DatabaseContext` per
  tenant name, no dynamic provider loading): `docs/ARCHITECTURE.md` (multi-tenancy section).
- There is no standalone runnable multitenant sample project in this repo
  (`pengdows-crud-example` doesn't cover this) — the test file above is the closest working
  demo.
