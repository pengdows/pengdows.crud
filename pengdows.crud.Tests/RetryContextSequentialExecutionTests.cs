using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class RetryContextSequentialExecutionTests
{
    private static readonly RetryContextOptions FastOptions = new()
    {
        MaxAttempts = 3,
        BaseDelay = TimeSpan.Zero,
        MaxDelay = TimeSpan.Zero
    };

    private static DatabaseContext CreateContext(fakeDbFactory factory)
    {
        return new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
    }

    [Fact]
    public async Task StartAsync_ExecutesQueuedCommandsInFifoOrderAndDequeuesOnSuccess()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var first = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");
        using var second = rc.CreateSqlContainer("UPDATE \"t2\" SET \"x\" = 2");

        var conn1 = new fakeDbConnection();
        var conn2 = new fakeDbConnection();
        factory.Connections.Add(conn1);
        factory.Connections.Add(conn2);

        await rc.StartAsync();

        Assert.Equal(0, rc.QueuedCommandCount);
        Assert.Contains("UPDATE \"t1\" SET \"x\" = 1", conn1.ExecutedNonQueryTexts);
        Assert.Contains("UPDATE \"t2\" SET \"x\" = 2", conn2.ExecutedNonQueryTexts);
    }

    [Fact]
    public async Task StartAsync_RetriesTransientFailureInPlaceThenSucceeds()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");

        var failingConn = new fakeDbConnection();
        failingConn.SetNonQueryExecuteException(
            new DeadlockException("simulated deadlock", SupportedDatabase.Sqlite));
        var succeedingConn = new fakeDbConnection();
        factory.Connections.Add(failingConn);
        factory.Connections.Add(succeedingConn);

        await rc.StartAsync();

        Assert.Equal(0, rc.QueuedCommandCount);
        Assert.Contains("UPDATE \"t1\" SET \"x\" = 1", succeedingConn.ExecutedNonQueryTexts);
    }

    [Fact]
    public async Task StartAsync_NonTransientFailureStopsImmediatelyAndLeavesLaterCommandsUnexecuted()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var first = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");
        using var second = rc.CreateSqlContainer("UPDATE \"t2\" SET \"x\" = 2");

        var failingConn = new fakeDbConnection();
        failingConn.SetNonQueryExecuteException(
            new UniqueConstraintViolationException("dup key", SupportedDatabase.Sqlite, isTransient: false));
        factory.Connections.Add(failingConn);

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => rc.StartAsync().AsTask());

        // Neither command completed successfully, so nothing was dequeued, and the second command
        // must never have been attempted at all.
        Assert.Equal(2, rc.QueuedCommandCount);
    }

    [Fact]
    public async Task StartAsync_RetryBudgetExhaustedPropagatesTransientExceptionAndLeavesCommandQueued()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var options = new RetryContextOptions { MaxAttempts = 2, BaseDelay = TimeSpan.Zero, MaxDelay = TimeSpan.Zero };
        var rc = new RetryContext(ctx, RetryContextType.Sequential, options);

        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");

        var conn1 = new fakeDbConnection();
        conn1.SetNonQueryExecuteException(new DeadlockException("deadlock #1", SupportedDatabase.Sqlite));
        var conn2 = new fakeDbConnection();
        conn2.SetNonQueryExecuteException(new DeadlockException("deadlock #2", SupportedDatabase.Sqlite));
        factory.Connections.Add(conn1);
        factory.Connections.Add(conn2);

        await Assert.ThrowsAsync<DeadlockException>(() => rc.StartAsync().AsTask());

        Assert.Equal(1, rc.QueuedCommandCount);
    }

    [Fact]
    public async Task StartAsync_CancellationDuringBackoffPropagatesUnwrapped()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var options = new RetryContextOptions
        {
            MaxAttempts = 5,
            BaseDelay = TimeSpan.FromMilliseconds(300),
            MaxDelay = TimeSpan.FromMilliseconds(300)
        };
        var rc = new RetryContext(ctx, RetryContextType.Sequential, options);

        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");

        var failingConn = new fakeDbConnection();
        failingConn.SetNonQueryExecuteException(
            new DeadlockException("simulated deadlock", SupportedDatabase.Sqlite));
        factory.Connections.Add(failingConn);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => rc.StartAsync(cts.Token).AsTask());
    }

    [Fact]
    public async Task StartAsync_PoolSaturationExceptionIsNeverRetried()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=retrycontext-saturation;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            MaxConcurrentWrites = 1,
            PoolAcquireTimeout = TimeSpan.FromMilliseconds(150)
        };
        using var ctx = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");

        var held = ctx.GetConnection(ExecutionType.Write);
        try
        {
            await Assert.ThrowsAsync<PoolSaturatedException>(() => rc.StartAsync().AsTask());
            Assert.Equal(1, rc.QueuedCommandCount);
        }
        finally
        {
            ctx.CloseAndDisposeConnection(held);
        }
    }

    [Fact]
    public async Task StopAsync_CancelsAnInFlightBackoffWait()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var options = new RetryContextOptions
        {
            MaxAttempts = 5,
            BaseDelay = TimeSpan.FromMilliseconds(500),
            MaxDelay = TimeSpan.FromMilliseconds(500)
        };
        var rc = new RetryContext(ctx, RetryContextType.Sequential, options);

        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");

        var failingConn = new fakeDbConnection();
        failingConn.SetNonQueryExecuteException(
            new DeadlockException("simulated deadlock", SupportedDatabase.Sqlite));
        factory.Connections.Add(failingConn);

        var startTask = rc.StartAsync().AsTask();
        await Task.Delay(30);
        await rc.StopAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startTask);
    }

    [Fact]
    public async Task StartAsync_ThrowsWhenMaxElapsedTimeIsExhaustedBeforeAnAttemptCanStart()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var options = new RetryContextOptions
        {
            MaxAttempts = 10,
            BaseDelay = TimeSpan.FromMilliseconds(100),
            MaxDelay = TimeSpan.FromMilliseconds(100),
            MaxElapsedTime = TimeSpan.FromMilliseconds(30)
        };
        var rc = new RetryContext(ctx, RetryContextType.Sequential, options);

        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");

        // Only one connection is seeded: the first attempt fails transiently almost instantly
        // (well within the 30ms budget), but the 100ms backoff delay that follows blows past it.
        // The proactive check at the top of the *next* attempt must catch that before ever trying
        // to pull a second connection.
        var conn1 = new fakeDbConnection();
        conn1.SetNonQueryExecuteException(new DeadlockException("deadlock", SupportedDatabase.Sqlite));
        factory.Connections.Add(conn1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rc.StartAsync().AsTask());

        Assert.Equal(1, rc.QueuedCommandCount);
    }

    [Fact]
    public async Task StartAsync_BoundsASlowAttemptToTheRemainingMaxElapsedTimeBudget()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=retrycontext-hang;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        await using var ctx = new DatabaseContext(config, factory);
        var connection = factory.CreatedConnections.Single();

        var options = new RetryContextOptions
        {
            MaxAttempts = 5,
            BaseDelay = TimeSpan.Zero,
            MaxDelay = TimeSpan.Zero,
            MaxElapsedTime = TimeSpan.FromMilliseconds(80)
        };
        var rc = new RetryContext(ctx, RetryContextType.Sequential, options);
        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");

        // Never completed — the command execution hangs forever unless something bounds it.
        var gate = connection.SetExecuteGate();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rc.StartAsync().AsTask());

        Assert.Equal(1, rc.QueuedCommandCount);

        // Release the gate so the now-cancelled task's awaited continuation isn't left dangling.
        gate.TrySetResult(true);
    }
}
