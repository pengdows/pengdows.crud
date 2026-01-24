using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.strategies.connection;

/// <summary>
/// SINGLE WRITER CONNECTION STRATEGY - DESIGN INTENT:
///
/// PURPOSE: Optimized for databases that allow unlimited concurrent readers but only ONE writer.
/// Maintains one persistent writer connection and creates ephemeral read-only connections as needed.
///
/// BEHAVIOR:
/// - Writes: Always use the single persistent writer connection (never dispose)
/// - Reads: Create ephemeral read-only connections with read-only hints applied
/// - The persistent writer connection is held for the entire DatabaseContext lifetime
/// - Read connections are disposed immediately after use
///
/// DATABASE EXAMPLES:
/// - SQL Server Compact Edition (SQLCE) - single writer limitation
/// - SQLite/DuckDB with WAL or shared in-memory cache - benefits from persistent writer
/// - File-based databases where write locks are expensive to acquire
/// - Any database with single-writer/multi-reader architecture
///
/// THREAD SAFETY:
/// - Writer connection: Protected by database-level write locking
/// - Reader connections: Each request gets its own ephemeral connection
///
/// PERFORMANCE BENEFITS:
/// - Eliminates write lock acquisition overhead on every operation
/// - Read operations don't interfere with the persistent writer
/// - Optimal for applications with mixed read/write workloads
///
/// DO NOT MODIFY: This strategy is specifically designed for single-writer database constraints
/// </summary>
internal class SingleWriterConnectionStrategy : SafeAsyncDisposableBase, IConnectionStrategy
{
    private readonly DatabaseContext _context;

    internal SingleWriterConnectionStrategy(DatabaseContext context)
    {
        _context = context;
    }

    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared)
    {
        // Writes always use the pinned writer connection.
        if (executionType == ExecutionType.Write)
        {
            return _context.GetSingleConnection();
        }

        // Reads create a new read-only connection. The underlying dialect will
        // apply read-only connection-string/session semantics as appropriate.
        return _context.GetStandardConnectionWithExecutionType(ExecutionType.Read, isShared: false, readOnly: true);
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

    public (ISqlDialect? dialect, IDataSourceInformation? dataSourceInfo) HandleDialectDetection(
        ITrackedConnection? initConnection,
        DbProviderFactory? factory,
        ILoggerFactory loggerFactory)
    {
        // SingleWriter strategy: use the persistent connection for detection
        // The initConnection becomes the persistent connection, so reuse it for dialect detection
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
