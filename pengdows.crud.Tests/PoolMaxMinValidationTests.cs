// =============================================================================
// FILE: PoolMaxMinValidationTests.cs
// PURPOSE: Tests for MaxPoolSize / MinPoolSize validation and ReadOnly write pool.
//
// Validates:
//   - MaxPoolSize < 0 → ArgumentOutOfRangeException during context construction
//   - MinPoolSize < 0 → ArgumentOutOfRangeException
//   - MinPoolSize > MaxPoolSize → ArgumentOutOfRangeException
//   - MinPoolSize == MaxPoolSize → valid
//   - ReadOnly context → write governor is forbidden
//   - ReadOnly context → read governor is enabled
// =============================================================================

using System;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PoolMaxMinValidationTests
{
    private static DatabaseContextConfiguration SqlServerConfig(
        string extraSettings = "",
        ReadWriteMode rwMode = ReadWriteMode.ReadWrite,
        int? maxWrites = null,
        int? maxReads = null)
        => new()
        {
            ConnectionString = $"Data Source=test;EmulatedProduct=SqlServer{(string.IsNullOrEmpty(extraSettings) ? "" : ";" + extraSettings)}",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.Standard,
            ReadWriteMode = rwMode,
            MaxConcurrentWrites = maxWrites,
            MaxConcurrentReads = maxReads
        };

    // =========================================================================
    // MaxPoolSize validation
    // =========================================================================

    [Fact]
    public void MaxConcurrentWrites_Negative_ThrowsArgumentOutOfRangeException()
    {
        // Config-level guard fires at property assignment
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => config.MaxConcurrentWrites = -1);
    }

    [Fact]
    public void MaxConcurrentReads_Negative_ThrowsArgumentOutOfRangeException()
    {
        // Config-level guard fires at property assignment
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => config.MaxConcurrentReads = -1);
    }

    [Fact]
    public void MaxPoolSize_FromConnectionString_Negative_ThrowsArgumentOutOfRangeException()
    {
        // Negative max pool size in connection string is caught during InitializePoolGovernors
        var config = SqlServerConfig("Max Pool Size=-1");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer)));
    }

    // =========================================================================
    // MinPoolSize clamping (polite correction, no throw)
    // =========================================================================

    [Fact]
    public void MinPoolSize_Negative_ClampsToZeroSilently()
    {
        // Negative min pool size is silently corrected to 0 — no exception thrown
        var config = SqlServerConfig("Min Pool Size=-1");

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));
        Assert.NotNull(ctx);
    }

    [Fact]
    public void MinPoolSize_GreaterThanMaxPoolSize_ClampsToMaxSilently()
    {
        // Min=10 > Max=5 → silently clamped to Min=5 — no exception thrown
        var config = SqlServerConfig("Min Pool Size=10;Max Pool Size=5");

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));
        Assert.NotNull(ctx);
    }

    [Fact]
    public void MinPoolSize_EqualToMaxPoolSize_IsValid()
    {
        // Min=Max=5 → already valid, no change needed
        var config = SqlServerConfig("Min Pool Size=5;Max Pool Size=5");

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));
        Assert.NotNull(ctx);
    }

    [Fact]
    public void MinPoolSize_Zero_IsValid()
    {
        var config = SqlServerConfig("Min Pool Size=0");

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));
        Assert.NotNull(ctx);
    }

    // =========================================================================
    // MaxPoolSize = 0 (forbidden pool via configuration)
    // =========================================================================

    [Fact]
    public void MaxConcurrentWrites_Zero_WriterGovernorIsForbidden()
    {
        var config = SqlServerConfig(maxWrites: 0);

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var snapshot = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.True(snapshot.Forbidden);
        Assert.False(snapshot.Disabled);
    }

    // =========================================================================
    // ReadOnly context: write pool is forbidden
    // =========================================================================

    [Fact]
    public void ReadOnly_WriterGovernor_IsForbidden()
    {
        var config = SqlServerConfig(rwMode: ReadWriteMode.ReadOnly);

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.True(writer.Forbidden, "Write pool must be forbidden for ReadOnly context");
        Assert.False(writer.Disabled);
    }

    [Fact]
    public void ReadOnly_ReaderGovernor_IsEnabled()
    {
        var config = SqlServerConfig(rwMode: ReadWriteMode.ReadOnly);

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        Assert.False(reader.Forbidden, "Read pool must not be forbidden for ReadOnly context");
        Assert.False(reader.Disabled);
    }

    [Fact]
    public void ReadWrite_BothGovernors_AreEnabled()
    {
        var config = SqlServerConfig(rwMode: ReadWriteMode.ReadWrite);

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        var reader = ctx.GetPoolStatisticsSnapshot(PoolLabel.Reader);

        Assert.False(writer.Forbidden, "Writer must not be forbidden for ReadWrite context");
        Assert.False(reader.Forbidden, "Reader must not be forbidden for ReadWrite context");
    }

    [Fact]
    public void ReadOnly_SingleWriter_WriterGovernor_IsForbidden()
    {
        // SingleWriter + ReadOnly: the writerLabelMax=1 override must NOT stomp the
        // rawWriterMax=0 (forbidden) that ReadOnly sets. The writer must remain forbidden.
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            DbMode = DbMode.SingleWriter,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        var writer = ctx.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.True(writer.Forbidden,
            "SingleWriter + ReadOnly: write governor must be forbidden (SingleWriter cannot override ReadOnly)");
    }
}
