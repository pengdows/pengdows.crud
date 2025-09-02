using pengdows.crud.tenant;
using Xunit;

namespace pengdows.crud.Tests.tenant;

public class MultiTenantOptionsTests
{
    [Fact]
    public void Constructor_InitializesEmptyTenantsList()
    {
        var options = new MultiTenantOptions();
        
        Assert.NotNull(options.Tenants);
        Assert.Empty(options.Tenants);
        Assert.IsType<List<TenantConfiguration>>(options.Tenants);
    }

    [Fact]
    public void Tenants_CanAddTenantConfigurations()
    {
        var options = new MultiTenantOptions();
        var tenantConfig = new TenantConfiguration { Name = "TestTenant" };
        
        options.Tenants.Add(tenantConfig);
        
        Assert.Single(options.Tenants);
        Assert.Equal("TestTenant", options.Tenants[0].Name);
    }

    [Fact]
    public void Tenants_CanAddMultipleTenantConfigurations()
    {
        var options = new MultiTenantOptions();
        var tenant1 = new TenantConfiguration { Name = "Tenant1" };
        var tenant2 = new TenantConfiguration { Name = "Tenant2" };
        
        options.Tenants.Add(tenant1);
        options.Tenants.Add(tenant2);
        
        Assert.Equal(2, options.Tenants.Count);
        Assert.Equal("Tenant1", options.Tenants[0].Name);
        Assert.Equal("Tenant2", options.Tenants[1].Name);
    }

    [Fact]
    public void Tenants_SupportsClearAndReAdd()
    {
        var options = new MultiTenantOptions();
        var tenant1 = new TenantConfiguration { Name = "Tenant1" };
        var tenant2 = new TenantConfiguration { Name = "Tenant2" };
        
        options.Tenants.Add(tenant1);
        options.Tenants.Clear();
        options.Tenants.Add(tenant2);
        
        Assert.Single(options.Tenants);
        Assert.Equal("Tenant2", options.Tenants[0].Name);
    }

    [Fact]
    public void Tenants_SupportsRemoval()
    {
        var options = new MultiTenantOptions();
        var tenant1 = new TenantConfiguration { Name = "Tenant1" };
        var tenant2 = new TenantConfiguration { Name = "Tenant2" };
        
        options.Tenants.Add(tenant1);
        options.Tenants.Add(tenant2);
        options.Tenants.Remove(tenant1);
        
        Assert.Single(options.Tenants);
        Assert.Equal("Tenant2", options.Tenants[0].Name);
    }

    [Fact]
    public void Tenants_InitPropertyIsImmutable()
    {
        var options = new MultiTenantOptions();
        var originalList = options.Tenants;
        
        originalList.Add(new TenantConfiguration { Name = "TestTenant" });
        
        Assert.Same(originalList, options.Tenants);
        Assert.Single(options.Tenants);
    }
}