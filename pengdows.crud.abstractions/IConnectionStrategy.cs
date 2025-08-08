using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud;

public interface IConnectionStrategy : ISafeAsyncDisposableBase
{
    ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false);
    void ReleaseConnection(ITrackedConnection? connection);
    ValueTask ReleaseConnectionAsync(ITrackedConnection? connection);
}
