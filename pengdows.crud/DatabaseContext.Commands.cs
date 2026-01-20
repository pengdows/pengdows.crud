using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace pengdows.crud;

/// <summary>
/// DatabaseContext partial class: Command and Parameter creation methods
/// </summary>
public partial class DatabaseContext
{
    /// <summary>
    /// Creates a new SQL container for building and executing queries.
    /// </summary>
    public ISqlContainer CreateSqlContainer(string? query = null)
    {
        // Provide a logger so container can emit diagnostics (e.g., prepare-disable notices)
        var logger = _loggerFactory.CreateLogger<ISqlContainer>();
        return SqlContainer.Create(this, query, logger);
    }

    /// <summary>
    /// Internal helper so TransactionContext can reuse the same logger factory for containers.
    /// </summary>
    internal ILogger<ISqlContainer> CreateSqlContainerLogger()
    {
        return _loggerFactory.CreateLogger<ISqlContainer>();
    }

    /// <summary>
    /// Creates a database parameter with the specified name, type, value, and direction.
    /// </summary>
    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value,
        ParameterDirection direction = ParameterDirection.Input)
    {
        var p = _dialect.CreateDbParameter(name, type, value);
        p.Direction = direction;
        return p;
    }

    /// <summary>
    /// Creates a database parameter with the specified name, type, and value (input direction).
    /// </summary>
    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        return CreateDbParameter(name, type, value, ParameterDirection.Input);
    }

    /// <summary>
    /// Creates a database parameter with the specified type, value, and direction (no name).
    /// </summary>
    public DbParameter CreateDbParameter<T>(DbType type, T value,
        ParameterDirection direction = ParameterDirection.Input)
    {
        return CreateDbParameter(null, type, value, direction);
    }

    /// <summary>
    /// Creates a database parameter with the specified type and value (input direction, no name).
    /// </summary>
    public DbParameter CreateDbParameter<T>(DbType type, T value)
    {
        return CreateDbParameter(type, value, ParameterDirection.Input);
    }

    /// <summary>
    /// Wraps an object name (table, column, etc.) with database-specific quoting.
    /// </summary>
    public string WrapObjectName(string name)
    {
        return _dialect.WrapObjectName(name);
    }

    /// <summary>
    /// Formats a parameter name for the current database dialect.
    /// </summary>
    public string MakeParameterName(DbParameter dbParameter)
    {
        return _dialect.MakeParameterName(dbParameter);
    }

    /// <summary>
    /// Formats a parameter name for the current database dialect.
    /// </summary>
    public string MakeParameterName(string parameterName)
    {
        return _dialect.MakeParameterName(parameterName);
    }

    /// <summary>
    /// Generates a random name suitable for temporary tables, aliases, etc.
    /// </summary>
    public string GenerateRandomName(int length = 5, int parameterNameMaxLength = 30)
    {
        return _dialect.GenerateRandomName(length, parameterNameMaxLength);
    }
}
