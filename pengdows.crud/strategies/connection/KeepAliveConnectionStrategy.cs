using pengdows.crud.wrappers;

namespace pengdows.crud.strategies.connection;

internal class KeepAliveConnectionStrategy : StandardConnectionStrategy
{
    public KeepAliveConnectionStrategy(DatabaseContext context) : base(context)
    {
    }

    public override void PostInitialize(ITrackedConnection? connection)
    {
        if (connection != null)
        {
            _context.ApplyConnectionSessionSettings(connection);
        }

        _context.SetPersistentConnection(connection);
    }

    public override void ReleaseConnection(ITrackedConnection? connection)
    {
        if (connection == null)
        {
            return;
        }

        if (ReferenceEquals(connection, _context.PersistentConnection))
        {
            return; // keep-alive connection stays open
        }

        connection.Dispose();
    }

    public override ValueTask ReleaseConnectionAsync(ITrackedConnection? connection)
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
