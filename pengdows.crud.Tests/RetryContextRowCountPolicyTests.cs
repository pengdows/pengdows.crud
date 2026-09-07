using System;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class RetryContextRowCountPolicyTests
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
    public async Task SetRowCountPolicy_ThrowsForContainerNotInThisContextsQueue()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);
        using var foreignContainer = ctx.CreateSqlContainer("SELECT 1");

        Assert.Throws<ArgumentException>(() => rc.SetRowCountPolicy(foreignContainer, RowCountPolicy.AtLeastOne));
    }

    [Fact]
    public async Task StartAsync_DefaultIgnorePolicyDoesNotValidateZeroRowsAffected()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer("DELETE FROM \"t1\" WHERE \"id\" = 1");
        // No SetRowCountPolicy call — default is Ignore.

        var conn = new fakeDbConnection();
        conn.EnqueueNonQueryResult(0);
        factory.Connections.Add(conn);

        await rc.StartAsync();

        Assert.Equal(0, rc.QueuedCommandCount);
    }

    [Fact]
    public async Task StartAsync_AtLeastOneThrowsOnZeroRowsAffectedAndLeavesCommandQueued()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1 WHERE \"id\" = 1");
        rc.SetRowCountPolicy(sc, RowCountPolicy.AtLeastOne);

        var conn = new fakeDbConnection();
        conn.EnqueueNonQueryResult(0);
        factory.Connections.Add(conn);

        var ex = await Assert.ThrowsAsync<RowCountPolicyViolationException>(() => rc.StartAsync().AsTask());

        Assert.Equal(RowCountPolicy.AtLeastOne, ex.Policy);
        Assert.Equal(0, ex.ActualRowsAffected);
        Assert.Equal(1, rc.QueuedCommandCount);
    }

    [Fact]
    public async Task StartAsync_ExactlyOneThrowsWhenMoreThanOneRowAffected()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");
        rc.SetRowCountPolicy(sc, RowCountPolicy.ExactlyOne);

        var conn = new fakeDbConnection();
        conn.EnqueueNonQueryResult(2);
        factory.Connections.Add(conn);

        var ex = await Assert.ThrowsAsync<RowCountPolicyViolationException>(() => rc.StartAsync().AsTask());

        Assert.Equal(RowCountPolicy.ExactlyOne, ex.Policy);
        Assert.Equal(2, ex.ActualRowsAffected);
    }

    [Fact]
    public async Task StartAsync_ExactlyOneSucceedsWithOneRowAffected()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");
        rc.SetRowCountPolicy(sc, RowCountPolicy.ExactlyOne);

        var conn = new fakeDbConnection();
        conn.EnqueueNonQueryResult(1);
        factory.Connections.Add(conn);

        await rc.StartAsync();

        Assert.Equal(0, rc.QueuedCommandCount);
    }

    [Fact]
    public async Task StartAsync_RowCountPolicyViolationIsNotRetried()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");
        rc.SetRowCountPolicy(sc, RowCountPolicy.AtLeastOne);

        // Only one connection is seeded. If the violation were mistakenly retried, the loop would
        // need a second connection (fakeDbFactory would auto-create one, returning 1 row and
        // masking the bug) — so we additionally assert the DML ran exactly once on this
        // connection (a session-setup PRAGMA also lands in ExecutedNonQueryTexts, so filter to the
        // actual command text rather than asserting the list has exactly one entry).
        var conn = new fakeDbConnection();
        conn.EnqueueNonQueryResult(0);
        factory.Connections.Add(conn);

        await Assert.ThrowsAsync<RowCountPolicyViolationException>(() => rc.StartAsync().AsTask());

        Assert.Single(conn.ExecutedNonQueryTexts, t => t.Contains("UPDATE \"t1\""));
    }
}
