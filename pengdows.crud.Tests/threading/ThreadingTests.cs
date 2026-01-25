using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.threading;
using Xunit;

namespace pengdows.crud.Tests.threading;

public class ThreadingTests
{
    [Fact]
    public void NoOpAsyncLocker_Instance_ReturnsSameInstance()
    {
        var instance1 = NoOpAsyncLocker.Instance;
        var instance2 = NoOpAsyncLocker.Instance;

        Assert.Same(instance1, instance2);
    }

    [Fact]
    public async Task NoOpAsyncLocker_LockAsync_CompletesImmediately()
    {
        var locker = NoOpAsyncLocker.Instance;

        var task = locker.LockAsync();

        Assert.True(task.IsCompleted);
        await task;
    }

    [Fact]
    public async Task NoOpAsyncLocker_LockAsync_WithCancellationToken_CompletesImmediately()
    {
        var locker = NoOpAsyncLocker.Instance;
        using var cts = new CancellationTokenSource();

        var task = locker.LockAsync(cts.Token);

        Assert.True(task.IsCompleted);
        await task;
    }

    [Fact]
    public async Task NoOpAsyncLocker_TryLockAsync_ReturnsTrue()
    {
        var locker = NoOpAsyncLocker.Instance;

        var result = await locker.TryLockAsync(TimeSpan.FromSeconds(1));

        Assert.True(result);
    }

    [Fact]
    public async Task NoOpAsyncLocker_TryLockAsync_WithCancellationToken_ReturnsTrue()
    {
        var locker = NoOpAsyncLocker.Instance;
        using var cts = new CancellationTokenSource();

        var result = await locker.TryLockAsync(TimeSpan.FromSeconds(1), cts.Token);

        Assert.True(result);
    }

    [Fact]
    public async Task NoOpAsyncLocker_DisposeAsync_CompletesImmediately()
    {
        var locker = NoOpAsyncLocker.Instance;

        var task = locker.DisposeAsync();

        Assert.True(task.IsCompleted);
        await task;
    }

    [Fact]
    public void RealAsyncLocker_Constructor_WithNullSemaphore_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RealAsyncLocker(null!));
    }

    [Fact]
    public void RealAsyncLocker_Constructor_WithSemaphore_Creates()
    {
        using var semaphore = new SemaphoreSlim(1, 1);

        using var locker = new RealAsyncLocker(semaphore);

        Assert.NotNull(locker);
    }

    [Fact]
    public void RealAsyncLocker_Constructor_WithSemaphoreAndLogger_Creates()
    {
        using var semaphore = new SemaphoreSlim(1, 1);
        var logger = new TestLogger();

        using var locker = new RealAsyncLocker(semaphore, logger);

        Assert.NotNull(locker);
    }

    [Fact]
    public async Task RealAsyncLocker_LockAsync_AcquiresLock()
    {
        using var semaphore = new SemaphoreSlim(1, 1);
        using var locker = new RealAsyncLocker(semaphore);

        await locker.LockAsync();

        Assert.Equal(0, semaphore.CurrentCount);
    }

    [Fact]
    public async Task RealAsyncLocker_TryLockAsync_WithTimeout_AcquiresLock()
    {
        using var semaphore = new SemaphoreSlim(1, 1);
        using var locker = new RealAsyncLocker(semaphore);

        var result = await locker.TryLockAsync(TimeSpan.FromSeconds(1));

        Assert.True(result);
        Assert.Equal(0, semaphore.CurrentCount);
    }

    [Fact]
    public async Task RealAsyncLocker_LockAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        using var semaphore = new SemaphoreSlim(1, 1);
        var locker = new RealAsyncLocker(semaphore);
        await locker.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => locker.LockAsync());
    }

    [Fact]
    public async Task RealAsyncLocker_TryLockAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        using var semaphore = new SemaphoreSlim(1, 1);
        var locker = new RealAsyncLocker(semaphore);
        await locker.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => locker.TryLockAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task RealAsyncLocker_DisposeAsync_ReleasesLock()
    {
        using var semaphore = new SemaphoreSlim(1, 1);
        var locker = new RealAsyncLocker(semaphore);

        await locker.LockAsync();
        Assert.Equal(0, semaphore.CurrentCount);

        await locker.DisposeAsync();
        Assert.Equal(1, semaphore.CurrentCount);
    }

    private class TestLogger : ILogger<RealAsyncLocker>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}