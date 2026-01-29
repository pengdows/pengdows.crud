using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<ITenantContextRegistry, TenantContextRegistry>();

        return services;
    }
}
