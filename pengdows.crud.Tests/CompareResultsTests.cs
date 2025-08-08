#region
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class CompareResultsTests
{
    private static StringBuilder Invoke(DatabaseContext ctx, Dictionary<string, string> expected, Dictionary<string, string> recorded)
    {
        var mi = typeof(DatabaseContext).GetMethod("CompareResults", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (StringBuilder)mi.Invoke(ctx, new object[] { expected, recorded })!;
    }

    private static DatabaseContext CreateContext()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={SupportedDatabase.SqlServer}",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.SingleConnection
        };
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        return new DatabaseContext(config, factory);
    }

    [Fact]
    public void CompareResults_NoDifferences_ReturnsEmpty()
    {
        using var ctx = CreateContext();
        var expected = new Dictionary<string, string> { { "ANSI_NULLS", "ON" } };
        var recorded = new Dictionary<string, string> { { "ANSI_NULLS", "ON" } };

        var sb = Invoke(ctx, expected, recorded);

        Assert.Equal(string.Empty, sb.ToString());
    }

    [Fact]
    public void CompareResults_SingleDifference_ReturnsSetStatement()
    {
        using var ctx = CreateContext();
        var expected = new Dictionary<string, string> { { "ANSI_NULLS", "ON" } };
        var recorded = new Dictionary<string, string> { { "ANSI_NULLS", "OFF" } };

        var sb = Invoke(ctx, expected, recorded);

        Assert.Equal("SET ANSI_NULLS ON", sb.ToString());
    }

    [Fact]
    public void CompareResults_MultipleDifferences_JoinWithNewLines()
    {
        using var ctx = CreateContext();
        var expected = new Dictionary<string, string>
        {
            { "ANSI_NULLS", "ON" },
            { "ANSI_PADDING", "ON" }
        };
        var recorded = new Dictionary<string, string>
        {
            { "ANSI_NULLS", "OFF" },
            { "ANSI_PADDING", "OFF" }
        };

        var sb = Invoke(ctx, expected, recorded);

        var expectedText = $"SET ANSI_NULLS ON{Environment.NewLine}SET ANSI_PADDING ON";
        Assert.Equal(expectedText, sb.ToString());
    }
}
