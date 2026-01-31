// =============================================================================
// FILE: DatabaseContext.Metrics.cs
// PURPOSE: Performance metrics collection and monitoring integration.
//
// AI SUMMARY:
// - Metrics property - Returns current DatabaseMetrics snapshot.
// - MetricsUpdated event - Fires when metrics change (connections, queries).
// - Tracked metrics:
//   * Connection counts (current open, max ever open)
//   * Connection wait times and pool saturation
//   * Transaction counts (started, committed, rolled back)
//   * Query timing and counts
// - MetricsCollector (internal) aggregates data from all operations.
// - IMPORTANT: MetricsUpdated handlers must NOT call back into the context.
//   They are observer notifications only - log, update UI, send to monitoring,
//   but don't execute queries or transactions.
// - Pool statistics available via GetPoolStatistics().
// - Attribution tracking for caller identification.
// =============================================================================

using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;

namespace pengdows.crud;

/// <summary>
/// DatabaseContext partial class: Metrics and performance monitoring.
/// </summary>
/// <remarks>
/// This partial provides metrics collection and event notifications for
/// monitoring database performance and resource usage.
/// </remarks>
public partial class DatabaseContext
{
    /// <summary>
    /// Gets a snapshot of current database metrics.
    /// </summary>
    public DatabaseMetrics Metrics => CreateMetricsSnapshot();

    /// <summary>
    /// Event raised when database metrics are updated (connection counts, query timing, etc.).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>⚠️ Re-Entrancy Warning:</strong>
    /// </para>
    /// <para>
    /// Do <b>NOT</b> call back into this DatabaseContext instance from event handlers. This includes:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Executing queries (LoadAsync, ExecuteScalarAsync, etc.)</description></item>
    ///   <item><description>Starting transactions (BeginTransaction)</description></item>
    ///   <item><description>Getting connections (GetConnection)</description></item>
    ///   <item><description>Any operation that acquires locks or uses the context</description></item>
    /// </list>
    /// <para>
    /// <strong>Why?</strong> The event is fired without holding locks (standard .NET pattern). Re-entrant calls
    /// may cause undefined behavior, interleaved state, or subtle bugs. Adding a lock would cause guaranteed
    /// deadlocks if subscribers try to use the context.
    /// </para>
    /// <para>
    /// <strong>Correct Usage:</strong> Treat this as a <b>non-blocking observer notification</b>. Log metrics,
    /// update UI, send to monitoring systems, but don't perform database operations.
    /// </para>
    /// <example>
    /// <code>
    /// // ✅ GOOD: Observer pattern (no re-entrancy)
    /// context.MetricsUpdated += (sender, metrics) =>
    /// {
    ///     _logger.LogInformation("Open connections: {count}", metrics.ConnectionsCurrent);
    ///     _monitoring.RecordMetric("db.connections", metrics.ConnectionsCurrent);
    /// };
    ///
    /// // ❌ BAD: Re-entrant call (may cause undefined behavior)
    /// context.MetricsUpdated += async (sender, metrics) =>
    /// {
    ///     // DON'T DO THIS!
    ///     var result = await helper.LoadListAsync(container);  // Re-enters context
    /// };
    /// </code>
    /// </example>
    /// </remarks>
    public event EventHandler<DatabaseMetrics> MetricsUpdated
    {
        add => _metricsUpdated += value;
        remove => _metricsUpdated -= value;
    }

    /// <summary>
    /// Gets the total number of connections created during the lifetime of this context.
    /// This includes both reused and newly created connections.
    /// </summary>
    public long TotalConnectionsCreated => Interlocked.Read(ref _totalConnectionsCreated);

    /// <summary>
    /// Gets the total number of connections that were reused from the connection pool.
    /// </summary>
    public long TotalConnectionsReused => Interlocked.Read(ref _totalConnectionsReused);

    /// <summary>
    /// Gets the total number of connection failures that occurred.
    /// </summary>
    public long TotalConnectionFailures => Interlocked.Read(ref _totalConnectionFailures);

    /// <summary>
    /// Gets the total number of connection timeout failures specifically.
    /// </summary>
    public long TotalConnectionTimeoutFailures => Interlocked.Read(ref _totalConnectionTimeoutFailures);

    /// <summary>
    /// Gets the connection pool efficiency ratio (reused / total created).
    /// Returns 0 if no connections have been created.
    /// </summary>
    public double ConnectionPoolEfficiency
    {
        get
        {
            var total = TotalConnectionsCreated;
            return total == 0 ? 0.0 : (double)TotalConnectionsReused / total;
        }
    }

    /// <summary>
    /// Exposes the internal MetricsCollector for infrastructure use.
    /// </summary>
    MetricsCollector? IMetricsCollectorAccessor.MetricsCollector => _metricsCollector;
    MetricsCollector? IMetricsCollectorAccessor.ReadMetricsCollector => _readerMetricsCollector;
    MetricsCollector? IMetricsCollectorAccessor.WriteMetricsCollector => _writerMetricsCollector;
    MetricsCollector? IMetricsCollectorAccessor.GetMetricsCollector(ExecutionType executionType)
    {
        return executionType == ExecutionType.Read ? _readerMetricsCollector : _writerMetricsCollector;
    }

