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

`DbProviderLoader`'s constructor is `internal` — `services.AddDbProviderLoading(...)` is the
*only* supported way to use this feature. There is no direct-construction path for consumers
outside `pengdows.crud` itself.

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

## Recognized dialect vs. wholly-unknown engine

Loading a provider does not by itself teach pengdows.crud anything about the database behind it.
Once `DatabaseContext` opens a connection through the loaded factory, `DatabaseDetectionService`
probes the connection (product/version queries) to resolve a `SupportedDatabase` value:

- **A recognized product** (any value in `SupportedDatabase` other than `Unknown`) gets that
  product's real `ISqlDialect` — identifier quoting, parameter markers, upsert strategy, generated-
  key retrieval, isolation-level mapping, capability flags, and every other dialect-specific
  behavior documented in `docs/supported-databases.md`.
- **An engine the detection probes can't identify** resolves to `SupportedDatabase.Unknown`, and
  `SqlDialectFactory` falls back to `Sql92Dialect` — a generic ANSI-SQL dialect (standard
  double-quote identifier quoting, standard positional/named parameter handling, no
  product-specific upsert/merge/pagination/isolation behavior). This lets you execute explicit,
  portable SQL through a provider `pengdows.crud` has never heard of, but you get none of the
  capability negotiation, optimized SQL generation, or verified-support guarantees a recognized
  dialect provides. See `docs/planning/future-work.md`'s "What 'supported database' means" section
  for the exact distinction between recognized dialect, generic provider compatibility, and
  verified database support — loading a provider only ever gets you the first two.
- If you're adding real support for a new engine, `Sql92Dialect` is the fallback you're
  overriding, not a starting template to copy — see CLAUDE.md's "Adding a New Database" checklist.

## Process-lifetime limitations

`Assembly.LoadFrom`/`Assembly.Load` (the two loading mechanisms `DbProviderLoader` uses) load into
the process's **default `AssemblyLoadContext`** — not a custom, collectible one. Consequences:

- A loaded provider assembly (and its dependencies) **stays loaded for the lifetime of the
  process**. There is no API to unload it, reload a different version, or hot-swap a provider DLL
  without restarting the application.
- Loading two different versions of the same provider assembly (or two providers that share a
  transitively-referenced dependency at incompatible versions) can fail or silently resolve to
  whichever version the runtime's default assembly-resolution rules pick first — the same
  dependency-version-collision risk any plugin-style assembly loading has, not something
  `DbProviderLoader` mitigates.
- Registering providers via `AddDbProviderLoading` at startup (before the service provider is
  built) is the supported pattern specifically because provider identity is expected to be fixed
  for the process's lifetime. Rotating a tenant to a different `DatabaseProviders` section key at
  runtime (see `TenantConnectionResolver`) only changes which *already-loaded* factory a tenant
  resolves to — it does not load a new assembly on demand or unload the old one.
