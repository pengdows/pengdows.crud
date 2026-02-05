#region

using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.strategies.connection;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class KeepAliveConnectionStrategyCoverageTests
{
    [Fact]
    public async Task GetConnectionAsync_OpenFailure_DISPOSES_AND_RETHROWS()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite, ConnectionFailureMode.FailAfterCount, failAfterCount: 1);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=keepalive.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.KeepAlive,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var ctx = new DatabaseContext(cfg, factory);
        var strategy = new KeepAliveConnectionStrategy();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await strategy.GetConnectionAsync(ctx, ExecutionType.Read, false));
    }

    [Fact]
    public async Task ReleaseConnectionAsync_DoesNotDisposePersistentConnection()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=keepalive;EmulatedProduct=SqlServer",
            DbMode = DbMode.KeepAlive,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.SqlServer));
        Assert.Equal(DbMode.KeepAlive, ctx.ConnectionMode);
        Assert.NotNull(ctx.PersistentConnection);

        var strategy = new KeepAliveConnectionStrategy(ctx);
        await strategy.ReleaseConnectionAsync(ctx.PersistentConnection);

        Assert.Equal(ConnectionState.Open, ctx.PersistentConnection.State);
    }

    [Fact]
    public void HandleDialectDetection_ReturnsNullWhenFactoryMissing()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=keepalive.db;EmulatedProduct=SqlServer",
            DbMode = DbMode.KeepAlive,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.SqlServer));
        var strategy = new KeepAliveConnectionStrategy(ctx);

        var result = strategy.HandleDialectDetection(null, null, NullLoggerFactory.Instance);

        Assert.Null(result.dialect);
        Assert.Null(result.dataSourceInfo);
    }
}
