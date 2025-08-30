namespace pengdows.crud.strategies;

using pengdows.crud.enums;
using pengdows.crud.wrappers;
using System;
using System.Threading.Tasks;

internal class StandardConnectionStrategy : IConnectionStrategy
{
    private readonly DatabaseContext _context;

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
            return asyncDisposable.DisposeAsync();

        connection?.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal class KeepAliveConnectionStrategy : StandardConnectionStrategy
{
    public KeepAliveConnectionStrategy(DatabaseContext context) : base(context)
    {
    }

    public override void PostInitialize(ITrackedConnection? connection)
    {
        if (connection != null)
            _context.ApplyConnectionSessionSettings(connection);
        _context.SetPersistentConnection(connection);
    }

    public override void ReleaseConnection(ITrackedConnection? connection)
    {
        if (connection == null)
            return;

        if (ReferenceEquals(connection, _context.PersistentConnection))
            return; // keep-alive connection stays open

        connection.Dispose();
    }

    public override ValueTask ReleaseConnectionAsync(ITrackedConnection? connection)
    {
        if (connection == null || ReferenceEquals(connection, _context.PersistentConnection))
            return ValueTask.CompletedTask;

        if (connection is IAsyncDisposable asyncDisposable)
            return asyncDisposable.DisposeAsync();

        connection.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal class SingleConnectionStrategy : IConnectionStrategy
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
            _context.ApplyConnectionSessionSettings(connection);
        _context.SetPersistentConnection(connection);
    }

    public void ReleaseConnection(ITrackedConnection? connection)
    {
        // persistent connection is reused, so never dispose it here
        if (connection == null || ReferenceEquals(connection, _context.PersistentConnection))
            return;

        connection.Dispose();
    }

    public ValueTask ReleaseConnectionAsync(ITrackedConnection? connection)
    {
        if (connection == null || ReferenceEquals(connection, _context.PersistentConnection))
            return ValueTask.CompletedTask;

        if (connection is IAsyncDisposable asyncDisposable)
            return asyncDisposable.DisposeAsync();

        connection.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal class SingleWriterConnectionStrategy : IConnectionStrategy
{
    private readonly DatabaseContext _context;

    public SingleWriterConnectionStrategy(DatabaseContext context)
    {
        _context = context;
    }

    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared)
    {
        return _context.GetSingleWriterConnection(executionType, isShared);
    }

    public void PostInitialize(ITrackedConnection? connection)
    {
        if (connection != null)
            _context.ApplyConnectionSessionSettings(connection);
        _context.SetPersistentConnection(connection);
    }

    public void ReleaseConnection(ITrackedConnection? connection)
    {
        if (connection == null || ReferenceEquals(connection, _context.PersistentConnection))
            return;

        connection.Dispose();
    }

    public ValueTask ReleaseConnectionAsync(ITrackedConnection? connection)
    {
        if (connection == null || ReferenceEquals(connection, _context.PersistentConnection))
            return ValueTask.CompletedTask;

        if (connection is IAsyncDisposable asyncDisposable)
            return asyncDisposable.DisposeAsync();

        connection.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal static class ConnectionStrategyFactory
{
    public static IConnectionStrategy Create(DatabaseContext context, DbMode mode)
    {
        return mode switch
        {
            DbMode.Standard => new StandardConnectionStrategy(context),
            DbMode.KeepAlive => new KeepAliveConnectionStrategy(context),
            DbMode.SingleConnection => new SingleConnectionStrategy(context),
            DbMode.SingleWriter => new SingleWriterConnectionStrategy(context),
            _ => new StandardConnectionStrategy(context)
        };
    }
}
