// =============================================================================
// FILE: ReusableAsyncLocker.cs
// PURPOSE: Reusable semaphore-based locker for TransactionContext (zero per-call allocation).
//
// AI SUMMARY:
// - Implements ILockerAsync with real SemaphoreSlim-based locking.
// - Designed for TransactionContext where the same SemaphoreSlim is locked/unlocked
//   repeatedly across many operations within a single transaction.
// - TrackDisposeState = false: Survives await using without being permanently disposed.
//   DisposeAsync merely releases the held lock, readying the instance for reuse.
// - Single allocation in TransactionContext constructor; GetLock() returns the same instance.
// - Eliminates per-operation RealAsyncLocker allocation overhead in hot paths (WriteStorm).
// - No contention stats or timeout — TransactionContext serializes by design,
//   so contention only happens if the caller misuses the API (concurrent access
//   on a single TransactionContext), which is already documented as unsupported.
// =============================================================================

using System.Runtime.CompilerServices;
using pengdows.crud.infrastructure;

namespace pengdows.crud.threading;

internal sealed class ReusableAsyncLocker : SafeAsyncDisposableBase, ILockerAsync
{
    private readonly SemaphoreSlim _semaphore;
    private int _lockState; // 0 = not held, 1 = held

    public ReusableAsyncLocker(SemaphoreSlim semaphore)
    {
        _semaphore = semaphore ?? throw new ArgumentNullException(nameof(semaphore));
    }

    /// <summary>
    /// Do not track dispose state — this instance is reused across many await using blocks.
    /// </summary>
    protected override bool TrackDisposeState => false;

    /// <inheritdoc />
    public void Lock()
    {
        if (_semaphore.Wait(0))
        {
            SetHeld();
            return;
        }

        _semaphore.Wait();
        SetHeld();
    }

    /// <inheritdoc />
    public Task LockAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        // Fast path: uncontended (common case for transaction serialization)
        if (_semaphore.Wait(0))
        {
            SetHeld();
            return Task.CompletedTask;
        }

        return LockAsyncSlow(cancellationToken);
    }

    private async Task LockAsyncSlow(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        SetHeld();
    }

    /// <inheritdoc />
    public Task<bool> TryLockAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<bool>(cancellationToken);
        }

        if (_semaphore.Wait(0))
        {
            SetHeld();
            return Task.FromResult(true);
        }

        return TryLockAsyncSlow(timeout, cancellationToken);
    }

    private async Task<bool> TryLockAsyncSlow(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var acquired = await _semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        if (acquired)
        {
            SetHeld();
        }

        return acquired;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetHeld()
    {
        Volatile.Write(ref _lockState, 1);
    }

    private void ReleaseIfHeld()
    {
        if (Interlocked.CompareExchange(ref _lockState, 0, 1) == 1)
        {
            _semaphore.Release();
        }
    }

    protected override void DisposeManaged()
    {
        ReleaseIfHeld();
    }

    protected override ValueTask DisposeManagedAsync()
    {
        ReleaseIfHeld();
        return ValueTask.CompletedTask;
    }
}
