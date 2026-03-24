namespace pengdows.crud.tenant;

/// <summary>
/// Provides access to <see cref="IDatabaseContext"/> instances for tenants.
/// </summary>
public interface ITenantContextRegistry
{
    /// <summary>
    /// Retrieves a database context for the specified tenant.
    /// </summary>
    /// <param name="tenant">Tenant identifier.</param>
    /// <returns>The associated database context.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the registry has been disposed.</exception>
    public IDatabaseContext GetContext(string tenant);

    /// <summary>
    /// Disposes and removes the cached context for the specified tenant.
    /// The next call to <see cref="GetContext"/> for this tenant will create a fresh context
    /// using the configuration currently registered in the tenant connection resolver.
    /// </summary>
    /// <remarks>
    /// Use this to pick up configuration changes for a single tenant:
    /// <list type="number">
    ///   <item>Re-register the updated configuration via <c>ITenantConnectionResolver.Register</c>.</item>
    ///   <item>Call <see cref="Invalidate"/> to evict the stale cached context.</item>
    ///   <item>The next <see cref="GetContext"/> call creates a fresh context with the new config.</item>
    /// </list>
    /// </remarks>
    /// <param name="tenant">Tenant identifier.</param>
    void Invalidate(string tenant);

    /// <summary>
    /// Disposes and removes all cached contexts.
    /// Subsequent calls to <see cref="GetContext"/> will create fresh contexts for each tenant.
    /// </summary>
    void InvalidateAll();

    /// <summary>
    /// Raised after a new <see cref="IDatabaseContext"/> is created for a tenant.
    /// Subscribers can use this to register the context with instrumentation or caches.
    /// </summary>
    event Action<IDatabaseContext>? ContextCreated;

    /// <summary>
    /// Raised after a tenant context has been disposed and removed from the registry
    /// (via <see cref="Invalidate"/> or <see cref="InvalidateAll"/>).
    /// Subscribers must clean up any references they hold to the context.
    /// </summary>
    event Action<IDatabaseContext>? ContextRemoved;
}