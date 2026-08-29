using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextGovernorDisposalTests
{
    [Fact]
    public async Task DisposeAsync_WaitsForOutstandingLease_ThenDisposesGovernors()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ProviderName = "fake",
            // Also used as WaitForDrainAsync's deadline during disposal (DatabaseContext.
            // DisposeGovernorAfterDrain(Async)). This test's intent is to prove drain-then-dispose
            // ordering, not to race a tight timeout — a short value here flaked under CI's shared
            // runners: connection.Dispose() releases the slot synchronously, but the drain signal's
            // continuation still has to be scheduled, and heavy parallel-test thread-pool
            // contention occasionally pushed that scheduling past a tight deadline. When it does,
            // WaitForDrainAsync's TimeoutException is caught and logged (deliberately, to avoid
            // disposing the governor's semaphore while a lease might still be genuinely
            // outstanding — see the ReleaseToken ordering comment in PoolGovernor.cs) rather than
            // rethrown, so governor.Dispose() is silently skipped and the final
            // Assert.Throws<ObjectDisposedException> fails with no exception at all. Generous
            // headroom here costs nothing — the wait still completes almost instantly under normal
            // conditions since the lease is released moments before DisposeAsync is awaited.
            PoolAcquireTimeout = TimeSpan.FromSeconds(5)
        };

        var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
        var readerGovernor = GetGovernor(context, "_readerGovernor");

        var connection = context.GetConnection(ExecutionType.Read);
        var disposeTask = context.DisposeAsync().AsTask();

        Assert.False(disposeTask.IsCompleted);

        connection.Dispose();
        await disposeTask;

        Assert.Throws<ObjectDisposedException>(() => readerGovernor.Acquire());
    }

    // CORE-025: DatabaseContext did not override ValidateCanCreateContainer, and its main
    // connection-acquisition entry point (GetStandardConnectionWithExecutionType) never called
    // ThrowIfDisposed() — combined with a disposed context's nulled-out governor fields making
    // AcquireSlot silently return an ungoverned default slot (see the AcquireSlot fix in
    // DatabaseContext.ConnectionLifecycle.cs), a container created either before or after
    // disposal could reach the provider and open a fresh physical connection completely outside
    // admission control instead of failing.
    [Fact]
    public void CreateSqlContainer_AfterDispose_ThrowsObjectDisposedException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("Server=test;Database=test;EmulatedProduct=SqlServer", factory);
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.CreateSqlContainer("SELECT 1"));
    }

    [Fact]
    public void GetConnection_AfterDispose_ThrowsObjectDisposedException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("Server=test;Database=test;EmulatedProduct=SqlServer", factory);
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.GetConnection(ExecutionType.Write));
    }

    [Fact]
    public async Task GetConnectionAsync_AfterDisposeAsync_ThrowsObjectDisposedException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("Server=test;Database=test;EmulatedProduct=SqlServer", factory);
        await context.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => context.GetConnection(ExecutionType.Read));
    }

    private static PoolGovernor GetGovernor(DatabaseContext context, string fieldName)
    {
        var field = typeof(DatabaseContext).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var governor = field!.GetValue(context) as PoolGovernor;
        Assert.NotNull(governor);
        return governor!;
    }

    // TEST-016: races a brand-new acquisition attempt against an in-flight DisposeAsync that
    // still has to drain-wait for an existing outstanding lease. DatabaseContext's disposal
    // sequence calls governor.Close() (P0 batch, this file's PoolGovernorTests companion)
    // synchronously before awaiting the drain, so a racing acquisition attempt started any time
    // after DisposeAsync() has been invoked — even while the drain wait is still pending on the
    // held lease — must be rejected outright rather than being handed a "post-close" lease.
    [Fact]
    public async Task DisposeAsync_RacingWithNewAcquisitionAttempt_RejectsIt_NoPostCloseLeaseGranted()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=race-drain-boundary;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            MaxConcurrentReads = 2,
            PoolAcquireTimeout = TimeSpan.FromSeconds(5)
        };

        var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        // Hold one lease so the drain has something to actually wait for.
        var held = context.GetConnection(ExecutionType.Read);

        var disposeTask = context.DisposeAsync().AsTask();
        Assert.False(disposeTask.IsCompleted);

        // Race: attempt a brand-new acquisition while disposal is in-flight but the held lease
        // has not yet been released — governor.Close() must already have run by this point.
        Assert.Throws<ObjectDisposedException>(() => context.GetConnection(ExecutionType.Read));

        held.Dispose();
        await disposeTask;

        // The context must remain fully, terminally disposed afterward too — the racing attempt
        // must not have left it in some half-closed state.
        Assert.Throws<ObjectDisposedException>(() => context.GetConnection(ExecutionType.Read));
    }
}
