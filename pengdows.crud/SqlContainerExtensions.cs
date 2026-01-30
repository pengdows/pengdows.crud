// =============================================================================
// FILE: SqlContainerExtensions.cs
// PURPOSE: Extension methods for ISqlContainer that add CancellationToken
//          support and convenience methods without breaking the interface.
//
// AI SUMMARY:
// - Provides CancellationToken overloads for ExecuteNonQueryAsync,
//   ExecuteScalarAsync, and ExecuteReaderAsync.
// - Uses pattern matching to prefer concrete SqlContainer implementations
//   when available (which support cancellation), falling back to interface
//   methods (which don't) when the container is a different implementation.
// - AppendQuery: Fluent helper to append SQL to the query builder.
// - ExecuteReaderSingleRowAsync: Optimized for single-row queries.
// - ExecuteScalarWriteAsync: For INSERT/UPDATE with RETURNING/OUTPUT clauses.
// - These extensions allow existing ISqlContainer-based code to gain
//   cancellation support without interface-breaking changes.
// =============================================================================

using System.Data;
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
    public static Task<int> ExecuteNonQueryAsync(
        this ISqlContainer container,
        CommandType commandType,
        CancellationToken cancellationToken)
    {
        if (container is SqlContainer concrete)
        {
            return concrete.ExecuteNonQueryAsync(commandType, cancellationToken);
        }

        // Fallback to interface method (no token support)
        return container.ExecuteNonQueryAsync(commandType);
    }

    /// <summary>
    /// Executes a scalar query with cancellation support.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="container">The SQL container.</param>
    /// <param name="commandType">The type of command.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The first column of the first row, or default if no results.</returns>
    public static Task<T?> ExecuteScalarAsync<T>(
        this ISqlContainer container,
        CommandType commandType,
        CancellationToken cancellationToken)
    {
        if (container is SqlContainer concrete)
        {
            return concrete.ExecuteScalarAsync<T>(commandType, cancellationToken);
        }

        return container.ExecuteScalarAsync<T>(commandType);
    }

    /// <summary>
    /// Executes a query returning a reader with cancellation support.
    /// </summary>
    /// <param name="container">The SQL container.</param>
    /// <param name="commandType">The type of command.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A tracked data reader for result iteration.</returns>
    public static Task<ITrackedReader> ExecuteReaderAsync(
        this ISqlContainer container,
        CommandType commandType,
        CancellationToken cancellationToken)
    {
        if (container is SqlContainer concrete)
        {
            return concrete.ExecuteReaderAsync(commandType, cancellationToken);
        }

        return container.ExecuteReaderAsync(commandType);
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
    public static Task<ITrackedReader> ExecuteReaderSingleRowAsync(
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
    /// Executes a write operation that returns a scalar value (e.g., INSERT with RETURNING).
    /// </summary>
    /// <typeparam name="T">The expected return type (typically the generated ID).</typeparam>
    /// <param name="container">The SQL container.</param>
    /// <param name="commandType">The type of command.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The returned value, typically a generated primary key.</returns>
    /// <remarks>
    /// <para>
    /// This method uses a write connection (not read) which is important for:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Databases with read replicas (ensures write goes to primary)</description></item>
    /// <item><description>SingleWriter mode (uses the pinned writer connection)</description></item>
    /// </list>
    /// <para>
    /// Common use cases: INSERT ... RETURNING id (PostgreSQL), INSERT ... OUTPUT (SQL Server).
    /// </para>
    /// </remarks>
    public static Task<T?> ExecuteScalarWriteAsync<T>(
        this ISqlContainer container,
        CommandType commandType = CommandType.Text,
        CancellationToken cancellationToken = default)
    {
        if (container is SqlContainer concrete)
        {
            return concrete.ExecuteScalarWriteAsync<T>(commandType, cancellationToken);
        }

        // Fallback to read-path scalar if non-concrete (may not work for writes)
        return container.ExecuteScalarAsync<T>(commandType, cancellationToken);
    }
}