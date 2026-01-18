using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.strategies.connection;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.strategies;

public sealed class KeepAliveConnectionStrategyTests
{
    [Fact]
    public async Task PostInitialize_PromotesConnectionToPersistent()
    {
        using var context = CreateContext(out _);
        var strategy = new KeepAliveConnectionStrategy();

        var connection = await strategy.GetConnectionAsync(context, ExecutionType.Write, isShared: false);

        Assert.Same(connection, context.PersistentConnection);
        Assert.Equal(ConnectionState.Open, connection.State);

        await strategy.CloseConnectionAsync(connection, context);

        Assert.Equal(ConnectionState.Open, connection.State); // sentinel remains open
    }

    [Fact]
    public void HandleDialectDetection_UsesPersistentConnectionWhenAvailable()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.KeepAlive
        };

        using var context = new DatabaseContext(config, factory);
        var strategy = new KeepAliveConnectionStrategy(context);
        var connection = strategy.GetConnection(ExecutionType.Read, isShared: false);
        strategy.PostInitialize(connection);

        var (dialect, dataSource) = strategy.HandleDialectDetection(null, factory, NullLoggerFactory.Instance);

        Assert.NotNull(dialect);
        Assert.NotNull(dataSource);
    }

    private static DatabaseContext CreateContext(out fakeDbFactory factory)
    {
        factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.KeepAlive
        };

        return new DatabaseContext(config, factory);
    }
}
