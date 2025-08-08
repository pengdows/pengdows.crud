using System.Threading.Tasks;

namespace pengdows.crud;

internal interface IConnectionStrategy
{
    ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false);
    void CloseAndDisposeConnection(ITrackedConnection? connection);
    ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? connection);
}

