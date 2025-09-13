using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using pengdows.crud.tenant;
using Xunit;

namespace pengdows.crud.Tests.tenant;

public class TenantServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMultiTenancy_BindsConfigurationAndRegistersServices()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MultiTenant:Tenants:0:Name"] = "a",
                ["MultiTenant:Tenants:0:DatabaseContextConfiguration:ProviderName"] = "fake-a",
                ["MultiTenant:Tenants:0:DatabaseContextConfiguration:ConnectionString"] = "Server=A;",
                ["MultiTenant:Tenants:1:Name"] = "b",
                ["MultiTenant:Tenants:1:DatabaseContextConfiguration:ProviderName"] = "fake-b",
                ["MultiTenant:Tenants:1:DatabaseContextConfiguration:ConnectionString"] = "Server=B;"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMultiTenancy(config);

        using var provider = services.BuildServiceProvider();

        var resolver = provider.GetRequiredService<ITenantConnectionResolver>();
        var contextRegistry = provider.GetRequiredService<ITenantContextRegistry>();
        var options = provider.GetRequiredService<IOptions<MultiTenantOptions>>();

        Assert.Equal("Server=A;", resolver.GetDatabaseContextConfiguration("a").ConnectionString);
        Assert.Equal("Server=B;", resolver.GetDatabaseContextConfiguration("b").ConnectionString);
        Assert.NotNull(contextRegistry);
        Assert.Equal(2, options.Value.Tenants.Count);
    }
}
