using pengdows.crud.enums;
using pengdows.crud.wrappers;
using pengdows.crud.infrastructure;

namespace pengdows.crud.connection;

internal class StandardConnectionStrategy : SafeAsyncDisposableBase, IConnectionStrategy
{
    private readonly Func<ITrackedConnection> _factory;

    public StandardConnectionStrategy(Func<ITrackedConnection> factory)
    {
        _factory = factory;
    }

    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false)
    {
        return _factory();
    }

    public void ReleaseConnection(ITrackedConnection? connection)
    {
        if (connection != null)
        {
            connection.Dispose();
        }
    }

    public ValueTask ReleaseConnectionAsync(ITrackedConnection? connection)
    {
        if (connection is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }

        connection?.Dispose();
        return ValueTask.CompletedTask;
    }
}

