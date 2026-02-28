using pengdows.crud.configuration;

namespace pengdows.crud.tenant;

/// <summary>
/// Resolves database context configuration information for tenants.
/// </summary>
public interface ITenantConnectionResolver
{
    /// <summary>
    /// Retrieves the configuration snapshot for the specified tenant identifier.
    /// </summary>
    /// <param name="tenant">Tenant identifier.</param>
    /// <remarks>
    /// The returned configuration is a snapshot captured at registration time.
    /// Mutations to the original object after registration have no effect.
    /// To update a tenant's configuration, call <c>Register</c> with the new config,
    /// then call <c>ITenantContextRegistry.Invalidate</c> to evict the cached context.
    /// </remarks>
    IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant);
}