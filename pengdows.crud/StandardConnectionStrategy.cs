using System;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.wrappers;
using pengdows.crud.infrastructure;

namespace pengdows.crud;

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

    public void CloseAndDisposeConnection(ITrackedConnection? connection)
    {
        if (connection != null)
        {
            connection.Dispose();
        }
    }

    public ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? connection)
    {
        if (connection is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }

        connection?.Dispose();
        return ValueTask.CompletedTask;
    }
}

