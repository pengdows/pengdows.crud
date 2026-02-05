using pengdows.crud.configuration;
using pengdows.crud.enums;
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
            MaxConcurrentReads = 10,
            MaxConcurrentWrites = 10
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.True(reader.Disabled);
        Assert.Equal(1, writer.TotalAcquired);
    }

    [Fact]
    public void SingleWriter_UsesStandardLifecycle_WithSingleWriteSlot()
    {
        // SingleWriter mode now uses Standard lifecycle with governor policy:
        // - Each write acquires/releases a permit (no pinned connection)
        // - WriteSlots = 1 enforces single-writer rule
        // - Turnstile provides writer preference fairness

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=MySql",
            ProviderName = SupportedDatabase.MySql.ToString(),
            DbMode = DbMode.SingleWriter,
            EnablePoolGovernor = true,
            MaxConcurrentReads = 10,
            MaxConcurrentWrites = 10 // Will be overridden to 1 for SingleWriter
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.MySql));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.False(reader.Disabled);
        Assert.Equal(1, writer.MaxPermits); // SingleWriter enforces single write slot
        Assert.Equal(0, writer.TotalAcquired); // No pre-acquired pinned permit

        // Acquire and release a write connection
        var writeConn1 = ctx.GetConnection(ExecutionType.Write);
        Assert.Equal(1, ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer).InUse);
        ctx.CloseAndDisposeConnection(writeConn1);
        Assert.Equal(0, ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer).InUse);

        // Acquire another write connection (proves per-operation lifecycle)
        var writeConn2 = ctx.GetConnection(ExecutionType.Write);
        Assert.Equal(1, ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer).InUse);
        ctx.CloseAndDisposeConnection(writeConn2);

        // Total write permits acquired should be 2 (one per connection)
        Assert.Equal(2, ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer).TotalAcquired);

        // Reads also work
        var readConn = ctx.GetConnection(ExecutionType.Read);
        ctx.CloseAndDisposeConnection(readConn);

        var readAfter = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader).TotalAcquired;
        Assert.True(readAfter >= 1);
    }
}
