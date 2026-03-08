using System;
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
    public void PoolingDisabled_Standard_ThrowsInvalidOperationException()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer;Pooling=false",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.Standard
        };

        Assert.Throws<InvalidOperationException>(() =>
            new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer)));
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
    public void SingleWriter_PoolingFalse_Throws()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;Pooling=false",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.SingleWriter
        };

        Assert.Throws<InvalidOperationException>(() =>
            new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer)));
    }

    [Fact]
    public void SingleConnection_PoolingFalse_IsIgnoredAndRemoved()
    {
        // SingleConnection returns early from ApplyPoolingDefaults — Pooling=false is stripped,
        // not rejected. No throw expected for this mode.
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

    // PoolSize sync: configured value wins over connection-string value
    // ----------------------------------------------------------------

    [Fact]
    public void MaxConcurrentWrites_OverridesConnectionStringMaxPoolSize_GovernorAndConnectionString()
    {
        // Connection string claims Max Pool Size=200; MaxConcurrentWrites=20 must win on both
        // the governor slot count and the stamped connection string value.
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer;Max Pool Size=200",
            DbMode = DbMode.Standard,
            MaxConcurrentWrites = 20,
            MaxConcurrentReads = 20
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.Equal(20, writer.MaxSlots);

        var builder = new DbConnectionStringBuilder { ConnectionString = ctx.ConnectionString };
        Assert.True(builder.TryGetValue("Max Pool Size", out var raw));
        Assert.Equal(20, Convert.ToInt32(raw));
    }

    [Fact]
    public void MaxConcurrentReads_OverridesConnectionStringMaxPoolSize_InReaderConnectionString()
    {
        // Connection string claims Max Pool Size=200; MaxConcurrentReads=15 must win.
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer;Max Pool Size=200",
            ReadOnlyConnectionString = "Data Source=replica;EmulatedProduct=SqlServer;Max Pool Size=200",
            DbMode = DbMode.Standard,
            MaxConcurrentReads = 15,
            MaxConcurrentWrites = 15
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        Assert.Equal(15, reader.MaxSlots);
    }

    [Fact]
    public void SingleWriter_StampsMaxPoolSize1_OntoConnectionString()
    {
        // Even when the user puts Max Pool Size=100 in the connection string, SingleWriter must
        // force it to 1 so the provider pool and the governor are in sync.
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer;Max Pool Size=100",
            DbMode = DbMode.SingleWriter
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.Equal(1, writer.MaxSlots);

        var builder = new DbConnectionStringBuilder { ConnectionString = ctx.ConnectionString };
        Assert.True(builder.TryGetValue("Max Pool Size", out var raw));
        Assert.Equal(1, Convert.ToInt32(raw));
    }

    [Fact]
    public void SingleWriterReadOnly_IsEquivalentToStandardReadOnly()
    {
        // SingleWriter adds a turnstile for writes and limits write concurrency to 1.
        // In ReadOnly mode there are zero writers, so SingleWriter provides no benefit
        // over Standard — the two modes must be functionally identical in ReadOnly.
        // Specifically: the reader connection string must retain pooling (not be stripped),
        // the write governor must be forbidden, and the read governor must be active.
        var swConfig = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.SingleWriter,
            ReadWriteMode = ReadWriteMode.ReadOnly,
            MaxConcurrentReads = 8
        };
        var stdConfig = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadOnly,
            MaxConcurrentReads = 8
        };

        using var swCtx = new DatabaseContext(swConfig, new fakeDbFactory(SupportedDatabase.SqlServer));
        using var stdCtx = new DatabaseContext(stdConfig, new fakeDbFactory(SupportedDatabase.SqlServer));

        // Both contexts: write pool forbidden, read pool active with 8 slots
        Assert.True(swCtx.GetPoolStatisticsSnapshot(PoolLabel.Writer).Forbidden);
        Assert.True(stdCtx.GetPoolStatisticsSnapshot(PoolLabel.Writer).Forbidden);
        Assert.Equal(8, swCtx.GetPoolStatisticsSnapshot(PoolLabel.Reader).MaxSlots);
        Assert.Equal(8, stdCtx.GetPoolStatisticsSnapshot(PoolLabel.Reader).MaxSlots);

        // SingleWriter ReadOnly must not strip pooling from the reader connection string
        var swBuilder = new DbConnectionStringBuilder { ConnectionString = swCtx.ConnectionString };
        Assert.False(swBuilder.ContainsKey("Pooling") && swBuilder["Pooling"]?.ToString() == "False");
    }

    [Fact]
    public void ReadOnly_WriterGovernorIsForbidden()
    {
        // A ReadOnly context must disable the write pool via the governor regardless of any
        // pool-size configuration in the connection string.
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer;Max Pool Size=100",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.True(writer.Forbidden);
    }

    [Fact]
    public void Standard_StampsWriteMaxOnWriterConnectionString()
    {
        // Standard/KeepAlive always uses separate pools for reads and writes (differentiated
        // via ApplicationName suffix or Connection Timeout delta).  The writer connection
        // string should carry exactly the write governor size.
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            MaxConcurrentReads = 5,
            MaxConcurrentWrites = 10
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.Equal(10, writer.MaxSlots);

        var builder = new DbConnectionStringBuilder { ConnectionString = ctx.ConnectionString };
        Assert.True(builder.TryGetValue("Max Pool Size", out var raw));
        Assert.Equal(10, Convert.ToInt32(raw));
    }

    [Fact]
    public void ReadOnly_SharedConnectionString_StampsReadPoolSizeOnConnectionString()
    {
        // When no separate ReadOnlyConnectionString is provided the reader shares
        // _connectionString. The read pool size must be stamped so the ADO.NET pool
        // is sized to match the read governor, not left at the user's unvalidated value.
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer;Max Pool Size=200",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadOnly,
            MaxConcurrentReads = 25
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        Assert.Equal(25, reader.MaxSlots);

        var builder = new DbConnectionStringBuilder { ConnectionString = ctx.ConnectionString };
        Assert.True(builder.TryGetValue("Max Pool Size", out var raw));
        Assert.Equal(25, Convert.ToInt32(raw));
    }
}