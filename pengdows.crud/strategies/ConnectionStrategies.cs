namespace pengdows.crud.strategies;

using pengdows.crud.enums;
using pengdows.crud.wrappers;
using System;
using System.Threading.Tasks;
using System.Threading;

internal class StandardConnectionStrategy : pengdows.crud.connection.IConnectionStrategy
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

    public new void Dispose() { }
    public new ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal class SingleConnectionStrategy : pengdows.crud.connection.IConnectionStrategy
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
            _context.ApplyConnectionSessionSettings(connection);
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

internal class SingleWriterConnectionStrategy : pengdows.crud.connection.IConnectionStrategy
{
    private readonly DatabaseContext _context;
    private int _disposed;

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
        {
            _context.ApplyConnectionSessionSettings(connection);
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

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    public void Dispose() { Interlocked.Exchange(ref _disposed, 1); }
    public ValueTask DisposeAsync() { Interlocked.Exchange(ref _disposed, 1); return ValueTask.CompletedTask; }
}

internal static class ConnectionStrategyFactory
{
    public static pengdows.crud.connection.IConnectionStrategy Create(DatabaseContext context, DbMode mode)
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
