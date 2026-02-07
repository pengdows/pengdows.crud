// =============================================================================
// FILE: TenantContextRegistry.cs
// PURPOSE: Registry managing DatabaseContext instances per tenant.
//
// AI SUMMARY:
// - Implements ITenantContextRegistry for tenant context lifecycle management.
// - Thread-safe: uses ConcurrentDictionary for context storage.
// - GetContext(tenant): Returns cached context or creates new one.
// - Lazy initialization: contexts created on first access per tenant.
// - Context creation:
//   * Gets config from ITenantConnectionResolver
//   * Resolves DbProviderFactory via keyed DI service
//   * Creates DatabaseContext with config, factory, and logger
// - Extends SafeAsyncDisposableBase for proper cleanup.
// - DisposeManaged/Async: disposes all cached tenant contexts.
// - Logs warnings for disposal errors (doesn't throw).
// =============================================================================

using System.Collections.Concurrent;
using System.Data.Common;
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using pengdows.crud.infrastructure;

namespace pengdows.crud.tenant;

public class TenantContextRegistry : SafeAsyncDisposableBase, ITenantContextRegistry
{
    private readonly ConcurrentDictionary<string, IDatabaseContext> _contexts = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionResolver _resolver;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly IDatabaseContextFactory _contextFactory;

    public TenantContextRegistry(
        IServiceProvider serviceProvider,
        ITenantConnectionResolver resolver,
        IDatabaseContextFactory contextFactory,
        ILoggerFactory loggerFactory)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
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
        return _contextFactory.Create(config, factory, _loggerFactory);
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
