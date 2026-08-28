// =============================================================================
// FILE: KeepAliveConnectionStrategy.cs
// PURPOSE: Connection strategy that maintains a sentinel connection to prevent database unload.
//
// AI SUMMARY:
// - Extends StandardConnectionStrategy with one additional persistent "sentinel" connection.
// - Sentinel connection is NEVER used for operations - exists only to keep DB engine loaded.
// - All actual work uses ephemeral connections (identical to Standard behavior).
// - Prevents costly shutdown/reload cycles in embedded databases (LocalDB, SQLite WAL).
// - PostInitialize() stores the sentinel connection on DatabaseContext.
// - ReleaseConnection() skips disposal if connection is the sentinel.
// - HandleDialectDetection() can use sentinel or create throwaway for detection.
// - EnsureSentinelHealthy() lazily detects and repairs a sentinel that unexpectedly broke/closed
//   (not via context disposal), called at the top of every GetConnection request.
// - Test extensions provide async convenience helpers for GetConnectionAsync/CloseConnectionAsync.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Linq;
using Microsoft.Extensions.Logging;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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
/// - LocalDB instances that might shut down when no connections are active — the only case
///   DatabaseContext.CoerceMode actually selects PreventDatabaseUnload automatically (LocalDb and
///   embedded Firebird under the applicable automatic-selection policy).
/// - Explicitly requested against a full-server database (PostgreSQL, SQL Server non-LocalDB,
///   etc.) — honored as "safe but less functional," not a recommended default.
///
/// NOT a use case: SQLite/DuckDB. CoerceMode always coerces a PreventDatabaseUnload request against either
/// of them to SingleWriter instead — a pinned idle connection does nothing for the write-lock
/// contention those engines need SingleWriter's turnstile for, so this strategy is never actually
/// reached for them regardless of what's requested.
///
/// THREAD SAFETY: Fully thread-safe. The sentinel connection reference is normally stable after
/// initialization, but EnsureSentinelHealthy() may lazily replace it (under a lock, with a
/// re-check) if it unexpectedly transitions to Broken/Closed — see that method's remarks.
///
/// IMPORTANT: The sentinel connection is NEVER used for actual operations - it exists purely
/// to keep the database engine loaded and prevent costly reload cycles.
///
/// DO NOT MODIFY: This strategy is specifically tuned for embedded database engine behavior
/// </summary>
internal class KeepAliveConnectionStrategy : StandardConnectionStrategy
{
    private readonly object _sentinelRepairLock = new();

    // Test-only hook: fires synchronously right after the disposed-context re-check inside
    // EnsureSentinelHealthy, before the replacement is installed. Lets a
    // test deterministically reproduce "Dispose() happens for the first time exactly in this
    // narrow window" without real threading — mirrors TrackedConnection.OpenTimingHook's pattern.
    internal static Action? PostDisposedCheckHook;

