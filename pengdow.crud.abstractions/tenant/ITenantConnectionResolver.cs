#region

using pengdow.crud.configuration;

#endregion

namespace pengdow.crud.tenant;

public interface ITenantConnectionResolver
{
    IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant);
}