#region

using System.Data;
using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;
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
    /// Global identifier used for tracing or tying back to instrumentation.
    /// </summary>
    Guid RootId { get; }

    /// <summary>
    /// Intended read/write posture of this context.
    /// </summary>
    ReadWriteMode ReadWriteMode { get; }

    /// <summary>
    /// Gets the base connection string for this context.
    /// </summary>
    string ConnectionString { get; }

    /// <summary>
    /// Human-readable name assigned to the context for diagnostics/scoping.
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Gets the DbDataSource if one was provided (e.g., NpgsqlDataSource).
    /// When available, provides better performance through shared prepared statement caching.
    /// Null if using traditional DbProviderFactory approach.
    /// </summary>
    DbDataSource? DataSource { get; }

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
    ProcWrappingStyle ProcWrappingStyle { get; }

    /// <summary>
    /// The hard limit of parameters this provider supports per statement.
    /// </summary>
    int MaxParameterLimit { get; }

    /// <summary>
    /// The hard limit of output parameters this provider supports per statement.
    /// </summary>
    int MaxOutputParameters { get; }

    /// <summary>
    /// The hard limit of output parameters this provider supports per statement.
    /// </summary>
    /// <remarks>
    /// Intentionally not part of the public interface to preserve compatibility.
    /// Query the active dialect for limits when needed.
    /// </remarks>
    // int MaxOutputParameters { get; }

    /// <summary>
    /// Current number of open connections. Usually 0 for DbMode.Standard, 1 otherwise.
    /// </summary>
    long NumberOfOpenConnections { get; }

    /// <summary>
    /// Snapshot of metrics collected for this context.
    /// </summary>
    DatabaseMetrics Metrics { get; }

    /// <summary>
    /// Raised whenever the metrics collector records a new observation.
    /// Subscribers receive the latest snapshot for the context.
    /// </summary>
    event EventHandler<DatabaseMetrics> MetricsUpdated;

    /// <summary>
    /// Detected database product (e.g., PostgreSQL, Oracle).
    /// </summary>
    SupportedDatabase Product { get; }

    /// <summary>
    /// Peak observed number of concurrently open connections. Used for tuning.
    /// </summary>
    long PeakOpenConnections { get; }

    /// <summary>
    /// The raw database product name (from metadata).
    /// </summary>
    string DatabaseProductName => DataSourceInfo.DatabaseProductName;

    /// <summary>
    /// Whether cmd.Prepare() is supported and should be used.
    /// </summary>
    bool PrepareStatements => DataSourceInfo.PrepareStatements;

    /// <summary>
    /// Override to force manual prepare on or off for all commands.
    /// When set, this overrides the dialect's PrepareStatements setting.
    /// </summary>
    bool? ForceManualPrepare { get; }

    /// <summary>
    /// When true, disables prepare for all commands regardless of dialect settings.
    /// Takes precedence over ForceManualPrepare.
    /// </summary>
    bool? DisablePrepare { get; }

    /// <summary>
    /// True if the provider supports named parameters (e.g., :name, @param).
    /// </summary>
    bool SupportsNamedParameters => DataSourceInfo.SupportsNamedParameters;

    /// <summary>
    /// True if the provider supports INSERT ... RETURNING or OUTPUT clause for identity retrieval.
    /// </summary>
    bool SupportsInsertReturning { get; }

    /// <summary>
    /// Prefix used for quoting identifiers.
    /// </summary>
    string QuotePrefix { get; }

    /// <summary>
    /// Suffix used for quoting identifiers.
    /// </summary>
    string QuoteSuffix { get; }

    /// <summary>
    /// Separator between parts of a composite identifier (e.g., schema and table).
    /// </summary>
    string CompositeIdentifierSeparator { get; }

    /// <summary>
    /// Wraps the provided identifier using the current dialect's quoting rules.
    /// </summary>
    /// <param name="name">The identifier to wrap.</param>
    /// <returns>The wrapped identifier or an empty string if <paramref name="name"/> is null or empty.</returns>
    string WrapObjectName(string name);

    /// <summary>
    /// Formats a parameter name according to the provider's conventions.
    /// </summary>
    /// <param name="dbParameter">The parameter to format.</param>
    /// <returns>The correctly formatted parameter name.</returns>
    string MakeParameterName(DbParameter dbParameter);

    /// <summary>
    /// Formats a raw parameter name string according to provider conventions.
    /// </summary>
    /// <param name="parameterName">The base parameter name.</param>
    /// <returns>The correctly formatted parameter name.</returns>
    string MakeParameterName(string parameterName);

    /// <summary>
    /// Indicates whether this context supports read operations.
    /// </summary>
    bool IsReadOnlyConnection { get; }

    /// <summary>
    /// True if read committed snapshot isolation (RCSI) is enabled on the database.
    /// </summary>
    bool RCSIEnabled { get; }

    /// <summary>
    /// True if snapshot isolation is enabled on the database.
    /// </summary>
    bool SnapshotIsolationEnabled { get; }

    /// <summary>
    /// Returns an async-compatible lock for this context instance.
    /// This is intended for internal coordination within pengdows.crud and should not be required by consumers.
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
    /// Creates a named DbParameter with an explicit direction.
    /// </summary>
    DbParameter CreateDbParameter<T>(string? name, DbType type, T value, ParameterDirection direction);

    /// <summary>
    /// Creates a positional DbParameter (no name specified).
    /// </summary>
    DbParameter CreateDbParameter<T>(DbType type, T value);

    // /// <summary>
    // /// Returns a tracked connection for the given execution type.
    // /// Optionally reused depending on mode.
    // /// </summary>
    // ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false);


    /// <summary>
    /// Begins a transaction using the native ADO.NET IsolationLevel.
    /// Not portable across all providers.
    /// </summary>
    ITransactionContext BeginTransaction(
        IsolationLevel? isolationLevel = null,
        ExecutionType executionType = ExecutionType.Write,
        bool? readOnly = null);

    /// <summary>
    /// Begins a transaction using a portable IsolationProfile abstraction.
    /// </summary>
    ITransactionContext BeginTransaction(
        IsolationProfile isolationProfile,
        ExecutionType executionType = ExecutionType.Write,
        bool? readOnly = null);

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
    /// Returns a connection to the strategy, disposing it if necessary.
    /// </summary>
    void CloseAndDisposeConnection(ITrackedConnection? conn);

    /// <summary>
    /// Returns a connection to the strategy asynchronously, disposing it if necessary.
    /// </summary>
    ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? conn);
}
