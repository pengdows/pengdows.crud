#region

using pengdows.crud.enums;

#endregion

namespace pengdows.crud.tenant;

public interface ITenantInformation
{
    SupportedDatabase DatabaseType { get; }
    string ConnectionString { get; }
}