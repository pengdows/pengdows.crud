// =============================================================================
// FILE: TenantConnectionResolver.cs
// PURPOSE: Resolves database configuration for tenant identifiers.
//
// AI SUMMARY:
// - Implements ITenantConnectionResolver for tenant-to-config mapping.
// - Thread-safe: uses ConcurrentDictionary with case-insensitive keys.
// - GetDatabaseContextConfiguration(tenant): Returns config or throws.
// - Register methods:
//   * Register(tenant, config): Single tenant registration
//   * Register(IEnumerable<TenantConfiguration>): Batch registration
//   * Register(MultiTenantOptions): With application name composition
// - Application name composition: "{baseApp}:{tenantName}" format.
// - Clear(): Removes all registered configurations.
// - Throws InvalidOperationException for unknown tenants.
// - Validates ProviderName is non-empty during registration.
// =============================================================================

using System.Collections.Concurrent;
using pengdows.crud.configuration;

namespace pengdows.crud.tenant;

public class TenantConnectionResolver : ITenantConnectionResolver
{
    private readonly ConcurrentDictionary<string, DatabaseContextConfiguration> _configurations;

    public TenantConnectionResolver()
        : this(Enumerable.Empty<TenantConfiguration>())
    {
    }

    public TenantConnectionResolver(IEnumerable<TenantConfiguration>? tenants)
    {
        _configurations =
            new ConcurrentDictionary<string, DatabaseContextConfiguration>(StringComparer.OrdinalIgnoreCase);

        if (tenants != null)
        {
            Register(tenants);
        }
    }

    public IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant)
    {
        if (string.IsNullOrWhiteSpace(tenant))
        {
            throw new ArgumentNullException(nameof(tenant), "Tenant ID must not be null or empty.");
        }

        if (!_configurations.TryGetValue(tenant, out var config))
        {
            throw new InvalidOperationException($"No database configuration registered for tenant '{tenant}'.");
        }

        return config;
    }

    public void Register(string tenant, DatabaseContextConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(tenant))
        {
            throw new ArgumentNullException(nameof(tenant));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (string.IsNullOrWhiteSpace(configuration.ProviderName))
        {
            throw new ArgumentException("Tenant configuration must include a non-empty ProviderName.",
                nameof(configuration));
        }

        _configurations[tenant] = configuration;
    }

    public void Register(IEnumerable<TenantConfiguration> tenants)
    {
        if (tenants == null)
        {
            throw new ArgumentNullException(nameof(tenants));
        }

        foreach (var tenant in tenants)
        {
            if (tenant == null)
            {
                continue;
            }

            if (tenant.DatabaseContextConfiguration == null ||
                string.IsNullOrWhiteSpace(tenant.DatabaseContextConfiguration.ProviderName))
            {
                throw new ArgumentException(
                    $"Tenant '{tenant?.Name}' configuration must include a non-empty ProviderName.");
            }

            Register(tenant.Name, tenant.DatabaseContextConfiguration);
        }
    }

    public void Register(MultiTenantOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var baseApp = options.ApplicationName?.Trim();
        if (string.IsNullOrWhiteSpace(baseApp))
        {
            Register(options.Tenants);
            return;
        }

        foreach (var tenant in options.Tenants)
        {
            if (tenant == null)
            {
                continue;
            }

            var tenantName = tenant.Name?.Trim();
            if (string.IsNullOrWhiteSpace(tenantName))
            {
                throw new ArgumentException("Tenant configuration must include a non-empty Name.");
            }

            var configuration = tenant.DatabaseContextConfiguration
                                ?? throw new ArgumentException(
                                    $"Tenant '{tenantName}' configuration missing DatabaseContextConfiguration.");

            if (!string.IsNullOrWhiteSpace(baseApp) &&
                string.IsNullOrWhiteSpace(configuration.ApplicationName))
            {
                configuration.ApplicationName = $"{baseApp}:{tenantName}";
            }

            Register(tenantName, configuration);
        }
    }

    public void Clear()
    {
        _configurations.Clear();
    }
}