    internal PoolStatisticsSnapshot GetPoolStatisticsSnapshot(PoolLabel label)
    {
        var governor = label == PoolLabel.Reader ? _readerGovernor : _writerGovernor;
        if (governor == null)
        {
            return new PoolStatisticsSnapshot(
                label,
                string.Empty,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                true);
        }

        return governor.GetSnapshot();
    }

    /// <summary>
    /// Tracks a connection failure for monitoring purposes.
    /// </summary>
    /// <param name="exception">The exception that caused the failure</param>
    internal void TrackConnectionFailure(Exception exception)
    {
        Interlocked.Increment(ref _totalConnectionFailures);

        // Track specific timeout failures
        if (IsTimeoutException(exception))
        {
            Interlocked.Increment(ref _totalConnectionTimeoutFailures);
        }

        _logger.LogWarning(exception, "Connection failure tracked: {ExceptionType}", exception.GetType().Name);
    }

    /// <summary>
    /// Tracks a connection reuse for monitoring purposes.
    /// </summary>
    internal void TrackConnectionReuse()
    {
        Interlocked.Increment(ref _totalConnectionsReused);
    }

    private static bool IsTimeoutException(Exception exception)
    {
        return exception is TimeoutException ||
               exception.GetType().Name.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
    }

    private DatabaseMetrics CreateMetricsSnapshot()
    {
        if (_metricsCollector == null)
        {
            return new DatabaseMetrics(
                default,
                default,
                SaturateToInt(NumberOfOpenConnections),
                SaturateToInt(PeakOpenConnections),
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var snapshot = _metricsCollector.CreateSnapshot();
        var readSnapshot = _readerMetricsCollector?.CreateSnapshot();
        var writeSnapshot = _writerMetricsCollector?.CreateSnapshot();

        var readMetrics = readSnapshot.HasValue ? CreateRoleMetrics(readSnapshot.Value) : default;
        var writeMetrics = writeSnapshot.HasValue ? CreateRoleMetrics(writeSnapshot.Value) : default;

        return new DatabaseMetrics(
            readMetrics,
            writeMetrics,
            SaturateToInt(NumberOfOpenConnections),
            SaturateToInt(PeakOpenConnections),
            snapshot.ConnectionsOpened,
            snapshot.ConnectionsClosed,
            snapshot.AvgConnectionHoldMs,
            snapshot.AvgConnectionOpenMs,
            snapshot.AvgConnectionCloseMs,
            snapshot.LongLivedConnections,
            snapshot.CommandsExecuted,
            snapshot.CommandsFailed,
            snapshot.CommandsTimedOut,
            snapshot.CommandsCancelled,
            snapshot.AvgCommandMs,
            snapshot.P95CommandMs,
            snapshot.P99CommandMs,
            snapshot.MaxParametersObserved,
            snapshot.RowsReadTotal,
            snapshot.RowsAffectedTotal,
            snapshot.PreparedStatements,
            snapshot.StatementsCached,
            snapshot.StatementsEvicted,
            snapshot.TransactionsActive,
            snapshot.TransactionsMax,
            snapshot.AvgTransactionMs);
    }

    private static DatabaseRoleMetrics CreateRoleMetrics(in MetricsCollector.MetricsSnapshot snapshot)
    {
        return new DatabaseRoleMetrics(
            snapshot.ConnectionsCurrent,
            snapshot.PeakOpenConnections,
            snapshot.ConnectionsOpened,
            snapshot.ConnectionsClosed,
            snapshot.AvgConnectionHoldMs,
            snapshot.AvgConnectionOpenMs,
            snapshot.AvgConnectionCloseMs,
            snapshot.LongLivedConnections,
            snapshot.CommandsExecuted,
            snapshot.CommandsFailed,
            snapshot.CommandsTimedOut,
            snapshot.CommandsCancelled,
            snapshot.AvgCommandMs,
            snapshot.P95CommandMs,
            snapshot.P99CommandMs,
            snapshot.MaxParametersObserved,
            snapshot.RowsReadTotal,
            snapshot.RowsAffectedTotal,
            snapshot.PreparedStatements,
            snapshot.StatementsCached,
            snapshot.StatementsEvicted,
            snapshot.TransactionsActive,
            snapshot.TransactionsMax,
            snapshot.AvgTransactionMs);
    }

    private void OnMetricsCollectorUpdated()
    {
        var handler = Volatile.Read(ref _metricsUpdated);
        if (handler == null)
        {
            return;
        }

        var metrics = CreateMetricsSnapshot();
        if (Volatile.Read(ref _metricsHasActivity) == 0)
        {
            if (!HasCommandActivity(in metrics))
            {
                return;
            }

            Interlocked.Exchange(ref _metricsHasActivity, 1);
        }

        handler.Invoke(this, metrics);
    }

    private static bool HasCommandActivity(in DatabaseMetrics metrics)
    {
        return metrics.CommandsExecuted > 0
               || metrics.CommandsFailed > 0
               || metrics.CommandsTimedOut > 0
               || metrics.CommandsCancelled > 0
               || metrics.RowsReadTotal > 0
               || metrics.RowsAffectedTotal > 0
               || metrics.TransactionsActive > 0
               || metrics.TransactionsMax > 0;
    }

    private static int SaturateToInt(long value)
    {
        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        if (value <= int.MinValue)
        {
            return int.MinValue;
        }

        return (int)value;
    }
}
