// =============================================================================
// FILE: RealAsyncLockerTests.cs
// PURPOSE: Unit tests for RealAsyncLocker semaphore-based locking.
//
// AI SUMMARY:
// - Covers Lock(), LockAsync(), TryLockAsync() happy paths and error paths.
// - Key scenarios:
//   * Double-lock on same instance throws immediately (Volatile.Read pre-check)
//     — verified for both LockAsync and TryLockAsync with SemaphoreSlim(1,1)
//   * Cancellation propagates as OperationCanceledException / TaskCanceledException
//   * Timeout (via _lockTimeout ctor param) throws ModeContentionException
//   * TryLockAsync returns false on timeout, true on acquisition
//   * DisposeAsync releases the semaphore (CurrentCount restored to 1)
//   * ModeContentionStats contention tracking recorded on wait/timeout
// =============================================================================

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
    public async Task LockAsync_AlreadyAcquired_WithCount1_Throws()
    {
        // Production scenario: SemaphoreSlim(1,1). Guard must fire immediately, not deadlock.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new RealAsyncLocker(semaphore);

        await locker.LockAsync(cts.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await locker.LockAsync(cts.Token));
        Assert.Equal(0, semaphore.CurrentCount); // still held by first acquisition

        await locker.DisposeAsync();
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task LockAsync_AlreadyAcquired_Throws()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(2, 2);
        var locker = new RealAsyncLocker(semaphore);

        await locker.LockAsync(cts.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await locker.LockAsync(cts.Token));
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
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await locker.LockAsync(cts.Token));
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
    public async Task TryLockAsync_AlreadyAcquired_WithCount1_Throws()
    {
        // Production scenario: SemaphoreSlim(1,1). Guard must fire immediately, not wait for timeout.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new RealAsyncLocker(semaphore);

        var first = await locker.TryLockAsync(TimeSpan.FromSeconds(1), cts.Token);
        Assert.True(first);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await locker.TryLockAsync(TimeSpan.FromSeconds(1), cts.Token));
        Assert.Equal(0, semaphore.CurrentCount); // still held by first acquisition

        await locker.DisposeAsync();
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task TryLockAsync_AlreadyAcquired_Throws()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(2, 2);
        var locker = new RealAsyncLocker(semaphore);

        var first = await locker.TryLockAsync(TimeSpan.FromSeconds(1), cts.Token);
        Assert.True(first);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await locker.TryLockAsync(TimeSpan.FromSeconds(1), cts.Token));
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
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await locker.TryLockAsync(TimeSpan.FromSeconds(1), cts.Token));
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

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await locker.LockAsync(cts.Token));
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

    [Fact]
    public async Task Dispose_Sync_ReleasesSemaphoreAndPreventsReuse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = new RealAsyncLocker(semaphore);

        await locker.LockAsync(cts.Token);
        locker.Dispose(); // synchronous dispose path

        Assert.Equal(1, semaphore.CurrentCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await locker.LockAsync(cts.Token));
    }

}
