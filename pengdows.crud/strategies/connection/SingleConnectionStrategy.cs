using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.strategies.connection;

internal class SingleConnectionStrategy :  IConnectionStrategy
{
    private readonly DatabaseContext _context;
    private int _disposed;

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

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    public void Dispose() { Interlocked.Exchange(ref _disposed, 1); }
    public ValueTask DisposeAsync() { Interlocked.Exchange(ref _disposed, 1); return ValueTask.CompletedTask; }
}
