#region

using System.Data;
using pengdow.crud.threading;
#pragma warning disable CS0108, CS0114

#endregion

namespace pengdow.crud.wrappers;

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