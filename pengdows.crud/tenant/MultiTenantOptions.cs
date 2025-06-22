namespace pengdows.crud.tenant;

public class MultiTenantOptions
{
 
    public List<TenantConfiguration> Tenants { get; init; } = new();
} 