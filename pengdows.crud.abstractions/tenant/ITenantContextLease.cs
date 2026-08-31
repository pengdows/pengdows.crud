namespace pengdows.crud.tenant;

/// <summary>
/// A reference-counted lease on a tenant's <see cref="IDatabaseContext"/>, obtained via
/// <see cref="ITenantContextRegistry.AcquireLease"/>/<see cref="ITenantContextRegistry.AcquireLeaseAsync"/>.
/// </summary>
/// <remarks>
/// Unlike <see cref="ITenantContextRegistry.GetContext"/>/<see cref="ITenantContextRegistry.GetContextAsync"/>,
/// which hand back a bare <see cref="IDatabaseContext"/> reference with no protection against a
/// concurrent <see cref="ITenantContextRegistry.Invalidate"/>/<see cref="ITenantContextRegistry.InvalidateAll"/>
/// disposing it, a lease guarantees <see cref="Context"/> will not be disposed by the registry
/// until this lease itself is disposed. Dispose the lease as soon as you're done with
/// <see cref="Context"/> — holding it longer than necessary delays a concurrent rotation's actual
/// disposal of the superseded context.
/// </remarks>
public interface ITenantContextLease : IDisposable, IAsyncDisposable
{
    /// <summary>The leased tenant context. Valid for use until this lease is disposed.</summary>
    IDatabaseContext Context { get; }
}
