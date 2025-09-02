#region

#endregion

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
    public IDatabaseContext GetContext(string tenant);
}
