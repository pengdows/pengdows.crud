#region

using pengdows.crud.enums;

#endregion

namespace pengdows.crud.tenant;

/// <summary>
/// Provides tenant-specific database connection information.
/// </summary>
public interface ITenantInformation
{
    /// <summary>
    /// Database type used by the tenant.
    /// </summary>
    SupportedDatabase DatabaseType { get; }

    /// <summary>
    /// Connection string for the tenant database.
    /// </summary>
    string ConnectionString { get; }
}
