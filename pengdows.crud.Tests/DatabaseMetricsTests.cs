using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.metrics;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseMetricsTests
{
    [Fact]
    public async Task ExecuteNonQueryAsync_UpdatesMetricsOnSuccess()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            EnableMetrics = true
        };
        await using var context = new DatabaseContext(config, factory);
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

        // Add failingConnection BEFORE creating context
        var failingConnection = new fakeDbConnection();
        failingConnection.SetCommandFailure("SELECT 1", new TimeoutException("boom"));
        factory.Connections.Add(failingConnection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            EnableMetrics = true
        };
        await using var context = new DatabaseContext(config, factory);

        // Re-assert the failure after initialization probes to ensure the test command hits the timeout path.
        failingConnection.SetCommandFailure("SELECT 1", new TimeoutException("boom"));

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

        // Add connection with reader results BEFORE creating context
        var connection = new fakeDbConnection();
        connection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "value", 1 } },
            new Dictionary<string, object?> { { "value", 2 } }
        });
        factory.Connections.Add(connection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            EnableMetrics = true
        };
        await using var context = new DatabaseContext(config, factory);

        // Initialization probes consume queued results, so re-prime the connection for the actual command.
        connection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "value", 1 } },
            new Dictionary<string, object?> { { "value", 2 } }
        });

        var container = context.CreateSqlContainer("SELECT value FROM data");

        await using (var reader = await container.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                // Read all rows
            }
        }

        var metrics = context.Metrics;
        Assert.Equal(2, metrics.RowsReadTotal);
        Assert.Equal(1, metrics.CommandsExecuted);
    }

    [Fact]
    public async Task TransactionCommit_UpdatesMetrics()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            EnableMetrics = true
        };
        await using var context = new DatabaseContext(config, factory);
        await using (var tx = context.BeginTransaction())
        {
            await Task.Delay(10);
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
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            EnableMetrics = true
        };
        await using var context = new DatabaseContext(config, factory);
        await using (context.BeginTransaction())
        {
            await Task.Delay(5);
        }

        var metrics = context.Metrics;
        Assert.Equal(0, metrics.TransactionsActive);
        Assert.True(metrics.TransactionsMax >= 1);
    }

    [Fact]
    public async Task MetricsUpdated_EventRaisesLatestSnapshot()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        // Add connection with reader results BEFORE creating context
        var commandConnection = new fakeDbConnection();
        commandConnection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "value", 1 } }
        });
        factory.Connections.Add(commandConnection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            EnableMetrics = true
        };
        await using var context = new DatabaseContext(config, factory);

        // Initialization probes consume queued results, so re-prime the connection for the actual command.
        commandConnection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "value", 1 } }
        });

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
        var value = await container.ExecuteScalarAsync<int>();
        Assert.Equal(1, value);

        var snapshot = await signal.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(observed);
        Assert.True(snapshot.CommandsExecuted >= 1);
        Assert.True(Volatile.Read(ref invocations) >= 1);
    }

    [Fact]
    public async Task MetricsUpdated_UnsubscribedHandlerNotInvoked()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        // Add connection with reader results BEFORE creating context
        var commandConnection = new fakeDbConnection();
        commandConnection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "value", 1 } }
        });
        factory.Connections.Add(commandConnection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:",
            EnableMetrics = true
        };
        await using var context = new DatabaseContext(config, factory);

        // Initialization probes consume queued results, so re-prime the connection for the actual command.
        commandConnection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { { "value", 1 } }
        });

        var invoked = false;
        EventHandler<DatabaseMetrics>? handler = (_, _) => invoked = true;
        context.MetricsUpdated += handler;
        context.MetricsUpdated -= handler;

        var container = context.CreateSqlContainer("SELECT 1");
        var value = await container.ExecuteScalarAsync<int>();
        Assert.Equal(1, value);

        Assert.False(invoked);
    }

    [Fact]
    public void DatabaseMetrics_RecordExposesConstructorValues()
    {
        var metrics = new DatabaseMetrics(
            ConnectionsCurrent: 1,
            ConnectionsMax: 2,
            ConnectionsOpened: 3,
            ConnectionsClosed: 4,
            AvgConnectionHoldMs: 5,
            AvgConnectionOpenMs: 6,
            AvgConnectionCloseMs: 7,
            LongLivedConnections: 8,
            CommandsExecuted: 9,
            CommandsFailed: 10,
            CommandsTimedOut: 11,
            CommandsCancelled: 12,
            AvgCommandMs: 13,
            P95CommandMs: 14,
            P99CommandMs: 15,
            MaxParametersObserved: 16,
            RowsReadTotal: 17,
            RowsAffectedTotal: 18,
            PreparedStatements: 19,
            StatementsCached: 20,
            StatementsEvicted: 21,
            TransactionsActive: 22,
            TransactionsMax: 23,
            AvgTransactionMs: 24);

        Assert.Equal(1, metrics.ConnectionsCurrent);
        Assert.Equal(2, metrics.ConnectionsMax);
        Assert.Equal(3, metrics.ConnectionsOpened);
        Assert.Equal(4, metrics.ConnectionsClosed);
        Assert.Equal(5, metrics.AvgConnectionHoldMs);
        Assert.Equal(6, metrics.AvgConnectionOpenMs);
        Assert.Equal(7, metrics.AvgConnectionCloseMs);
        Assert.Equal(8, metrics.LongLivedConnections);
        Assert.Equal(9, metrics.CommandsExecuted);
        Assert.Equal(10, metrics.CommandsFailed);
        Assert.Equal(11, metrics.CommandsTimedOut);
        Assert.Equal(12, metrics.CommandsCancelled);
        Assert.Equal(13, metrics.AvgCommandMs);
        Assert.Equal(14, metrics.P95CommandMs);
        Assert.Equal(15, metrics.P99CommandMs);
        Assert.Equal(16, metrics.MaxParametersObserved);
        Assert.Equal(17, metrics.RowsReadTotal);
        Assert.Equal(18, metrics.RowsAffectedTotal);
        Assert.Equal(19, metrics.PreparedStatements);
        Assert.Equal(20, metrics.StatementsCached);
        Assert.Equal(21, metrics.StatementsEvicted);
        Assert.Equal(22, metrics.TransactionsActive);
        Assert.Equal(23, metrics.TransactionsMax);
        Assert.Equal(24, metrics.AvgTransactionMs);
    }
}
