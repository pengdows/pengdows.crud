using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.wrappers;

namespace pengdows.crud.strategies.connection;

/// <summary>
/// ARCHITECTURAL DESIGN INTENT - DO NOT MODIFY WITHOUT UNDERSTANDING:
///
/// Connection strategies encapsulate the complete lifecycle and management policy for database connections.
/// Each strategy represents a different connection management pattern optimized for specific database
/// characteristics and usage scenarios.
///
/// CORE RESPONSIBILITIES:
/// 1. Connection acquisition policy (when to create new vs reuse existing)
/// 2. Connection lifecycle management (persistent vs ephemeral)
/// 3. Resource cleanup and disposal semantics
/// 4. Thread safety and concurrency control
///
/// DESIGN PRINCIPLES:
/// - Each connection returned by GetConnection() has a clear ownership model
/// - Strategies determine whether connections should be closed/disposed or kept alive
/// - The strategy itself decides ephemeral vs persistent connection behavior
/// - Initialization and dialect detection should be handled by the strategy, not DatabaseContext
///
/// FUTURE ARCHITECTURAL DIRECTION:
/// - Strategies should handle their own dialect detection logic
/// - Connection disposal decisions should be entirely encapsulated in ReleaseConnection
/// - DatabaseContext should delegate all connection policy decisions to the active strategy
/// </summary>
internal interface IConnectionStrategy
{
    /// <summary>
    /// Asynchronously releases a connection according to the strategy's lifecycle policy.
    /// Strategy determines whether to dispose immediately, return to pool, or keep persistent.
    /// </summary>
    ValueTask ReleaseConnectionAsync(ITrackedConnection? connection);

    /// <summary>
    /// Synchronously releases a connection according to the strategy's lifecycle policy.
    /// Strategy determines whether to dispose immediately, return to pool, or keep persistent.
    /// </summary>
    void ReleaseConnection(ITrackedConnection? connection);

    /// <summary>
    /// Acquires a connection for the specified execution type according to the strategy's policy.
    /// Strategy determines whether to return persistent, ephemeral, or pooled connections.
    /// </summary>
    ITrackedConnection GetConnection(ExecutionType executionType, bool isShared);

    /// <summary>
    /// Handles dialect detection and initialization for this strategy.
    /// Strategy decides whether to reuse initialization connection, create throwaway connection, etc.
    /// Returns (dialect, dataSourceInfo) or (null, null) if fallback SQL-92 should be used.
    /// </summary>
    (ISqlDialect? dialect, IDataSourceInformation? dataSourceInfo) HandleDialectDetection(
        ITrackedConnection? initConnection,
        DbProviderFactory factory,
        ILoggerFactory loggerFactory);
}
