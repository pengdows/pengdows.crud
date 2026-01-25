#region

using System.Data;
using System.Data.Common;
using System.Text;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

/// <summary>
/// Represents a composable, parameterized SQL container that supports dynamic query building,
/// safe parameter binding, and execution in the context of a tracked database connection.
/// </summary>
public interface ISqlContainer : ISafeAsyncDisposableBase
{
    /// <summary>
    /// Gets the <see cref="StringBuilder"/> used to compose the SQL query.
    /// </summary>
    StringBuilder Query { get; }

    /// <summary>
    /// Gets the current count of parameters added to the container.
    /// </summary>
    int ParameterCount { get; }

    /// <summary>
    /// Indicates whether a WHERE clause has already been appended to the query.
    /// </summary>
    bool HasWhereAppended { get; set; }

    /// <summary>
    /// Prefix used for quoting identifiers.
    /// </summary>
    string QuotePrefix { get; }

    /// <summary>
    /// Suffix used for quoting identifiers.
    /// </summary>
    string QuoteSuffix { get; }

    /// <summary>
    /// Separator between parts of a composite identifier.
    /// </summary>
    string CompositeIdentifierSeparator { get; }

    /// <summary>
    /// Wraps the provided identifier using the container's dialect rules.
    /// </summary>
    /// <param name="name">The identifier to wrap.</param>
    /// <returns>The wrapped identifier or an empty string when <paramref name="name"/> is null or empty.</returns>
    string WrapObjectName(string name);

    /// <summary>
    /// Formats a parameter name using the container's dialect.
    /// </summary>
    /// <param name="dbParameter">The parameter to format.</param>
    /// <returns>The correctly formatted parameter name.</returns>
    string MakeParameterName(DbParameter dbParameter);

    /// <summary>
    /// Formats a raw parameter name using the container's dialect.
    /// </summary>
    /// <param name="parameterName">The base parameter name.</param>
    /// <returns>The correctly formatted parameter name.</returns>
    string MakeParameterName(string parameterName);

    /// <summary>
    /// Creates a new <see cref="DbParameter"/> without adding it to the container.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="name">Parameter name or <c>null</c> for auto-generation.</param>
    /// <param name="type">Database type of the parameter.</param>
    /// <param name="value">The value to assign.</param>
    /// <returns>The created parameter.</returns>
    DbParameter CreateDbParameter<T>(string? name, DbType type, T value);

    /// <summary>
    /// Creates an unnamed <see cref="DbParameter"/> without adding it to the container.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="type">Database type of the parameter.</param>
    /// <param name="value">The value to assign.</param>
    /// <returns>The created parameter.</returns>
    DbParameter CreateDbParameter<T>(DbType type, T value);

    /// <summary>
    /// Adds a pre-constructed parameter to the container.
    /// </summary>
    /// <param name="parameter">The parameter to add.</param>
    void AddParameter(DbParameter parameter);

    /// <summary>
    /// Adds a parameter by type and value, returning the created <see cref="DbParameter"/>.
    /// </summary>
    /// <typeparam name="T">The type of the parameter value.</typeparam>
    /// <param name="type">Database type of the parameter.</param>
    /// <param name="value">The value to assign.</param>
    /// <returns>The created parameter.</returns>
    DbParameter AddParameterWithValue<T>(DbType type, T value);

    /// <summary>
    /// Adds a named parameter by type and value.
    /// </summary>
    /// <typeparam name="T">The type of the parameter value.</typeparam>
    /// <param name="name">Parameter name or <c>null</c> for an auto-generated name.</param>
    /// <param name="type">Database type of the parameter.</param>
    /// <param name="value">The value to assign.</param>
    /// <returns>The created parameter.</returns>
    DbParameter AddParameterWithValue<T>(string? name, DbType type, T value);

    /// <summary>
    /// Adds a parameter by type and value with an explicit direction, returning the created parameter.
    /// </summary>
    /// <typeparam name="T">The type of the parameter value.</typeparam>
    /// <param name="type">Database type of the parameter.</param>
    /// <param name="value">The value to assign.</param>
    /// <param name="direction">The parameter direction.</param>
    /// <returns>The created parameter.</returns>
    DbParameter AddParameterWithValue<T>(DbType type, T value, ParameterDirection direction);

    /// <summary>
    /// Adds a named parameter by type and value with an explicit direction.
    /// </summary>
    /// <typeparam name="T">The type of the parameter value.</typeparam>
    /// <param name="name">Parameter name or <c>null</c> for an auto-generated name.</param>
    /// <param name="type">Database type of the parameter.</param>
    /// <param name="value">The value to assign.</param>
    /// <param name="direction">The parameter direction.</param>
    /// <returns>The created parameter.</returns>
    DbParameter AddParameterWithValue<T>(string? name, DbType type, T value, ParameterDirection direction);

    /// <summary>
    /// Sets an existing parameter's value by name.
    /// </summary>
    void SetParameterValue(string parameterName, object? newValue);

    /// <summary>
    /// Gets a parameter's value by name.
    /// </summary>
    object? GetParameterValue(string parameterName);

    /// <summary>
    /// Gets a parameter's value by name and coerces it to type <typeparamref name="T"/>.
    /// </summary>
    T GetParameterValue<T>(string parameterName);

