namespace pengdows.crud.threading;

/// <summary>
/// Provides locking semantics with both synchronous and asynchronous acquisition.
/// </summary>
/// <remarks>
/// <para>
/// This interface extends both <see cref="IDisposable"/> and <see cref="IAsyncDisposable"/>
/// to support proper resource cleanup in both synchronous and asynchronous code paths.
/// </para>
/// <para>
/// <strong>Important:</strong> In synchronous contexts (constructors, non-async methods),
/// always use <see cref="Lock"/> instead of <c>LockAsync().GetAwaiter().GetResult()</c>
/// to avoid potential deadlocks in contexts with a SynchronizationContext.
/// </para>
/// </remarks>
public interface ILockerAsync : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Acquires the lock synchronously, blocking until the lock is available.
    /// </summary>
    /// <remarks>
    /// Use this method in synchronous code paths such as constructors or
    /// non-async methods. This avoids sync-over-async deadlock risks that
    /// can occur with <c>LockAsync().GetAwaiter().GetResult()</c>.
    /// </remarks>
    void Lock();

    /// <summary>
    /// Acquires the lock asynchronously, awaiting if necessary.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the lock acquisition.</param>
    Task LockAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to acquire the lock within the specified timeout.
    /// </summary>
    /// <param name="timeout">How long to wait for the lock.</param>
    /// <param name="cancellationToken">Token used to cancel the wait.</param>
    /// <returns>True if the lock was acquired; false if the timeout elapsed.</returns>
    Task<bool> TryLockAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}