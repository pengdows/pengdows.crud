#region

using System.Data;
using pengdows.crud.threading;

#endregion

namespace pengdows.crud.wrappers;

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

    ILockerAsync GetLock();
    DataTable GetSchema();
}