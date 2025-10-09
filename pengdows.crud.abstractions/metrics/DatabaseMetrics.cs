namespace pengdows.crud.metrics;

/// <summary>
/// Snapshot of cheap database metrics collected per <see cref="IDatabaseContext"/> instance.
/// </summary>
/// <param name="ConnectionsCurrent">Current number of open connections held by the context.</param>
/// <param name="ConnectionsMax">Historical maximum number of concurrently open connections.</param>
/// <param name="ConnectionsOpened">Total connections opened since context creation.</param>
/// <param name="ConnectionsClosed">Total connections closed since context creation.</param>
/// <param name="AvgConnectionHoldMs">Exponential weighted moving average of connection hold duration in milliseconds.</param>
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
public readonly record struct DatabaseMetrics(
    int ConnectionsCurrent,
    int ConnectionsMax,
    long ConnectionsOpened,
    long ConnectionsClosed,
    double AvgConnectionHoldMs,
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
    double AvgTransactionMs);
