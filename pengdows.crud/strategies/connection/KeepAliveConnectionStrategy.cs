using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.strategies.connection;

/// <summary>
/// KEEP-ALIVE CONNECTION STRATEGY - DESIGN INTENT:
///
/// PURPOSE: Identical to Standard strategy except maintains one unused "sentinel" connection to prevent
/// database engine from unloading in embedded/local database scenarios.
///
/// BEHAVIOR:
/// - Creates ephemeral connections for all actual work (identical to Standard)
/// - Maintains one persistent "sentinel" connection that is never used for operations
/// - The sentinel connection prevents the database from shutting down between operations
/// - All working connections are disposed immediately when released (like Standard)
///
/// SPECIFIC USE CASES:
/// - SQLite databases where you want to prevent WAL mode cleanup between operations
/// - LocalDB instances that might shut down when no connections are active
/// - Embedded databases that have expensive startup costs
/// - File-based databases where keeping the engine loaded improves performance
///
/// THREAD SAFETY: Fully thread-safe - sentinel connection is read-only after initialization
///
/// IMPORTANT: The sentinel connection is NEVER used for actual operations - it exists purely
/// to keep the database engine loaded and prevent costly reload cycles.
///
/// DO NOT MODIFY: This strategy is specifically tuned for embedded database engine behavior
/// </summary>
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
        if (connection != null)
        {
            _context.ApplyConnectionSessionSettings(connection);
        }

        _context.SetPersistentConnection(connection);
    }

    public override ITrackedConnection GetConnection(ExecutionType executionType, bool isShared)
    {
        // Fail fast on acquisition to match tests that expect factory/open failures
        var conn = base.GetConnection(executionType, isShared);
        try
        {
            // Try to open immediately so open-time failures surface here
            if (conn.State != ConnectionState.Open)
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

    public override (ISqlDialect? dialect, IDataSourceInformation? dataSourceInfo) HandleDialectDetection(
        ITrackedConnection? initConnection,
        DbProviderFactory factory,
        ILoggerFactory loggerFactory)
    {
        var detectionTarget = initConnection ?? _context.PersistentConnection;
        var ownsConnection = false;

        if (detectionTarget == null)
        {
            detectionTarget = _context.FactoryCreateConnection(_context.ConnectionString, true, _context.IsReadOnlyConnection);
            ownsConnection = true;
        }

        try
        {
            if (detectionTarget.State != ConnectionState.Open)
            {
                detectionTarget.Open();
            }

            var dialect = SqlDialectFactory.CreateDialect(detectionTarget, factory, loggerFactory);
            var dataSourceInfo = new DataSourceInformation(dialect);
            return (dialect, dataSourceInfo);
        }
        catch
        {
            return (null, null);
        }
        finally
        {
            if (ownsConnection && detectionTarget != null)
            {
                try { detectionTarget.Dispose(); } catch { /* ignore */ }
            }
        }
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
