using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.strategies.connection;

public class KeepAliveConnectionStrategy : StandardConnectionStrategy
{
    public KeepAliveConnectionStrategy(DatabaseContext context) : base(context)
    {
    }

    // Parameterless ctor for tests that pass context per call
    public KeepAliveConnectionStrategy() : base(null!)
    {
    }

    public override void PostInitialize(ITrackedConnection? connection)
    {
        _context.SetPersistentConnection(connection);
    }

    public override ITrackedConnection GetConnection(ExecutionType executionType, bool isShared)
    {
        // Fail fast on acquisition to match tests that expect factory/open failures
        var conn = base.GetConnection(executionType, isShared);
        try
        {
            // Try to open immediately so open-time failures surface here
            if (conn.State != System.Data.ConnectionState.Open)
            {
                conn.Open();
            }
        }
        catch
        {
            // Dispose and rethrow to avoid leaking partially initialized connections
            try { conn.Dispose(); } catch { /* ignore */ }
            throw;
        }
        return conn;
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

public static class KeepAliveConnectionStrategyTestExtensions
{
    // Convenience async helpers expected by tests
    public static Task<ITrackedConnection> GetConnectionAsync(this KeepAliveConnectionStrategy _, DatabaseContext context, ExecutionType executionType, bool isShared)
    {
        var strat = new KeepAliveConnectionStrategy(context);
        var conn = strat.GetConnection(executionType, isShared);
        strat.PostInitialize(conn);
        return Task.FromResult(conn);
    }

    public static Task CloseConnectionAsync(this KeepAliveConnectionStrategy _, ITrackedConnection? connection, DatabaseContext context)
    {
        var strat = new KeepAliveConnectionStrategy(context);
        return strat.ReleaseConnectionAsync(connection).AsTask();
    }
}
