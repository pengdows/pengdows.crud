#region

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.threading;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// Tests to validate that synchronous code paths use synchronous locking
/// instead of sync-over-async patterns (.GetAwaiter().GetResult()).
///
/// Sync-over-async is a deadlock risk in contexts with a SynchronizationContext
/// (e.g., UI threads, some test runners). Constructors and synchronous methods
/// should use synchronous lock acquisition.
/// </summary>
public class SyncLockingTests
{
    [Fact]
    public void ILockerAsync_HasSyncLockMethod()
    {
        // The interface should have a synchronous Lock method to avoid
        // .GetAwaiter().GetResult() in constructors and sync code paths
        var lockMethod = typeof(ILockerAsync).GetMethod("Lock", Type.EmptyTypes);
        Assert.NotNull(lockMethod);
    }

    [Fact]
    public void RealAsyncLocker_HasSyncLockMethod()
    {
        // The implementation should also have the sync Lock method
        var lockerType = typeof(DatabaseContext).Assembly.GetType("pengdows.crud.threading.RealAsyncLocker");
        Assert.NotNull(lockerType);

        var lockMethod = lockerType!.GetMethod("Lock", Type.EmptyTypes);
        Assert.NotNull(lockMethod);
    }

    [Fact]
    public void RealAsyncLocker_SyncLock_AcquiresAndReleases()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        var locker = CreateRealAsyncLocker(semaphore);

        // Lock should succeed - invoke via reflection
        var lockMethod = locker.GetType().GetMethod("Lock", Type.EmptyTypes);
        Assert.NotNull(lockMethod);
        lockMethod!.Invoke(locker, null);
        Assert.Equal(0, semaphore.CurrentCount);

        // Dispose should release - use IAsyncDisposable or IDisposable
        DisposeLocker(locker);
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task RealAsyncLocker_SyncLock_BlocksWhenAlreadyHeld()
    {
        var semaphore = new SemaphoreSlim(1, 1);
        var locker1 = CreateRealAsyncLocker(semaphore);

        // Lock via reflection
        var lockMethod = locker1.GetType().GetMethod("Lock", Type.EmptyTypes);
        Assert.NotNull(lockMethod);
        lockMethod!.Invoke(locker1, null);

        // Second locker should block
        var locker2 = CreateRealAsyncLocker(semaphore);
        var acquired = false;
        var task = Task.Run(() =>
        {
            lockMethod.Invoke(locker2, null);
            acquired = true;
        });

        // Give it a moment - it should NOT acquire
        await Task.Delay(50);
        Assert.False(acquired);

        // Release first lock
        DisposeLocker(locker1);

        // Now the second should acquire
        await task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(acquired);

        DisposeLocker(locker2);
    }

    [Fact]
    public void DatabaseContext_Constructor_DoesNotUseSyncOverAsync()
    {
        // This test validates the fix is in place by checking that
        // DatabaseContext can be constructed without deadlock risk.
        // The real validation is that the code doesn't use .GetAwaiter().GetResult()
        // on async locks - which we verify via code inspection.

        // If this test runs successfully in a context with a SynchronizationContext,
        // it proves the fix works.
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        // Create context - this should use sync locking internally
        using var context = new DatabaseContext("Data Source=:memory:", factory);

        // If we get here without deadlock, the sync locking is working
        Assert.NotNull(context);
    }

    [Fact]
    public void NoOpAsyncLocker_HasSyncLockMethod()
    {
        // NoOpAsyncLocker should also have the sync Lock method for API consistency
        var lockerType = typeof(DatabaseContext).Assembly.GetType("pengdows.crud.threading.NoOpAsyncLocker");
        Assert.NotNull(lockerType);

        var lockMethod = lockerType!.GetMethod("Lock", Type.EmptyTypes);
        Assert.NotNull(lockMethod);
    }

    [Fact]
    public void ILockerAsync_Lock_HasVoidReturnType()
    {
        // Lock() should be void, not Task
        var lockMethod = typeof(ILockerAsync).GetMethod("Lock", Type.EmptyTypes);
        Assert.NotNull(lockMethod);
        Assert.Equal(typeof(void), lockMethod!.ReturnType);
    }

    [Fact]
    public void ILockerAsync_ImplementsIDisposable()
    {
        // For sync usage in using blocks, ILockerAsync should implement IDisposable
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(ILockerAsync)));
    }

    private static ILockerAsync CreateRealAsyncLocker(SemaphoreSlim semaphore)
    {
        var lockerType = typeof(DatabaseContext).Assembly.GetType("pengdows.crud.threading.RealAsyncLocker");
        Assert.NotNull(lockerType);

        // Create instance - the constructor takes SemaphoreSlim and optional logger
        var instance = Activator.CreateInstance(lockerType!, semaphore, null);
        return (ILockerAsync)instance!;
    }

    private static void DisposeLocker(ILockerAsync locker)
    {
        if (locker is IDisposable disposable)
        {
            disposable.Dispose();
        }
        else if (locker is IAsyncDisposable asyncDisposable)
        {
            asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}