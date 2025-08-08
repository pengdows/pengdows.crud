using pengdow.crud.configuration;

namespace pengdow.crud.tenant;

public class TenantConfiguration
{
    public string Name { get; set; } = string.Empty;
    public DatabaseContextConfiguration DatabaseContextConfiguration { get; set; } = new();
}
