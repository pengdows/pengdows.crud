// =============================================================================
// FILE: DatabaseContext.cs
// PURPOSE: Primary entry point for database operations - manages connections,
//          transactions, SQL execution, and dialect detection.
//
// AI SUMMARY:
// - This is the main class users create to interact with a database.
// - Key responsibilities:
//   * Connection management (pooling, open late/close early philosophy)
//   * Transaction creation (BeginTransaction returns TransactionContext)
//   * SQL container creation (CreateSqlContainer for building queries)
//   * Dialect detection (auto-detects PostgreSQL, SQL Server, etc.)
//   * Metrics collection (connection counts, timings)
// - Connection modes (DbMode):
//   * Standard - ephemeral connections per operation (recommended for production)
//   * KeepAlive - sentinel connection + ephemeral work connections
//   * SingleWriter - governor-based single writer policy with ephemeral connections (for SQLite file mode)
//   * SingleConnection - all work through one connection (for :memory: SQLite)
// - Thread-safe: concurrent operations are supported in all modes.
// - Singleton per connection string: register in DI as singleton.
// - Partial class split across multiple files:
//   * DatabaseContext.cs - Core properties and disposal
//   * DatabaseContext.Initialization.cs - Constructor and setup
//   * DatabaseContext.ConnectionLifecycle.cs - Connection acquisition/release
//   * DatabaseContext.Commands.cs - SqlContainer logger hook (creation lives in ContextBase)
//   * DatabaseContext.Transactions.cs - Transaction handling
//   * DatabaseContext.Metrics.cs - Performance metrics
// =============================================================================

#region

using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.dialects;
using pengdows.crud.@internal;
using pengdows.crud.isolation;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using pengdows.crud.strategies.connection;
using pengdows.crud.strategies.proc;
using pengdows.crud.metrics;

#endregion

namespace pengdows.crud;