    internal KeepAliveConnectionStrategy(DatabaseContext context) : base(context)
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
            // Preserve the strategy's historical test/standalone behavior. Normal
            // DatabaseContext construction registers its initialization sentinel before the
            // strategy is created, so this branch is only used when a strategy is initialized
            // directly with a connection.
            _context.SetPersistentConnection(connection);
            _context.RegisterSentinel(connection, ExecutionType.Read);
        }
    }

    public override ITrackedConnection GetConnection(ExecutionType executionType, bool isShared)
    {
        EnsureSentinelHealthy();

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
            conn.Dispose();
            throw;
        }

        return conn;
    }

    /// <summary>
    /// Detects and repairs a KeepAlive sentinel connection that unexpectedly transitioned to
    /// Broken/Closed (e.g. a network blip, the embedded engine restarting) rather than via
    /// intentional context disposal. Called lazily at the top of every GetConnection request —
    /// KeepAlive guarantees the sentinel is repaired before the next connection-requiring
    /// operation, not continuously with zero interruption (there is no background monitor).
    ///
    /// A cheap unlocked State check handles the overwhelmingly common healthy case; the actual
    /// repair — disposing the dead connection (which releases its pool-governor slot via
    /// TrackedConnection.ReleaseSlot) and acquiring a fresh one — runs under a lock with a
    /// re-check, so concurrent callers (KeepAlive explicitly allows concurrent work) don't race
    /// to double-repair.
    /// </summary>
    private void EnsureSentinelHealthy()
    {
        var snapshot = _context.GetSentinelSnapshot();
        if (snapshot.Count == 0 || snapshot.All(s => IsHealthy(s.Connection)))
        {
            return;
        }

        lock (_sentinelRepairLock)
        {
            foreach (var (current, executionType) in _context.GetSentinelSnapshot())
            {
                if (IsHealthy(current))
                {
                    continue;
                }

                RepairSentinel(current, executionType);
            }
        }
    }

    private void RepairSentinel(ITrackedConnection current, ExecutionType executionType)
    {
        if (_context.IsDisposed)
        {
            return;
        }

        _context.Logger.LogWarning(
            "PreventDatabaseUnload sentinel connection was {State}; reconnecting.", current.State);

        try
        {
            current.Dispose();
        }
        catch
        {
            // Already broken — best-effort cleanup, nothing meaningful to do with a failure here.
        }

        var connectionString = executionType == ExecutionType.Read
            ? _context.RawReaderConnectionString
            : _context.RawConnectionString;
        var replacement = _context.FactoryCreateConnection(executionType, connectionString, true);
        try
        {
            replacement.Open();

            if (_context.IsDisposed)
            {
                replacement.Dispose();
                return;
            }

            PostDisposedCheckHook?.Invoke();

            if (!_context.ReplaceSentinel(current, replacement, executionType))
            {
                replacement.Dispose();
            }
        }
        catch
        {
            replacement.Dispose();
            throw;
        }
    }

    private static bool IsHealthy(ITrackedConnection connection)
    {
        return connection.State != ConnectionState.Broken && connection.State != ConnectionState.Closed;
    }

    public override void ReleaseConnection(ITrackedConnection? connection)
    {
        if (connection == null)
        {
            return;
        }

        if (_context.GetSentinelSnapshot().Any(s => ReferenceEquals(s.Connection, connection)))
        {
            return; // keep-alive connection stays open
        }

        connection.Dispose();
    }

    public override ValueTask ReleaseConnectionAsync(ITrackedConnection? connection)
    {
        return ReleaseNonPersistentConnectionAsync(
            connection,
            _context.GetSentinelSnapshot().Any(s => ReferenceEquals(s.Connection, connection))
                ? connection
                : null);
    }

    public override (ISqlDialect? dialect, IDataSourceInformation? dataSourceInfo) HandleDialectDetection(
        ITrackedConnection? initConnection,
        DbProviderFactory? factory,
        ILoggerFactory loggerFactory)
    {
        var detectionTarget = initConnection ?? _context.PersistentConnection;
        var ownsConnection = false;

        if (detectionTarget == null)
        {
            detectionTarget =
                _context.FactoryCreateConnection(_context.RawConnectionString, true);
            ownsConnection = true;
        }

        try
        {
            if (detectionTarget.State != ConnectionState.Open)
            {
                detectionTarget.Open();
            }

            if (factory != null)
            {
                var dialect = SqlDialectFactory.CreateDialect(detectionTarget, factory, loggerFactory);
                var dataSourceInfo = new DataSourceInformation(dialect);
                return (dialect, dataSourceInfo);
            }

            return (null, null);
        }
        catch
        {
            return (null, null);
        }
        finally
        {
            if (ownsConnection && detectionTarget != null)
            {
                detectionTarget.Dispose();
            }
        }
    }
}

internal static class KeepAliveConnectionStrategyTestExtensions
{
    // Convenience async helpers expected by tests
    internal static Task<ITrackedConnection> GetConnectionAsync(this KeepAliveConnectionStrategy _,
        DatabaseContext context, ExecutionType executionType, bool isShared)
    {
        var strat = new KeepAliveConnectionStrategy(context);
        var conn = strat.GetConnection(executionType, isShared);
        strat.PostInitialize(conn);
        return Task.FromResult(conn);
    }

    internal static Task CloseConnectionAsync(this KeepAliveConnectionStrategy _, ITrackedConnection? connection,
        DatabaseContext context)
    {
        var strat = new KeepAliveConnectionStrategy(context);
        return strat.ReleaseConnectionAsync(connection).AsTask();
    }
}
