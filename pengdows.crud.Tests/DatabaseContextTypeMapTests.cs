using pengdows.crud.@internal;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextTypeMapTests
{
    [Fact]
    public void EachContext_ExposesUniqueTypeMapRegistry()
    {
        var sqliteFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var sqlServerFactory = new fakeDbFactory(SupportedDatabase.SqlServer);

        var sqliteConfig = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            ProviderName = SupportedDatabase.Sqlite.ToString()
        };

        var sqlServerConfig = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString()
        };

        using var sqliteContext = new DatabaseContext(sqliteConfig, sqliteFactory);
        using var sqlServerContext = new DatabaseContext(sqlServerConfig, sqlServerFactory);

        var sqliteRegistry = sqliteContext.GetInternalTypeMapRegistry();
        var sqlServerRegistry = sqlServerContext.GetInternalTypeMapRegistry();

        Assert.NotSame(sqliteRegistry, sqlServerRegistry);
        Assert.Same(sqliteRegistry, sqliteContext.GetInternalTypeMapRegistry());
        Assert.Same(sqlServerRegistry, sqlServerContext.GetInternalTypeMapRegistry());
    }
}
