namespace pengdows.crud.strategies;

using pengdows.crud.enums;
using pengdows.crud.wrappers;
using System.Threading.Tasks;

internal interface IConnectionStrategy
{
    ITrackedConnection GetConnection(ExecutionType executionType, bool isShared);
    void PostInitialize(ITrackedConnection? connection);
    void ReleaseConnection(ITrackedConnection? connection);
    ValueTask ReleaseConnectionAsync(ITrackedConnection? connection);
}
