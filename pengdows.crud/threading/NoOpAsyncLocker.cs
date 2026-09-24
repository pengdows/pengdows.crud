// =============================================================================
// FILE: NoOpAsyncLocker.cs
// PURPOSE: No-op locker for ephemeral connections (zero synchronization overhead).
//
// AI SUMMARY:
// - Implements ILockerAsync with no actual locking.
// - Singleton pattern: NoOpAsyncLocker.Instance.
// - All methods are no-ops:
//   * Lock(): Returns immediately
//   * LockAsync(): Returns ValueTask.CompletedTask
//   * TryLockAsync(): Returns ValueTask.FromResult(true)
// - Used for ephemeral (per-operation) connections in Standard/KeepAlive modes, and wherever
//   a gate does not apply (e.g., no serialized open, no single-connection transaction gate).
// - TrackDisposeState = false: Singleton doesn't need disposal tracking.
// - Extends SafeAsyncDisposableBase for interface compatibility.
// - Zero overhead: No semaphore, no allocation, no contention.
// =============================================================================

using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.threading;

internal sealed class NoOpAsyncLocker : SafeAsyncDisposableBase, ILockerAsync
{
    public static readonly NoOpAsyncLocker Instance = new();

    private NoOpAsyncLocker()
    {
    }

    protected override bool TrackDisposeState => false;

    /// <inheritdoc />
    public void Lock()
    {
        // No-op: no actual locking required
    }

    public ValueTask LockAsync(CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask<bool> TryLockAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(true);
}
