// =============================================================================
// FILE: TenantContextRegistry.cs
// PURPOSE: Registry managing DatabaseContext instances per tenant.
//
// AI SUMMARY:
// - Implements ITenantContextRegistry for tenant context lifecycle management.
// - Thread-safe: ConcurrentDictionary of Lazy<IDatabaseContext> prevents double-create
//   under concurrent access (Lazy.ExecutionAndPublication ensures one factory call).
// - GetContext(tenant): Returns cached context or creates one; throws if disposed.
// - Invalidate(tenant): Removes and disposes the cached context for one tenant.
//   Only disposes if the Lazy was already evaluated (avoids spurious construction).
// - InvalidateAll(): Evicts all cached contexts; next GetContext recreates each.
// - Optional MaxTenantCount cap: throws when adding a new tenant would exceed the limit.
// - Context creation:
//   * Gets config from ITenantConnectionResolver
//   * Resolves DbProviderFactory via keyed DI service
//   * Creates DatabaseContext with config, factory, and logger
// - Extends SafeAsyncDisposableBase for proper cleanup.
// - DisposeManaged/Async: disposes only already-evaluated contexts; logs warnings on error.
// =============================================================================

using System.Collections.Concurrent;
using System.Data.Common;
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.tenant;

/// <summary>
/// Thread-safe registry of <see cref="IDatabaseContext"/> instances keyed by tenant identifier.
/// </summary>
/// <remarks>
/// <para>
/// Each tenant context is created lazily on first access and cached for subsequent calls.
/// <see cref="Invalidate"/> or <see cref="InvalidateAll"/> can be used to evict stale
/// contexts when tenant configuration changes:
/// </para>
/// <list type="number">
///   <item>Update the tenant's configuration via <c>ITenantConnectionResolver.Register</c>.</item>
///   <item>Call <see cref="Invalidate"/> (or <see cref="InvalidateAll"/>) to evict the stale context.</item>
///   <item>The next <see cref="GetContext"/> call creates a fresh context using the new configuration.</item>
/// </list>
/// <para>
/// <b>Disposal:</b> After this registry is disposed, <see cref="GetContext"/> throws
/// <see cref="ObjectDisposedException"/>. Contexts obtained before disposal continue working
/// normally until they are themselves disposed.
/// </para>
/// <para>
/// <b>Cardinality:</b> The optional <c>maxTenantCount</c> constructor parameter enforces an upper
/// bound on distinct tenants. Unbounded registries in long-lived apps with many tenants can cause
/// connection-pool explosion; call <see cref="InvalidateAll"/> or use the cap accordingly.
/// </para>
/// </remarks>
public class TenantContextRegistry : SafeAsyncDisposableBase, ITenantContextRegistry
{
    private readonly ConcurrentDictionary<string, Lazy<IDatabaseContext>> _contexts = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionResolver _resolver;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly IDatabaseContextFactory _contextFactory;
    private readonly int? _maxTenantCount;

    public event Action<IDatabaseContext>? ContextCreated;
    public event Action<IDatabaseContext>? ContextRemoved;

    /// <param name="serviceProvider">DI service provider used to resolve keyed <see cref="DbProviderFactory"/> instances.</param>
    /// <param name="resolver">Maps tenant identifiers to their database configurations.</param>
    /// <param name="contextFactory">Factory used to construct <see cref="IDatabaseContext"/> instances.</param>
    /// <param name="loggerFactory">Logger factory for the registry and created contexts.</param>
    /// <param name="maxTenantCount">
    /// Optional upper bound on distinct cached tenants. When set and the limit is reached,
    /// <see cref="GetContext"/> throws <see cref="InvalidOperationException"/> for new tenants.
    /// Call <see cref="Invalidate"/> or <see cref="InvalidateAll"/> to evict unused entries.
    /// </param>
    public TenantContextRegistry(
        IServiceProvider serviceProvider,
        ITenantConnectionResolver resolver,
        IDatabaseContextFactory contextFactory,
        ILoggerFactory loggerFactory,
        int? maxTenantCount = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<TenantContextRegistry>();

        if (maxTenantCount.HasValue && maxTenantCount.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTenantCount),
                "maxTenantCount must be greater than zero when specified.");
        }

        _maxTenantCount = maxTenantCount;
    }

    /// <inheritdoc/>
    public IDatabaseContext GetContext(string tenant)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(tenant))
        {
            throw new ArgumentNullException(nameof(tenant), "Tenant identifier must not be null or empty.");
        }

        // Enforce cap: only block addition of genuinely new tenants.
        if (_maxTenantCount.HasValue
            && _contexts.Count >= _maxTenantCount.Value
            && !_contexts.ContainsKey(tenant))
        {
            throw new InvalidOperationException(
                $"TenantContextRegistry has reached its maximum tenant count of {_maxTenantCount}. " +
                "Call Invalidate() or InvalidateAll() to evict unused tenants before adding new ones.");
        }

        return _contexts.GetOrAdd(
            tenant,
            key => new Lazy<IDatabaseContext>(
                () => CreateDatabaseContext(key),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    /// <inheritdoc/>
    public void Invalidate(string tenant)
    {
        if (_contexts.TryRemove(tenant, out var lazy) && lazy.IsValueCreated)
        {
            var context = lazy.Value;
            try
            {
                context.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing tenant context during invalidation for tenant '{Tenant}'.",
                    tenant);
            }
            finally
            {
                ContextRemoved?.Invoke(context);
            }
        }
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        foreach (var key in _contexts.Keys.ToArray())
        {
            Invalidate(key);
        }
    }

    private IDatabaseContext CreateDatabaseContext(string tenant)
    {
        var config = _resolver.GetDatabaseContextConfiguration(tenant);

        var factory = _serviceProvider.GetKeyedService<DbProviderFactory>(config.ProviderName)
                      ?? throw new InvalidOperationException($"No factory registered for '{config.ProviderName}'.");

        var context = _contextFactory.Create(config, factory, _loggerFactory);
        ContextCreated?.Invoke(context);
        return context;
    }

    protected override void DisposeManaged()
    {
        foreach (var lazy in _contexts.Values)
        {
            if (!lazy.IsValueCreated)
            {
                continue;
            }

            try
            {
                var context = lazy.Value;
                context.Dispose();
                ContextRemoved?.Invoke(context);
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
        foreach (var lazy in _contexts.Values)
        {
            if (!lazy.IsValueCreated)
            {
                continue;
            }

            try
            {
                var context = lazy.Value;
                if (context is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    context.Dispose();
                }

                ContextRemoved?.Invoke(context);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error asynchronously disposing tenant context during shutdown.");
            }
        }

        _contexts.Clear();
    }
}
