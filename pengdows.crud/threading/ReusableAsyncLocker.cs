// =============================================================================
// FILE: ReusableAsyncLocker.cs
// PURPOSE: Reusable semaphore-based locker for TransactionContext (zero per-call allocation).
//
// AI SUMMARY:
// - Implements ILockerAsync with real SemaphoreSlim-based locking.
// - Designed for TransactionContext where the same SemaphoreSlim is locked/unlocked
//   repeatedly across many operations within a single transaction. Also used by
//   DatabaseContext for the DuckDB serialized-open gate.
// - TrackDisposeState = false: Survives await using without being permanently disposed.
//   DisposeAsync merely releases the held lock, readying the instance for reuse.
// - Single allocation in TransactionContext constructor; GetLock() returns the same instance.
// - Eliminates per-operation RealAsyncLocker allocation overhead in hot paths (WriteStorm).
// - No contention stats or timeout — TransactionContext serializes by design,
//   so contention only happens if the caller misuses the API (concurrent access
//   on a single TransactionContext), which is already documented as unsupported.
// - MarkHeldByActiveReader(): while set, ANY contended lock attempt fails fast with
//   InvalidOperationException instead of blocking — a reader still open on the connection means
//   nothing can safely use it until the reader is disposed. Cleared when the hold is released.
// =============================================================================

using System.Runtime.CompilerServices;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.threading;

internal sealed class ReusableAsyncLocker : SafeAsyncDisposableBase, ILockerAsync
{
    private readonly SemaphoreSlim _semaphore;
    private int _lockState; // 0 = not held, 1 = held
    private volatile bool _heldByActiveReader;

    public ReusableAsyncLocker(SemaphoreSlim semaphore)
    {
        _semaphore = semaphore ?? throw new ArgumentNullException(nameof(semaphore));
    }

    /// <summary>
    /// Do not track dispose state — this instance is reused across many await using blocks.
    /// </summary>
    protected override bool TrackDisposeState => false;

    /// <summary>
    /// Marks the current hold as owned by a reader that stays open while the caller iterates it.
    /// Until the hold is released, any contended lock attempt throws instead of waiting: a nested
    /// command from the flow that owns the reader would otherwise wait forever, and any other
    /// caller would reach the provider while a reader is still open on the same connection.
    /// </summary>
    internal void MarkHeldByActiveReader()
    {
        _heldByActiveReader = true;
    }

    /// <inheritdoc />
    public void Lock()
    {
        if (_semaphore.Wait(0))
        {
            SetHeld();
            return;
        }

        ThrowIfBlockedBehindActiveReader();

        // No timeout or cancellation: TransactionContext is single-threaded by design.
        // Contention here means the caller is misusing the API (concurrent ops on one
        // transaction), which is already documented as unsupported.
        _semaphore.Wait();
        SetHeld();
    }

    /// <inheritdoc />
    public ValueTask LockAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        // Fast path: uncontended (common case for transaction serialization)
        if (_semaphore.Wait(0))
        {
            SetHeld();
            return ValueTask.CompletedTask;
        }

        ThrowIfBlockedBehindActiveReader();
        return LockAsyncSlow(cancellationToken);
    }

    private async ValueTask LockAsyncSlow(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        SetHeld();
    }

    /// <inheritdoc />
    public ValueTask<bool> TryLockAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<bool>(cancellationToken);
        }

        if (_semaphore.Wait(0))
        {
            SetHeld();
            return ValueTask.FromResult(true);
        }

        ThrowIfBlockedBehindActiveReader();
        return TryLockAsyncSlow(timeout, cancellationToken);
    }

    private async ValueTask<bool> TryLockAsyncSlow(TimeSpan timeout, CancellationToken cancellationToken)
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

    private void ThrowIfBlockedBehindActiveReader()
    {
        if (_heldByActiveReader)
        {
            throw new InvalidOperationException(
                "Cannot execute another command, or commit/roll back this transaction, while a " +
                "reader opened on it is still active. Dispose the reader (or finish consuming it) " +
                "first.");
        }
    }

    private void ReleaseIfHeld()
    {
        if (Interlocked.CompareExchange(ref _lockState, 0, 1) == 1)
        {
            _heldByActiveReader = false;
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
