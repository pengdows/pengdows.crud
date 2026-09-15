using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// Sequential now wraps every command in its own transaction and only ever retries after a
// *confirmed* rollback (a rollback that itself fails closes with RetryOutcomeUnknownException
// instead) — so a confirmed rollback alone is sufficient proof nothing durably applied, regardless
// of the statement's shape. There is no more DELETE/[Version]-guarded-UPDATE-vs-everything-else
// distinction (the RetrySafety-based statement-shape gate this file's name refers to was removed
// once that reasoning stopped holding for the IdempotentViaUniqueConstraint case — see the class
// doc comment history, or git blame, for detail); every test below intentionally exercises the
// same rule across different statement shapes to confirm the rule really is shape-independent, not
// to imply that some shapes are still treated specially.
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
    public async Task StartAsync_UnrelatedV0ParameterStillRetriesConfirmedExecutionFailure()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer(
            "UPDATE \"t1\" SET \"counter\" = \"counter\" + @v0 WHERE \"id\" = @k0");
        sc.AddParameterWithValue("v0", DbType.Int32, 1);
        sc.AddParameterWithValue("k0", DbType.Int32, 1);

        var conn = new fakeDbConnection();
        conn.SetNonQueryExecuteException(new DeadlockException("simulated deadlock", SupportedDatabase.Sqlite));
        factory.Connections.Add(conn);

        await rc.StartAsync();
        Assert.Equal(0, rc.QueuedCommandCount);
    }

    [Fact]
    public async Task StartAsync_TransientFailureOnUnguardedUpdate_RetriesAfterConfirmedRollback()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1 WHERE \"id\" = 1");

        var failingConn = new fakeDbConnection();
        failingConn.SetNonQueryExecuteException(
            new DeadlockException("simulated deadlock", SupportedDatabase.Sqlite));
        factory.Connections.Add(failingConn);

        await rc.StartAsync();
        Assert.Equal(0, rc.QueuedCommandCount);
    }

    [Fact]
    public async Task StartAsync_TransientFailureOnBareInsert_RetriesAfterConfirmedRollback()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer("INSERT INTO \"t1\" (\"x\") VALUES (1)");

        var failingConn = new fakeDbConnection();
        failingConn.SetNonQueryExecuteException(
            new DeadlockException("simulated deadlock", SupportedDatabase.Sqlite));
        factory.Connections.Add(failingConn);

        await rc.StartAsync();
        Assert.Equal(0, rc.QueuedCommandCount);
    }

    // Sequential now wraps every command in its own transaction and only ever retries after a
    // *confirmed* rollback (RollbackOrThrowOutcomeUnknownAsync fails closed with
    // RetryOutcomeUnknownException if rollback itself can't be confirmed) — so a prior attempt's
    // execution genuinely cannot have durably landed: confirmed rollback means nothing persisted.
    // A UniqueConstraintViolationException on a later attempt can therefore only mean some other
    // writer inserted that value in the meantime, not "my own earlier attempt already committed
    // this." It must propagate as a real conflict, the same as on any other attempt.
    [Fact]
    public async Task StartAsync_UniqueConstraintViolationOnRetry_PropagatesAsGenuineConflict_NotTreatedAsSuccess()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer("INSERT INTO \"t1\" (\"idempotency_key\") VALUES ('abc')");

        var firstAttemptConn = new fakeDbConnection();
        firstAttemptConn.SetNonQueryExecuteException(
            new DeadlockException("simulated deadlock", SupportedDatabase.Sqlite));
        var retryConn = new fakeDbConnection();
        retryConn.SetNonQueryExecuteException(
            new UniqueConstraintViolationException("idempotency_key already exists", SupportedDatabase.Sqlite,
                constraintName: "UQ_idempotency_key"));
        factory.Connections.Add(firstAttemptConn);
        factory.Connections.Add(retryConn);

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => rc.StartAsync().AsTask());

        Assert.Equal(1, rc.QueuedCommandCount);
    }

}
