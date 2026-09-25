using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextGovernorDisposalTests
{
    [Fact]
    public async Task DisposeAsync_WaitsForOutstandingLease_ThenDisposesGovernors()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ProviderName = "fake",
            PoolAcquireTimeout = TimeSpan.FromMilliseconds(250)
        };

        var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
        var readerGovernor = GetGovernor(context, "_readerGovernor");

        var connection = context.GetConnection(ExecutionType.Read);
        var disposeTask = context.DisposeAsync().AsTask();

        Assert.False(disposeTask.IsCompleted);

        connection.Dispose();
        await disposeTask;

        Assert.Throws<ObjectDisposedException>(() => readerGovernor.Acquire());
    }

    // CORE-025: DatabaseContext did not override ValidateCanCreateContainer, and its main
    // connection-acquisition entry point (GetStandardConnectionWithExecutionType) never called
    // ThrowIfDisposed() — combined with a disposed context's nulled-out governor fields making
    // AcquireSlot silently return an ungoverned default slot (see the AcquireSlot fix in
    // DatabaseContext.ConnectionLifecycle.cs), a container created either before or after
    // disposal could reach the provider and open a fresh physical connection completely outside
    // admission control instead of failing.
    [Fact]
    public void CreateSqlContainer_AfterDispose_ThrowsObjectDisposedException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("Server=test;Database=test;EmulatedProduct=SqlServer", factory);
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.CreateSqlContainer("SELECT 1"));
    }

    [Fact]
    public void GetConnection_AfterDispose_ThrowsObjectDisposedException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("Server=test;Database=test;EmulatedProduct=SqlServer", factory);
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.GetConnection(ExecutionType.Write));
    }

    [Fact]
    public async Task GetConnectionAsync_AfterDisposeAsync_ThrowsObjectDisposedException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("Server=test;Database=test;EmulatedProduct=SqlServer", factory);
        await context.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => context.GetConnection(ExecutionType.Read));
    }

    // BP-110(d) (3.0 CORE-026): on a governor drain timeout during disposal, DisposeManaged()
    // proceeded unconditionally to DisposeOwnedDataSources() even though the timeout means a
    // lease may still be genuinely outstanding — tearing down the data source that the leaked
    // connection still depends on. Leaked rather than corrupted is the safe default.
    [Fact]
    public void Dispose_GovernorDrainTimesOut_DoesNotDisposeOwnedDataSources()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer) { SupportsNativeDataSource = true };
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=drain-timeout;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ProviderName = "fake",
            PoolAcquireTimeout = TimeSpan.FromMilliseconds(50)
        };

        var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
        Assert.NotEmpty(factory.CreatedDataSources);

        // Hold a connection open so the writer governor cannot drain within PoolAcquireTimeout.
        var heldConnection = context.GetConnection(ExecutionType.Write);

        context.Dispose();

        Assert.All(factory.CreatedDataSources, source => Assert.False(source.WasDisposed));

        heldConnection.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_GovernorDrainTimesOut_DoesNotDisposeOwnedDataSources()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer) { SupportsNativeDataSource = true };
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=drain-timeout-async;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ProviderName = "fake",
            PoolAcquireTimeout = TimeSpan.FromMilliseconds(50)
        };

        var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
        Assert.NotEmpty(factory.CreatedDataSources);

        var heldConnection = context.GetConnection(ExecutionType.Write);

        await context.DisposeAsync();

        Assert.All(factory.CreatedDataSources, source => Assert.False(source.WasDisposed));

        heldConnection.Dispose();
    }

    [Fact]
    public void Dispose_GovernorsDrainCleanly_DisposesOwnedDataSources()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer) { SupportsNativeDataSource = true };
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=drain-clean;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ProviderName = "fake",
            PoolAcquireTimeout = TimeSpan.FromMilliseconds(50)
        };

        var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
        Assert.NotEmpty(factory.CreatedDataSources);

        context.Dispose();

        Assert.All(factory.CreatedDataSources, source => Assert.True(source.WasDisposed));
    }

    private static PoolGovernor GetGovernor(DatabaseContext context, string fieldName)
    {
        var field = typeof(DatabaseContext).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var governor = field!.GetValue(context) as PoolGovernor;
        Assert.NotNull(governor);
        return governor!;
    }
}
