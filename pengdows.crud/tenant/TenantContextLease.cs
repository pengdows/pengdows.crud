using pengdows.crud.infrastructure;

namespace pengdows.crud.tenant;

/// <summary>
/// Default <see cref="ITenantContextLease"/> implementation returned by
/// <see cref="TenantContextRegistry.AcquireLease"/>/<see cref="TenantContextRegistry.AcquireLeaseAsync"/>.
/// Disposing this lease releases the registry's internal refcount on the tenant's context — see
/// <see cref="ITenantContextLease"/>'s remarks for the guarantee this provides.
/// </summary>
internal sealed class TenantContextLease : SafeAsyncDisposableBase, ITenantContextLease
{
    private readonly TenantContextRegistry.TenantContextEntry _entry;
    private readonly TenantContextRegistry _registry;
    private readonly string _tenant;

    public IDatabaseContext Context { get; }

    internal TenantContextLease(IDatabaseContext context, TenantContextRegistry.TenantContextEntry entry,
        TenantContextRegistry registry, string tenant)
    {
        Context = context;
        _entry = entry;
        _registry = registry;
        _tenant = tenant;
    }

    protected override void DisposeManaged()
    {
        _entry.ReleaseLease(_registry, _tenant);
    }
}
