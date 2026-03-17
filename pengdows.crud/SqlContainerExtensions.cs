// =============================================================================
// FILE: SqlContainerExtensions.cs
// PURPOSE: Extension methods for ISqlContainer that add CancellationToken
//          support and convenience methods without breaking the interface.
//
// AI SUMMARY:
// - Provides ExecutionType routing for scalar, reader, and non-query methods.
// - Uses pattern matching to prefer concrete SqlContainer implementations
//   when available, falling back to interface methods for other implementations.
// - AppendQuery: Fluent helper to append SQL to the query builder.
// - ExecuteReaderSingleRowAsync: Optimized for single-row queries.
// - ExecuteScalarRequiredAsync/OrNull/Try with ExecutionType: Connection pool routing.
// - These extensions allow ISqlContainer-based code to specify execution intent
//   (Read/Write) without interface-breaking changes.
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud;

/// <summary>
/// Extension methods that add <see cref="CancellationToken"/> support and convenience methods
/// to <see cref="ISqlContainer"/> without breaking interface compatibility.
/// </summary>
/// <remarks>
/// <para>
/// These methods use pattern matching to detect the concrete <see cref="SqlContainer"/>
/// type and call the cancellation-aware overloads when available. If the container is
/// a different implementation (like a mock), it falls back to the interface methods.
/// </para>
/// <para>
/// <strong>Performance Note:</strong> For best cancellation support and performance,
/// use containers obtained from <see cref="IDatabaseContext.CreateSqlContainer"/>.
/// </para>
/// </remarks>
public static class SqlContainerExtensions
{
    /// <summary>
    /// Appends SQL text to the container's query builder.
    /// </summary>
    /// <param name="container">The SQL container.</param>
    /// <param name="sql">The SQL text to append.</param>
    /// <returns>The container for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="container"/> is null.</exception>
    public static ISqlContainer AppendQuery(this ISqlContainer container, string sql)
    {
        if (container is null)
        {
            throw new ArgumentNullException(nameof(container));
        }

        if (!string.IsNullOrEmpty(sql))
        {
            container.Query.Append(sql);
        }

        return container;
    }

    /// <summary>
    /// Executes a non-query command with cancellation support.
    /// </summary>
    /// <param name="container">The SQL container.</param>
    /// <param name="commandType">The type of command (Text, StoredProcedure, TableDirect).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of rows affected.</returns>
    public static ValueTask<int> ExecuteNonQueryAsync(
        this ISqlContainer container,
        CommandType commandType,
        CancellationToken cancellationToken)
    {
        if (container is SqlContainer concrete)
        {
            return concrete.ExecuteNonQueryAsync(commandType, cancellationToken);
        }

        return container.ExecuteNonQueryAsync(commandType, cancellationToken);
    }

    /// <summary>
    /// Executes a scalar query that must return a value, with an explicit execution intent.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="container">The SQL container.</param>
    /// <param name="executionType">Execution intent (Read/Write) for pool selection.</param>
    /// <param name="commandType">The type of command.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The scalar value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the query returns no rows, or when the value is null and <typeparamref name="T"/> is non-nullable.</exception>
    /// <remarks>
    /// Use <see cref="ExecutionType.Write"/> for INSERT ... RETURNING / OUTPUT queries
    /// to ensure write connection routing (important for read replicas and SingleWriter mode).
    /// </remarks>
    public static ValueTask<T> ExecuteScalarRequiredAsync<T>(
        this ISqlContainer container,
        ExecutionType executionType,
        CommandType commandType = CommandType.Text,
        CancellationToken cancellationToken = default)
    {
        if (container is SqlContainer concrete)
        {
            return concrete.ExecuteScalarRequiredAsync<T>(executionType, commandType, cancellationToken);
        }

        return container.ExecuteScalarRequiredAsync<T>(executionType, commandType, cancellationToken);
    }

