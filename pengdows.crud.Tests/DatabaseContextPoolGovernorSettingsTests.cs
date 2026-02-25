using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class DatabaseContextPoolGovernorSettingsTests
{
    [Fact]
    public void PoolingDisabled_WithoutOverrides_UsesDefaults()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer;Pooling=false",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.Standard
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.False(reader.Disabled);
        Assert.False(writer.Disabled);
        Assert.Equal(SqlDialect.FallbackMaxPoolSize, reader.MaxSlots);
        Assert.Equal(SqlDialect.FallbackMaxPoolSize, writer.MaxSlots);
    }

    [Fact]
    public void PoolingDisabled_WithOverrides_UsesConfiguredLimits()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer;Pooling=false",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.Standard,
            MaxConcurrentReads = 3,
            MaxConcurrentWrites = 4
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.False(reader.Disabled);
        Assert.False(writer.Disabled);
        // Reader and writer have independent governors with their own configured limits
        Assert.Equal(3, reader.MaxSlots);
        Assert.Equal(4, writer.MaxSlots);
    }

    [Fact]
    public void IndependentPools_RetainConfiguredReadWriteLimits()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.Standard,
            MaxConcurrentReads = 5,
            MaxConcurrentWrites = 10
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.False(reader.Disabled);
        Assert.False(writer.Disabled);
        // Reader and writer have independent governors with their own configured limits
        Assert.Equal(5, reader.MaxSlots);
        Assert.Equal(10, writer.MaxSlots);
    }

    [Fact]
    public void StandardMode_DoesNotSharePool_WhenConnectionStringsMatch()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Unknown",
            ProviderName = "fake",
            DbMode = DbMode.Standard,
            MaxConcurrentReads = 7,
            MaxConcurrentWrites = 3
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Unknown));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.Equal(7, reader.MaxSlots);
        Assert.Equal(3, writer.MaxSlots);
    }

    [Fact]
    public void SplitPools_RetainIndependentReadWriteLimits()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.SingleWriter,
            MaxConcurrentReads = 5,
            MaxConcurrentWrites = 10
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.False(reader.Disabled);
        Assert.False(writer.Disabled);
        Assert.Equal(5, reader.MaxSlots);
        Assert.Equal(1, writer.MaxSlots);
    }

    [Fact]
    public void SingleConnection_DisablesGovernors()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.SingleConnection
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.True(reader.Disabled);
        Assert.True(writer.Disabled);
    }

    [Fact]
    public void SingleWriter_PoolingFalse_IsIgnoredAndRemoved()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite;Pooling=false",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.SingleWriter
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));

        var builder = new DbConnectionStringBuilder { ConnectionString = ctx.ConnectionString };
        Assert.False(builder.ContainsKey("Pooling"));

        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.False(writer.Disabled);
        Assert.Equal(1, writer.MaxSlots);
    }

    [Fact]
    public void SingleConnection_PoolingFalse_IsIgnoredAndRemoved()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite;Pooling=false",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.SingleConnection
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));

        var builder = new DbConnectionStringBuilder { ConnectionString = ctx.ConnectionString };
        Assert.False(builder.ContainsKey("Pooling"));

        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.True(writer.Disabled);
    }
}