#region

using pengdows.crud.configuration;

#endregion

namespace pengdows.crud.tenant;

public interface ITenantConnectionResolver
{
    IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant);
}