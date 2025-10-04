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
    string ConnectionString { get; set; }
    int ConnectionTimeout { get; }
    string Database { get; }
    ConnectionState State { get; }
    string DataSource { get; }
    string ServerVersion { get; }
    IDbTransaction BeginTransaction();
    IDbTransaction BeginTransaction(IsolationLevel isolationLevel);
    void ChangeDatabase(string databaseName);
    void Close();
    IDbCommand CreateCommand();
    void Open();
    Task OpenAsync(CancellationToken cancellationToken = default);
    DataTable GetSchema(string dataSourceInformation);
    void Dispose();
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
