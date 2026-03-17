using System.Data;
using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;
using pengdows.crud.wrappers;

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
    /// Timeout for internal mode locks (SingleWriter / SingleConnection) and
    /// transaction completion locks (Commit / Rollback). <c>null</c> means wait
    /// indefinitely. Corresponds to <see cref="configuration.IDatabaseContextConfiguration.ModeLockTimeout"/>.
    /// </summary>
    TimeSpan? ModeLockTimeout { get; }

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
    /// <remarks>
    /// Handlers must NOT call back into the context (no queries, no transactions).
    /// Always unsubscribe when the subscriber is disposed — DatabaseContext is a
    /// singleton and the event outlives short-lived handlers, causing memory leaks
    /// if handlers are not removed.
    /// </remarks>
    event EventHandler<DatabaseMetrics> MetricsUpdated;

    /// <summary>
    /// The SQL dialect in use for this context.
    /// </summary>
    ISqlDialect Dialect { get; }

    /// <summary>
    /// Detected database product (e.g., PostgreSQL, Oracle).
    /// </summary>
    SupportedDatabase Product { get; }

    /// <summary>
    /// Peak observed number of concurrently open connections. Used for tuning.
    /// </summary>
    long PeakOpenConnections { get; }

    /// <summary>
    /// Maximum size of the reader plan cache that TableGateway instances should maintain.
    /// </summary>
    int? ReaderPlanCacheSize => null;

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
    /// True if the same named parameter can appear multiple times in a single SQL statement.
    /// </summary>
    bool SupportsRepeatedNamedParameters => DataSourceInfo.SupportsRepeatedNamedParameters;

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
    /// Returns the baseline session settings SQL for the current context.
    /// These are settings that apply regardless of execution intent (e.g. syntax, quoting).
    /// </summary>
    string GetBaseSessionSettings();

    /// <summary>
    /// Returns the read-only intent session settings SQL for the current context.
    /// </summary>
    string GetReadOnlySessionSettings();

    /// <summary>
    /// True if snapshot isolation is enabled on the database.
    /// </summary>
    bool SnapshotIsolationEnabled { get; }

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

    /// <summary>
    /// Begins a transaction using the native ADO.NET IsolationLevel.
    /// Not portable across all providers.
    /// <see cref="ExecutionType.Read"/> creates a read-only transaction.
    /// </summary>
    ITransactionContext BeginTransaction(
        IsolationLevel? isolationLevel = null,
        ExecutionType executionType = ExecutionType.Write);

    /// <summary>
    /// Begins a transaction using a portable IsolationProfile abstraction.
    /// <see cref="ExecutionType.Read"/> creates a read-only transaction.
    /// </summary>
    ITransactionContext BeginTransaction(
        IsolationProfile isolationProfile,
        ExecutionType executionType = ExecutionType.Write);

    /// <summary>
    /// Begins a transaction asynchronously using the native ADO.NET IsolationLevel.
    /// <see cref="ExecutionType.Read"/> creates a read-only transaction.
    /// </summary>
    ValueTask<ITransactionContext> BeginTransactionAsync(
        IsolationLevel? isolationLevel = null,
        ExecutionType executionType = ExecutionType.Write,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a transaction asynchronously using a portable IsolationProfile abstraction.
    /// <see cref="ExecutionType.Read"/> creates a read-only transaction.
    /// </summary>
    ValueTask<ITransactionContext> BeginTransactionAsync(
        IsolationProfile isolationProfile,
        ExecutionType executionType = ExecutionType.Write,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a unique parameter name for the current operation (e.g., p1, p2, p42).
    /// </summary>
    string GenerateParameterName();

    /// <summary>
    /// Generates a random object name that conforms to the provider's identifier rules.
    /// </summary>
    string GenerateRandomName(int length = 5, int parameterNameMaxLength = 30);

}
