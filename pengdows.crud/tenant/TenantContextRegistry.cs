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
    // Case-insensitive to match TenantConnectionResolver's tenant-ID comparison
    // (StringComparer.OrdinalIgnoreCase) — "tenant-x" and "TENANT-X" must resolve to exactly
    // one cached context, not two independently governed ones.
    private readonly ConcurrentDictionary<string, Lazy<IDatabaseContext>> _contexts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _admissionLock = new();
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
        if (string.IsNullOrWhiteSpace(tenant))
        {
            throw new ArgumentNullException(nameof(tenant), "Tenant identifier must not be null or empty.");
        }

        // Looped rather than a single pass: a racing Invalidate() (or registry disposal) can
        // evict our freshly-installed Lazy before it finishes evaluating — see the orphan-check
        // below. ThrowIfDisposed() is re-checked on every iteration (not just once up front) so
        // a retry forced by a disposal race fails closed with ObjectDisposedException instead of
        // looping forever re-creating and re-orphaning contexts against a disposed registry.
        while (true)
        {
            ThrowIfDisposed();

            // Fast path: already-cached tenant — fully lock-free, no behavior change and no
            // contention with the admission lock below. No orphan-check needed here: this Lazy
            // was observed to be registered at the moment of lookup, which is the same
            // guarantee normal cache reads provide (a subsequent concurrent Invalidate is not a
            // leak, just an ordinary point-in-time race).
            if (_contexts.TryGetValue(tenant, out var existingLazy))
            {
                return ResolveLazy(tenant, existingLazy);
            }

            Lazy<IDatabaseContext> lazy;
            if (_maxTenantCount.HasValue)
            {
                // Cap enforcement requires the count-check and the add to be atomic together,
                // otherwise two threads racing to admit two DIFFERENT new tenants can both pass
                // the check before either calls GetOrAdd, letting the cap be exceeded. Only the
                // fast dictionary check+add happens under the lock; CreateDatabaseContext() stays
                // deferred inside Lazy<T>.Value, resolved outside the lock below, so hold time
                // stays negligible even though tenant construction itself may be slow.
                lock (_admissionLock)
                {
                    if (!_contexts.TryGetValue(tenant, out existingLazy))
                    {
                        if (_contexts.Count >= _maxTenantCount.Value)
                        {
                            throw new InvalidOperationException(
                                $"TenantContextRegistry has reached its maximum tenant count of {_maxTenantCount}. " +
                                "Call Invalidate() or InvalidateAll() to evict unused tenants before adding new ones.");
                        }

                        existingLazy = _contexts.GetOrAdd(
                            tenant,
                            key => new Lazy<IDatabaseContext>(
                                () => CreateDatabaseContext(key),
                                LazyThreadSafetyMode.ExecutionAndPublication));
                    }
                }

                lazy = existingLazy;
            }
            else
            {
                // No cap configured: unchanged, fully lock-free hot path.
                lazy = _contexts.GetOrAdd(
                    tenant,
                    key => new Lazy<IDatabaseContext>(
                        () => CreateDatabaseContext(key),
                        LazyThreadSafetyMode.ExecutionAndPublication));
            }

            var context = ResolveLazy(tenant, lazy);

            // A racing Invalidate() — or registry disposal's DisposeManaged(), which skips any
            // Lazy that isn't yet IsValueCreated before clearing the dictionary — may have
            // removed `lazy` between the GetOrAdd above and this point. When that happens,
            // Invalidate's own dispose-if-created check was a no-op (the Lazy wasn't
            // IsValueCreated yet), so `context` was just created but is unreachable through the
            // registry: nobody else can ever observe it. Rather than hand back a live,
            // untracked context (a permanent leak), dispose it as an orphan and retry — the next
            // iteration either finds a healthy tracked context another thread installed, creates
            // a fresh one that survives this check, or throws ObjectDisposedException if the
            // registry itself was disposed in the interim.
            if (_contexts.TryGetValue(tenant, out var currentLazy) && ReferenceEquals(currentLazy, lazy))
            {
                return context;
            }

            DisposeOrphanedContext(context);
        }
    }

    private void DisposeOrphanedContext(IDatabaseContext context)
    {
        try
        {
            context.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error disposing an orphaned tenant context created during a racing Invalidate() or registry disposal.");
        }
    }

    private IDatabaseContext ResolveLazy(string tenant, Lazy<IDatabaseContext> lazy)
    {
        try
        {
            return lazy.Value;
        }
        catch
        {
            // Remove the faulted Lazy so the next GetContext call gets a fresh attempt.
            // TryRemove(KeyValuePair) checks both key and value before removing, making
            // two concurrent fault scenarios safe:
            //   (a) Thread A faults and removes; Thread B concurrently also holds the same
            //       faulted Lazy and calls TryRemove — returns false (no-op), no corruption.
            //   (b) Thread A faults and removes; Thread C immediately adds a healthy Lazy
            //       for the same key — Thread A's TryRemove finds a different value and
            //       returns false, leaving the healthy entry intact.
            _contexts.TryRemove(new KeyValuePair<string, Lazy<IDatabaseContext>>(tenant, lazy));
            throw;
        }
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