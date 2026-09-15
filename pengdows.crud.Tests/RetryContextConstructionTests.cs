using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class RetryContextConstructionTests
{
    private static DatabaseContext CreateContext(fakeDbFactory factory)
    {
        return new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
    }

    [Fact]
    public void Constructor_ThrowsOnNullContext()
    {
        Assert.Throws<ArgumentNullException>(() => new RetryContext(null!, RetryContextType.Sequential));
    }

    [Fact]
    public async Task Constructor_ThrowsWhenGivenTransactionContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        using var txn = ctx.BeginTransaction();

        Assert.Throws<NotSupportedException>(() => new RetryContext(txn, RetryContextType.Sequential));
    }

    [Fact]
    public async Task CreateSqlContainer_QueuesWithoutExecuting()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);

        Assert.Equal(0, rc.QueuedCommandCount);

        using var sc = rc.CreateSqlContainer("UPDATE \"Customers\" SET \"Name\" = 'Ada'");

        Assert.Equal(1, rc.QueuedCommandCount);
        Assert.False(rc.IsStarted);
        // Nothing should have touched the connection factory yet — building a container is pure
        // SQL/parameter assembly, not execution.
        Assert.Empty(factory.Connections);
    }

    [Fact]
    public async Task CreateSqlContainer_ThrowsAfterStartAsyncCalled()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);

        await rc.StartAsync(); // empty queue — completes immediately

        Assert.True(rc.IsStarted);
        Assert.Throws<InvalidOperationException>(() => rc.CreateSqlContainer("SELECT 1"));
    }

    [Fact]
    public async Task StartAsync_ThrowsOnSecondCall()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);

        await rc.StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => rc.StartAsync().AsTask());
    }

    [Fact]
    public async Task StartAsync_EmptyQueue_CompletesImmediatelyAndMarksCompleted()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);

        Assert.False(rc.IsCompleted);
        await rc.StartAsync();
        Assert.True(rc.IsCompleted);
    }

    [Fact]
    public async Task PolicySetters_RejectChangesAfterStart()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);
        using var sc = rc.CreateSqlContainer("DELETE FROM \"t1\"");

        factory.Connections.Add(new fakeDbConnection());
        await rc.StartAsync();

        Assert.Throws<InvalidOperationException>(() => rc.SetRowCountPolicy(sc, RowCountPolicy.ExactlyOne));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveMaxAttempts(int maxAttempts)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = CreateContext(factory);

        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryContext(
            ctx, RetryContextType.Sequential, new RetryContextOptions { MaxAttempts = maxAttempts }));
    }

    [Fact]
    public void Constructor_RejectsNegativeTimingOptions()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = CreateContext(factory);

        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryContext(
            ctx, RetryContextType.Sequential, new RetryContextOptions
            {
                BaseDelay = TimeSpan.FromMilliseconds(-1)
            }));
    }

    [Fact]
    public void Constructor_RejectsNegativeMaxDelay()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = CreateContext(factory);

        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryContext(
            ctx, RetryContextType.Sequential, new RetryContextOptions
            {
                MaxDelay = TimeSpan.FromMilliseconds(-1)
            }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveMaxElapsedTime(int milliseconds)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = CreateContext(factory);

        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryContext(
            ctx, RetryContextType.Sequential, new RetryContextOptions
            {
                MaxElapsedTime = TimeSpan.FromMilliseconds(milliseconds)
            }));
    }

    [Fact]
    public async Task StartAsync_RejectsUnknownRetryContextTypeAndLeavesPlanQueued()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, (RetryContextType)99);
        using var sc = rc.CreateSqlContainer("DELETE FROM \"t1\"");

        await Assert.ThrowsAsync<NotSupportedException>(() => rc.StartAsync().AsTask());

        Assert.True(rc.IsStarted);
        Assert.True(rc.IsCompleted);
        Assert.Equal(1, rc.QueuedCommandCount);
    }

    [Fact]
    public async Task StartAsync_AlreadyCancelledTokenCompletesAsCancelled()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);
        using var sc = rc.CreateSqlContainer("DELETE FROM \"t1\"");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rc.StartAsync(cts.Token).AsTask());

        Assert.Equal(1, rc.QueuedCommandCount);
        Assert.True(rc.IsCompleted);
    }
}
