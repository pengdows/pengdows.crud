using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-029: the serialized-open lock (DuckDB) was one ReusableAsyncLocker shared by every caller,
/// and its "held" flag belongs to the instance. A caller whose wait was cancelled then disposed
/// its lock and released the semaphore held by another caller, letting a third open concurrently.
/// </summary>
public sealed class ConnectionOpenLockSharingTests
{
    [Fact]
    public async Task CancelledWaiter_DisposingItsLock_DoesNotReleaseTheHoldersLock()
    {
        await using var ctx = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=open-lock.duckdb;EmulatedProduct=DuckDB",
            DbMode = DbMode.Standard
        }, new fakeDbFactory(SupportedDatabase.DuckDB));

        var holder = ctx.GetConnectionOpenLock();
        await holder.LockAsync();

        var waiter = ctx.GetConnectionOpenLock();
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiter.LockAsync(cts.Token));
        }

        await waiter.DisposeAsync();

        var third = ctx.GetConnectionOpenLock();
        Assert.False(await third.TryLockAsync(TimeSpan.FromMilliseconds(50)),
            "A third caller got the open lock while the first still held it.");

        await holder.DisposeAsync();
        Assert.True(await third.TryLockAsync(TimeSpan.FromSeconds(5)));
        await third.DisposeAsync();
    }
}
