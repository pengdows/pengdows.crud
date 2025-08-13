using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.threading;
using Xunit;

namespace pengdows.crud.Tests.threading;

public class RealAsyncLockerTests
{
    [Fact]
    public void Constructor_NullSemaphore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RealAsyncLocker(null!));
    }

    [Fact]
    public async Task LockAsync_WaitsForRelease()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        var first = new RealAsyncLocker(semaphore);
        await first.LockAsync().ConfigureAwait(false);

        var acquired = false;
        var second = new RealAsyncLocker(semaphore);
        var task = Task.Run(async () =>
        {
            await second.LockAsync().ConfigureAwait(false);
            acquired = true;
        });

        await Task.Delay(100).ConfigureAwait(false);
        Assert.False(acquired);

        await first.DisposeAsync().ConfigureAwait(false);
        await task.ConfigureAwait(false);
        Assert.True(acquired);
        await second.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task LockAsync_AlreadyAcquired_Throws()
    {
        var semaphore = new SemaphoreSlim(2, 2);
        var locker = new RealAsyncLocker(semaphore);

        await locker.LockAsync().ConfigureAwait(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => locker.LockAsync()).ConfigureAwait(false);
        Assert.Equal(1, semaphore.CurrentCount);

        await locker.DisposeAsync().ConfigureAwait(false);
        Assert.Equal(2, semaphore.CurrentCount);
    }

    [Fact]
    public async Task LockAsync_Cancelled_Throws()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        await semaphore.WaitAsync().ConfigureAwait(false);
        var locker = new RealAsyncLocker(semaphore);
        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAsync<OperationCanceledException>(() => locker.LockAsync(cts.Token)).ConfigureAwait(false);
        semaphore.Release();
    }

    [Fact]
    public async Task TryLockAsync_SucceedsWhenAvailable()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new RealAsyncLocker(semaphore);
        var result = await locker.TryLockAsync(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
        Assert.True(result);
        await locker.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task TryLockAsync_Timeout_ReturnsFalse()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        var first = new RealAsyncLocker(semaphore);
        await first.LockAsync().ConfigureAwait(false);
        var second = new RealAsyncLocker(semaphore);
        var result = await second.TryLockAsync(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        Assert.False(result);
        await second.DisposeAsync().ConfigureAwait(false);
        await first.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task TryLockAsync_AlreadyAcquired_Throws()
    {
        var semaphore = new SemaphoreSlim(2, 2);
        var locker = new RealAsyncLocker(semaphore);

        var first = await locker.TryLockAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        Assert.True(first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => locker.TryLockAsync(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        Assert.Equal(1, semaphore.CurrentCount);

        await locker.DisposeAsync().ConfigureAwait(false);
        Assert.Equal(2, semaphore.CurrentCount);
    }

    [Fact]
    public async Task DisposeAsync_WithoutLock_IsNoOp()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new RealAsyncLocker(semaphore);
        await locker.DisposeAsync().ConfigureAwait(false);
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task TryLockAsync_Cancelled_Throws()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        await semaphore.WaitAsync().ConfigureAwait(false);
        var locker = new RealAsyncLocker(semaphore);
        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAsync<OperationCanceledException>(() => locker.TryLockAsync(TimeSpan.FromSeconds(1), cts.Token)).ConfigureAwait(false);
        semaphore.Release();
    }

    [Fact]
    public async Task DisposeAsync_DoubleDispose_IsIdempotent()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new RealAsyncLocker(semaphore);

        await locker.LockAsync().ConfigureAwait(false);
        await locker.DisposeAsync().ConfigureAwait(false);
        await locker.DisposeAsync().ConfigureAwait(false);

        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task DisposeAsync_PreventsReuse()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new RealAsyncLocker(semaphore);

        await locker.LockAsync().ConfigureAwait(false);
        await locker.DisposeAsync().ConfigureAwait(false);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => locker.LockAsync()).ConfigureAwait(false);
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCalls_ReleaseOnce()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new RealAsyncLocker(semaphore);

        await locker.LockAsync().ConfigureAwait(false);

        await Task.WhenAll(
            locker.DisposeAsync().AsTask(),
            locker.DisposeAsync().AsTask()).ConfigureAwait(false);

        Assert.Equal(1, semaphore.CurrentCount);
    }
}

