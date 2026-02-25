namespace pengdows.crud.metrics;

/// <summary>
/// Snapshot of metrics collected for a specific execution role (read or write).
/// </summary>
/// <param name="ConnectionsCurrent">Current number of open connections held by the role.</param>
/// <param name="PeakOpenConnections">Historical maximum number of concurrently open connections observed.</param>
/// <param name="ConnectionsOpened">Total connections opened since context creation.</param>
/// <param name="ConnectionsClosed">Total connections closed since context creation.</param>
/// <param name="AvgConnectionHoldMs">Exponential weighted moving average of connection hold duration in milliseconds.</param>
/// <param name="AvgConnectionOpenMs">Exponential weighted moving average of connection open duration in milliseconds.</param>
/// <param name="AvgConnectionCloseMs">Exponential weighted moving average of connection close duration in milliseconds.</param>
/// <param name="LongLivedConnections">Count of connections held longer than the configured threshold.</param>
/// <param name="CommandsExecuted">Total commands that completed successfully.</param>
/// <param name="CommandsFailed">Total commands that failed.</param>
/// <param name="CommandsTimedOut">Commands that failed due to a timeout.</param>
/// <param name="CommandsCancelled">Commands cancelled via <see cref="CancellationToken"/>.</param>
/// <param name="AvgCommandMs">Exponential weighted moving average of command duration in milliseconds.</param>
/// <param name="P95CommandMs">Approximate 95th percentile command duration (milliseconds).</param>
/// <param name="P99CommandMs">Approximate 99th percentile command duration (milliseconds).</param>
/// <param name="MaxParametersObserved">Maximum parameter count observed on a command.</param>
/// <param name="RowsReadTotal">Total rows read by data readers.</param>
/// <param name="RowsAffectedTotal">Total rows affected by non-query operations.</param>
/// <param name="PreparedStatements">Number of commands successfully prepared.</param>
/// <param name="StatementsCached">Number of unique prepared statement shapes cached.</param>
/// <param name="StatementsEvicted">Number of cached statement shapes evicted.</param>
/// <param name="TransactionsActive">Current active transactions.</param>
/// <param name="TransactionsMax">Historical max concurrent transactions.</param>
/// <param name="AvgTransactionMs">Exponential weighted moving average of transaction duration in milliseconds.</param>
/// <param name="TransactionsCommitted">Total transactions successfully committed.</param>
/// <param name="TransactionsRolledBack">Total transactions rolled back.</param>
/// <param name="SlowCommandsTotal">Total commands that exceeded the slow-command threshold.</param>
/// <param name="P95TransactionMs">Approximate 95th percentile transaction duration (milliseconds).</param>
/// <param name="P99TransactionMs">Approximate 99th percentile transaction duration (milliseconds).</param>
/// <param name="ErrorDeadlocks">Total deadlock errors detected.</param>
/// <param name="ErrorSerializationFailures">Total serialization failures (snapshot isolation conflicts).</param>
/// <param name="ErrorConstraintViolations">Total constraint violation errors (unique, FK, not-null, check).</param>
public sealed record DatabaseRoleMetrics(
    int ConnectionsCurrent,
    int PeakOpenConnections,
    long ConnectionsOpened,
    long ConnectionsClosed,
    double AvgConnectionHoldMs,
    double AvgConnectionOpenMs,
    double AvgConnectionCloseMs,
    long LongLivedConnections,
    long CommandsExecuted,
    long CommandsFailed,
    long CommandsTimedOut,
    long CommandsCancelled,
    double AvgCommandMs,
    double P95CommandMs,
    double P99CommandMs,
    int MaxParametersObserved,
    long RowsReadTotal,
    long RowsAffectedTotal,
    long PreparedStatements,
    long StatementsCached,
    long StatementsEvicted,
    int TransactionsActive,
    int TransactionsMax,
    double AvgTransactionMs,
    long TransactionsCommitted,
    long TransactionsRolledBack,
    long SlowCommandsTotal,
    double P95TransactionMs,
    double P99TransactionMs,
    long ErrorDeadlocks,
    long ErrorSerializationFailures,
    long ErrorConstraintViolations)
{
    /// <summary>
    /// Represents an empty role metrics snapshot (no role-specific tracking active).
    /// </summary>
    public static readonly DatabaseRoleMetrics None = new(
        0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0);
}