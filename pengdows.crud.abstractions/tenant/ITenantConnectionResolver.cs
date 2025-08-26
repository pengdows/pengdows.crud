#region

using pengdows.crud.configuration;

#endregion

namespace pengdows.crud.tenant;

/// <summary>
/// Resolves database context configuration information for tenants.
/// </summary>
public interface ITenantConnectionResolver
{
    /// <summary>
    /// Retrieves configuration for the specified tenant identifier.
    /// </summary>
    /// <param name="tenant">Tenant identifier.</param>
    IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant);
}
