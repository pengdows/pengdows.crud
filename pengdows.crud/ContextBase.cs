// =============================================================================
// FILE: ContextBase.cs
// PURPOSE: Shared helper logic for DatabaseContext and TransactionContext.
//
// AI SUMMARY:
// - Centralizes identical helpers for SQL container creation, parameter creation,
//   and dialect-based quoting/parameter naming.
// - Provides overridable hooks for logger selection and validation.
// - Keeps DatabaseContext/TransactionContext focused on connection and lifecycle
//   responsibilities while sharing common SQL helpers.
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.dialects;
using pengdows.crud.infrastructure;

namespace pengdows.crud;

/// <summary>
/// Shared helper base for database contexts.
/// </summary>
public abstract class ContextBase : SafeAsyncDisposableBase
{
    protected abstract ISqlDialect DialectCore { get; }

    /// <summary>
    /// Optional logger resolver for SQL containers.
    /// </summary>
    protected virtual ILogger<ISqlContainer>? ResolveSqlContainerLogger()
    {
        return null;
    }

    /// <summary>
    /// Optional validation hook before creating a SQL container.
    /// </summary>
    protected virtual void ValidateCanCreateContainer()
    {
    }

    /// <inheritdoc/>
    public ISqlContainer CreateSqlContainer(string? query = null)
    {
        ValidateCanCreateContainer();
        return SqlContainer.Create((IDatabaseContext)this, query, ResolveSqlContainerLogger());
    }

    /// <inheritdoc/>
    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value,
        ParameterDirection direction = ParameterDirection.Input)
    {
        var p = DialectCore.CreateDbParameter(name, type, value);
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
        return DialectCore.WrapObjectName(name);
    }

    /// <inheritdoc/>
    public string MakeParameterName(DbParameter dbParameter)
    {
        return DialectCore.MakeParameterName(dbParameter);
    }

    /// <inheritdoc/>
    public string MakeParameterName(string parameterName)
    {
        return DialectCore.MakeParameterName(parameterName);
    }

    /// <inheritdoc/>
    public string GenerateRandomName(int length = 5, int parameterNameMaxLength = 30)
    {
        return DialectCore.GenerateRandomName(length, parameterNameMaxLength);
    }

    /// <inheritdoc/>
    public virtual string QuotePrefix => DialectCore.QuotePrefix;

    /// <inheritdoc/>
    public virtual string QuoteSuffix => DialectCore.QuoteSuffix;

    /// <inheritdoc/>
    public virtual bool SupportsInsertReturning => DialectCore.SupportsInsertReturning;

    /// <inheritdoc/>
    public virtual string CompositeIdentifierSeparator => DialectCore.CompositeIdentifierSeparator;

    /// <inheritdoc/>
    public virtual int MaxOutputParameters => (DialectCore as SqlDialect)?.MaxOutputParameters ?? 0;

    /// <summary>
    /// Exposes the active dialect for internal consumers.
    /// </summary>
    public ISqlDialect Dialect => DialectCore;
}
