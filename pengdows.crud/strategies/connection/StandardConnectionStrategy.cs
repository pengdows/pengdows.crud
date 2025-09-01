using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.strategies.connection;

internal class StandardConnectionStrategy : IConnectionStrategy
{
    protected readonly DatabaseContext _context;
    private int _disposed;

    public StandardConnectionStrategy(DatabaseContext context)
    {
        _context = context;
    }

    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared)
    {
        return _context.FactoryCreateConnection(null, isShared);
    }

    public virtual void PostInitialize(ITrackedConnection? connection)
    {
        connection?.Dispose();
    }

    public virtual void ReleaseConnection(ITrackedConnection? connection)
    {
        connection?.Dispose();
    }

    public virtual ValueTask ReleaseConnectionAsync(ITrackedConnection? connection)
    {
        if (connection is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }

        connection?.Dispose();
        return ValueTask.CompletedTask;
    }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    public void Dispose() { Interlocked.Exchange(ref _disposed, 1); }
    public ValueTask DisposeAsync() { Interlocked.Exchange(ref _disposed, 1); return ValueTask.CompletedTask; }
}
