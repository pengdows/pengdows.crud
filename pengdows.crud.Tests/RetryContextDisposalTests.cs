using System;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class RetryContextDisposalTests
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
    public async Task Dispose_DisposesQueuedContainersThatWereNeverExecuted()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);

        var first = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");
        var second = rc.CreateSqlContainer("UPDATE \"t2\" SET \"x\" = 2");

        rc.Dispose();

        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_DisposesQueuedContainersThatWereNeverExecuted()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential);

        var first = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");

        await rc.DisposeAsync();

        Assert.True(first.IsDisposed);
    }

    [Fact]
    public async Task Dispose_AfterSuccessfulStartAsync_StillDisposesTheExecutedContainer()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        await using var ctx = CreateContext(factory);
        var rc = new RetryContext(ctx, RetryContextType.Sequential, FastOptions);

        var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");
        factory.Connections.Add(new fakeDbConnection());

        await rc.StartAsync();

        // RetryContext owns every container it ever created, not just the currently-queued ones —
        // disposal happens when RetryContext itself is disposed, not the moment an attempt
        // succeeds.
        Assert.False(sc.IsDisposed);

        rc.Dispose();

        Assert.True(sc.IsDisposed);
    }

    [Fact]
    public async Task Dispose_WhileStartAsyncIsStillRunning_ThrowsAndLeavesTheInFlightAttemptUncorrupted()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=retrycontext-dispose-while-running;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        await using var ctx = new DatabaseContext(config, factory);
        var connection = factory.CreatedConnections.Single();

        var rc = new RetryContext(ctx, RetryContextType.Sequential);
        using var sc = rc.CreateSqlContainer("UPDATE \"t1\" SET \"x\" = 1");

        var gate = connection.SetExecuteGate(); // hangs the attempt indefinitely until released

        var startTask = rc.StartAsync().AsTask();
        await Task.Delay(30); // let StartAsync actually reach the gated await

        try
        {
            Assert.Throws<InvalidOperationException>(() => rc.Dispose());
            Assert.True(rc.IsDisposed);
        }
        finally
        {
            // However the assertions above land, the gate must be released so the in-flight
            // attempt (and this context's SingleConnection teardown) doesn't hang the test run.
            gate.SetResult(true);
        }

        // The aborted Dispose must not have touched the queue/containers — the in-flight attempt
        // should complete normally, not throw or hang, once the gate is released.
        await startTask;

        Assert.Equal(0, rc.QueuedCommandCount);
    }
}
