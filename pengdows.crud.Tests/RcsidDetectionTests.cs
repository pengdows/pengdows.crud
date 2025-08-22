using System.Collections.Generic;
using System.Reflection;
using System.Data.Common;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class RcsidDetectionTests
{
    private static MethodInfo GetMethod()
        => typeof(DatabaseContext).GetMethod("DetectRCSI", BindingFlags.Instance | BindingFlags.NonPublic)!;

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

    private static ITrackedConnection BuildConnection(int rcsiFlag)
    {
        var inner = new FakeDbConnection
        {
            ConnectionString = $"Data Source=:memory:;EmulatedProduct={SupportedDatabase.SqlServer}"
        };
        inner.EnqueueScalarResult(rcsiFlag);
        inner.Open();
        return new TrackedConnection(inner);
    }

    [Fact]
    public void DetectRCSI_ReturnsTrueWhenEnabled()
    {
        using var ctx = CreateContext();
        var method = GetMethod();
        var conn = BuildConnection(1);
        var result = (bool)method.Invoke(ctx, new object[] { conn });
        Assert.True(result);
    }

    [Fact]
    public void DetectRCSI_ReturnsFalseWhenDisabled()
    {
        using var ctx = CreateContext();
        var method = GetMethod();
        var conn = BuildConnection(0);
        var result = (bool)method.Invoke(ctx, new object[] { conn });
        Assert.False(result);
    }
}
