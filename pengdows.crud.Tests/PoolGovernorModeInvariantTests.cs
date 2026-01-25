using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PoolGovernorModeInvariantTests
{
    [Fact]
    public void SingleConnection_DisablesReaderGovernor_AndPinsWriterPermit()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.SingleConnection,
            EnablePoolGovernor = true,
            ReadPoolSize = 10,
            WritePoolSize = 10
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.True(reader.Disabled);
        Assert.Equal(1, writer.TotalAcquired);
    }

    [Fact]
    public void SingleWriter_SharedPool_ReservesCapacityForPinnedWriter()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=MySql",
            ProviderName = SupportedDatabase.MySql.ToString(),
            DbMode = DbMode.SingleWriter,
            EnablePoolGovernor = true,
            ReadPoolSize = 10,
            WritePoolSize = 10
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.MySql));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.False(reader.Disabled);

        var before = writer.TotalAcquired;
        _ = ctx.GetConnection(ExecutionType.Write);
        _ = ctx.GetConnection(ExecutionType.Write);
        var after = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer).TotalAcquired;

        Assert.Equal(before, after);

        var readConn = ctx.GetConnection(ExecutionType.Read);
        ctx.CloseAndDisposeConnection(readConn);

        var readAfter = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader).TotalAcquired;
        Assert.True(readAfter >= 1);
    }
}