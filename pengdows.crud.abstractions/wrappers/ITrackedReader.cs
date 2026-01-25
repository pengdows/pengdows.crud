#region

using System.Data;

#endregion

namespace pengdows.crud.wrappers;

/// <summary>
/// Represents a data reader that tracks its owning connection for proper disposal.
/// </summary>
/// <remarks>
/// <para><strong>Lease semantics:</strong></para>
/// <para>
/// An <see cref="ITrackedReader"/> holds a lease over the connection lock for its lifetime.
/// The lock is acquired at creation and released at disposal.
/// </para>
/// <para><strong>Required usage:</strong></para>
/// <para>
/// Always dispose using <c>await using</c> (preferred) or <c>using</c>. While a reader is active,
/// other operations on the same connection (including transaction-scoped operations) may block.
/// </para>
/// <para><strong>Auto-disposal:</strong></para>
/// <para>
/// Implementations dispose themselves when <c>Read()</c>/<c>ReadAsync()</c> returns <c>false</c>.
/// You must still use <c>await using</c> to handle early termination, exceptions, and cancellation.
/// </para>
/// </remarks>
public interface ITrackedReader : IDataReader, IAsyncDisposable
{
    /// <summary>
    /// Advances the reader to the next record asynchronously.
    /// </summary>
    /// <returns><c>true</c> if another record is available; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// When this returns <c>false</c>, implementations may auto-dispose.
    /// Always use <c>await using</c> for early termination, exceptions, and cancellation.
    /// </remarks>
    Task<bool> ReadAsync();

    /// <summary>
    /// Advances the reader to the next record asynchronously with cancellation support.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns><c>true</c> if another record is available; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Cancellation does not dispose the reader. Always dispose on cancellation paths to release the lock.
    /// </remarks>
    Task<bool> ReadAsync(CancellationToken cancellationToken);
}