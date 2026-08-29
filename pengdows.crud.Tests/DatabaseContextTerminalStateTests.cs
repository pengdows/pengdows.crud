using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.metrics;
using Xunit;

namespace pengdows.crud.Tests;

// TEST-015: context terminal-state enforcement. Existing coverage
// (DatabaseContextGovernorDisposalTests) already proves CreateSqlContainer/GetConnection throw
// ObjectDisposedException when called AFTER disposal. The gap this fills: a container created
// BEFORE disposal, then EXECUTED after disposal — does the disposed-context guard actually fire
// before the execution path touches connections, admission accounting, or metrics, or does a
// pre-existing container quietly bypass the guard?
public class DatabaseContextTerminalStateTests
{
    private static DatabaseContext CreateContext()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=terminal-state;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            EnableMetrics = true
        };
        return new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_OnContainerCreatedBeforeDispose_ThrowsObjectDisposedException_NoAdmissionSideEffects()
    {
        var context = CreateContext();
        using var sc = context.CreateSqlContainer("INSERT INTO t (x) VALUES (1)");

        context.Dispose();

        var before = context.GetPoolStatisticsSnapshot(PoolLabel.Writer);

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => sc.ExecuteNonQueryAsync(CommandType.Text).AsTask());

        var after = context.GetPoolStatisticsSnapshot(PoolLabel.Writer);
        Assert.Equal(before.TotalAcquired, after.TotalAcquired);
        Assert.Equal(0, after.TotalAcquired);
    }

    [Fact]
    public async Task ExecuteReaderAsync_OnContainerCreatedBeforeDispose_ThrowsObjectDisposedException_NoAdmissionSideEffects()
    {
        var context = CreateContext();
        using var sc = context.CreateSqlContainer("SELECT 1");

        context.Dispose();

        var before = context.GetPoolStatisticsSnapshot(PoolLabel.Reader);

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => sc.ExecuteReaderAsync(ExecutionType.Read, CommandType.Text, default).AsTask());

        var after = context.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        Assert.Equal(before.TotalAcquired, after.TotalAcquired);
        Assert.Equal(0, after.TotalAcquired);
    }

    [Fact]
    public async Task ExecuteScalarOrNullAsync_OnContainerCreatedBeforeDispose_ThrowsObjectDisposedException()
    {
        var context = CreateContext();
        using var sc = context.CreateSqlContainer("SELECT 1");

        context.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => sc.ExecuteScalarOrNullAsync<int>(CommandType.Text).AsTask());
    }

    [Fact]
    public void BeginTransaction_AfterDispose_ThrowsObjectDisposedException()
    {
        var context = CreateContext();
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.BeginTransaction());
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_OnContainerCreatedBeforeDisposeAsync_ThrowsObjectDisposedException()
    {
        var context = CreateContext();
        using var sc = context.CreateSqlContainer("INSERT INTO t (x) VALUES (1)");

        await ((IAsyncDisposable)context).DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => sc.ExecuteNonQueryAsync(CommandType.Text).AsTask());
    }
}
