using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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

        await using (var locker =
                     new RealAsyncLocker(semaphore, stats, DbMode.SingleConnection, TimeSpan.FromMilliseconds(100)))
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

        await using var holder =
            new RealAsyncLocker(semaphore, stats, DbMode.SingleConnection, TimeSpan.FromMilliseconds(250));
        await holder.LockAsync();

        // Use a local async function rather than Task.Run: LockAsyncSlow calls RecordWaitStart()
        // synchronously before its first await, so CurrentWaiters is already 1 by the time
        // WaiterTask() returns to the caller.  Task.Run defers execution to the thread pool and
        // can delay past the spin deadline when the process is under heavy test-runner load,
        // causing the holder to release before the waiter even starts (fast-path acquisition,
        // no contention recorded).
        async Task WaiterTask()
        {
            await using var locker = new RealAsyncLocker(semaphore, stats, DbMode.SingleConnection,
                TimeSpan.FromMilliseconds(250));
            await locker.LockAsync();
        }

        var waiter = WaiterTask();

        var spinDeadline = DateTime.UtcNow.AddMilliseconds(500);
        while (stats.GetSnapshot().CurrentWaiters == 0 && DateTime.UtcNow < spinDeadline)
        {
            await Task.Delay(1);
        }

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

        await using var holder =
            new RealAsyncLocker(semaphore, stats, DbMode.SingleConnection, TimeSpan.FromMilliseconds(25));
        await holder.LockAsync();

        var ex = await Assert.ThrowsAsync<ModeContentionException>(async () =>
        {
            await using var waiter =
                new RealAsyncLocker(semaphore, stats, DbMode.SingleConnection, TimeSpan.FromMilliseconds(25));
            await waiter.LockAsync();
        });

        Assert.Equal(DbMode.SingleConnection, ex.Mode);
        Assert.True(ex.Snapshot.TotalTimeouts >= 1);
    }
}
