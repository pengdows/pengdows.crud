#region

using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#endregion

namespace pengdows.crud.tenant;

public class TenantContextRegistry : ITenantContextRegistry
{
    private readonly ConcurrentDictionary<string, IDatabaseContext> _contexts = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionResolver _resolver;
    private readonly IServiceProvider _serviceProvider;

    public TenantContextRegistry(
        IServiceProvider serviceProvider,
        ITenantConnectionResolver resolver,
        ILoggerFactory loggerFactory)
    {
        _serviceProvider = serviceProvider;
        _resolver = resolver;
        _loggerFactory = loggerFactory;
    }

    public IDatabaseContext GetContext(string tenant)
    {
        return _contexts.GetOrAdd(tenant, CreateDatabaseContext);
    }

    private IDatabaseContext CreateDatabaseContext(string tenant)
    {
        var config =
            _resolver.GetDatabaseContextConfiguration(tenant); // contains connection string + SupportedDatabase

        var factory = _serviceProvider.GetKeyedService<DbProviderFactory>(config.ProviderName)
                      ?? throw new InvalidOperationException($"No factory registered for {config.ProviderName}");
        return new DatabaseContext(config, factory, _loggerFactory);
    }
}