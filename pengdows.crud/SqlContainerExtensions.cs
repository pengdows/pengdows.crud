using System.Data;
using pengdows.crud.wrappers;

namespace pengdows.crud;

/// <summary>
/// Non-breaking extension methods that add CancellationToken overloads for ISqlContainer.
/// These prefer the concrete SqlContainer implementations when available, and fall back
/// to the existing interface methods otherwise.
/// </summary>
public static class SqlContainerExtensions
{
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

    // Convenience wrapper for write-path scalar (INSERT ... RETURNING / OUTPUT)
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
