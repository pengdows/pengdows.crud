namespace pengdows.crud.tenant;

/// <summary>
/// Provides access to <see cref="IDatabaseContext"/> instances for tenants.
/// </summary>
public interface ITenantContextRegistry
{
    /// <summary>
    /// Retrieves a database context for the specified tenant.
    /// </summary>
    /// <remarks>
    /// Returns a bare reference with no protection against a concurrent <see cref="Invalidate"/>/
    /// <see cref="InvalidateAll"/> disposing this exact context immediately after it's returned.
    /// Fine for the common case — resolve, then immediately pass the result into one gateway call
    /// in the same synchronous flow. If you hold the context across any later point where a
    /// concurrent invalidation could race your usage, use <see cref="AcquireLease"/> instead.
    /// </remarks>
    /// <param name="tenant">Tenant identifier.</param>
    /// <returns>The associated database context.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the registry has been disposed.</exception>
    public IDatabaseContext GetContext(string tenant);

    /// <summary>
    /// Acquires a reference-counted lease on the tenant's context, protecting it from being
    /// disposed by a concurrent <see cref="Invalidate"/>/<see cref="InvalidateAll"/> until the
    /// lease itself is disposed. Prefer this over <see cref="GetContext"/> whenever you need a
    /// guarantee stronger than "nothing concurrent will rotate this tenant while I'm using it."
    /// </summary>
    /// <param name="tenant">Tenant identifier.</param>
    /// <returns>A lease wrapping the tenant's context. Dispose it when done.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the registry has been disposed.</exception>
    public ITenantContextLease AcquireLease(string tenant);

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