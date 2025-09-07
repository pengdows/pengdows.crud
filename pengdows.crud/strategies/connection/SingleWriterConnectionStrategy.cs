using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.strategies.connection;

internal class SingleWriterConnectionStrategy : SafeAsyncDisposableBase, IConnectionStrategy
{
    private readonly DatabaseContext _context;

    public SingleWriterConnectionStrategy(DatabaseContext context)
    {
        _context = context;
    }

    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared)
    {
        // Writes always use the pinned writer connection.
        if (executionType == ExecutionType.Write)
        {
            return _context.GetSingleConnection();
        }

        // Reads create a new read-only connection. The underlying dialect will
        // apply read-only connection-string/session semantics as appropriate.
        return _context.GetStandardConnection(isShared: false, readOnly: true);
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
