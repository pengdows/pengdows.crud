using System;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The three states a pool's governor can be in, observed through the context's public statistics:
/// enabled with a size, forbidden (a size of 0), and disabled (no governor at all).
/// </summary>
public sealed class PoolGovernorStatesTests
{
    private static DatabaseContext PostgreSql(Action<DatabaseContextConfiguration> configure)
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=db1;Database=d;Username=u;Password=p",
            DbMode = DbMode.Standard
        };
        configure(config);
        return new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.PostgreSql));
    }

    [Fact]
    public void ASizedPool_IsEnabled_WithThatManySlots()
    {
        using var context = PostgreSql(c => c.MaxConcurrentWrites = 2);

        var writer = context.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.False(writer.Disabled);
        Assert.False(writer.Forbidden);
        Assert.Equal(2, writer.MaxSlots);
    }

    [Fact]
    public void APoolSizedZero_IsForbidden_AndRefusesEveryAcquire()
    {
        using var context = PostgreSql(c => c.MaxConcurrentReads = 0);

        var reader = context.GetPoolStatisticsSnapshot(PoolLabel.Reader);

        Assert.True(reader.Forbidden);
        Assert.False(reader.Disabled);
        Assert.Equal(0, reader.MaxSlots);
        Assert.Throws<pengdows.crud.exceptions.PoolForbiddenException>(() => context.GetConnection(ExecutionType.Read));
    }

    [Fact]
    public void ASingleConnectionContext_HasNoGovernors_SoBothPoolsReportDisabled()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection
        };
        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));

        Assert.True(context.GetPoolStatisticsSnapshot(PoolLabel.Reader).Disabled);
        Assert.True(context.GetPoolStatisticsSnapshot(PoolLabel.Writer).Disabled);
    }
}
