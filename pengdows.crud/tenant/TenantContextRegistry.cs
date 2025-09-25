#region

using System;
using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using pengdows.crud.infrastructure;

#endregion

namespace pengdows.crud.tenant;

public class TenantContextRegistry : SafeAsyncDisposableBase, ITenantContextRegistry
{
    private readonly ConcurrentDictionary<string, IDatabaseContext> _contexts = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionResolver _resolver;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    public TenantContextRegistry(
        IServiceProvider serviceProvider,
        ITenantConnectionResolver resolver,
        ILoggerFactory loggerFactory)
    {
        _serviceProvider = serviceProvider;
        _resolver = resolver;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TenantContextRegistry>();
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

    protected override void DisposeManaged()
    {
        foreach (var context in _contexts.Values)
        {
            try
            {
                context.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing tenant context during shutdown.");
            }
        }

        _contexts.Clear();
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        foreach (var context in _contexts.Values)
        {
            try
            {
                if (context is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    context.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error asynchronously disposing tenant context during shutdown.");
            }
        }

        _contexts.Clear();
    }
}
