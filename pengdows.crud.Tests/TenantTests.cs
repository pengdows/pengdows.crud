using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.tenant;
using Xunit;

namespace pengdows.crud.Tests;

public class TenantTests
{
    private sealed class StubResolver : ITenantConnectionResolver
    {
        private readonly IDatabaseContextConfiguration _cfg;

        public StubResolver(IDatabaseContextConfiguration cfg)
        {
            _cfg = cfg;
        }

        public IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant)
        {
            return _cfg;
        }
    }

    [Fact]
    public async Task TenantContextRegistry_ResolvesContextFromKeyedFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        var cfg = new DatabaseContextConfiguration
        {
            ProviderName = "fake-sqlite",
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        };

        using var provider = services.BuildServiceProvider();
        using var registry = new TenantContextRegistry(provider, new StubResolver(cfg),
            provider.GetRequiredService<ILoggerFactory>());

        using var ctx = registry.GetContext("tenant1");
        using var sc = ctx.CreateSqlContainer("SELECT 1");
        var affected = await sc.ExecuteNonQueryAsync();
        Assert.Equal(1, affected); // fake provider default non-query result
    }

    [Fact]
    public async Task TenantServiceCollectionExtensions_RegistersResolverAndRegistry()
    {
        var dict = new Dictionary<string, string?>
        {
            ["MultiTenant:Tenants:0:Name"] = "tenant-di-a",
            ["MultiTenant:Tenants:0:DatabaseContextConfiguration:ProviderName"] = "fake-sqlite",
            ["MultiTenant:Tenants:0:DatabaseContextConfiguration:ConnectionString"] =
                "Data Source=test;EmulatedProduct=Sqlite"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<DbProviderFactory>("fake-sqlite",
            (sp, key) => new fakeDbFactory(SupportedDatabase.Sqlite));

        services.AddMultiTenancy(configuration);
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<ITenantContextRegistry>();
        using var ctx = registry.GetContext("tenant-di-a");
        using var sc = ctx.CreateSqlContainer("SELECT 1");
        var affected = await sc.ExecuteNonQueryAsync();
        Assert.Equal(1, affected);
    }
}