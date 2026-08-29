// =============================================================================
// FILE: TenantServiceCollectionExtensions.cs
// PURPOSE: DI extension methods for registering multi-tenancy services.
//
// AI SUMMARY:
// - Extension methods for IServiceCollection multi-tenant setup.
// - AddMultiTenancy(configuration): Registers all tenant services.
// - Reads "MultiTenant" section from IConfiguration.
// - Registers:
//   * IOptions<MultiTenantOptions> via Configure<T>
//   * ITenantConnectionResolver as singleton (TenantConnectionResolver)
//   * ITenantContextRegistry as singleton (TenantContextRegistry)
// - Creates TenantConnectionResolver and pre-registers all tenants.
// - Requires DbProviderFactory keyed services for each provider.
// - Usage: services.AddMultiTenancy(Configuration);
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using pengdows.crud;

namespace pengdows.crud.tenant;

public static class TenantServiceCollectionExtensions
{
    public static IServiceCollection AddMultiTenancy(this IServiceCollection services, IConfiguration configuration)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var options = new MultiTenantOptions();
        configuration.GetSection("MultiTenant").Bind(options);

        // Register options to enable IOptions<MultiTenantOptions>
        services.Configure<MultiTenantOptions>(configuration.GetSection("MultiTenant"));

        var resolver = new TenantConnectionResolver();
        resolver.Register(options);

        services.AddSingleton<ITenantConnectionResolver>(resolver);
        services.TryAddSingleton<IDatabaseContextFactory, DefaultDatabaseContextFactory>();

        // CORE-017: plain `AddSingleton<ITenantContextRegistry, TenantContextRegistry>()`
        // relies on implementation-type constructor DI, which supplies TenantContextRegistry's
        // optional `int? maxTenantCount = null` parameter with its default — it has no way to
        // pull a value from configuration for an unregistered parameter type. Registering via
        // an explicit factory that closes over `options.MaxTenantCount` (already bound above)
        // is what actually lets the "MultiTenant:MaxTenantCount" config value reach the
        // registry's cardinality cap through this standard DI entry point.
        services.AddSingleton<ITenantContextRegistry>(sp => new TenantContextRegistry(
            sp,
            sp.GetRequiredService<ITenantConnectionResolver>(),
            sp.GetRequiredService<IDatabaseContextFactory>(),
            sp.GetRequiredService<ILoggerFactory>(),
            options.MaxTenantCount));

        return services;
    }
}