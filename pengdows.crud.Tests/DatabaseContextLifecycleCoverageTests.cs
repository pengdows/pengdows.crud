using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextLifecycleCoverageTests
{
    [Fact]
    public void SingleWriter_UsesStandardLifecycle()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter,
            ProviderName = "fake",
            EnableMetrics = true
        };

        using var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var writer1 = context.GetConnection(ExecutionType.Write);
        context.CloseAndDisposeConnection(writer1);

        var writer2 = context.GetConnection(ExecutionType.Write);
        Assert.NotSame(writer1, writer2); // per-operation connections
        context.CloseAndDisposeConnection(writer2);
    }

    [Fact]
    public void DatabaseContext_Constructors_CoverOverloads()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var context1 = new DatabaseContext("Data Source=:memory:", factory);
        Assert.Contains("data source=:memory:", context1.ConnectionString, StringComparison.OrdinalIgnoreCase);

        using var context2 = new DatabaseContext("Data Source=:memory:", factory, new TypeMapRegistry());
        Assert.Contains("data source=:memory:", context2.ConnectionString, StringComparison.OrdinalIgnoreCase);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;Mode=Memory",
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };

        using var context3 = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
        Assert.Equal(
            config.ConnectionString.ToLowerInvariant(),
            context3.ConnectionString.ToLowerInvariant());

        var sharedConfig = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file:memdb?mode=memory&cache=shared",
            DbMode = DbMode.SingleWriter,
            ProviderName = "fake",
            EnableMetrics = true
        };

        using var sharedContext = new DatabaseContext(sharedConfig, factory, NullLoggerFactory.Instance);
        var sharedWriter = sharedContext.GetConnection(ExecutionType.Write);
        Assert.NotNull(sharedWriter);
        sharedContext.CloseAndDisposeConnection(sharedWriter);

        var sharedReader = sharedContext.GetConnection(ExecutionType.Read);
        Assert.NotNull(sharedReader);
        sharedContext.CloseAndDisposeConnection(sharedReader);
    }

    [Fact]
    public void DatabaseContext_MetricsSnapshot_IncludesPoolStats()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };

        using var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
        context.TrackConnectionReuse();
        context.TrackConnectionFailure(new TimeoutException("boom"));

        var metrics = context.Metrics;
        Assert.True(metrics.ConnectionsCurrent >= 0);
        Assert.True(metrics.CommandsExecuted >= 0);

        var readerSnapshot = context.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        var writerSnapshot = context.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        Assert.Equal(PoolLabel.Reader, readerSnapshot.Label);
        Assert.Equal(PoolLabel.Writer, writerSnapshot.Label);
        Assert.True(readerSnapshot.Disabled || readerSnapshot.MaxPermits >= 0);
    }
}
