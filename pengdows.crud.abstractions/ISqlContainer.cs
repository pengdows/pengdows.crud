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
    /// Gets the string used to prefix quoted identifiers.
    /// </summary>
    string QuotePrefix { get; }

    /// <summary>
    /// Gets the string used to suffix quoted identifiers.
    /// </summary>
    string QuoteSuffix { get; }

    /// <summary>
    /// Gets the separator used when quoting composite identifiers.
    /// </summary>
    string CompositeIdentifierSeparator { get; }

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
    /// <returns>The wrapped command text.</returns>
    string WrapForStoredProc(ExecutionType executionType, bool includeParameters = true);

    /// <summary>
    /// Wraps an object name using the current quoting rules.
    /// </summary>
    /// <param name="objectName">The name to wrap.</param>
    /// <returns>The wrapped object name.</returns>
    string WrapObjectName(string objectName);

    /// <summary>
    /// Generates a command parameter name for the given parameter.
    /// </summary>
    /// <param name="parameter">The parameter for which to generate a name.</param>
    /// <returns>The generated name.</returns>
    string MakeParameterName(DbParameter parameter);
}
