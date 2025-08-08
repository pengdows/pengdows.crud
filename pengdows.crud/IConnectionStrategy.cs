using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.wrappers;
using pengdows.crud.infrastructure;

namespace pengdows.crud;

internal interface IConnectionStrategy : ISafeAsyncDisposableBase
{
    ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false);
    void CloseAndDisposeConnection(ITrackedConnection? connection);
    ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? connection);
}

