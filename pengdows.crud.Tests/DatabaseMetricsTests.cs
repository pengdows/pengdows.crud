using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseMetricsTests
{
    [Fact]
    public async Task ExecuteNonQueryAsync_UpdatesMetricsOnSuccess()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var container = context.CreateSqlContainer("UPDATE data SET value = @p1");
        container.AddParameterWithValue("p1", DbType.String, "value");

        var result = await container.ExecuteNonQueryAsync();

        Assert.Equal(1, result);
        var metrics = context.Metrics;
        Assert.Equal(1, metrics.CommandsExecuted);
        Assert.True(metrics.RowsAffectedTotal >= 1);
    }

    [Fact]
    public async Task ExecuteScalarAsync_TimeoutIsTrackedAsFailure()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var failingConnection = new fakeDbConnection();
        failingConnection.SetCommandFailure("SELECT 1", new TimeoutException("boom"));
        factory.Connections.Add(failingConnection);

        var container = context.CreateSqlContainer("SELECT 1");

        await Assert.ThrowsAsync<TimeoutException>(() => container.ExecuteScalarAsync<int>());

        var metrics = context.Metrics;
        Assert.Equal(1, metrics.CommandsFailed);
        Assert.Equal(1, metrics.CommandsTimedOut);
    }

    [Fact]
    public async Task ExecuteReaderAsync_TracksRowsRead()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = new fakeDbConnection();
        connection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "value", 1 } },
            new Dictionary<string, object?> { { "value", 2 } }
        });
        factory.Connections.Add(connection);

        await using var context = new DatabaseContext("Data Source=:memory:", factory);
        var container = context.CreateSqlContainer("SELECT value FROM data");
        await using var reader = await container.ExecuteReaderAsync().ConfigureAwait(false);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
        }

        var metrics = context.Metrics;
        Assert.Equal(2, metrics.RowsReadTotal);
        Assert.Equal(1, metrics.CommandsExecuted);
    }

    [Fact]
    public async Task TransactionCommit_UpdatesMetrics()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);

        await using (var tx = context.BeginTransaction())
        {
            await Task.Delay(10).ConfigureAwait(false);
            tx.Commit();
        }

        var metrics = context.Metrics;
        Assert.Equal(0, metrics.TransactionsActive);
        Assert.True(metrics.TransactionsMax >= 1);
    }

    [Fact]
    public async Task TransactionDisposeWithoutCommit_StillRecordsMetrics()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var context = new DatabaseContext("Data Source=:memory:", factory);

        await using (context.BeginTransaction())
        {
            await Task.Delay(5).ConfigureAwait(false);
        }

        var metrics = context.Metrics;
        Assert.Equal(0, metrics.TransactionsActive);
        Assert.True(metrics.TransactionsMax >= 1);
    }

    [Fact]
    public async Task MetricsUpdated_EventRaisesLatestSnapshot()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.Connections.Add(new fakeDbConnection());

        await using var context = new DatabaseContext("Data Source=:memory:", factory);

        var commandConnection = new fakeDbConnection();
        commandConnection.EnqueueScalarResult(1);
        factory.Connections.Add(commandConnection);

        DatabaseMetrics? observed = null;
        var invocations = 0;
        var signal = new TaskCompletionSource<DatabaseMetrics>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.MetricsUpdated += (_, metrics) =>
        {
            observed = metrics;
            Interlocked.Increment(ref invocations);
            signal.TrySetResult(metrics);
        };

        var container = context.CreateSqlContainer("SELECT 1");
        var value = await container.ExecuteScalarAsync<int>().ConfigureAwait(false);
        Assert.Equal(1, value);

        var snapshot = await signal.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        Assert.NotNull(observed);
        Assert.True(snapshot.CommandsExecuted >= 1);
        Assert.True(Volatile.Read(ref invocations) >= 1);
    }

    [Fact]
    public async Task MetricsUpdated_UnsubscribedHandlerNotInvoked()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.Connections.Add(new fakeDbConnection());

        await using var context = new DatabaseContext("Data Source=:memory:", factory);

        var commandConnection = new fakeDbConnection();
        commandConnection.EnqueueScalarResult(1);
        factory.Connections.Add(commandConnection);

        var invoked = false;
        EventHandler<DatabaseMetrics>? handler = (_, _) => invoked = true;
        context.MetricsUpdated += handler;
        context.MetricsUpdated -= handler;

        var container = context.CreateSqlContainer("SELECT 1");
        var value = await container.ExecuteScalarAsync<int>().ConfigureAwait(false);
        Assert.Equal(1, value);

        Assert.False(invoked);
    }
}
