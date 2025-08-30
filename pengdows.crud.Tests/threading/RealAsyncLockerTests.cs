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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(1, 1);
        var first = new RealAsyncLocker(semaphore);
        await first.LockAsync(cts.Token);

        var acquired = false;
        var second = new RealAsyncLocker(semaphore);
        var task = Task.Run(async () =>
        {
            await second.LockAsync(cts.Token);
            acquired = true;
        });

        await Task.Delay(100, cts.Token);
        Assert.False(acquired);

        await first.DisposeAsync();
        await task;
        Assert.True(acquired);
        await second.DisposeAsync();
    }

    [Fact]
    public async Task LockAsync_AlreadyAcquired_Throws()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(2, 2);
        var locker = new RealAsyncLocker(semaphore);

        await locker.LockAsync(cts.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => locker.LockAsync(cts.Token));
        Assert.Equal(1, semaphore.CurrentCount);

        await locker.DisposeAsync();
        Assert.Equal(2, semaphore.CurrentCount);
    }

    [Fact]
    public async Task LockAsync_Cancelled_Throws()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        using var ctsWait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await semaphore.WaitAsync(ctsWait.Token);
        var locker = new RealAsyncLocker(semaphore);
        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAsync<OperationCanceledException>(() => locker.LockAsync(cts.Token));
        semaphore.Release();
    }

    [Fact]
    public async Task TryLockAsync_SucceedsWhenAvailable()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new RealAsyncLocker(semaphore);
        var result = await locker.TryLockAsync(TimeSpan.FromMilliseconds(100), cts.Token);
        Assert.True(result);
        await locker.DisposeAsync();
    }

    [Fact]
    public async Task TryLockAsync_Timeout_ReturnsFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(1, 1);
        var first = new RealAsyncLocker(semaphore);
        await first.LockAsync(cts.Token);
        var second = new RealAsyncLocker(semaphore);
        var result = await second.TryLockAsync(TimeSpan.FromMilliseconds(50), cts.Token);
        Assert.False(result);
        await second.DisposeAsync();
        await first.DisposeAsync();
    }

    [Fact]
    public async Task TryLockAsync_AlreadyAcquired_Throws()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(2, 2);
        var locker = new RealAsyncLocker(semaphore);

        var first = await locker.TryLockAsync(TimeSpan.FromSeconds(1), cts.Token);
        Assert.True(first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => locker.TryLockAsync(TimeSpan.FromSeconds(1), cts.Token));
        Assert.Equal(1, semaphore.CurrentCount);

        await locker.DisposeAsync();
        Assert.Equal(2, semaphore.CurrentCount);
    }

    [Fact]
    public async Task DisposeAsync_WithoutLock_IsNoOp()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new RealAsyncLocker(semaphore);
        await locker.DisposeAsync();
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task TryLockAsync_Cancelled_Throws()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        using var ctsWait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await semaphore.WaitAsync(ctsWait.Token);
        var locker = new RealAsyncLocker(semaphore);
        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAsync<OperationCanceledException>(() => locker.TryLockAsync(TimeSpan.FromSeconds(1), cts.Token));
        semaphore.Release();
    }

    [Fact]
    public async Task DisposeAsync_DoubleDispose_IsIdempotent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new RealAsyncLocker(semaphore);

        await locker.LockAsync(cts.Token);
        await locker.DisposeAsync();
        await locker.DisposeAsync();

        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task DisposeAsync_PreventsReuse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new RealAsyncLocker(semaphore);

        await locker.LockAsync(cts.Token);
        await locker.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => locker.LockAsync(cts.Token));
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCalls_ReleaseOnce()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new RealAsyncLocker(semaphore);

        await locker.LockAsync(cts.Token);

        await Task.WhenAll(
            locker.DisposeAsync().AsTask(),
            locker.DisposeAsync().AsTask());

        Assert.Equal(1, semaphore.CurrentCount);
    }
}