    /// <summary>
    /// Executes a scalar query that may return no rows or null, with an explicit execution intent.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="container">The SQL container.</param>
    /// <param name="executionType">Execution intent (Read/Write) for pool selection.</param>
    /// <param name="commandType">The type of command.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The scalar value, or <c>null</c> if no rows or the value was DBNull.</returns>
    public static ValueTask<T?> ExecuteScalarOrNullAsync<T>(
        this ISqlContainer container,
        ExecutionType executionType,
        CommandType commandType = CommandType.Text,
        CancellationToken cancellationToken = default)
    {
        if (container is SqlContainer concrete)
        {
            return concrete.ExecuteScalarOrNullAsync<T>(executionType, commandType, cancellationToken);
        }

        return container.ExecuteScalarOrNullAsync<T>(executionType, commandType, cancellationToken);
    }

    /// <summary>
    /// Executes a scalar query returning a <see cref="ScalarResult{T}"/> with an explicit execution intent.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="container">The SQL container.</param>
    /// <param name="executionType">Execution intent (Read/Write) for pool selection.</param>
    /// <param name="commandType">The type of command.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="ScalarResult{T}"/> with unambiguous status.</returns>
    public static ValueTask<ScalarResult<T>> TryExecuteScalarAsync<T>(
        this ISqlContainer container,
        ExecutionType executionType,
        CommandType commandType = CommandType.Text,
        CancellationToken cancellationToken = default)
    {
        if (container is SqlContainer concrete)
        {
            return concrete.TryExecuteScalarAsync<T>(executionType, commandType, cancellationToken);
        }

        return container.TryExecuteScalarAsync<T>(executionType, commandType, cancellationToken);
    }

    /// <summary>
    /// Executes a query returning a reader with cancellation support.
    /// </summary>
    /// <param name="container">The SQL container.</param>
    /// <param name="commandType">The type of command.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A tracked data reader for result iteration.</returns>
    public static ValueTask<ITrackedReader> ExecuteReaderAsync(
        this ISqlContainer container,
        CommandType commandType,
        CancellationToken cancellationToken)
    {
        if (container is SqlContainer concrete)
        {
            return concrete.ExecuteReaderAsync(commandType, cancellationToken);
        }

        return container.ExecuteReaderAsync(commandType, cancellationToken);
    }

    /// <summary>
    /// Executes a query returning a reader with an explicit execution intent.
    /// </summary>
    /// <param name="container">The SQL container.</param>
    /// <param name="executionType">Execution intent (Read/Write) for pool selection.</param>
    /// <param name="commandType">The type of command.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A tracked data reader for result iteration.</returns>
    public static ValueTask<ITrackedReader> ExecuteReaderAsync(
        this ISqlContainer container,
        ExecutionType executionType,
        CommandType commandType = CommandType.Text,
        CancellationToken cancellationToken = default)
    {
        if (container is SqlContainer concrete)
        {
            return concrete.ExecuteReaderAsync(executionType, commandType, cancellationToken);
        }

        return container.ExecuteReaderAsync(executionType, commandType, cancellationToken);
    }

    /// <summary>
    /// Executes a query optimized for returning a single row.
    /// </summary>
    /// <param name="container">The SQL container.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A tracked data reader positioned at the first (and only expected) row.</returns>
    /// <remarks>
    /// Uses <see cref="CommandBehavior.SingleRow"/> hint for potential performance optimization.
    /// Falls back to normal reader if the container doesn't support the optimization.
    /// </remarks>
    public static ValueTask<ITrackedReader> ExecuteReaderSingleRowAsync(
        this ISqlContainer container,
        CancellationToken cancellationToken = default)
    {
        if (container is SqlContainer concrete)
        {
            return concrete.ExecuteReaderSingleRowAsync(cancellationToken);
        }

        // Fallback to normal reader when container is not concrete
        return container.ExecuteReaderAsync(CommandType.Text, cancellationToken);
    }

