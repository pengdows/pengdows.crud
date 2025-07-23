namespace pengdows.crud.strategies;

using pengdows.crud.enums;
using pengdows.crud.wrappers;

internal interface IConnectionStrategy
{
    ITrackedConnection GetConnection(ExecutionType executionType, bool isShared);
    void PostInitialize(ITrackedConnection? connection);
}
