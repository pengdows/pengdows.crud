using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.strategies.connection;

internal interface IConnectionStrategy
{
    ValueTask ReleaseConnectionAsync(ITrackedConnection? connection);
    void ReleaseConnection(ITrackedConnection? connection);
    ITrackedConnection GetConnection(ExecutionType executionType, bool isShared);
}