/// <summary>
/// Primary database context for connection management, transaction handling, and SQL execution.
/// </summary>
/// <remarks>
/// <para><strong>Terminology:</strong></para>
/// <para>
/// <c>DatabaseContext</c> is not equivalent to Entity Framework's <c>DbContext</c>.
/// It is a <b>singleton execution coordinator</b> bound to a specific provider + connection string.
/// </para>
/// <para>
/// <strong>Concurrent callers are supported:</strong>
/// Standard mode: parallel operations using ephemeral connections.
/// KeepAlive mode: identical to Standard; additionally keeps one idle read connection open to prevent the DB from unloading.
/// SingleConnection mode: all operations share one persistent connection and serialize on a shared lock.
/// SingleWriter uses the governor to serialize writes without a persistent connection.
/// APIs returning <see cref="wrappers.ITrackedReader"/> hold a connection lease until disposed.
/// </para>
/// <para><strong>Lifetime:</strong></para>
/// <para>
/// Register <c>DatabaseContext</c> as a <b>singleton per unique connection string</b>.
/// This is required for modes that maintain persistent connections (e.g. KeepAlive, SingleConnection).
/// </para>
///
/// <para><strong>Concurrency contract:</strong></para>
/// <list type="bullet">
///   <item>
///     <description>
///     <b>Standard:</b> concurrent calls are allowed; each operation uses an ephemeral provider connection.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>KeepAlive:</b> concurrent calls are allowed; a pinned sentinel connection prevents unload, but work still uses
///     ephemeral provider connections.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>SingleWriter:</b> identical to Standard; governor caps writable connections to 1 concurrent writer and 0 writers on read-only connections; writer-starvation-prevention turnstile enabled.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>SingleConnection:</b> all operations serialize on the single pinned connection.
///     </description>
///   </item>
/// </list>
///
/// <para><strong>Locking model:</strong></para>
/// <para>
/// The context itself does not act as the serialization primitive. Serialization happens at the <b>connection lock</b>
/// returned by the tracked connection. Shared connections use a real lock; ephemeral connections use a no-op lock.
/// </para>
///
/// <para><strong>Callbacks / re-entrancy:</strong></para>
/// <para>
/// Do not call back into the same <c>DatabaseContext</c> instance from metrics/event handlers. Treat callbacks as observers.
/// </para>
///
/// </remarks>
public partial class DatabaseContext : ContextBase, IDatabaseContext, IContextIdentity, ISqlDialectProvider,
    IMetricsCollectorAccessor, IInternalConnectionProvider, ITypeMapAccessor
{
    private readonly DbProviderFactory _factory = null!;
    private DbDataSource? _dataSource;
    private DbDataSource? _readerDataSource;
    private readonly bool _dataSourceProvided;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<IDatabaseContext> _logger;

    /// <summary>Exposes the context's logger to connection strategies in the same assembly.</summary>
    internal ILogger Logger => _logger;
    private IConnectionStrategy _connectionStrategy = null!;
    private IProcWrappingStrategy _procWrappingStrategy = null!;
    private ProcWrappingStyle _procWrappingStyle;
    private ITrackedConnection? _connection = null;
    // PreventDatabaseUnload sentinels: one per enabled pool (writer and, when a dedicated reader
    // connection string exists, reader). Guarded by _sentinelLock; _connection mirrors the first.
    private readonly List<(ITrackedConnection Connection, ExecutionType ExecutionType)> _sentinels = new();
    private readonly object _sentinelLock = new();
    private SemaphoreSlim? _connectionOpenGate;
    private ReusableAsyncLocker? _connectionOpenLocker;
    // Only allocated for DbMode.SingleConnection. A transaction holds this for its whole
    // lifetime; ordinary non-transactional operations acquire it briefly per-command. See
    // GetSingleConnectionTransactionGate() for details.
    private SemaphoreSlim? _singleConnectionTransactionGate;

    private long _connectionCount;
    private string _connectionString = string.Empty;
    private string _readerConnectionString = string.Empty;
    private string _redactedConnectionString = string.Empty;
    private string _redactedReaderConnectionString = string.Empty;
    private readonly Action<DbConnection> _disposeHandler;
    private StateChangeEventHandler _stateChangeHandler = null!;
    private Action<ITrackedConnection> _firstOpenHandlerRw = null!;
    private Action<ITrackedConnection> _firstOpenHandlerRo = null!;
    private Func<ITrackedConnection, CancellationToken, Task> _firstOpenHandlerAsyncRw = null!;
    private Func<ITrackedConnection, CancellationToken, Task> _firstOpenHandlerAsyncRo = null!;
    private DataSourceInformation _dataSourceInfo = null!;
    private readonly SqlDialect _dialect = null!;
    private IsolationResolver _isolationResolver = null!;
    private bool _isReadConnection = true;
    private bool _isWriteConnection = true;
    private long _peakOpenConnections;

    // Additional performance counters for granular connection pool monitoring
    private long _totalConnectionsCreated;
    private long _totalConnectionsReused;
    private long _totalConnectionFailures;
    private long _totalConnectionTimeoutFailures;
    private readonly CommandPrepareMode _prepareMode;
    private readonly int? _readerPlanCacheSize;
    private bool? _rcsiPrefetch;
    private bool? _snapshotIsolationPrefetch;
    private bool _sessionSettingsDetectionCompleted;
    private string? _cachedReadOnlySessionSettings;
    private string? _cachedReadWriteSessionSettings;
    // Set to true when the corresponding DataSource has session settings baked into its
    // startup Options parameter; allows skipping the per-checkout SET round-trip.
    private bool _rwSettingsBakedIntoDataSource;
    private bool _roSettingsBakedIntoDataSource;
    private string? _connectionNamePrefixWrite;
    private string? _connectionNamePrefixRead;
    private readonly MetricsCollector? _metricsCollector;
    private readonly MetricsCollector? _readerMetricsCollector;
    private readonly MetricsCollector? _writerMetricsCollector;
    private EventHandler<DatabaseMetrics>? _metricsUpdated;
    private int _metricsHasActivity;
    private PoolGovernor? _readerGovernor;
    private PoolGovernor? _writerGovernor;
    private readonly ModeContentionStats _modeContentionStats = new();
    private readonly AttributionStats _attributionStats = new();
    private TimeSpan _poolAcquireTimeout = TimeSpan.FromSeconds(DatabaseContextConfiguration.DefaultPoolAcquireSeconds);
    private TimeSpan? _modeLockTimeout = TimeSpan.FromSeconds(DatabaseContextConfiguration.DefaultModeLockSeconds);
    private bool _effectivePoolGovernorEnabled = true;
    private bool _enableSingleWriterFairness = true;
    private SessionInitializationFailureMode _sessionInitializationFailureMode = SessionInitializationFailureMode.BestEffort;
    private int? _maxQueuedWrites;
    private int? _maxQueuedReads;
    private int? _configuredReadPoolSize;
    private int? _configuredWritePoolSize;
    private bool _explicitReadOnlyConnectionString;
    private bool _readOnlyConnectionStringTargetsSameDatabase;
    private const string DefaultApplicationName = "pengdows.crud";
    private const string ReadOnlyApplicationNameSuffix = "-ro";
    private const string WriteApplicationNameSuffix = "-rw";
    internal const int AbsoluteMaxPoolSize = 512;

    /// <inheritdoc/>
    public Guid RootId { get; } = Guid.NewGuid();

    internal DbProviderFactory Factory => _factory;

    private ReadWriteMode _readWriteMode = ReadWriteMode.ReadWrite;

    /// <inheritdoc/>
    /// <remarks>
    /// Write-once, set from <see cref="configuration.DatabaseContextConfiguration.ReadWriteMode"/>
    /// during construction (see Initialization.cs) - deliberately not changeable after that.
    /// _isReadConnection/_isWriteConnection are baked into the connection string, pool sizing,
    /// and connection-strategy selection at construction (Initialization.cs), none of which
    /// re-run on a later change, so honoring one here would desync those from this flag rather
    /// than actually reconfigure anything. The public setter shipped in 2.0.5 and is retained only
    /// for binary compatibility: it does nothing once the value has been set at construction.
    /// </remarks>
    public ReadWriteMode ReadWriteMode
    {
        get => _readWriteMode;
        [Obsolete("ReadWriteMode is fixed at construction; set DatabaseContextConfiguration.ReadWriteMode instead. Assigning it after construction has no effect.", false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        set
        {
            // Intentionally ignored: the value was already set during construction.
        }
    }

    private void InitializeReadWriteMode(ReadWriteMode value)
    {
        _readWriteMode = value == ReadWriteMode.WriteOnly ? ReadWriteMode.ReadWrite : value;
        _isReadConnection = (_readWriteMode & ReadWriteMode.ReadOnly) == ReadWriteMode.ReadOnly;
        _isWriteConnection = (_readWriteMode & ReadWriteMode.WriteOnly) == ReadWriteMode.WriteOnly;
        if (_isWriteConnection)
        {
            //write connection implies read connection
            _isReadConnection = true;
        }
    }

    /// <inheritdoc/>
    public string Name { get; private set; }

    /// <inheritdoc/>
    public string ConnectionString => _redactedConnectionString;

    internal string RawConnectionString => _connectionString;

    internal string RawReaderConnectionString => _readerConnectionString;

    /// <summary>
    /// Gets the DbDataSource if one was provided (e.g., NpgsqlDataSource).
    /// When available, provides better performance through shared prepared statement caching.
    /// Null if using traditional DbProviderFactory approach.
    /// </summary>
    /// <inheritdoc/>
    [Obsolete("DatabaseContext.DataSource bypasses pengdows.crud connection governance; use the context execution APIs instead.", false)]
    public DbDataSource? DataSource => _dataSource;

    /// <inheritdoc/>
    public bool IsReadOnlyConnection => _isReadConnection && !_isWriteConnection;

    internal bool ShouldUseReadOnlyForReadIntent()
    {
        if (ReadWriteMode == ReadWriteMode.ReadOnly)
        {
            return true;
        }

        // DuckDB read-only connections can lock out concurrent writers when sharing the same
        // file. This safety rule must be evaluated before any explicit ReadOnlyConnectionString
        // is honored — an explicit reader connection string does not change DuckDB's
        // file-locking behavior, so it must not bypass this guard.
        if (_dataSourceInfo?.Product == SupportedDatabase.DuckDB)
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public bool RCSIEnabled { get; private set; }

    /// <inheritdoc/>
    public bool SnapshotIsolationEnabled { get; private set; }

    /// <inheritdoc/>
    public IReadOnlySet<IsolationLevel> GetSupportedIsolationLevels() => _isolationResolver.GetSupportedLevels();

    /// <summary>
    /// Returns a no-op locker.
    /// </summary>
    /// <remarks>
    /// Context-level locking is intentionally a no-op. Serialization happens at the connection level:
    /// connections returned by <c>GetConnection(...)</c> provide the real lock when a mode uses shared/pinned connections.
    /// </remarks>
    internal ILockerAsync GetLockInternal()
    {
        ThrowIfDisposed();
        return NoOpAsyncLocker.Instance;
    }

    ILockerAsync IInternalConnectionProvider.GetLock()
    {
        return GetLockInternal();
    }


    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
    /// <inheritdoc/>
    public DbMode ConnectionMode { get; private set; }


    internal ITypeMapRegistry TypeMapRegistry { get; }

    ITypeMapRegistry ITypeMapAccessor.TypeMapRegistry => TypeMapRegistry;

    /// <inheritdoc/>
    public IDataSourceInformation DataSourceInfo => _dataSourceInfo;

    /// <inheritdoc/>
    /// <inheritdoc/>
    public string GetBaseSessionSettings() => _dialect.GetBaseSessionSettings(null);

    /// <inheritdoc/>
    public string GetReadOnlySessionSettings() => _dialect.GetReadOnlySessionSettings();

    /// <inheritdoc/>
    public SupportedDatabase Product => _dataSourceInfo?.Product ?? SupportedDatabase.Unknown;

    internal bool RequiresSerializedOpen { get; private set; }

    // ProcWrappingStyle is defined below with a setter to update strategy
    /// <inheritdoc/>
    public int MaxParameterLimit => _dataSourceInfo.MaxParameterLimit;

    /// <inheritdoc/>
    public override int MaxOutputParameters => _dataSourceInfo.MaxOutputParameters;

    /// <inheritdoc/>
    public long PeakOpenConnections => Interlocked.Read(ref _peakOpenConnections);

    /// <inheritdoc/>
    public long NumberOfOpenConnections => Interlocked.Read(ref _connectionCount);

    /// <inheritdoc/>
    public int? ReaderPlanCacheSize => _readerPlanCacheSize;

    /// <inheritdoc/>
    public override string CompositeIdentifierSeparator => _dataSourceInfo.CompositeIdentifierSeparator;

    /// <inheritdoc/>
    public CommandPrepareMode PrepareMode => _prepareMode;

    internal void AssertIsReadConnection()
    {
        if (!_isReadConnection)
        {
            throw new InvalidOperationException("The connection is not read connection.");
        }
    }

    internal void AssertIsWriteConnection()
    {
        if (!_isWriteConnection)
        {
            throw new InvalidOperationException("The connection is not write connection.");
        }
    }


    /// <remarks>
    /// Write-once, set from the detected dialect's real ProcWrappingStyle after DB detection
    /// completes (DatabaseContext.Initialization.cs assigns the backing field directly at both
    /// of its detection sites - the inline Standard-mode detection and the main constructor).
    /// IDatabaseContext.ProcWrappingStyle is get-only. The public setter shipped in 2.0.5 and is
    /// retained only for binary compatibility: it does nothing once the value has been set at
    /// construction. Test fixtures that need to force a style without real detection use
    /// <see cref="OverrideProcWrappingStyle"/>.
    /// </remarks>
    public ProcWrappingStyle ProcWrappingStyle
    {
        get => _procWrappingStyle;
        [Obsolete("ProcWrappingStyle is detected from the database at construction. Assigning it after construction has no effect.", false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        set
        {
            // Intentionally ignored: the value was already set during construction.
        }
    }

    internal void OverrideProcWrappingStyle(ProcWrappingStyle value)
    {
        _procWrappingStyle = value;
        _procWrappingStrategy = ProcWrappingStrategyFactory.Create(value);
    }

    internal IProcWrappingStrategy ProcWrappingStrategy => _procWrappingStrategy;

    // BP-110 (3.0 CORE-026): set once a governor fails to drain during disposal, so neither
    // disposal pass tears down data sources an outstanding lease may still depend on.
    private bool _sharedResourceDisposalDeferred;

    private void DisposeOwnedDataSources()
    {
        var primaryOwned = _dataSourceProvided ? null : _dataSource;
        var readerOwned = _readerDataSource;

        if (ReferenceEquals(readerOwned, primaryOwned))
        {
            readerOwned = null;
        }

        if (_dataSourceProvided && ReferenceEquals(readerOwned, _dataSource))
        {
            readerOwned = null;
        }

        try
        {
            primaryOwned?.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            readerOwned?.Dispose();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _dataSource = null;
            _readerDataSource = null;
        }
    }

    private async ValueTask DisposeOwnedDataSourcesAsync()
    {
        var primaryOwned = _dataSourceProvided ? null : _dataSource;
        var readerOwned = _readerDataSource;

        if (ReferenceEquals(readerOwned, primaryOwned))
        {
            readerOwned = null;
        }

        if (_dataSourceProvided && ReferenceEquals(readerOwned, _dataSource))
        {
            readerOwned = null;
        }

        try
        {
            if (primaryOwned is IAsyncDisposable ad)
            {
                await ad.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                primaryOwned?.Dispose();
            }
        }
        catch
        {
            // ignore
        }

        try
        {
            if (readerOwned is IAsyncDisposable rd)
            {
                await rd.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                readerOwned?.Dispose();
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            _dataSource = null;
            _readerDataSource = null;
        }
    }

    protected override void DisposeManaged()
    {
        if (_metricsCollector != null)
        {
            _metricsCollector.MetricsChanged -= OnMetricsCollectorUpdated;
        }

        DisposePersistentConnections();

        try
        {
            _connectionOpenLocker?.Dispose();
            _connectionOpenGate?.Dispose();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _connectionOpenLocker = null;
            _connectionOpenGate = null;
        }

        // BP-110 (3.0 CORE-026): if a governor timed out draining, a lease may still be
        // genuinely outstanding — do not dispose data sources that outstanding work may depend
        // on. Leaked rather than corrupted is the safe default; the context itself is still
        // fully disposed either way.
        //
        // The deferral is sticky: DisposeManagedAsync ends with base.DisposeManagedAsync(),
        // whose default implementation re-enters this method. On that second pass the
        // governors are already null (so they report "drained"), and without the flag the
        // data sources would be disposed anyway.
        if (DisposePoolGovernors() && !_sharedResourceDisposalDeferred)
        {
            DisposeOwnedDataSources();
        }
        else if (!_sharedResourceDisposalDeferred)
        {
            _sharedResourceDisposalDeferred = true;
            _logger.LogWarning(
                "Deferring data-source disposal: a pool governor did not drain before the " +
                "disposal timeout, so a lease may still be genuinely outstanding.");
        }

        base.DisposeManaged();
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        if (_metricsCollector != null)
        {
            _metricsCollector.MetricsChanged -= OnMetricsCollectorUpdated;
        }

        await DisposePersistentConnectionsAsync().ConfigureAwait(false);

        try
        {
            _connectionOpenLocker?.Dispose();
            _connectionOpenGate?.Dispose();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _connectionOpenLocker = null;
            _connectionOpenGate = null;
        }

        // See DisposeManaged's sync counterpart for why this is conditional.
        if (await DisposePoolGovernorsAsync().ConfigureAwait(false) && !_sharedResourceDisposalDeferred)
        {
            await DisposeOwnedDataSourcesAsync().ConfigureAwait(false);
        }
        else if (!_sharedResourceDisposalDeferred)
        {
            _sharedResourceDisposalDeferred = true;
            _logger.LogWarning(
                "Deferring data-source disposal: a pool governor did not drain before the " +
                "disposal timeout, so a lease may still be genuinely outstanding.");
        }

        await base.DisposeManagedAsync().ConfigureAwait(false);
    }

    /// <returns>
    /// True if every governor drained (or was never engaged) within the timeout; false if at
    /// least one governor timed out — callers must treat that as "do not tear down shared
    /// resources yet."
    /// </returns>
    private bool DisposePoolGovernors()
    {
        var readerGovernor = _readerGovernor;
        var writerGovernor = _writerGovernor;
        _readerGovernor = null;
        _writerGovernor = null;

        var writerDrained = DisposeGovernorAfterDrain(writerGovernor);
        var readerDrained = DisposeGovernorAfterDrain(readerGovernor);
        return writerDrained && readerDrained;
    }

    private async ValueTask<bool> DisposePoolGovernorsAsync()
    {
        var readerGovernor = _readerGovernor;
        var writerGovernor = _writerGovernor;
        _readerGovernor = null;
        _writerGovernor = null;

        var writerDrained = await DisposeGovernorAfterDrainAsync(writerGovernor).ConfigureAwait(false);
        var readerDrained = await DisposeGovernorAfterDrainAsync(readerGovernor).ConfigureAwait(false);
        return writerDrained && readerDrained;
    }

    private bool DisposeGovernorAfterDrain(PoolGovernor? governor)
    {
        if (governor == null)
        {
            return true;
        }

        try
        {
            governor.WaitForDrainAsync(_poolAcquireTimeout).GetAwaiter().GetResult();
            governor.Dispose();
            return true;
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Timed out waiting for {GovernorLabel} governor to drain during disposal.", governor.Label);
            return false;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Canceled while waiting for {GovernorLabel} governor to drain during disposal.", governor.Label);
            return false;
        }
    }

    private async ValueTask<bool> DisposeGovernorAfterDrainAsync(PoolGovernor? governor)
    {
        if (governor == null)
        {
            return true;
        }

        try
        {
            await governor.WaitForDrainAsync(_poolAcquireTimeout).ConfigureAwait(false);
            governor.Dispose();
            return true;
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Timed out waiting for {GovernorLabel} governor to drain during async disposal.", governor.Label);
            return false;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Canceled while waiting for {GovernorLabel} governor to drain during async disposal.", governor.Label);
            return false;
        }
    }

    protected override ISqlDialect DialectCore => _dialect;

    /// <summary>
    /// BP-110 (3.0 CORE-025): rejects container creation on a disposed context as early as
    /// possible. Execution-time checks (see GetStandardConnectionWithExecutionType) still guard
    /// the actual connection-acquisition path for a container created before disposal completes.
    /// </summary>
    protected override void ValidateCanCreateContainer()
    {
        ThrowIfDisposed();
    }

    /// <inheritdoc/>
    public new ISqlDialect Dialect => _dialect;

    ISqlDialect ISqlDialectProvider.Dialect => _dialect;
}
