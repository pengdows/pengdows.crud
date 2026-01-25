#region

using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.strategies.connection;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests.strategies;

public class SingleConnectionStrategyTests
{
    private static DatabaseContext CreateSingleConnectionContext()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.SingleConnection
        };
        return new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite), NullLoggerFactory.Instance);
    }

    [Fact]
    public void GetConnection_ReturnsPersistentConnection()
    {
        using var ctx = CreateSingleConnectionContext();
        var strategy = new SingleConnectionStrategy(ctx);
        var persistent = ctx.GetConnection(ExecutionType.Read); // under SingleConnection this pins a connection

        var c1 = strategy.GetConnection(ExecutionType.Read, false);
        var c2 = strategy.GetConnection(ExecutionType.Write, true);

        Assert.Same(persistent, c1);
        Assert.Same(persistent, c2);
    }

    [Fact]
    public void PostInitialize_SetsPersistentConnection()
    {
        using var ctx = CreateSingleConnectionContext();
        var strategy = new SingleConnectionStrategy(ctx);
        var conn = ctx.GetConnection(ExecutionType.Read);

        strategy.PostInitialize(conn);
        Assert.Same(conn, ctx.PersistentConnection);
    }

    [Fact]
    public async Task ReleaseConnection_DisposesNonPersistentOnly()
    {
        using var ctx = CreateSingleConnectionContext();
        var strategy = new SingleConnectionStrategy(ctx);
        var persistent = ctx.GetConnection(ExecutionType.Read);

        // Non-persistent (separate instance)
        var disposed = false;
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var temp = new TrackedConnection(factory.CreateConnection(), onDispose: _ => disposed = true);

        strategy.ReleaseConnection(persistent);
        Assert.False(persistent == null); // sanity

        strategy.ReleaseConnection(temp);
        Assert.True(disposed);

        // Async path
        disposed = false;
        temp = new TrackedConnection(factory.CreateConnection(), onDispose: _ => disposed = true);
        await strategy.ReleaseConnectionAsync(temp);
        Assert.True(disposed);
    }
}