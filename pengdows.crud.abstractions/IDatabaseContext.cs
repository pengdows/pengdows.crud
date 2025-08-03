#region

using System.Data;
using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.threading;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

/// <summary>
/// Represents a runtime abstraction over a database context.
/// Handles parameter creation, connection lifetime, quoting logic,
/// transaction configuration, and dialect-specific behavior.
/// </summary>
public interface IDatabaseContext : ISafeAsyncDisposableBase
{
    /// <summary>
    /// Which DbMode this connection is using (e.g., standard, single-connection).
    /// </summary>
    DbMode ConnectionMode { get; }

    /// <summary>
    /// Type mapping registry for compiled accessors, enum coercions, and JSON handlers.
    /// </summary>
    ITypeMapRegistry TypeMapRegistry { get; }

    /// <summary>
    /// Metadata gathered from connection.GetSchema and provider heuristics.
    /// </summary>
    IDataSourceInformation DataSourceInfo { get; }

    /// <summary>
    /// Provider-specific SQL preamble to unify connection behavior (e.g., SET flags).
    /// </summary>
    string SessionSettingsPreamble { get; }

    /// <summary>
    /// Stored Procedure wrapping style (CALL vs EXEC vs plain SELECT).
    /// </summary>
    ProcWrappingStyle ProcWrappingStyle { get; set; }

    /// <summary>
    /// The hard limit of parameters this provider supports per statement.
    /// </summary>
    int MaxParameterLimit { get; }

    /// <summary>
    /// The hard limit of output parameters this provider supports per statement.
    /// </summary>
    int MaxOutputParameters { get; }

    /// <summary>
    /// Current number of open connections. Usually 0 for DbMode.Standard, 1 otherwise.
    /// </summary>
    long NumberOfOpenConnections { get; }

    /// <summary>
    /// Character or string that prefixes quoted identifiers (e.g., ", `, [).
    /// </summary>
    string QuotePrefix { get; }

    /// <summary>
    /// Character or string that suffixes quoted identifiers.
    /// </summary>
    string QuoteSuffix { get; }

    /// <summary>
    /// Separator used between schema/table/column parts, typically "."
    /// </summary>
    string CompositeIdentifierSeparator { get; }

    /// <summary>
    /// Detected database product (e.g., PostgreSQL, Oracle).
    /// </summary>
    SupportedDatabase Product { get; }

    /// <summary>
    /// Max observed number of concurrently open connections. Used for tuning.
    /// </summary>
    long MaxNumberOfConnections { get; }

    /// <summary>
    /// The raw database product name (from metadata).
    /// </summary>
    string DatabaseProductName => DataSourceInfo.DatabaseProductName;

    /// <summary>
    /// Whether cmd.Prepare() is supported and should be used.
    /// </summary>
    bool PrepareStatements => DataSourceInfo.PrepareStatements;

    /// <summary>
    /// True if the provider supports named parameters (e.g., :name, @param).
    /// </summary>
    bool SupportsNamedParameters => DataSourceInfo.SupportsNamedParameters;

    /// <summary>
    /// Indicates whether this context supports read operations.
    /// </summary>
    bool IsReadOnlyConnection { get; }

    bool RCSIEnabled { get; }

    /// <summary>
    /// Returns an async-compatible lock for this context instance.
    /// </summary>
    ILockerAsync GetLock();

    /// <summary>
    /// Creates a new SQL container for building statements.
    /// </summary>
    ISqlContainer CreateSqlContainer(string? query = null);

    /// <summary>
    /// Creates a named DbParameter.
    /// </summary>
    DbParameter CreateDbParameter<T>(string? name, DbType type, T value);

    /// <summary>
    /// Creates a positional DbParameter (no name specified).
    /// </summary>
    DbParameter CreateDbParameter<T>(DbType type, T value);

    /// <summary>
    /// Returns a tracked connection for the given execution type.
    /// Optionally reused depending on mode.
    /// </summary>
    ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false);

    /// <summary>
    /// Wraps the given identifier in quote characters.
    /// </summary>
    string WrapObjectName(string name);

    /// <summary>
    /// Begins a transaction using the native ADO.NET IsolationLevel.
    /// Not portable across all providers.
    /// </summary>
    ITransactionContext BeginTransaction(IsolationLevel? isolationLevel = null);

    /// <summary>
    /// Begins a transaction using a portable IsolationProfile abstraction.
    /// </summary>
    ITransactionContext BeginTransaction(IsolationProfile isolationProfile);

    /// <summary>
    /// Returns a randomly generated, collision-safe parameter/alias name.
    /// </summary>
    string GenerateRandomName(int length = 5, int parameterNameMaxLength = 30);

    /// <summary>
    /// Throws if this context is not writable.
    /// </summary>
    void AssertIsWriteConnection();

    /// <summary>
    /// Throws if this context is not readable.
    /// </summary>
    void AssertIsReadConnection();

    /// <summary>
    /// Formats a DbParameter name for command use.
    /// </summary>
    string MakeParameterName(DbParameter dbParameter);

    /// <summary>
    /// Formats a raw parameter name for command use.
    /// </summary>
    string MakeParameterName(string parameterName);

    /// <summary>
    /// Releases a tracked connection manually (if needed).
    /// </summary>
    void CloseAndDisposeConnection(ITrackedConnection? conn);
}