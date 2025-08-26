using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.connection;

/// <summary>
/// Manages acquisition and release of tracked database connections.
/// </summary>
internal interface IConnectionStrategy : ISafeAsyncDisposableBase
{
    /// <summary>
    /// Obtains a connection for the given execution type.
    /// </summary>
    /// <param name="executionType">Requested execution type.</param>
    /// <param name="isShared">Indicates whether the connection can be shared.</param>
    /// <returns>The acquired tracked connection.</returns>
    ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false);

    /// <summary>
    /// Releases a previously obtained connection.
    /// </summary>
    /// <param name="connection">The connection to release.</param>
    void ReleaseConnection(ITrackedConnection? connection);

    /// <summary>
    /// Releases a connection asynchronously.
    /// </summary>
    /// <param name="connection">The connection to release.</param>
    ValueTask ReleaseConnectionAsync(ITrackedConnection? connection);
}
