// =============================================================================
// FILE: PreventDatabaseUnloadConnectionStrategy.cs
// PURPOSE: Connection strategy that maintains a sentinel connection to prevent database unload.
//
// AI SUMMARY:
// - Extends StandardConnectionStrategy with one additional persistent "sentinel" connection.
// - Sentinel connection is NEVER used for operations - exists only to keep DB engine loaded.
// - All actual work uses ephemeral connections (identical to Standard behavior).
// - Prevents costly shutdown/reload cycles (e.g. SQL Server LocalDB). SQLite/DuckDB/Access
//   never reach this strategy: their dialects coerce PreventDatabaseUnload to SingleWriter.
// - PostInitialize() stores the sentinel connection on DatabaseContext.
// - ReleaseConnection() skips disposal if connection is the sentinel.
// - HandleDialectDetection() can use sentinel or create throwaway for detection.
// - EnsureSentinelHealthy()/EnsureSentinelHealthyAsync() lazily detect and repair any sentinel
//   (one per enabled pool) that unexpectedly broke/closed, at the top of every GetConnection.
// - Test extensions provide async convenience helpers for GetConnectionAsync/CloseConnectionAsync.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.strategies.connection;

/// <summary>
/// PREVENT-DATABASE-UNLOAD CONNECTION STRATEGY - DESIGN INTENT:
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
/// - LocalDB instances that might shut down when no connections are active
/// - Embedded databases that have expensive startup costs
/// - File-based databases where keeping the engine loaded improves performance
///
/// THREAD SAFETY: Fully thread-safe. Sentinel references are normally stable after
/// initialization, but EnsureSentinelHealthy() may lazily replace one (under a lock, with a
/// re-check and a compare-and-swap install) if it unexpectedly transitions to Broken/Closed.
///
/// IMPORTANT: The sentinel connection is NEVER used for actual operations - it exists purely
/// to keep the database engine loaded and prevent costly reload cycles.
///
/// DO NOT MODIFY: This strategy is specifically tuned for embedded database engine behavior
/// </summary>
internal class PreventDatabaseUnloadConnectionStrategy : StandardConnectionStrategy
{
    private readonly object _sentinelRepairLock = new();
    private readonly SemaphoreSlim _sentinelRepairAsyncLock = new(1, 1);

    // Test-only hook: fires synchronously right after the post-open disposed-context re-check
    // inside sentinel repair, before the replacement is installed. Lets a test deterministically
    // reproduce "Dispose() happens exactly in this window" — mirrors TrackedConnection.OpenTimingHook.
    internal static Action? PostDisposedCheckHook;

    internal PreventDatabaseUnloadConnectionStrategy(DatabaseContext context) : base(context)
    {
    }

    // Parameterless ctor for tests that pass context per call
    public PreventDatabaseUnloadConnectionStrategy() : base(null!)
    {
    }

