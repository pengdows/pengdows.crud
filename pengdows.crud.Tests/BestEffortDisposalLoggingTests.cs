using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.Tests.Logging;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Review 2026-09-29 (Schneier: fail loudly). Best-effort disposal must keep going after a failure,
/// but the failure must leave a trace: these paths swallowed exceptions with empty catch blocks.
/// Also: DisposeOwnedDataSourcesAsync never disposed the data sources retired by
/// RebuildOwnedDataSourcesForChangedConnectionStrings, so an async-disposed context leaked them.
/// </summary>
public sealed class BestEffortDisposalLoggingTests
{
    // Firebird: Best selects PreventDatabaseUnload, which raises Min Pool Size after the data sources
    // exist, so they are rebuilt and the originals retired.
    private const string FirebirdCs = "Data Source=localhost;Database=/data/test.fdb;EmulatedProduct=Firebird";

    private static (DatabaseContext Context, fakeDbFactory Factory, ListLoggerProvider Logs, LoggerFactory LoggerFactory)
        CreateFirebird(Exception? dataSourceDisposeFailure = null)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird)
        {
            SupportsNativeDataSource = true,
            ThrowOnDataSourceDispose = dataSourceDisposeFailure
        };
        var logs = new ListLoggerProvider();
        var loggerFactory = new LoggerFactory(new[] { logs });
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = FirebirdCs,
            DbMode = DbMode.Best
        }, factory, loggerFactory);
        Assert.Equal(DbMode.PreventDatabaseUnload, context.ConnectionMode);
        return (context, factory, logs, loggerFactory);
    }

    private static Mock<ITrackedConnection> ThrowingSentinel(ConnectionState state)
    {
        var sentinel = new Mock<ITrackedConnection>();
        sentinel.SetupGet(c => c.State).Returns(state);
        sentinel.SetupGet(c => c.ConnectionString).Returns(FirebirdCs);
        // ITrackedConnection redeclares Dispose, so the interface callers use must be set up directly.
        sentinel.Setup(c => c.Dispose()).Throws(new InvalidOperationException("sentinel dispose failed"));
        sentinel.As<IDisposable>().Setup(c => c.Dispose()).Throws(new InvalidOperationException("sentinel dispose failed"));
        sentinel.As<IAsyncDisposable>().Setup(c => c.DisposeAsync()).Throws(new InvalidOperationException("sentinel dispose failed"));
        return sentinel;
    }

    private static void AssertLoggedFailure(ListLoggerProvider logs, string exceptionMessage)
    {
        Assert.Contains(logs.Entries, e => e.Exception?.Message == exceptionMessage && e.Level >= LogLevel.Debug);
    }

    [Fact]
    public async Task DisposeAsync_DisposesRetiredDataSources()
    {
        var (context, factory, _, loggerFactory) = CreateFirebird();
        using var _ = loggerFactory;
        Assert.True(factory.CreatedDataSources.Count > 1, "expected the data sources to have been rebuilt");

        await context.DisposeAsync();

        Assert.All(factory.CreatedDataSources, source => Assert.True(source.WasDisposed));
    }

    [Fact]
    public void Dispose_DisposesRetiredDataSources()
    {
        var (context, factory, _, loggerFactory) = CreateFirebird();
        using var _ = loggerFactory;

        context.Dispose();

        Assert.All(factory.CreatedDataSources, source => Assert.True(source.WasDisposed));
    }

    [Fact]
    public void Dispose_DataSourceDisposeFailure_IsLoggedNotThrown()
    {
        var (context, _, logs, loggerFactory) = CreateFirebird(new InvalidOperationException("data source dispose failed"));
        using var _ = loggerFactory;

        context.Dispose();

        AssertLoggedFailure(logs, "data source dispose failed");
    }

    [Fact]
    public async Task DisposeAsync_DataSourceDisposeFailure_IsLoggedNotThrown()
    {
        var (context, _, logs, loggerFactory) = CreateFirebird(new InvalidOperationException("data source dispose failed"));
        using var _ = loggerFactory;

        await context.DisposeAsync();

        AssertLoggedFailure(logs, "data source dispose failed");
    }

    [Fact]
    public void SuspendSentinelsForDdl_SentinelDisposeFailure_IsLoggedAndStillSuspends()
    {
        var (context, _, logs, loggerFactory) = CreateFirebird();
        using var _ = loggerFactory;
        using var __ = context;
        context.RegisterSentinel(ThrowingSentinel(ConnectionState.Open).Object, ExecutionType.Write);

        Assert.True(context.SuspendSentinelsForDdl());

        AssertLoggedFailure(logs, "sentinel dispose failed");
    }

    [Fact]
    public void Dispose_PersistentConnectionDisposeFailure_IsLoggedNotThrown()
    {
        var (context, _, logs, loggerFactory) = CreateFirebird();
        using var _ = loggerFactory;
        context.RegisterSentinel(ThrowingSentinel(ConnectionState.Open).Object, ExecutionType.Write);

        context.Dispose();

        AssertLoggedFailure(logs, "sentinel dispose failed");
    }

    [Fact]
    public async Task DisposeAsync_PersistentConnectionDisposeFailure_IsLoggedNotThrown()
    {
        var (context, _, logs, loggerFactory) = CreateFirebird();
        using var _ = loggerFactory;
        context.RegisterSentinel(ThrowingSentinel(ConnectionState.Open).Object, ExecutionType.Write);

        await context.DisposeAsync();

        AssertLoggedFailure(logs, "sentinel dispose failed");
    }

    [Fact]
    public async Task SentinelRepair_BrokenSentinelDisposeFailure_IsLoggedAndRepairContinues()
    {
        var (context, _, logs, loggerFactory) = CreateFirebird();
        using var _ = loggerFactory;
        await using var __ = context;
        var broken = ThrowingSentinel(ConnectionState.Broken);
        context.RegisterSentinel(broken.Object, ExecutionType.Write);

        await using (var sc = context.CreateSqlContainer("SELECT 1"))
        {
            await sc.ExecuteScalarOrNullAsync<int>();
        }

        AssertLoggedFailure(logs, "sentinel dispose failed");
        Assert.DoesNotContain(context.GetSentinelSnapshot(), s => ReferenceEquals(s.Connection, broken.Object));
    }
}
