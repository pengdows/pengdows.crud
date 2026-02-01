using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class DatabaseContextPoolGovernorSettingsTests
{
    [Fact]
    public void PoolingDisabled_WithoutOverrides_DisablesGovernors()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer;Pooling=false",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.Standard,
            EnablePoolGovernor = true
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.True(reader.Disabled);
        Assert.True(writer.Disabled);
    }

    [Fact]
    public void PoolingDisabled_WithOverrides_UsesConfiguredLimits()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer;Pooling=false",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.Standard,
            EnablePoolGovernor = true,
            ReadPoolSize = 3,
            WritePoolSize = 4
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.False(reader.Disabled);
        Assert.False(writer.Disabled);
        Assert.Equal(3, reader.MaxPermits);
        Assert.Equal(3, writer.MaxPermits);
    }

    [Fact]
    public void SharedPools_UseLowerMaxWhenReadWriteLimitsDiffer()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.Standard,
            EnablePoolGovernor = true,
            ReadPoolSize = 5,
            WritePoolSize = 10
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.False(reader.Disabled);
        Assert.False(writer.Disabled);
        Assert.Equal(5, reader.MaxPermits);
        Assert.Equal(5, writer.MaxPermits);
    }

    [Fact]
    public void PoolingDisabled_WithGovernorDisabled_StillEnforcesLimits()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer;Pooling=false",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.Standard,
            EnablePoolGovernor = false
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.False(reader.Disabled);
        Assert.False(writer.Disabled);
    }

    [Fact]
    public void StandardMode_ForcesGovernorOn()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.Standard,
            EnablePoolGovernor = false,
            ReadPoolSize = 4,
            WritePoolSize = 6
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.False(reader.Disabled);
        Assert.False(writer.Disabled);
    }

    [Fact]
    public void SplitPools_RetainIndependentReadWriteLimits()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.SingleWriter,
            EnablePoolGovernor = true,
            ReadPoolSize = 5,
            WritePoolSize = 10
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.False(reader.Disabled);
        Assert.False(writer.Disabled);
        Assert.Equal(5, reader.MaxPermits);
        Assert.Equal(10, writer.MaxPermits);
    }

    [Fact]
    public void SingleConnection_AllowsDisablingGovernor()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.SingleConnection,
            EnablePoolGovernor = false
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.True(reader.Disabled);
        Assert.True(writer.Disabled);
    }
}
