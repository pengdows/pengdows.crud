using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.strategies.connection;

internal class StandardConnectionStrategy : SafeAsyncDisposableBase, IConnectionStrategy
{
    protected readonly DatabaseContext _context;

    public StandardConnectionStrategy(DatabaseContext context)
    {
        _context = context;
    }

    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared)
    {
        return _context.FactoryCreateConnection(null, isShared, _context.IsReadOnlyConnection);
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

}
