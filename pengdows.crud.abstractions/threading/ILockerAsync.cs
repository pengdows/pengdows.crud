namespace pengdows.crud.threading;

/// <summary>
/// Provides asynchronous locking semantics.
/// </summary>
public interface ILockerAsync : IAsyncDisposable
{
    /// <summary>
    /// Acquires the lock, awaiting if necessary.
    /// </summary>
    Task LockAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to acquire the lock within the specified timeout.
    /// </summary>
    /// <param name="timeout">How long to wait for the lock.</param>
    /// <param name="cancellationToken">Token used to cancel the wait.</param>
    /// <returns>True if the lock was acquired.</returns>
    Task<bool> TryLockAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
