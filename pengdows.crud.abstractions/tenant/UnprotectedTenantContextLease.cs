namespace pengdows.crud.tenant;

/// <summary>
/// Lease returned by the default <see cref="ITenantContextRegistry.AcquireLease"/> for registries
/// compiled against 2.0.5: it wraps <see cref="ITenantContextRegistry.GetContext"/> and holds no
/// reference count, so disposing it never disposes the context.
/// </summary>
internal sealed class UnprotectedTenantContextLease : ITenantContextLease
{
    public UnprotectedTenantContextLease(IDatabaseContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public IDatabaseContext Context { get; }

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
