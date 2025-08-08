using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud;

internal interface IConnectionStrategy
{
    ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false);
    void CloseAndDisposeConnection(ITrackedConnection? connection);
    ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? connection);
}

