using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.strategies.connection;

internal class SingleConnectionStrategy : SafeAsyncDisposableBase, IConnectionStrategy
{
    private readonly DatabaseContext _context;

    public SingleConnectionStrategy(DatabaseContext context)
    {
        _context = context;
    }

    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared)
    {
        return _context.GetSingleConnection();
    }

    public void PostInitialize(ITrackedConnection? connection)
    {
        if (connection != null)
        {
            _context.ApplyPersistentConnectionSessionSettings(connection);
        }

        _context.SetPersistentConnection(connection);
    }

    public void ReleaseConnection(ITrackedConnection? connection)
    {
        // persistent connection is reused, so never dispose it here
        if (connection == null || ReferenceEquals(connection, _context.PersistentConnection))
        {
            return;
        }

        connection.Dispose();
    }

    public ValueTask ReleaseConnectionAsync(ITrackedConnection? connection)
    {
        if (connection == null || ReferenceEquals(connection, _context.PersistentConnection))
        {
            return ValueTask.CompletedTask;
        }

        if (connection is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }

        connection.Dispose();
        return ValueTask.CompletedTask;
    }

}
