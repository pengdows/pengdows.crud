using System;
using System.Collections.Generic;
using System.Reflection;
using System.Data;
using Moq;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class CheckForSqlServerSettingsTests
{
    private static MethodInfo GetMethod()
        => typeof(DatabaseContext).GetMethod("CheckForSqlServerSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static FieldInfo GetSettingsField()
        => typeof(DatabaseContext).GetField("_connectionSessionSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;

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

    private static void Invoke(DatabaseContext ctx, ITrackedConnection conn)
    {
        GetMethod().Invoke(ctx, new object[] { conn });
    }

    private static string GetSessionSettings(DatabaseContext ctx)
        => (string)GetSettingsField().GetValue(ctx)!;

    private static void SetSessionSettings(DatabaseContext ctx, string value)
        => GetSettingsField().SetValue(ctx, value);

    private static ITrackedConnection BuildConnection(IEnumerable<Dictionary<string, object>> rows)
    {
        var reader = new FakeDbDataReader(rows);
        var command = new Mock<IDbCommand>();
        command.SetupProperty(c => c.CommandText);
        command.Setup(c => c.ExecuteReader()).Returns(reader);

        var conn = new Mock<ITrackedConnection>();
        conn.Setup(c => c.CreateCommand()).Returns(command.Object);
        return conn.Object;
    }

    [Fact]
    public void CheckForSqlServerSettings_NoDifferences_LeavesSettingsUnchanged()
    {
        using var ctx = CreateContext();
        SetSessionSettings(ctx, string.Empty);

        var rows = new[]
        {
            new Dictionary<string, object> { { "a", "ANSI_NULLS" }, { "b", "SET" } },
            new Dictionary<string, object> { { "a", "ANSI_PADDING" }, { "b", "SET" } },
            new Dictionary<string, object> { { "a", "ANSI_WARNINGS" }, { "b", "SET" } },
            new Dictionary<string, object> { { "a", "ARITHABORT" }, { "b", "SET" } },
            new Dictionary<string, object> { { "a", "CONCAT_NULL_YIELDS_NULL" }, { "b", "SET" } },
            new Dictionary<string, object> { { "a", "QUOTED_IDENTIFIER" }, { "b", "SET" } },
            new Dictionary<string, object> { { "a", "NUMERIC_ROUNDABORT" }, { "b", "OFF" } }
        };

        var conn = BuildConnection(rows);
        Invoke(ctx, conn);

        Assert.Equal(string.Empty, GetSessionSettings(ctx));
    }

    [Fact]
    public void CheckForSqlServerSettings_Differences_BuildsSettingsScript()
    {
        using var ctx = CreateContext();
        SetSessionSettings(ctx, string.Empty);

        var rows = new[]
        {
            new Dictionary<string, object> { { "a", "ANSI_NULLS" }, { "b", "SET" } },
            new Dictionary<string, object> { { "a", "ANSI_PADDING" }, { "b", "SET" } },
            new Dictionary<string, object> { { "a", "ANSI_WARNINGS" }, { "b", "OFF" } },
            new Dictionary<string, object> { { "a", "ARITHABORT" }, { "b", "SET" } }
        };

        var conn = BuildConnection(rows);
        Invoke(ctx, conn);

        var nl = Environment.NewLine;
        var expected =
            $"SET NOCOUNT ON;{nl}" +
            $"SET ANSI_WARNINGS ON{nl}" +
            $"SET CONCAT_NULL_YIELDS_NULL ON{nl}" +
            $"SET QUOTED_IDENTIFIER ON;{nl}" +
            $"SET NOCOUNT OFF;{nl}";

        Assert.Equal(expected, GetSessionSettings(ctx));
    }
}
