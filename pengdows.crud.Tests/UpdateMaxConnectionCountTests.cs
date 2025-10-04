using System.Reflection;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class UpdateMaxConnectionCountTests
{
    private static MethodInfo GetMethod()
        => typeof(DatabaseContext).GetMethod("UpdateMaxConnectionCount", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static FieldInfo GetField()
        => typeof(DatabaseContext).GetField("_maxNumberOfOpenConnections", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static DatabaseContext CreateContext()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.SingleConnection
        };

        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        return new DatabaseContext(config.ConnectionString, factory);
    }

    [Fact]
    public void UpdateMaxConnectionCount_IncreasesWhenHigher()
    {
        using var ctx = CreateContext();
        GetField().SetValue(ctx, 5L);

        GetMethod().Invoke(ctx, new object[] { 10L });

        Assert.Equal(10L, (long)GetField().GetValue(ctx)!);
    }

    [Fact]
    public void UpdateMaxConnectionCount_DoesNotDecrease()
    {
        using var ctx = CreateContext();
        GetField().SetValue(ctx, 10L);

        GetMethod().Invoke(ctx, new object[] { 5L });

        Assert.Equal(10L, (long)GetField().GetValue(ctx)!);
    }
}
