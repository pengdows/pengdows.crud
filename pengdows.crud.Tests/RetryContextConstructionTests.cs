using System;
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
}
