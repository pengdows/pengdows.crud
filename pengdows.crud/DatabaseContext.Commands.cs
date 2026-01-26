using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace pengdows.crud;

/// <summary>
/// DatabaseContext partial class: Command and Parameter creation methods
/// </summary>
public partial class DatabaseContext
{
    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value,
        ParameterDirection direction = ParameterDirection.Input)
    {
        var p = _dialect.CreateDbParameter(name, type, value);
        p.Direction = direction;
        return p;
    }

    /// <inheritdoc/>
    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        return CreateDbParameter(name, type, value, ParameterDirection.Input);
    }

    /// <inheritdoc/>
    public DbParameter CreateDbParameter<T>(DbType type, T value,
        ParameterDirection direction = ParameterDirection.Input)
    {
        return CreateDbParameter(null, type, value, direction);
    }

    /// <inheritdoc/>
    public DbParameter CreateDbParameter<T>(DbType type, T value)
    {
        return CreateDbParameter(type, value, ParameterDirection.Input);
    }

    /// <inheritdoc/>
    public string WrapObjectName(string name)
    {
        return _dialect.WrapObjectName(name);
    }

    /// <inheritdoc/>
    public string MakeParameterName(DbParameter dbParameter)
    {
        return _dialect.MakeParameterName(dbParameter);
    }

    /// <inheritdoc/>
    public string MakeParameterName(string parameterName)
    {
        return _dialect.MakeParameterName(parameterName);
    }

    /// <inheritdoc/>
    public string GenerateRandomName(int length = 5, int parameterNameMaxLength = 30)
    {
        return _dialect.GenerateRandomName(length, parameterNameMaxLength);
    }
}
