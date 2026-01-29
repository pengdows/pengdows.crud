namespace pengdows.crud.tenant;

public class MultiTenantOptions
{
    public string ApplicationName { get; set; } = string.Empty;
    public List<TenantConfiguration> Tenants { get; init; } = new();
}
