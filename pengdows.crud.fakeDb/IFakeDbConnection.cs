#region

using System.Data;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.fakeDb;

/// <summary>
/// Fake database connection used for tests to simulate provider behavior.
/// </summary>
public interface IFakeDbConnection : IDbConnection, IAsyncDisposable
{
    /// <summary>
    /// Database product to emulate for dialect behavior.
    /// </summary>
    SupportedDatabase EmulatedProduct { get; set; }

    /// <summary>
    /// Number of times <see cref="IDbConnection.Open"/> was called.
    /// </summary>
    int OpenCount { get; }

    /// <summary>
    /// Number of times <see cref="IDbConnection.OpenAsync"/> was called.
    /// </summary>
    int OpenAsyncCount { get; }

    /// <summary>
    /// Number of times <see cref="IDbConnection.Close"/> was called.
    /// </summary>
    int CloseCount { get; }

    /// <summary>
    /// Number of times the connection was disposed.
    /// </summary>
    int DisposeCount { get; }

    /// <summary>
    /// Queued reader result sets that have not yet been consumed.
    /// </summary>
    IReadOnlyCollection<IEnumerable<Dictionary<string, object>>> RemainingReaderResults { get; }

    /// <summary>
    /// Queued scalar results that have not yet been consumed.
    /// </summary>
    IReadOnlyCollection<object?> RemainingScalarResults { get; }

    /// <summary>
    /// Queued non-query results that have not yet been consumed.
    /// </summary>
    IReadOnlyCollection<int> RemainingNonQueryResults { get; }

    /// <summary>
    /// Captured command texts for non-query execution.
    /// </summary>
    IReadOnlyCollection<string> ExecutedNonQueryTexts { get; }

    /// <summary>
    /// Enqueues a result set to be returned by the next reader execution.
    /// </summary>
    /// <param name="rows">Rows to return.</param>
    void EnqueueReaderResult(IEnumerable<Dictionary<string, object>> rows);

    /// <summary>
    /// Enqueues a scalar value to be returned by the next scalar execution.
    /// </summary>
    /// <param name="value">Scalar value.</param>
    void EnqueueScalarResult(object? value);

    /// <summary>
    /// Enqueues a non-query result to be returned by the next non-query execution.
    /// </summary>
    /// <param name="value">Rows affected value.</param>
    void EnqueueNonQueryResult(int value);

    /// <summary>
    /// Registers a scalar result for a specific command text.
    /// </summary>
    /// <param name="commandText">Command text to match.</param>
    /// <param name="value">Scalar value to return.</param>
    void SetScalarResultForCommand(string commandText, object? value);

    /// <summary>
    /// Sets the server version string reported by this connection.
    /// </summary>
    /// <param name="version">Version string.</param>
    void SetServerVersion(string version);

    /// <summary>
    /// Sets the max parameter limit reported by the connection.
    /// </summary>
    /// <param name="limit">Parameter limit to report.</param>
    void SetMaxParameterLimit(int limit);

    /// <summary>
    /// Gets the configured max parameter limit if set.
    /// </summary>
    int? GetMaxParameterLimit();

    /// <summary>
    /// Configures the connection to fail when opening.
    /// </summary>
    /// <param name="shouldFail">Whether open should fail.</param>
    /// <param name="skipFirstOpen">Whether to allow the first open to succeed.</param>
    void SetFailOnOpen(bool shouldFail = true, bool skipFirstOpen = false);

    /// <summary>
    /// Configures the connection to fail when creating or executing commands.
    /// </summary>
    /// <param name="shouldFail">Whether command creation/execution should fail.</param>
    void SetFailOnCommand(bool shouldFail = true);

    /// <summary>
    /// Configures the connection to fail when beginning a transaction.
    /// </summary>
    /// <param name="shouldFail">Whether begin transaction should fail.</param>
    void SetFailOnBeginTransaction(bool shouldFail = true);

    /// <summary>
    /// Sets the exception thrown for simulated failures.
    /// </summary>
    /// <param name="exception">Exception instance to throw.</param>
    void SetCustomFailureException(Exception exception);

    /// <summary>
    /// Configures the connection to fail after a specified number of opens.
    /// </summary>
    /// <param name="openCount">Number of successful opens before failing.</param>
    void SetFailAfterOpenCount(int openCount);

    /// <summary>
    /// Marks the connection as broken.
    /// </summary>
    /// <param name="skipFirst">Whether to allow the first operation to succeed.</param>
    void BreakConnection(bool skipFirst = false);

    /// <summary>
    /// Clears any configured failure conditions.
    /// </summary>
    void ResetFailureConditions();

    /// <summary>
    /// Returns schema information for the connection.
    /// </summary>
    DataTable GetSchema();

    /// <summary>
    /// Returns schema information for the specified collection.
    /// </summary>
    /// <param name="meta">Schema collection name.</param>
    DataTable GetSchema(string meta);

    /// <summary>
    /// Opens the connection asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OpenAsync(CancellationToken cancellationToken);
}