    // Parameter inspection helpers are intentionally not part of the public interface for compatibility

    /// <summary>
    /// Executes the current query as a non-query command.
    /// </summary>
    /// <param name="commandType">Type of command to execute.</param>
    /// <returns>The number of rows affected.</returns>
    Task<int> ExecuteNonQueryAsync(CommandType commandType = CommandType.Text);

    /// <summary>
    /// Executes the current query as a non-query command with cancellation support.
    /// </summary>
    /// <param name="commandType">Type of command to execute.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of rows affected.</returns>
    Task<int> ExecuteNonQueryAsync(CommandType commandType, CancellationToken cancellationToken);

    /// <summary>
    /// Executes the query and returns the first column of the first row in the result set.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="commandType">Type of command to execute.</param>
    /// <returns>The scalar value or <c>null</c> if no results.</returns>
    Task<T?> ExecuteScalarAsync<T>(CommandType commandType = CommandType.Text);

    /// <summary>
    /// Executes the query and returns the first column of the first row with cancellation support.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="commandType">Type of command to execute.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The scalar value or <c>null</c> if no results.</returns>
    Task<T?> ExecuteScalarAsync<T>(CommandType commandType, CancellationToken cancellationToken);

    /// <summary>
    /// Executes the query and returns a tracked data reader.
    /// </summary>
    /// <param name="commandType">Type of command to execute.</param>
    /// <returns>A tracked reader over the results.</returns>
    /// <remarks>
    /// <para><strong>Reader-as-lease:</strong></para>
    /// <para>
    /// The returned <see cref="ITrackedReader"/> holds the connection lock for its entire lifetime.
    /// The lock is acquired when the reader is created and released only when the reader is disposed.
    /// </para>
    /// <para><strong>Required usage:</strong></para>
    /// <para>
    /// Always use <c>await using</c> (or <c>using</c>). The reader auto-disposes when it reaches end-of-results,
    /// but you must still dispose on early termination (e.g., <c>break</c> in a loop).
    /// </para>
    /// <para><strong>Transactions:</strong></para>
    /// <para>
    /// When executed inside a transaction, the reader holds the transaction connection lock.
    /// Dispose the reader before issuing additional commands on the same transaction.
    /// </para>
    /// <example>
    /// <code>
    /// await using var reader = await container.ExecuteReaderAsync();
    /// while (await reader.ReadAsync())
    /// {
    ///     // consume row
    /// }
    /// // Lock released here
    /// </code>
    /// </example>
    /// </remarks>
    Task<ITrackedReader> ExecuteReaderAsync(CommandType commandType = CommandType.Text);

    /// <summary>
    /// Executes the query and returns a tracked data reader with cancellation support.
    /// </summary>
    /// <param name="commandType">Type of command to execute.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A tracked reader over the results.</returns>
    /// <remarks>
    /// <para><strong>Reader-as-lease:</strong></para>
    /// <para>
    /// The returned <see cref="ITrackedReader"/> holds the connection lock for its entire lifetime.
    /// Cancellation does not dispose the reader; always use <c>await using</c> to release the lock on cancellation paths.
    /// </para>
    /// <para><strong>Transactions:</strong></para>
    /// <para>
    /// In transaction contexts, dispose the reader before issuing additional commands on the same transaction connection.
    /// </para>
    /// </remarks>
    Task<ITrackedReader> ExecuteReaderAsync(CommandType commandType, CancellationToken cancellationToken);

    /// <summary>
    /// Adds multiple parameters to the container.
    /// </summary>
    /// <param name="list">The parameters to add.</param>
    void AddParameters(IEnumerable<DbParameter> list);

    /// <summary>
    /// Creates a <see cref="DbCommand"/> for the given tracked connection.
    /// </summary>
    /// <param name="conn">The connection to associate with the command.</param>
    /// <returns>The created command.</returns>
    DbCommand CreateCommand(ITrackedConnection conn);

    /// <summary>
    /// Clears the accumulated query and parameters.
    /// </summary>
    void Clear();

    /// <summary>
    /// Wraps the query for execution as a stored procedure with optional return capture when supported.
    /// </summary>
    /// <param name="executionType">The procedure execution type.</param>
    /// <param name="includeParameters">Whether to include parameters in the wrapper.</param>
    /// <param name="captureReturn">Whether to capture the procedure return value when supported.</param>
    string WrapForStoredProc(ExecutionType executionType, bool includeParameters = true, bool captureReturn = false);

    /// <summary>
    /// Creates a lightweight clone of this container with the same SQL query and parameter structure,
    /// allowing parameter values to be updated without affecting the original.
    /// </summary>
    /// <returns>A cloned container ready for parameter value updates.</returns>
    ISqlContainer Clone();

    /// <summary>
    /// Creates a lightweight clone of this container with the same SQL query and parameter structure,
    /// but with a different database context. This is essential for cached containers that need to
    /// work with different contexts (e.g., transactions, multi-tenancy).
    /// </summary>
    /// <param name="context">The database context to use for the cloned container. If null, uses the original context.</param>
    /// <returns>A cloned container with the specified context, ready for parameter value updates.</returns>
    ISqlContainer Clone(IDatabaseContext? context);
}