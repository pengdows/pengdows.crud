// =============================================================================
// FILE: ReusableAsyncLocker.cs
// PURPOSE: Reusable semaphore-based locker for TransactionContext (zero per-call allocation).
//
// AI SUMMARY:
// - Implements ILockerAsync with real SemaphoreSlim-based locking.
// - Designed for TransactionContext where the same SemaphoreSlim is locked/unlocked
//   repeatedly across many operations within a single transaction.
// - ONE instance is shared by every caller and its held state belongs to the instance, not to a
//   caller: dispose it only after your own Lock/LockAsync succeeded. Disposing it after a failed
//   or cancelled attempt releases whoever holds it (REV-029 removed the DuckDB open-gate use for
//   this reason; REV-041 fixed SqlContainer's release after a rejected attempt).
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
// - TryDeferUntilActiveReaderReleases(): work (the transaction's rollback after a Dispose) that
//   runs when the reader's hold is released (REV-027).
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
    private DeferredWork? _afterActiveReader;

    // Work to run once the active reader releases the lock: Sync from Dispose, Async from DisposeAsync.
    internal sealed record DeferredWork(Action Sync, Func<ValueTask> Async);

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

    internal bool IsHeldByActiveReader => _heldByActiveReader;

    /// <summary>
    /// Defers <paramref name="work"/> until the active reader releases the lock (REV-027). Returns
    /// false when no reader holds the lock any more and the work was not taken, in which case the
    /// caller runs it itself.
    /// </summary>
    internal bool TryDeferUntilActiveReaderReleases(DeferredWork work)
    {
        // Publish, then re-check; the release clears the flag, then takes the work. Both sides use
        // a full fence between their store and load, so one of them always sees the other.
        Interlocked.Exchange(ref _afterActiveReader, work);
        if (_heldByActiveReader)
        {
            return true;
        }

        return !ReferenceEquals(Interlocked.CompareExchange(ref _afterActiveReader, null, work), work);
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

    private DeferredWork? ReleaseIfHeld()
    {
        if (Interlocked.CompareExchange(ref _lockState, 0, 1) != 1)
        {
            return null;
        }

        _heldByActiveReader = false;
        _semaphore.Release();
        return Interlocked.Exchange(ref _afterActiveReader, null);
    }

    protected override void DisposeManaged()
    {
        ReleaseIfHeld()?.Sync();
    }

    protected override ValueTask DisposeManagedAsync()
    {
        var deferred = ReleaseIfHeld();
        return deferred == null ? ValueTask.CompletedTask : deferred.Async();
    }
}
