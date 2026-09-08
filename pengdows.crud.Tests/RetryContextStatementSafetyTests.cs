using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class RetryContextStatementSafetyTests
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
    public async Task StartAsync_TransientFailureOnDelete_RetriesAutomatically()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer("DELETE FROM \"t1\"");

        var failingConn = new fakeDbConnection();
        failingConn.SetNonQueryExecuteException(
            new DeadlockException("simulated deadlock", SupportedDatabase.Sqlite));
        var succeedingConn = new fakeDbConnection();
        factory.Connections.Add(failingConn);
        factory.Connections.Add(succeedingConn);

        await rc.StartAsync();

        Assert.Equal(0, rc.QueuedCommandCount);
    }

    [Fact]
    public async Task StartAsync_TransientFailureOnVersionGuardedUpdate_RetriesAutomatically()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer(
            "UPDATE \"t1\" SET \"x\" = @i0 WHERE \"id\" = @k0 AND \"version\" = @v0");
        sc.AddParameterWithValue("i0", DbType.Int32, 1);
        sc.AddParameterWithValue("k0", DbType.Int32, 1);
        sc.AddParameterWithValue("v0", DbType.Int32, 1);

        var failingConn = new fakeDbConnection();
        failingConn.SetNonQueryExecuteException(
            new DeadlockException("simulated deadlock", SupportedDatabase.Sqlite));
        var succeedingConn = new fakeDbConnection();
        factory.Connections.Add(failingConn);
        factory.Connections.Add(succeedingConn);

        await rc.StartAsync();

        Assert.Equal(0, rc.QueuedCommandCount);
    }

    [Fact]
    public async Task StartAsync_TransientFailureOnUnguardedUpdate_FailsClosedWithRetryOutcomeUnknown()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1 WHERE \"id\" = 1");

        var failingConn = new fakeDbConnection();
        failingConn.SetNonQueryExecuteException(
            new DeadlockException("simulated deadlock", SupportedDatabase.Sqlite));
        factory.Connections.Add(failingConn);

        var ex = await Assert.ThrowsAsync<RetryOutcomeUnknownException>(() => rc.StartAsync().AsTask());

        Assert.Equal(1, ex.Attempt);
        Assert.IsType<DeadlockException>(ex.InnerException);
        // Never dequeued: RetryContext refused to guess whether the failed attempt applied.
        Assert.Equal(1, rc.QueuedCommandCount);
    }

    [Fact]
    public async Task StartAsync_TransientFailureOnBareInsert_FailsClosedWithRetryOutcomeUnknown()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer("INSERT INTO \"t1\" (\"x\") VALUES (1)");

        var failingConn = new fakeDbConnection();
        failingConn.SetNonQueryExecuteException(
            new DeadlockException("simulated deadlock", SupportedDatabase.Sqlite));
        factory.Connections.Add(failingConn);

        await Assert.ThrowsAsync<RetryOutcomeUnknownException>(() => rc.StartAsync().AsTask());

        Assert.Equal(1, rc.QueuedCommandCount);
    }

    [Fact]
    public async Task StartAsync_IdempotentInsert_TreatsUniqueConstraintViolationOnRetryAsSuccess()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer("INSERT INTO \"t1\" (\"idempotency_key\") VALUES ('abc')");
        rc.SetRetrySafety(sc, RetrySafety.IdempotentViaUniqueConstraint);

        // First attempt fails transiently (but, per the idempotency-key declaration, may have
        // already landed server-side). Second attempt's retry hits the row the first attempt
        // actually inserted, reported as a unique-constraint violation on the idempotency key —
        // proof of success, not a real conflict.
        var firstAttemptConn = new fakeDbConnection();
        firstAttemptConn.SetNonQueryExecuteException(
            new DeadlockException("simulated deadlock", SupportedDatabase.Sqlite));
        var retryConn = new fakeDbConnection();
        retryConn.SetNonQueryExecuteException(
            new UniqueConstraintViolationException("idempotency_key already exists", SupportedDatabase.Sqlite));
        factory.Connections.Add(firstAttemptConn);
        factory.Connections.Add(retryConn);

        await rc.StartAsync();

        Assert.Equal(0, rc.QueuedCommandCount);
    }

    [Fact]
    public async Task StartAsync_IdempotentInsert_GenuineConstraintViolationOnFirstAttemptStillPropagates()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer("INSERT INTO \"t1\" (\"idempotency_key\") VALUES ('abc')");
        rc.SetRetrySafety(sc, RetrySafety.IdempotentViaUniqueConstraint);

        // No prior transient failure preceded this — attempt 1 hitting the constraint means a
        // real, pre-existing conflict (e.g. a genuine duplicate caller request), not confirmation
        // of this RetryContext's own earlier attempt. Must propagate, not be swallowed.
        var conn = new fakeDbConnection();
        conn.SetNonQueryExecuteException(
            new UniqueConstraintViolationException("idempotency_key already exists", SupportedDatabase.Sqlite));
        factory.Connections.Add(conn);

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => rc.StartAsync().AsTask());

        Assert.Equal(1, rc.QueuedCommandCount);
    }

    [Fact]
    public void SetRetrySafety_ThrowsForContainerNotInThisContextsQueue()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);
        using var foreignContainer = ctx.CreateSqlContainer("SELECT 1");

        Assert.Throws<ArgumentException>(
            () => rc.SetRetrySafety(foreignContainer, RetrySafety.IdempotentViaUniqueConstraint));
    }
}
