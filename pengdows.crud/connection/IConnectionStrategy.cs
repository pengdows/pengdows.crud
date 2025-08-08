using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.connection;

internal interface IConnectionStrategy : ISafeAsyncDisposableBase
{
    ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false);
    void ReleaseConnection(ITrackedConnection? connection);
    ValueTask ReleaseConnectionAsync(ITrackedConnection? connection);
}
