#region

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

public class MariaDbDialectTests
{
    private static MariaDbDialect CreateDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        return new MariaDbDialect(factory, NullLogger<MariaDbDialect>.Instance);
    }

    private static void SetVersion(MariaDbDialect dialect, Version version)
    {
        // Inject ProductInfo with ParsedVersion so IsInitialized=true and IsAtLeast() works
        var field = typeof(SqlDialect).GetField("_productInfo", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(dialect, new DatabaseProductInfo
        {
            ProductName = "MariaDB",
            ProductVersion = version.ToString(),
            ParsedVersion = version,
            DatabaseType = SupportedDatabase.MariaDb,
            StandardCompliance = dialect.DetermineStandardCompliance(version)
        });
    }

    [Fact]
    public void Properties_Are_MariaDb_Specific()
    {
        var d = CreateDialect();
        Assert.Equal(SupportedDatabase.MariaDb, d.DatabaseType);
        Assert.Equal("`", d.QuotePrefix);
        Assert.Equal("`", d.QuoteSuffix);
        Assert.Equal("SELECT LAST_INSERT_ID()", d.GetLastInsertedIdQuery());
        Assert.True(d.SupportsIdentityColumns);
        Assert.False(d.SupportsJsonTypes);

        // Backtick wrapping
        Assert.Equal("`s`.`t`", d.WrapObjectName("s.t"));
    }

    [Fact]
    public void FeatureGates_Depends_On_Version()
    {
        var d = CreateDialect();

        SetVersion(d, new Version(10, 1));
        Assert.False(d.SupportsWindowFunctions);
        Assert.False(d.SupportsCommonTableExpressions);

        SetVersion(d, new Version(10, 2));
        Assert.True(d.SupportsWindowFunctions);
        Assert.True(d.SupportsCommonTableExpressions);

        SetVersion(d, new Version(5, 7));
        Assert.False(d.SupportsWindowFunctions);
        Assert.Equal(SqlStandardLevel.Sql99, d.DetermineStandardCompliance(new Version(5, 7)));

        Assert.Equal(SqlStandardLevel.Sql92, d.DetermineStandardCompliance(null));
    }

    [Fact]
    public async Task GetProductNameAsync_Rewrites_MySql_To_MariaDb()
    {
        var d = CreateDialect();
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb);
        var conn = factory.CreateConnection();
        var schema = DataSourceInformation.BuildEmptySchema(
            "MySQL", // reported product name
            "10.3",
            "@p[0-9]+",
            "@{0}",
            64,
            @"@\\w+",
            @"@\\w+",
            true);
        using var tracked = new FakeTrackedConnection(conn, schema, new Dictionary<string, object>());

        var name = await d.GetProductNameAsync(tracked);
        Assert.Equal("MariaDB", name);
    }
}
