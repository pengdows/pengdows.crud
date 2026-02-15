#region

using System.Data;
using pengdows.crud.connection;
using pengdows.crud.threading;

#pragma warning disable CS0108, CS0114

#endregion

namespace pengdows.crud.wrappers;

/// <summary>
/// Represents a connection that tracks usage and exposes additional helpers such as locking.
/// </summary>
public interface ITrackedConnection : IDbConnection
{
    /// <summary>
    /// Gets or sets the connection string for this connection.
    /// </summary>
    string ConnectionString { get; set; }

    /// <summary>
    /// Gets the connection timeout in seconds.
    /// </summary>
    int ConnectionTimeout { get; }

    /// <summary>
    /// Gets the current database name.
    /// </summary>
    string Database { get; }

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// Gets the data source name for this connection.
    /// </summary>
    string DataSource { get; }

    /// <summary>
    /// Gets the server version string.
    /// </summary>
    string ServerVersion { get; }

    /// <summary>
    /// Begins a transaction with the default isolation level.
    /// </summary>
    IDbTransaction BeginTransaction();

    /// <summary>
    /// Begins a transaction with the specified isolation level.
    /// </summary>
    /// <param name="isolationLevel">Isolation level to use.</param>
    IDbTransaction BeginTransaction(IsolationLevel isolationLevel);

    /// <summary>
    /// Changes the current database for this connection.
    /// </summary>
    /// <param name="databaseName">Target database name.</param>
    void ChangeDatabase(string databaseName);

    /// <summary>
    /// Closes the connection.
    /// </summary>
    void Close();

    /// <summary>
    /// Creates a command associated with this connection.
    /// </summary>
    IDbCommand CreateCommand();

    /// <summary>
    /// Opens the connection.
    /// </summary>
    void Open();

    /// <summary>
    /// Opens the connection asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets schema information for the specified collection name.
    /// </summary>
    /// <param name="dataSourceInformation">Schema collection name.</param>
    DataTable GetSchema(string dataSourceInformation);

    /// <summary>
    /// Disposes the connection.
    /// </summary>
    void Dispose();

    /// <summary>
    /// Disposes the connection asynchronously.
    /// </summary>
    ValueTask DisposeAsync();

    /// <summary>
    /// Provides an asynchronous lock tied to this connection.
    /// </summary>
    ILockerAsync GetLock();

    /// <summary>
    /// Retrieves schema information for the current connection.
    /// </summary>
    DataTable GetSchema();

    /// <summary>
    /// Per-connection state for prepare behavior tracking
    /// </summary>
    ConnectionLocalState LocalState { get; }
}
