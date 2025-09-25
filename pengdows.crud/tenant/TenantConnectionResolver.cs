using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
        _configurations = new ConcurrentDictionary<string, DatabaseContextConfiguration>(StringComparer.OrdinalIgnoreCase);

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
            throw new ArgumentException("Tenant configuration must include a non-empty ProviderName.", nameof(configuration));
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

            if (tenant.DatabaseContextConfiguration == null || string.IsNullOrWhiteSpace(tenant.DatabaseContextConfiguration.ProviderName))
            {
                throw new ArgumentException($"Tenant '{tenant?.Name}' configuration must include a non-empty ProviderName.");
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

        Register(options.Tenants);
    }

    public void Clear()
    {
        _configurations.Clear();
    }
}
