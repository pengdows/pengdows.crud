using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class RetryContextTransactionalExecutionTests
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
    public async Task StartAsync_AllCommandsSucceed_CommitsOnce()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Transactional, FastOptions);

        using var first = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");
        using var second = rc.CreateSqlContainer("UPDATE \"t2\" SET \"x\" = 2");

        var conn = new fakeDbConnection();
        factory.Connections.Add(conn);

        await rc.StartAsync();

        Assert.Contains("UPDATE \"t1\" SET \"x\" = 1", conn.ExecutedNonQueryTexts);
        Assert.Contains("UPDATE \"t2\" SET \"x\" = 2", conn.ExecutedNonQueryTexts);
        Assert.NotNull(conn.LastTransaction);
        Assert.Equal(1, conn.LastTransaction!.CommitCallCount);
        Assert.Equal(0, conn.LastTransaction!.RollbackCallCount);
    }

    [Fact]
    public async Task StartAsync_TransientFailure_RollsBackThenRetriesEntireQueueAndSucceeds()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Transactional, FastOptions);

        using var first = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");
        using var second = rc.CreateSqlContainer("UPDATE \"t2\" SET \"x\" = 2");

        var failingConn = new fakeDbConnection();
        failingConn.SetNonQueryExecuteException(
            new DeadlockException("simulated deadlock", SupportedDatabase.Sqlite));
        var succeedingConn = new fakeDbConnection();
        factory.Connections.Add(failingConn);
        factory.Connections.Add(succeedingConn);

        await rc.StartAsync();

        // The first attempt's transaction rolled back rather than committed, and never even
        // reached the second command (t2) — the second attempt re-ran the whole set from scratch
        // against a fresh connection/transaction.
        Assert.NotNull(failingConn.LastTransaction);
        Assert.Equal(0, failingConn.LastTransaction!.CommitCallCount);
        Assert.Equal(1, failingConn.LastTransaction!.RollbackCallCount);
        Assert.DoesNotContain("UPDATE \"t2\" SET \"x\" = 2", failingConn.ExecutedNonQueryTexts);

        Assert.Contains("UPDATE \"t1\" SET \"x\" = 1", succeedingConn.ExecutedNonQueryTexts);
        Assert.Contains("UPDATE \"t2\" SET \"x\" = 2", succeedingConn.ExecutedNonQueryTexts);
        Assert.Equal(1, succeedingConn.LastTransaction!.CommitCallCount);
    }

    [Fact]
    public async Task StartAsync_NonTransientFailure_RollsBackAndPropagatesImmediately()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Transactional, FastOptions);

        using var first = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");
        using var second = rc.CreateSqlContainer("UPDATE \"t2\" SET \"x\" = 2");

        var failingConn = new fakeDbConnection();
        failingConn.SetNonQueryExecuteException(
            new UniqueConstraintViolationException("dup key", SupportedDatabase.Sqlite, isTransient: false));
        factory.Connections.Add(failingConn);

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => rc.StartAsync().AsTask());

        Assert.NotNull(failingConn.LastTransaction);
        Assert.Equal(0, failingConn.LastTransaction!.CommitCallCount);
        Assert.Equal(1, failingConn.LastTransaction!.RollbackCallCount);
        Assert.DoesNotContain("UPDATE \"t2\" SET \"x\" = 2", failingConn.ExecutedNonQueryTexts);
    }

    [Fact]
    public async Task StartAsync_RetryBudgetExhausted_PropagatesTransientExceptionAfterFinalRollback()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var options = new RetryContextOptions { MaxAttempts = 2, BaseDelay = TimeSpan.Zero, MaxDelay = TimeSpan.Zero };
        var rc = new RetryContext(ctx, RetryContextType.Transactional, options);

        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");

        var conn1 = new fakeDbConnection();
        conn1.SetNonQueryExecuteException(new DeadlockException("deadlock #1", SupportedDatabase.Sqlite));
        var conn2 = new fakeDbConnection();
        conn2.SetNonQueryExecuteException(new DeadlockException("deadlock #2", SupportedDatabase.Sqlite));
        factory.Connections.Add(conn1);
        factory.Connections.Add(conn2);

        await Assert.ThrowsAsync<DeadlockException>(() => rc.StartAsync().AsTask());

        Assert.Equal(1, conn1.LastTransaction!.RollbackCallCount);
        Assert.Equal(1, conn2.LastTransaction!.RollbackCallCount);
    }

    [Fact]
    public async Task StartAsync_RowCountPolicyViolation_RollsBackAndPropagatesImmediately()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Transactional, FastOptions);

        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");
        rc.SetRowCountPolicy(sc, RowCountPolicy.ExactlyOne);

        var conn = new fakeDbConnection();
        conn.EnqueueNonQueryResult(0);
        factory.Connections.Add(conn);

        await Assert.ThrowsAsync<RowCountPolicyViolationException>(() => rc.StartAsync().AsTask());

        Assert.NotNull(conn.LastTransaction);
        Assert.Equal(0, conn.LastTransaction!.CommitCallCount);
        Assert.Equal(1, conn.LastTransaction!.RollbackCallCount);
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
        var rc = new RetryContext(ctx, RetryContextType.Transactional, options);

        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");

        var failingConn = new fakeDbConnection();
        failingConn.SetNonQueryExecuteException(
            new DeadlockException("simulated deadlock", SupportedDatabase.Sqlite));
        factory.Connections.Add(failingConn);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => rc.StartAsync(cts.Token).AsTask());
    }
}