    /// <summary>
    /// Executes a single-row reader with an explicit execution intent.
    /// </summary>
    /// <param name="container">The SQL container.</param>
    /// <param name="executionType">Execution intent (Read/Write) for pool selection.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A tracked data reader positioned at the first (and only expected) row.</returns>
    public static ValueTask<ITrackedReader> ExecuteReaderSingleRowAsync(
        this ISqlContainer container,
        ExecutionType executionType,
        CancellationToken cancellationToken = default)
    {
        if (container is SqlContainer concrete)
        {
            return concrete.ExecuteReaderSingleRowAsync(executionType, cancellationToken);
        }

        return container.ExecuteReaderAsync(executionType, CommandType.Text, cancellationToken);
    }

    // =========================================================================
    // Fluent query-building helpers
    //
    // These methods write directly into sc.Query so call sites don't have to
    // reach through sc.Query.Append(sc.WrapObjectName(...)) for every token.
    // All identifier quoting delegates to WrapObjectName, which correctly
    // splits dotted names (e.g. "p.col") into per-segment quoted tokens.
    // =========================================================================

    /// <summary>
    /// Appends a dialect-quoted identifier to the query.
    /// </summary>
    /// <example><c>sc.AppendName("post_date")</c> → <c>"post_date"</c></example>
    public static ISqlContainer AppendName(this ISqlContainer container, string name)
    {
        container.Query.Append(container.WrapObjectName(name));
        return container;
    }

    /// <summary>
    /// Appends a dialect-quoted alias-qualified identifier to the query.
    /// Equivalent to <c>AppendName("alias.name")</c> — the dialect splits on the dot.
    /// </summary>
    /// <example><c>sc.AppendName("p", "post_date")</c> → <c>"p"."post_date"</c></example>
    public static ISqlContainer AppendName(this ISqlContainer container, string alias, string name)
    {
        container.Query
            .Append(container.WrapObjectName(alias))
            .Append(container.CompositeIdentifierSeparator)
            .Append(container.WrapObjectName(name));
        return container;
    }

    /// <summary>
    /// Appends the dialect-formatted parameter placeholder for an existing parameter, does not add it to the parameter list
    /// </summary>
    public static ISqlContainer AppendParam(this ISqlContainer container, DbParameter parameter)
    {
        container.Query.Append(container.MakeParameterName(parameter));
        return container;
    }

    /// <summary>
    /// Appends the dialect-formatted parameter placeholder for a named parameter, does not add it to the parameter list
    /// </summary>
    public static ISqlContainer AppendParam(this ISqlContainer container, string parameterName)
    {
        container.Query.Append(container.MakeParameterName(parameterName));
        return container;
    }

    /// <summary>Appends <c> = </c> to the query.</summary>
    public static ISqlContainer AppendEquals(this ISqlContainer container)
    {
        container.Query.Append(SqlFragments.EqualsOp);
        return container;
    }

    /// <summary>Appends <c> AND </c> to the query.</summary>
    public static ISqlContainer AppendAnd(this ISqlContainer container)
    {
        container.Query.Append(SqlFragments.And);
        return container;
    }

    /// <summary>Appends <c> WHERE </c> to the query.</summary>
    public static ISqlContainer AppendWhere(this ISqlContainer container)
    {
        container.Query.Append(SqlFragments.Where);
        return container;
    }

    /// <summary>Appends <c> IN (</c> to the query.</summary>
    public static ISqlContainer AppendIn(this ISqlContainer container)
    {
        container.Query.Append(SqlFragments.In);
        return container;
    }

    /// <summary>Appends <c>)</c> to the query.</summary>
    public static ISqlContainer AppendCloseParen(this ISqlContainer container)
    {
        container.Query.Append(SqlFragments.CloseParen);
        return container;
    }

    /// <summary>Appends <c>, </c> to the query.</summary>
    public static ISqlContainer AppendComma(this ISqlContainer container)
    {
        container.Query.Append(SqlFragments.Comma);
        return container;
    }
}
