namespace pengdows.crud.strategies;

using pengdows.crud.enums;
using pengdows.crud.wrappers;

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
