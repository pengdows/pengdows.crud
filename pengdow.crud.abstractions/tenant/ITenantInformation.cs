#region

using pengdow.crud.enums;

#endregion

namespace pengdow.crud.tenant;

public interface ITenantInformation
{
    SupportedDatabase DatabaseType { get; }
    string ConnectionString { get; }
}