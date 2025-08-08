using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud;

internal sealed class SingleConnectionStrategy : IConnectionStrategy
{
    private readonly ITrackedConnection _connection;

    public SingleConnectionStrategy(ITrackedConnection connection)
    {
        _connection = connection;
    }

    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false)
    {
        return _connection;
    }

    public void CloseAndDisposeConnection(ITrackedConnection? connection)
    {
        // no-op
    }

    public ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? connection)
    {
        return ValueTask.CompletedTask;
    }
}

