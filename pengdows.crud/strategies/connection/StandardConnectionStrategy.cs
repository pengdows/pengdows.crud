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
// - Base class for PreventDatabaseUnloadConnectionStrategy (adds sentinel connection).
// =============================================================================

using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
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
        return _context.GetStandardConnectionWithExecutionType(executionType, isShared);
    }

    public virtual ValueTask<ITrackedConnection> GetConnectionAsync(ExecutionType executionType, bool isShared,
        CancellationToken cancellationToken = default)
    {
        return _context.GetStandardConnectionWithExecutionTypeAsync(executionType, isShared, cancellationToken);
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

    /// <summary>
    /// Shared helper for strategies that skip disposal of a persistent connection.
    /// </summary>
    internal static ValueTask ReleaseNonPersistentConnectionAsync(
        ITrackedConnection? connection, ITrackedConnection? persistentConnection)
    {
        if (connection == null || ReferenceEquals(connection, persistentConnection))
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
        // useAsync: false takes the synchronous product-detection probes, but the dialect's version
        // detection (DetectDatabaseInfoAsync) is async-only, so this can block on it (REV-036/044).
        return HandleDialectDetectionCoreAsync(initConnection, factory, loggerFactory, false, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    public Task<(ISqlDialect? dialect, IDataSourceInformation? dataSourceInfo)> HandleDialectDetectionAsync(
        ITrackedConnection? initConnection,
        DbProviderFactory? factory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        return HandleDialectDetectionCoreAsync(initConnection, factory, loggerFactory, true, cancellationToken)
            .AsTask();
    }

    /// <summary>
    /// The one detection implementation behind <see cref="HandleDialectDetection"/> and
    /// <see cref="HandleDialectDetectionAsync"/> (BP-311): <paramref name="useAsync"/> picks the
    /// blocking or asynchronous provider call at each I/O step.
    /// </summary>
    protected virtual async ValueTask<(ISqlDialect? dialect, IDataSourceInformation? dataSourceInfo)>
        HandleDialectDetectionCoreAsync(
            ITrackedConnection? initConnection,
            DbProviderFactory? factory,
            ILoggerFactory loggerFactory,
            bool useAsync,
            CancellationToken cancellationToken)
    {
        // Standard strategy: reuse the initialization connection for detection; DatabaseContext
        // disposes it afterwards (Standard/SingleWriter).
        if (initConnection != null)
        {
            // When factory is null, fall back to SQL-92 dialect
            if (factory != null)
            {
                var dialect = useAsync
                    ? await SqlDialectFactory.CreateDialectAsync(initConnection, factory, loggerFactory,
                        cancellationToken).ConfigureAwait(false)
                    : SqlDialectFactory.CreateDialect(initConnection, factory, loggerFactory);
                var dataSourceInfo = new DataSourceInformation(dialect);
                return (dialect, dataSourceInfo);
            }
        }

        return (null, null);
    }
}
