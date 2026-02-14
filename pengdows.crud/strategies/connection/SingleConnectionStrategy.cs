// =============================================================================
// FILE: SingleConnectionStrategy.cs
// PURPOSE: Connection strategy where ALL operations use ONE persistent connection.
//
// AI SUMMARY:
// - Most restrictive strategy: all reads AND writes share single connection.
// - Required for isolated in-memory databases (SQLite :memory:, DuckDB :memory:).
// - Connection loss = data loss for in-memory databases.
// - GetConnection() always returns the same persistent connection.
// - ReleaseConnection() never disposes the persistent connection.
// - PostInitialize() stores connection as persistent on DatabaseContext.
// - HandleDialectDetection() uses persistent connection directly.
// - Thread safety: Application code must serialize access externally.
// - Lowest overhead but highest latency (no concurrency).
// - Extends SafeAsyncDisposableBase for proper cleanup on context disposal.
// =============================================================================

using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.strategies.connection;

/// <summary>
/// SINGLE CONNECTION STRATEGY - DESIGN INTENT:
///
/// PURPOSE: All operations (reads AND writes) funnel through ONE persistent connection.
/// Most restrictive strategy, used when database limitations require absolute connection serialization.
///
/// BEHAVIOR:
/// - ALL operations use the same persistent connection
/// - No concurrent database operations possible (serialized at connection level)
/// - Connection is never disposed until DatabaseContext disposal
/// - Maximum simplicity with maximum constraints
///
/// DATABASE EXAMPLES:
/// - Isolated in-memory SQLite/DuckDB databases (Data Source=:memory:) where each connection has its own store
/// - Single-user file databases that don't support concurrent connections
/// - Embedded databases with strict single-connection limitations
/// - Testing scenarios where connection state isolation is critical
///
/// THREAD SAFETY:
/// - All operations are serialized through the single connection
/// - ApplicationCode must handle concurrency above the connection layer
/// - No internal concurrency management needed
///
/// PERFORMANCE CHARACTERISTICS:
/// - Lowest overhead (no connection creation/disposal costs)
/// - Highest latency (all operations serialized)
/// - Predictable behavior (no connection pool variability)
///
/// WHEN TO USE:
/// - In-memory databases where connection loss means data loss
/// - Databases with hard single-connection limits
/// - Testing scenarios requiring deterministic connection behavior
///
/// DO NOT MODIFY: This strategy is the simplest possible - all operations use one connection
/// </summary>
internal class SingleConnectionStrategy : SafeAsyncDisposableBase, IConnectionStrategy
{
    private readonly DatabaseContext _context;

    internal SingleConnectionStrategy(DatabaseContext context)
    {
        _context = context;
    }

    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared)
    {
        return _context.GetSingleConnection();
    }

    public void PostInitialize(ITrackedConnection? connection)
    {
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
        return StandardConnectionStrategy.ReleaseNonPersistentConnectionAsync(
            connection, _context.PersistentConnection);
    }

    public (ISqlDialect? dialect, IDataSourceInformation? dataSourceInfo) HandleDialectDetection(
        ITrackedConnection? initConnection,
        DbProviderFactory? factory,
        ILoggerFactory loggerFactory)
    {
        // SingleConnection strategy: use the persistent connection for detection
        // The initConnection becomes the single persistent connection, so reuse it
        var connectionForDetection = _context.PersistentConnection ?? initConnection;

        if (connectionForDetection != null && factory != null)
        {
            var dialect = SqlDialectFactory.CreateDialect(connectionForDetection, factory, loggerFactory);
            var dataSourceInfo = new DataSourceInformation(dialect);
            return (dialect, dataSourceInfo);
        }

        return (null, null);
    }
}