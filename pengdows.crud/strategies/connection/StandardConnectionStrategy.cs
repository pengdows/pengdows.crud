// =============================================================================
// FILE: StandardConnectionStrategy.cs
// PURPOSE: Default connection strategy using ephemeral connections with pooling.
//
// AI SUMMARY:
// - Production-recommended strategy for scalable database connections.
// - Creates ephemeral connections per operation, relying on ADO.NET pooling.
// - "Open late, close early" - connections disposed immediately after use.
// - No persistent connections - each GetConnection() creates new from pool.
// - Thread-safe: No shared state between concurrent operations.
// - Ideal for: SQL Server, PostgreSQL, MySQL, Oracle with connection pooling.
// - Base class for KeepAliveConnectionStrategy (adds sentinel connection).
// =============================================================================

using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.strategies.connection;

/// <summary>
/// STANDARD CONNECTION STRATEGY - DESIGN INTENT:
///
/// PURPOSE: Default production strategy for scalable database connections with provider connection pooling.
///
/// BEHAVIOR:
/// - Creates ephemeral connections for each operation
/// - Relies entirely on provider connection pooling (ADO.NET pooling)
/// - Connections are opened late (when needed) and closed early (after use)
/// - No persistent connections - each GetConnection() creates a new connection
/// - All connections are immediately disposed when released
///
/// IDEAL FOR:
/// - Production databases with proper connection pooling (SQL Server, PostgreSQL, etc.)
/// - High concurrency scenarios where connection pool manages resource limits
/// - Cloud environments where connection limits are enforced at the provider level
/// - Any database that properly supports connection pooling
///
/// THREAD SAFETY: Fully thread-safe - no shared state between operations
///
/// DO NOT MODIFY: This is the baseline strategy - changes here affect all production deployments
/// </summary>
internal class StandardConnectionStrategy : SafeAsyncDisposableBase, IConnectionStrategy
{
    protected readonly DatabaseContext _context;

    internal StandardConnectionStrategy(DatabaseContext context)
    {
        _context = context;
    }

    public virtual ITrackedConnection GetConnection(ExecutionType executionType, bool isShared)
    {
        var executionIsRead = executionType == ExecutionType.Read;
        var readOnly = _context.IsReadOnlyConnection || executionIsRead;
        return _context.GetStandardConnectionWithExecutionType(executionType, isShared, readOnly);
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

    public virtual (ISqlDialect? dialect, IDataSourceInformation? dataSourceInfo) HandleDialectDetection(
        ITrackedConnection? initConnection,
        DbProviderFactory? factory,
        ILoggerFactory loggerFactory)
    {
        // Standard strategy: reuse the initialization connection for detection, then dispose it
        if (initConnection != null)
        {
            // When using DbDataSource, factory is created from the connection and will not be null
            // When factory is null, fall back to SQL-92 dialect
            if (factory != null)
            {
                var dialect = SqlDialectFactory.CreateDialect(initConnection, factory, loggerFactory);
                var dataSourceInfo = new DataSourceInformation(dialect);
                return (dialect, dataSourceInfo);
            }
        }

        return (null, null);
    }
}
