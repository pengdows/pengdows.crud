using System.Threading.Tasks;
using pengdow.crud.enums;
using pengdow.crud.infrastructure;
using pengdow.crud.wrappers;

namespace pengdow.crud;

public interface IConnectionStrategy : ISafeAsyncDisposableBase
{
    ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false);
    void ReleaseConnection(ITrackedConnection? connection);
    ValueTask ReleaseConnectionAsync(ITrackedConnection? connection);
}