    public override void PostInitialize(ITrackedConnection? connection)
    {
        if (connection != null)
        {
            // Normal DatabaseContext construction registers its sentinels before the strategy is
            // created; this branch only serves a strategy initialized directly with a connection.
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

    public override async ValueTask<ITrackedConnection> GetConnectionAsync(ExecutionType executionType,
        bool isShared, CancellationToken cancellationToken = default)
    {
        await EnsureSentinelHealthyAsync(cancellationToken).ConfigureAwait(false);

        // Fail fast on acquisition to match tests that expect factory/open failures
        var conn = await base.GetConnectionAsync(executionType, isShared, cancellationToken).ConfigureAwait(false);
        try
        {
            // Try to open immediately so open-time failures surface here
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
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
    /// Detects and repairs a sentinel that unexpectedly transitioned to Broken/Closed (network blip,
    /// engine restart) rather than via context disposal. Called lazily at the top of every
    /// GetConnection — the guarantee is "repaired before the next connection-requiring operation",
    /// not continuous (there is no background monitor). Disposing the dead sentinel releases its
    /// pool permit; the replacement acquires a fresh one from the same pool, so permit accounting
    /// stays at exactly one per sentinel.
    /// </summary>
    private void EnsureSentinelHealthy()
    {
        // A DDL statement has the sentinels deliberately closed (see
        // DatabaseContext.SuspendSentinelsForDdl); reopening them now would block that DDL.
        if (_context.SentinelsSuspended || AllSentinelsHealthy())
        {
            return;
        }

        lock (_sentinelRepairLock)
        {
            foreach (var (current, executionType) in _context.GetSentinelSnapshot())
            {
                if (!IsHealthy(current))
                {
                    RepairSentinel(current, executionType);
                }
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
        DisposeQuietly(current);

        var replacement = _context.CreateSentinelConnection(executionType);
        try
        {
            replacement.Open();
            InstallReplacement(current, replacement, executionType);
        }
        catch
        {
            replacement.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Async counterpart of <see cref="EnsureSentinelHealthy"/>: same detect/lock/re-check shape,
    /// but opens the replacement with OpenAsync under a SemaphoreSlim so an async caller never
    /// blocks a thread-pool thread. A sync and an async repair racing on the same sentinel is safe:
    /// <see cref="DatabaseContext.ReplaceSentinel"/> is a compare-and-swap and the loser disposes
    /// its replacement.
    /// </summary>
    /// <summary>
    /// Reopens the sentinels a DDL statement closed (see DatabaseContext.SuspendSentinelsForDdl).
    /// Same repair path as a lost sentinel, but logged at Debug: the close was deliberate.
    /// </summary>
    internal ValueTask RestoreSentinelsAfterDdlAsync(CancellationToken cancellationToken) =>
        EnsureSentinelHealthyAsync(cancellationToken, deliberateClose: true);

    private async ValueTask EnsureSentinelHealthyAsync(CancellationToken cancellationToken,
        bool deliberateClose = false)
    {
        if (_context.SentinelsSuspended || AllSentinelsHealthy())
        {
            return;
        }

        await _sentinelRepairAsyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var (current, executionType) in _context.GetSentinelSnapshot())
            {
                if (!IsHealthy(current))
                {
                    await RepairSentinelAsync(current, executionType, cancellationToken, deliberateClose)
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _sentinelRepairAsyncLock.Release();
        }
    }

    private async ValueTask RepairSentinelAsync(ITrackedConnection current, ExecutionType executionType,
        CancellationToken cancellationToken, bool deliberateClose = false)
    {
        if (_context.IsDisposed)
        {
            return;
        }

        if (deliberateClose)
        {
            _context.Logger.LogDebug("Reopening PreventDatabaseUnload sentinel closed for a DDL statement.");
        }
        else
        {
            _context.Logger.LogWarning(
                "PreventDatabaseUnload sentinel connection was {State}; reconnecting.", current.State);
        }
        DisposeQuietly(current);

        var replacement = _context.CreateSentinelConnection(executionType);
        try
        {
            await replacement.OpenAsync(cancellationToken).ConfigureAwait(false);
            InstallReplacement(current, replacement, executionType);
        }
        catch
        {
            replacement.Dispose();
            throw;
        }
    }

    private void InstallReplacement(ITrackedConnection current, ITrackedConnection replacement,
        ExecutionType executionType)
    {
        if (_context.IsDisposed)
        {
            replacement.Dispose();
            return;
        }

        PostDisposedCheckHook?.Invoke();

        // Compare-and-swap; also refuses once the context is disposed, so a Dispose() that lands
        // after the check above cannot leave the replacement orphaned.
        if (!_context.ReplaceSentinel(current, replacement, executionType))
        {
            replacement.Dispose();
        }
    }

    private bool AllSentinelsHealthy()
    {
        foreach (var (connection, _) in _context.GetSentinelSnapshot())
        {
            if (!IsHealthy(connection))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHealthy(ITrackedConnection connection)
    {
        return connection.State != ConnectionState.Broken && connection.State != ConnectionState.Closed;
    }

    private static void DisposeQuietly(ITrackedConnection connection)
    {
        try
        {
            connection.Dispose();
        }
        catch
        {
            // Already broken — best-effort cleanup; disposal releases its pool permit.
        }
    }

    private bool IsSentinel(ITrackedConnection connection)
    {
        return _context.GetSentinelSnapshot().Any(s => ReferenceEquals(s.Connection, connection));
    }

    public override void ReleaseConnection(ITrackedConnection? connection)
    {
        if (connection == null)
        {
            return;
        }

        if (IsSentinel(connection))
        {
            return; // sentinel stays open
        }

        connection.Dispose();
    }

    public override ValueTask ReleaseConnectionAsync(ITrackedConnection? connection)
    {
        return ReleaseNonPersistentConnectionAsync(
            connection,
            connection != null && IsSentinel(connection) ? connection : null);
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

    protected override void DisposeManaged()
    {
        _sentinelRepairAsyncLock.Dispose();
        base.DisposeManaged();
    }

    protected override ValueTask DisposeManagedAsync()
    {
        _sentinelRepairAsyncLock.Dispose();
        return base.DisposeManagedAsync();
    }
}

internal static class PreventDatabaseUnloadConnectionStrategyTestExtensions
{
    // Convenience async helpers expected by tests
    internal static Task<ITrackedConnection> GetConnectionAsync(this PreventDatabaseUnloadConnectionStrategy _,
        DatabaseContext context, ExecutionType executionType, bool isShared)
    {
        var strat = new PreventDatabaseUnloadConnectionStrategy(context);
        var conn = strat.GetConnection(executionType, isShared);
        strat.PostInitialize(conn);
        return Task.FromResult(conn);
    }

    internal static Task CloseConnectionAsync(this PreventDatabaseUnloadConnectionStrategy _, ITrackedConnection? connection,
        DatabaseContext context)
    {
        var strat = new PreventDatabaseUnloadConnectionStrategy(context);
        return strat.ReleaseConnectionAsync(connection).AsTask();
    }
}