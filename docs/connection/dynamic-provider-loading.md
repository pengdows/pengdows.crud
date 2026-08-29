# Dynamic Provider Loading (`DbProviderLoader`)

`DbProviderLoader` (`pengdows.crud/configuration/DbProviderLoader.cs`) loads `DbProviderFactory`
instances from configuration instead of requiring the host application to reference the
provider's assembly and construct the factory in code. It exists primarily to support
`ITenantContextRegistry`, where the set of providers in use is data (per-tenant configuration),
not something known at compile time.

## Configuration shape

```json
{
  "DatabaseProviders": {
    "sqlserver": {
      "ProviderName": "System.Data.SqlClient",
      "AssemblyName": "System.Data.SqlClient",
      "FactoryType": "System.Data.SqlClient.SqlClientFactory"
    },
    "custom-oracle": {
      "ProviderName": "Oracle.ManagedDataAccess.Client",
      "AssemblyPath": "providers/Oracle.ManagedDataAccess.dll",
      "FactoryType": "Oracle.ManagedDataAccess.Client.OracleClientFactory"
    }
  }
}
```

The standard entry point is the `IServiceCollection` extension method — call it once at startup,
typically alongside `AddMultiTenancy` if you're using both:

```csharp
services.AddDbProviderLoading(configuration);
```

This constructs a `DbProviderLoader` internally and calls `LoadAndRegisterProviders(services)`,
so all provider factories are loaded and registered as part of DI composition, before the service
provider is built. Pass an `ILogger<DbProviderLoader>` explicitly if you need provider-loading
diagnostics — one can't be resolved automatically at this point since the container isn't built
yet:

```csharp
services.AddDbProviderLoading(configuration, myPreBuiltLogger);
```

Using `DbProviderLoader` directly (what `AddDbProviderLoading` does under the hood) is only
necessary outside a plain `IServiceCollection` composition flow:

```csharp
var loader = new DbProviderLoader(configuration, logger);
loader.LoadAndRegisterProviders(services);
```

Each entry under `DatabaseProviders` is keyed by an arbitrary **section key** you choose
(`"sqlserver"`, `"custom-oracle"` above) — it does not need to match `ProviderName` or any
ADO.NET invariant name.

## Factory resolution order

For each configured provider, `LoadProviderFactory` resolves the `DbProviderFactory` instance in
this order:

1. **Assembly load** (only if `AssemblyPath` or `AssemblyName` is set):
   - `AssemblyPath` — loaded via `Assembly.LoadFrom`, resolved relative to
     `AppDomain.CurrentDomain.BaseDirectory`. The resolved path (and, if it is a symlink, its
     fully-resolved real target) **must stay within the application's base directory** — this is
     an enforced containment boundary, not a convention; a path or a symlink target that
     escapes it throws `InvalidOperationException`. Directory-component symlinks are not
     specifically walked — the boundary assumes the app's own base directory hasn't itself been
     compromised with a planted symlinked directory, a materially larger threat than a single
     configured path.
   - `AssemblyName` — loaded via `Assembly.Load` (uses the standard assembly-resolution probing,
     not path-based).
2. **`FactoryType` static accessor**, once an assembly is loaded: looks up a public static
   `Instance` member on the named type and uses its value as the factory. **Both conventions
   real ADO.NET providers use are supported** — a static `Instance` *property* (e.g. Npgsql's
   `NpgsqlFactory.Instance`) or a static `Instance` *field* (e.g.
   `System.Data.SqlClient.SqlClientFactory.Instance`, MySql.Data's
   `MySqlClientFactory.Instance`). The property is checked first if a type happens to expose
   both. Neither existing throws `InvalidOperationException` naming both conventions in the
   message.
3. **Fallback**: if no `FactoryType`/assembly step applies, resolves via
   `DbProviderFactories.GetFactory(ProviderName)` — the legacy machine/app-config provider
   registration path, for providers already registered that way.

## DI registration key vs. `ProviderName` — the gotcha

`LoadAndRegisterProviders` registers each resolved factory as a **keyed singleton under the
configuration section key**, not under `ProviderName`:

```csharp
services.AddKeyedSingleton<DbProviderFactory>(providerKey, factory); // providerKey = "sqlserver" above
DbProviderFactories.RegisterFactory(config.ProviderName, factory);   // legacy compatibility path only
```

`ITenantContextRegistry`/`TenantContextRegistry.CreateDatabaseContext` resolves a tenant's
factory with:

```csharp
_serviceProvider.GetKeyedService<DbProviderFactory>(tenantConfig.ProviderName)
```

**This means a tenant's `IDatabaseContextConfiguration.ProviderName` must equal the
`DatabaseProviders` section key it should resolve to — not necessarily the ADO.NET invariant
name that field's own name suggests.** In the example above, a tenant that should use the first
provider must set `ProviderName = "sqlserver"`, not `"System.Data.SqlClient"` — even though
`"System.Data.SqlClient"` is what the *loader's own* `ProviderName` field holds for that same
entry (that value is only used for the legacy `DbProviderFactories.RegisterFactory` call, a
separate registration path from keyed DI). Setting a tenant's `ProviderName` to the invariant
name instead of the section key throws `InvalidOperationException` naming the actual
requirement and giving an example of the expected shape.

If you don't use `ITenantContextRegistry` and only need `DbProviderLoader` for its own sake,
resolve the factory the same way it was registered — by section key:

```csharp
var factory = provider.GetRequiredKeyedService<DbProviderFactory>("sqlserver");
```

## Tenant cardinality cap (`MaxTenantCount`)

`TenantContextRegistry` accepts an optional `maxTenantCount` constructor argument that throws
`InvalidOperationException` from `GetContext` once that many distinct tenants have been cached
(call `Invalidate`/`InvalidateAll` to free capacity). The standard `services.AddMultiTenancy(configuration)`
path exposes this as `MultiTenantOptions.MaxTenantCount`, bound from the `MultiTenant` config
section:

```json
{
  "MultiTenant": {
    "MaxTenantCount": 500,
    "Tenants": [ ... ]
  }
}
```

Leave it unset (`null`) for an unbounded registry — the default. A long-lived process serving
many distinct, dynamically-discovered tenants should set this to bound worst-case connection-pool
growth across tenants.
