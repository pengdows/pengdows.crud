#region

using pengdows.crud.configuration;

#endregion

namespace pengdows.crud.tenant;

public class TenantConfiguration
{
    public string Name { get; set; } = string.Empty;
    public DatabaseContextConfiguration DatabaseContextConfiguration { get; set; } = new();
}