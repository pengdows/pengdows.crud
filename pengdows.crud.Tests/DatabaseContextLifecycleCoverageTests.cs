using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextLifecycleCoverageTests
{
    [Fact]
    public void SingleWriter_GetSingleWriterConnection_CoverBranches()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;Mode=Memory;Cache=Shared",
            DbMode = DbMode.SingleWriter,
            ProviderName = "fake",
            EnableMetrics = true
        };

        using var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        // Write connection should reuse the persistent connection
        var writer = context.GetSingleWriterConnection(ExecutionType.Write);
        Assert.NotNull(writer);

        // Read connection in shared mode should reuse the same connection
        var sharedRead = context.GetSingleWriterConnection(ExecutionType.Read, isShared: true);
        Assert.Same(writer, sharedRead);

        // Read connection without shared should return the persistent connection for isolated memory
        var readOnly = context.GetSingleWriterConnection(ExecutionType.Read);
        Assert.Same(writer, readOnly);

        context.CloseAndDisposeConnection(readOnly);
        context.CloseAndDisposeConnection(writer);
        context.CloseAndDisposeConnection(sharedRead);
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
        var sharedWriter = sharedContext.GetSingleWriterConnection(ExecutionType.Write);
        var sharedReadOnly = sharedContext.GetSingleWriterConnection(ExecutionType.Read);
        Assert.NotSame(sharedWriter, sharedReadOnly);

        var sharedSharedRead = sharedContext.GetSingleWriterConnection(ExecutionType.Read, isShared: true);
        Assert.Same(sharedWriter, sharedSharedRead);

        sharedContext.CloseAndDisposeConnection(sharedReadOnly);
        sharedContext.CloseAndDisposeConnection(sharedSharedRead);
        sharedContext.CloseAndDisposeConnection(sharedWriter);
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
