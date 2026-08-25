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

    private static PoolGovernor GetGovernor(DatabaseContext context, string fieldName)
    {
        var field = typeof(DatabaseContext).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var governor = field!.GetValue(context) as PoolGovernor;
        Assert.NotNull(governor);
        return governor!;
    }
}
