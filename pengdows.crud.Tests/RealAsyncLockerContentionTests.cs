using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.metrics;
using pengdows.crud.threading;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class RealAsyncLockerContentionTests
{
    [Fact]
    public async Task LockAsync_Uncontended_DoesNotRecordWait()
    {
        var stats = new ModeContentionStats();
        var semaphore = new SemaphoreSlim(1, 1);

        await using (var locker = new RealAsyncLocker(semaphore, stats, DbMode.SingleConnection, TimeSpan.FromMilliseconds(100)))
        {
            await locker.LockAsync();
        }

        var snapshot = stats.GetSnapshot();
        Assert.Equal(0, snapshot.TotalWaits);
        Assert.Equal(0, snapshot.CurrentWaiters);
    }

    [Fact]
    public async Task LockAsync_Contended_RecordsWaitStats()
    {
        var stats = new ModeContentionStats();
        var semaphore = new SemaphoreSlim(1, 1);

        await using var holder = new RealAsyncLocker(semaphore, stats, DbMode.SingleConnection, TimeSpan.FromMilliseconds(250));
        await holder.LockAsync();

        var waiter = Task.Run(async () =>
        {
            await using var locker = new RealAsyncLocker(semaphore, stats, DbMode.SingleConnection, TimeSpan.FromMilliseconds(250));
            await locker.LockAsync();
        });

        await Task.Delay(25);
        await holder.DisposeAsync();
        await waiter;

        var snapshot = stats.GetSnapshot();
        Assert.True(snapshot.TotalWaits >= 1);
        Assert.True(snapshot.TotalWaitTimeTicks > 0);
        Assert.True(snapshot.PeakWaiters >= 1);
    }

    [Fact]
    public async Task LockAsync_WhenTimeout_ThrowsModeContentionException()
    {
        var stats = new ModeContentionStats();
        var semaphore = new SemaphoreSlim(1, 1);

        await using var holder = new RealAsyncLocker(semaphore, stats, DbMode.SingleConnection, TimeSpan.FromMilliseconds(25));
        await holder.LockAsync();

        var ex = await Assert.ThrowsAsync<ModeContentionException>(async () =>
        {
            await using var waiter = new RealAsyncLocker(semaphore, stats, DbMode.SingleConnection, TimeSpan.FromMilliseconds(25));
            await waiter.LockAsync();
        });

        Assert.Equal(DbMode.SingleConnection, ex.Mode);
        Assert.True(ex.Snapshot.TotalTimeouts >= 1);
    }
}

