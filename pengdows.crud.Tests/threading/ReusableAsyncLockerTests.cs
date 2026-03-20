using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.threading;
using Xunit;

namespace pengdows.crud.Tests.threading;

public class ReusableAsyncLockerTests
{
    [Fact]
    public void Constructor_NullSemaphore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ReusableAsyncLocker(null!));
    }

    // -------------------------------------------------------------------------
    // Lock() — fast path (semaphore uncontended)
    // -------------------------------------------------------------------------

    [Fact]
    public void Lock_Uncontended_AcquiresImmediately()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(semaphore);

        locker.Lock();

        // Semaphore should now be held (count = 0)
        Assert.False(semaphore.Wait(0));

        locker.Dispose();
    }

    // -------------------------------------------------------------------------
    // Lock() — slow path (semaphore already held): lines 51-53
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Lock_Contended_WaitsUntilReleased()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(semaphore);

        // Pre-acquire the semaphore so the fast path fails
        await semaphore.WaitAsync(cts.Token);

        var acquired = false;
        var task = Task.Run(() =>
        {
            locker.Lock(); // Goes to slow path (lines 51-52)
            acquired = true;
        });

        await Task.Delay(50, cts.Token);
        Assert.False(acquired);

        semaphore.Release(); // Allow locker to proceed
        await task.WaitAsync(cts.Token);
        Assert.True(acquired);

        locker.Dispose();
    }

    // -------------------------------------------------------------------------
    // LockAsync() — fast path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LockAsync_Uncontended_AcquiresImmediately()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(semaphore);

        await locker.LockAsync();

        Assert.False(semaphore.Wait(0));

        await locker.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // LockAsync() — slow path (lines 76-77): goes through LockAsyncSlow
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LockAsync_Contended_WaitsUntilReleased()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(semaphore);

        // Pre-acquire so the fast path fails
        semaphore.Wait();

        var acquired = false;
        var lockTask = Task.Run(async () =>
        {
            await locker.LockAsync(cts.Token); // Goes to LockAsyncSlow (lines 76-77)
            acquired = true;
        });

        await Task.Delay(50, cts.Token);
        Assert.False(acquired);

        semaphore.Release();
        await lockTask;
        Assert.True(acquired);

        await locker.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // TryLockAsync() — fast path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryLockAsync_Uncontended_ReturnsTrue()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(semaphore);

        var result = await locker.TryLockAsync(TimeSpan.FromSeconds(1));

        Assert.True(result);

        await locker.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // TryLockAsync() — slow path, eventually acquired (line 101)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryLockAsync_Contended_AcquiresAfterRelease()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(semaphore);

        // Pre-acquire so the fast path fails
        semaphore.Wait();

        var acquiredTask = Task.Run(async () =>
            await locker.TryLockAsync(TimeSpan.FromSeconds(3), cts.Token)); // Goes to TryLockAsyncSlow (line 101)

        await Task.Delay(50, cts.Token);
        semaphore.Release(); // Let it acquire

        var result = await acquiredTask;
        Assert.True(result); // Should have acquired

        await locker.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // TryLockAsync() — slow path, timeout (line 101: acquired=false)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryLockAsync_Contended_TimesOut_ReturnsFalse()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(semaphore);

        // Pre-acquire so it can't be obtained
        semaphore.Wait();

        // Use a very short timeout to force timeout path
        var result = await locker.TryLockAsync(TimeSpan.FromMilliseconds(10));

        Assert.False(result); // Should timeout
        // Semaphore is still held externally — release it for cleanup
        semaphore.Release();
    }

    // -------------------------------------------------------------------------
    // Reuse: dispose and re-lock (validates TrackDisposeState = false)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Locker_IsReusable_AfterDisposeAsync()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new ReusableAsyncLocker(semaphore);

        // First use
        await locker.LockAsync();
        await locker.DisposeAsync(); // releases the lock

        // Second use — should work because TrackDisposeState = false
        await locker.LockAsync();
        await locker.DisposeAsync();
    }
}
