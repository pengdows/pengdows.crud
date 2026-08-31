using System.Threading;
using System.Threading.Tasks;

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
    /// in the same synchronous/async flow. If you hold the context across an <c>await</c> boundary,
    /// or otherwise can't guarantee your usage completes atomically with respect to a concurrent
    /// rotation, use <see cref="AcquireLease"/> instead.
    /// </remarks>
    /// <param name="tenant">Tenant identifier.</param>
    /// <returns>The associated database context.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the registry has been disposed.</exception>
    public IDatabaseContext GetContext(string tenant);

    /// <summary>
    /// Asynchronously retrieves a database context for the specified tenant, sharing the same
    /// cache as <see cref="GetContext"/> — a tenant resolved through either method is created
    /// at most once and observed identically by both.
    /// </summary>
    /// <remarks>
    /// For a not-yet-cached tenant, this performs the underlying connection/dialect-detection
    /// work via <see cref="IDatabaseContextFactory.CreateAsync"/> instead of the blocking
    /// <see cref="IDatabaseContextFactory.Create"/>, so the calling thread is not held for the
    /// duration of context construction. An already-cached tenant resolves immediately, just
    /// like <see cref="GetContext"/>.
    /// </remarks>
    /// <remarks>
    /// Same bare-reference caveat as <see cref="GetContext"/> — see <see cref="AcquireLeaseAsync"/>
    /// for a version that protects the returned context from a concurrent <see cref="Invalidate"/>/
    /// <see cref="InvalidateAll"/>.
    /// </remarks>
    /// <param name="tenant">Tenant identifier.</param>
    /// <param name="cancellationToken">
    /// Observed only while a not-yet-cached tenant's context is being created; has no effect
    /// once the tenant is cached. If another concurrent caller is already constructing the same
    /// not-yet-cached tenant, cancelling this token stops only this caller's own wait — it does
    /// not cancel the shared in-flight construction other callers are still waiting on.
    /// </param>
    /// <returns>The associated database context.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the registry has been disposed.</exception>
    public Task<IDatabaseContext> GetContextAsync(string tenant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires a reference-counted lease on the tenant's context, protecting it from being
    /// disposed by a concurrent <see cref="Invalidate"/>/<see cref="InvalidateAll"/> until the
    /// lease itself is disposed. Prefer this over <see cref="GetContext"/> when you hold the
    /// context across an <c>await</c> boundary or otherwise need a guarantee stronger than
    /// "nothing concurrent will rotate this tenant while I'm using it."
    /// </summary>
    /// <param name="tenant">Tenant identifier.</param>
    /// <returns>A lease wrapping the tenant's context. Dispose it when done.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the registry has been disposed.</exception>
    public ITenantContextLease AcquireLease(string tenant);

    /// <summary>
    /// Asynchronous, non-blocking counterpart to <see cref="AcquireLease"/> — see
    /// <see cref="GetContextAsync"/> for the non-blocking-construction contract this shares.
    /// </summary>
    /// <param name="tenant">Tenant identifier.</param>
    /// <param name="cancellationToken">
    /// Observed only while a not-yet-cached tenant's context is being created, or while waiting on
    /// a shared in-flight construction another caller started; has no effect once resolved.
    /// </param>
    /// <returns>A lease wrapping the tenant's context. Dispose it when done.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the registry has been disposed.</exception>
    public Task<ITenantContextLease> AcquireLeaseAsync(string tenant, CancellationToken cancellationToken = default);

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
    /// <remarks>
    /// There is no separate "tenant ID" carried by this event — the multi-tenancy model is
    /// context-per-tenant, so the <see cref="IDatabaseContext"/> instance passed to the handler
    /// already *is* the tenant's identity. A subscriber that needs to correlate this callback
    /// with "which tenant" already knows, because it is the same caller that resolved that tenant
    /// via <see cref="GetContext"/> in the first place.
    /// </remarks>
    event Action<IDatabaseContext>? ContextCreated;

    /// <summary>
    /// Raised after a tenant context has been disposed and removed from the registry —
    /// via <see cref="Invalidate"/>, <see cref="InvalidateAll"/>, or registry disposal
    /// (<see cref="IAsyncDisposable.DisposeAsync"/>/<see cref="IDisposable.Dispose"/>, which
    /// disposes and raises this event for every context the registry has created).
    /// Subscribers must clean up any references they hold to the context.
    /// </summary>
    /// <remarks>
    /// See <see cref="ContextCreated"/>'s remarks — this event likewise carries no separate
    /// tenant-ID field; the context instance is the identity.
    /// </remarks>
    event Action<IDatabaseContext>? ContextRemoved;
}