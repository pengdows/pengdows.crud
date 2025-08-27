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
public interface ISqlContainer :ISafeAsyncDisposableBase
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
    DbParameter AddParameterWithValue<T>(DbType type, T value,
        ParameterDirection direction = ParameterDirection.Input);

    /// <summary>
    /// Adds a named parameter by type and value.
    /// </summary>
    /// <typeparam name="T">The type of the parameter value.</typeparam>
    /// <param name="name">Parameter name or <c>null</c> for an auto-generated name.</param>
    /// <param name="type">Database type of the parameter.</param>
    /// <param name="value">The value to assign.</param>
    /// <returns>The created parameter.</returns>
    DbParameter AddParameterWithValue<T>(string? name, DbType type, T value,
        ParameterDirection direction = ParameterDirection.Input);

    /// <summary>
    /// Sets a parameter value for an existing parameter.
    /// </summary>
    /// <param name="parameterName">The name of the parameter to update.</param>
    /// <param name="newValue">The new value to assign.</param>
    void SetParameterValue(string parameterName, object? newValue);

    /// <summary>
    /// Retrieves the value of a parameter as an <see cref="object"/>.
    /// </summary>
    /// <param name="parameterName">The name of the parameter.</param>
    /// <returns>The parameter value.</returns>
    object? GetParameterValue(string parameterName);

    /// <summary>
    /// Retrieves the value of a parameter and coerces it to the specified type using
    /// <see cref="TypeCoercionHelper"/>.
    /// </summary>
    /// <typeparam name="T">The expected type of the parameter value.</typeparam>
    /// <param name="parameterName">The name of the parameter.</param>
    /// <returns>The parameter value cast to <typeparamref name="T"/>.</returns>
    T GetParameterValue<T>(string parameterName);

    /// <summary>
    /// Executes the current query as a non-query command.
    /// </summary>
    /// <param name="commandType">Type of command to execute.</param>
    /// <returns>The number of rows affected.</returns>
    Task<int> ExecuteNonQueryAsync(CommandType commandType = CommandType.Text);

    /// <summary>
    /// Executes the query and returns the first column of the first row in the result set.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="commandType">Type of command to execute.</param>
    /// <returns>The scalar value or <c>null</c> if no results.</returns>
    Task<T?> ExecuteScalarAsync<T>(CommandType commandType = CommandType.Text);

    /// <summary>
    /// Executes the query and returns a tracked data reader.
    /// </summary>
    /// <param name="commandType">Type of command to execute.</param>
    /// <returns>A tracked reader over the results.</returns>
    Task<ITrackedReader> ExecuteReaderAsync(CommandType commandType = CommandType.Text);

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
    /// Wraps the query for execution as a stored procedure.
    /// </summary>
    /// <param name="executionType">The procedure execution type.</param>
    /// <param name="includeParameters">Whether to include parameters in the wrapper.</param>
    /// <param name="captureReturn">Whether to capture a return value.</param>
    /// <returns>The wrapped command text.</returns>
    string WrapForStoredProc(ExecutionType executionType, bool includeParameters = true, bool captureReturn = false);

    /// <summary>
    /// Wraps the query for execution as a CREATE-like stored procedure and captures a return value.
    /// </summary>
    /// <param name="includeParameters">Whether to include parameters in the wrapper.</param>
    /// <returns>The wrapped command text.</returns>
    string WrapForCreateWithReturn(bool includeParameters = true);

    /// <summary>
    /// Wraps the query for execution as an UPDATE-like stored procedure and captures a return value.
    /// </summary>
    /// <param name="includeParameters">Whether to include parameters in the wrapper.</param>
    /// <returns>The wrapped command text.</returns>
    string WrapForUpdateWithReturn(bool includeParameters = true);

    /// <summary>
    /// Wraps the query for execution as a DELETE-like stored procedure and captures a return value.
    /// </summary>
    /// <param name="includeParameters">Whether to include parameters in the wrapper.</param>
    /// <returns>The wrapped command text.</returns>
    string WrapForDeleteWithReturn(bool includeParameters = true);

}
