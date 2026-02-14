using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.threading;
using Xunit;

namespace pengdows.crud.Tests;

public class ReusableAsyncLockerTests
{
    // ---------------------------------------------------------------
    // Constructor
    // ---------------------------------------------------------------

    [Fact]
    public void Constructor_NullSemaphore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ReusableAsyncLocker(null!));
    }

    [Fact]
    public void Constructor_ValidSemaphore_DoesNotThrow()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);
        Assert.NotNull(locker);
    }

    // ---------------------------------------------------------------
    // Lock (synchronous)
    // ---------------------------------------------------------------

    [Fact]
    public void Lock_Uncontended_AcquiresAndReleasesViaSyncDispose()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        locker.Lock();
        Assert.Equal(0, sem.CurrentCount); // held

        locker.Dispose();
        Assert.Equal(1, sem.CurrentCount); // released
    }

    [Fact]
    public async Task Lock_Contended_BlocksUntilReleased()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        // Take the semaphore externally to force contention
        sem.Wait();
        Assert.Equal(0, sem.CurrentCount);

        var acquired = false;
        var lockTask = Task.Run(() =>
        {
            locker.Lock();
            acquired = true;
        });

        // Give the task time to block
        await Task.Delay(50);
        Assert.False(acquired);

        // Release the external hold
        sem.Release();
        await lockTask;
        Assert.True(acquired);

        // Clean up
        locker.Dispose();
        Assert.Equal(1, sem.CurrentCount);
    }

    [Fact]
    public void Lock_ReusableAfterDispose()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        // First cycle
        locker.Lock();
        Assert.Equal(0, sem.CurrentCount);
        locker.Dispose();
        Assert.Equal(1, sem.CurrentCount);

        // Second cycle — reuse the same locker
        locker.Lock();
        Assert.Equal(0, sem.CurrentCount);
        locker.Dispose();
        Assert.Equal(1, sem.CurrentCount);
    }

    // ---------------------------------------------------------------
    // LockAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task LockAsync_Uncontended_AcquiresImmediately()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        await locker.LockAsync();
        Assert.Equal(0, sem.CurrentCount);

        await locker.DisposeAsync();
        Assert.Equal(1, sem.CurrentCount);
    }

    [Fact]
    public async Task LockAsync_CancelledToken_ThrowsTaskCanceledException()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => locker.LockAsync(cts.Token));

        // Semaphore should still be available — lock was never acquired
        Assert.Equal(1, sem.CurrentCount);
    }

    [Fact]
    public async Task LockAsync_CancelledDuringWait_ThrowsOperationCanceled()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);
        var cts = new CancellationTokenSource();

        // Exhaust the semaphore to force slow path
        sem.Wait();

        var lockTask = locker.LockAsync(cts.Token);

        // Give the task time to enter wait
        await Task.Delay(50);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lockTask);

        // Release the external hold
        sem.Release();
    }

    [Fact]
    public async Task LockAsync_Contended_WaitsUntilReleased()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        // Exhaust semaphore externally
        await sem.WaitAsync();

        var acquired = false;
        var lockTask = Task.Run(async () =>
        {
            await locker.LockAsync();
            acquired = true;
        });

        await Task.Delay(50);
        Assert.False(acquired);

        sem.Release();
        await lockTask;
        Assert.True(acquired);

        await locker.DisposeAsync();
        Assert.Equal(1, sem.CurrentCount);
    }

    [Fact]
    public async Task LockAsync_ReusableAcrossMultipleAwaitUsingCycles()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        for (var i = 0; i < 5; i++)
        {
            await locker.LockAsync();
            Assert.Equal(0, sem.CurrentCount);
            await locker.DisposeAsync();
            Assert.Equal(1, sem.CurrentCount);
        }
    }

    // ---------------------------------------------------------------
    // TryLockAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task TryLockAsync_Uncontended_ReturnsTrue()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        var result = await locker.TryLockAsync(TimeSpan.FromSeconds(1));
        Assert.True(result);
        Assert.Equal(0, sem.CurrentCount);

        await locker.DisposeAsync();
        Assert.Equal(1, sem.CurrentCount);
    }

    [Fact]
    public async Task TryLockAsync_TimesOut_ReturnsFalse()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        // Exhaust semaphore
        sem.Wait();

        var result = await locker.TryLockAsync(TimeSpan.FromMilliseconds(50));
        Assert.False(result);

        // Semaphore still held externally
        Assert.Equal(0, sem.CurrentCount);
        sem.Release();
    }

    [Fact]
    public async Task TryLockAsync_CancelledToken_ThrowsTaskCanceledException()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => locker.TryLockAsync(TimeSpan.FromSeconds(1), cts.Token));

        Assert.Equal(1, sem.CurrentCount);
    }

    [Fact]
    public async Task TryLockAsync_CancelledDuringWait_ThrowsOperationCanceled()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);
        var cts = new CancellationTokenSource();

        // Exhaust semaphore to force slow path
        sem.Wait();

        var tryTask = locker.TryLockAsync(TimeSpan.FromSeconds(30), cts.Token);

        await Task.Delay(50);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tryTask);

        sem.Release();
    }

    [Fact]
    public async Task TryLockAsync_Contended_AcquiresAfterRelease()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        // Exhaust semaphore
        await sem.WaitAsync();

        var tryTask = Task.Run(async () =>
            await locker.TryLockAsync(TimeSpan.FromSeconds(5)));

        await Task.Delay(50);
        sem.Release();

        var result = await tryTask;
        Assert.True(result);

        await locker.DisposeAsync();
        Assert.Equal(1, sem.CurrentCount);
    }

    // ---------------------------------------------------------------
    // Dispose / DisposeAsync — release semantics
    // ---------------------------------------------------------------

    [Fact]
    public void Dispose_WithoutLock_DoesNotDoubleFree()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        // Dispose without ever locking — should be a no-op
        locker.Dispose();
        Assert.Equal(1, sem.CurrentCount); // still 1, no extra release
    }

    [Fact]
    public async Task DisposeAsync_WithoutLock_DoesNotDoubleFree()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        await locker.DisposeAsync();
        Assert.Equal(1, sem.CurrentCount);
    }

    [Fact]
    public void Dispose_MultipleTimes_ReleasesOnlyOnce()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        locker.Lock();
        Assert.Equal(0, sem.CurrentCount);

        locker.Dispose();
        Assert.Equal(1, sem.CurrentCount);

        // Second dispose should not double-release
        locker.Dispose();
        Assert.Equal(1, sem.CurrentCount);
    }

    [Fact]
    public async Task DisposeAsync_MultipleTimes_ReleasesOnlyOnce()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        await locker.LockAsync();
        Assert.Equal(0, sem.CurrentCount);

        await locker.DisposeAsync();
        Assert.Equal(1, sem.CurrentCount);

        await locker.DisposeAsync();
        Assert.Equal(1, sem.CurrentCount);
    }

    // ---------------------------------------------------------------
    // TrackDisposeState = false (IsDisposed always false)
    // ---------------------------------------------------------------

    [Fact]
    public void IsDisposed_AlwaysFalse_BecauseTrackDisposeStateIsFalse()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        Assert.False(locker.IsDisposed);

        locker.Lock();
        locker.Dispose();

        // Still false — TrackDisposeState = false
        Assert.False(locker.IsDisposed);
    }

    // ---------------------------------------------------------------
    // Reuse across many cycles (simulates TransactionContext pattern)
    // ---------------------------------------------------------------

    [Fact]
    public async Task ReusableAcrossManyCycles_SimulatesTransactionPattern()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        for (var i = 0; i < 100; i++)
        {
            await locker.LockAsync();
            Assert.Equal(0, sem.CurrentCount);
            await locker.DisposeAsync();
            Assert.Equal(1, sem.CurrentCount);
        }
    }

    [Fact]
    public void ReusableAcrossManySyncCycles()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        for (var i = 0; i < 100; i++)
        {
            locker.Lock();
            Assert.Equal(0, sem.CurrentCount);
            locker.Dispose();
            Assert.Equal(1, sem.CurrentCount);
        }
    }

    // ---------------------------------------------------------------
    // Mixed sync/async usage
    // ---------------------------------------------------------------

    [Fact]
    public async Task MixedSyncAndAsyncUsage()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        // Sync lock, async dispose
        locker.Lock();
        Assert.Equal(0, sem.CurrentCount);
        await locker.DisposeAsync();
        Assert.Equal(1, sem.CurrentCount);

        // Async lock, sync dispose
        await locker.LockAsync();
        Assert.Equal(0, sem.CurrentCount);
        locker.Dispose();
        Assert.Equal(1, sem.CurrentCount);

        // TryLock, async dispose
        var acquired = await locker.TryLockAsync(TimeSpan.FromSeconds(1));
        Assert.True(acquired);
        Assert.Equal(0, sem.CurrentCount);
        await locker.DisposeAsync();
        Assert.Equal(1, sem.CurrentCount);
    }

    // ---------------------------------------------------------------
    // Concurrent access (stress test)
    // ---------------------------------------------------------------

    [Fact]
    public async Task ConcurrentLockAsync_SerializesAccess()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);
        var counter = 0;
        var errors = 0;

        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            for (var i = 0; i < 50; i++)
            {
                await locker.LockAsync();
                try
                {
                    var before = counter;
                    counter++;
                    // If serialization fails, another thread could increment between read and write
                    if (counter != before + 1)
                    {
                        Interlocked.Increment(ref errors);
                    }
                }
                finally
                {
                    await locker.DisposeAsync();
                }
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(0, errors);
        Assert.Equal(1000, counter);
        Assert.Equal(1, sem.CurrentCount);
    }

    [Fact]
    public async Task ConcurrentTryLockAsync_SerializesAccess()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);
        var counter = 0;

        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            for (var i = 0; i < 50; i++)
            {
                var acquired = await locker.TryLockAsync(TimeSpan.FromSeconds(5));
                if (acquired)
                {
                    try
                    {
                        Interlocked.Increment(ref counter);
                    }
                    finally
                    {
                        await locker.DisposeAsync();
                    }
                }
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(500, counter);
        Assert.Equal(1, sem.CurrentCount);
    }

    // ---------------------------------------------------------------
    // TryLockAsync with zero timeout
    // ---------------------------------------------------------------

    [Fact]
    public async Task TryLockAsync_ZeroTimeout_Uncontended_ReturnsTrue()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        var result = await locker.TryLockAsync(TimeSpan.Zero);
        Assert.True(result);

        await locker.DisposeAsync();
    }

    [Fact]
    public async Task TryLockAsync_ZeroTimeout_Contended_ReturnsFalse()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        sem.Wait(); // exhaust

        var result = await locker.TryLockAsync(TimeSpan.Zero);
        Assert.False(result);

        sem.Release();
    }

    // ---------------------------------------------------------------
    // Semaphore state integrity after failed TryLock
    // ---------------------------------------------------------------

    [Fact]
    public async Task TryLockAsync_FailedAttempt_DoesNotCorruptState()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        // Exhaust the semaphore
        sem.Wait();

        // Fail to acquire
        var result = await locker.TryLockAsync(TimeSpan.FromMilliseconds(10));
        Assert.False(result);

        // Release external hold
        sem.Release();

        // Now it should work
        result = await locker.TryLockAsync(TimeSpan.FromSeconds(1));
        Assert.True(result);
        Assert.Equal(0, sem.CurrentCount);

        await locker.DisposeAsync();
        Assert.Equal(1, sem.CurrentCount);
    }

    // ---------------------------------------------------------------
    // Dispose does not throw on unheld lock (idempotent release)
    // ---------------------------------------------------------------

    [Fact]
    public async Task DisposeAsync_AfterFailedTryLock_DoesNotRelease()
    {
        using var sem = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(sem);

        sem.Wait(); // exhaust

        var result = await locker.TryLockAsync(TimeSpan.FromMilliseconds(10));
        Assert.False(result);

        // Dispose should not release (lock was never acquired)
        await locker.DisposeAsync();
        Assert.Equal(0, sem.CurrentCount); // still exhausted

        sem.Release();
        Assert.Equal(1, sem.CurrentCount);
    }
}
