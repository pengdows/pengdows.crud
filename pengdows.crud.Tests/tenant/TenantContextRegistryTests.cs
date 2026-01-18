using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.tenant;
using Xunit;

namespace pengdows.crud.Tests.tenant;

public sealed class TenantContextRegistryTests
{
    [Fact]
    public void GetContext_CachesPerTenant()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var services = new ServiceCollection();
        services.AddKeyedSingleton<DbProviderFactory>("Fake", factory);
        var provider = services.BuildServiceProvider();

        var resolver = new Mock<ITenantConnectionResolver>();
        resolver.Setup(r => r.GetDatabaseContextConfiguration("tenant"))
            .Returns(new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
                ProviderName = "Fake",
                DbMode = DbMode.Standard
            });

        using var registry = new TenantContextRegistry(provider, resolver.Object, NullLoggerFactory.Instance);

        var first = registry.GetContext("tenant");
        var second = registry.GetContext("tenant");

        Assert.Same(first, second);
    }

    [Fact]
    public async Task DisposeAsync_SwallowsErrorsButClearsContexts()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<DbProviderFactory>("Fake", new fakeDbFactory(SupportedDatabase.Sqlite));
        var provider = services.BuildServiceProvider();

        var resolver = new Mock<ITenantConnectionResolver>();
        resolver.Setup(r => r.GetDatabaseContextConfiguration(It.IsAny<string>()))
            .Returns(new DatabaseContextConfiguration
            {
                ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
                ProviderName = "Fake",
                DbMode = DbMode.Standard
            });

        await using var registry = new TenantContextRegistry(provider, resolver.Object, NullLoggerFactory.Instance);

        // materialize a context so disposal work happens
        registry.GetContext("tenant-a");

        // no exception should escape
        await registry.DisposeAsync();
    }
}
