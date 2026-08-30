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
//   * PreventDatabaseUnload - passive sentinel connection(s) + ephemeral work connections
//   * SingleWriter - governor-based single writer policy with ephemeral connections (for SQLite file mode)
//   * SingleConnection - all work through one connection (for :memory: SQLite)
// - Thread-safe: concurrent operations are supported in all modes.
// - Singleton per connection string: register in DI as singleton.
// - Partial class split across multiple files:
//   * DatabaseContext.cs - Core properties and disposal
//   * DatabaseContext.Initialization.cs - Constructor and setup
//   * DatabaseContext.ConnectionLifecycle.cs - Connection acquisition/release
//   * DatabaseContext.Commands.cs - SqlContainer creation
//   * DatabaseContext.Transactions.cs - Transaction handling
//   * DatabaseContext.Metrics.cs - Performance metrics
// =============================================================================

#region

using System.Data;
using System.Data.Common;
using System.Collections.Generic;
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
/// PreventDatabaseUnload mode: identical to Standard; additionally keeps one unused sentinel per materially separate pool to prevent the DB from unloading.
/// SingleConnection mode: all operations share one persistent connection and serialize on a shared lock.
/// SingleWriter uses the governor to serialize writes without a persistent connection.
/// APIs returning <see cref="wrappers.ITrackedReader"/> hold a connection lease until disposed.
/// </para>
/// <para><strong>Lifetime:</strong></para>
/// <para>
/// Register <c>DatabaseContext</c> as a <b>singleton per unique connection string</b>.
/// This is required for modes that maintain persistent connections (e.g. PreventDatabaseUnload, SingleConnection).
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
///     <b>PreventDatabaseUnload:</b> concurrent calls are allowed; passive sentinel connections prevent unload, but work still uses
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
    private DbProviderFactory _factory = null!;
    private DbDataSource? _dataSource;
    private DbDataSource? _readerDataSource;
    private bool _dataSourceProvided;
    private ILoggerFactory _loggerFactory = null!;
    private ILogger<IDatabaseContext> _logger = null!;

    /// <summary>Exposes the context's logger to connection strategies in the same assembly.</summary>
    internal ILogger Logger => _logger;
    private IConnectionStrategy _connectionStrategy = null!;
    private IProcWrappingStrategy _procWrappingStrategy = null!;
    private ProcWrappingStyle _procWrappingStyle;
    private ITrackedConnection? _connection = null;
    private readonly object _sentinelLock = new();
    private readonly List<(ITrackedConnection Connection, ExecutionType ExecutionType)> _sentinels = [];
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
    private Action<DbConnection> _disposeHandler = null!;
    private StateChangeEventHandler _stateChangeHandler = null!;
    private Action<ITrackedConnection> _firstOpenHandlerRw = null!;
    private Action<ITrackedConnection> _firstOpenHandlerRo = null!;
    private Func<ITrackedConnection, CancellationToken, Task> _firstOpenHandlerAsyncRw = null!;
    private Func<ITrackedConnection, CancellationToken, Task> _firstOpenHandlerAsyncRo = null!;
    private DataSourceInformation _dataSourceInfo = null!;
    private SqlDialect _dialect = null!;
    private IsolationResolver _isolationResolver = null!;
    private bool _isReadConnection = true;
    private bool _isWriteConnection = true;
    private long _peakOpenConnections;

    // Additional performance counters for granular connection pool monitoring
    private long _totalConnectionsCreated;
    private long _totalConnectionsReused;
    private long _totalConnectionFailures;
    private long _totalConnectionTimeoutFailures;
    private CommandPrepareMode _prepareMode;
    private int? _readerPlanCacheSize;
    // Non-nullable: InitializeInternals always assigns a concrete value (default false) before
    // returning, for every product/mode — there is no code path that reaches the outer
    // constructor with these still unset. A `bool?` here previously implied a real "not yet
    // known" state that could fall through to a live IsReadCommittedSnapshotOn/
    // IsSnapshotIsolationOn re-query; that fallback was dead code (see DatabaseContext.Initialization.cs).
    private bool _rcsiPrefetch;
    private bool _snapshotIsolationPrefetch;
    private bool _sessionSettingsDetectionCompleted;
    private string? _cachedReadOnlySessionSettings;
    private string? _cachedReadWriteSessionSettings;
    // Set to true when the corresponding DataSource has session settings baked into its
    // startup Options parameter; allows skipping the per-checkout SET round-trip.
    private bool _rwSettingsBakedIntoDataSource;
    private bool _roSettingsBakedIntoDataSource;
    private string? _connectionNamePrefixWrite;
    private string? _connectionNamePrefixRead;
    private MetricsCollector? _metricsCollector;
    private MetricsCollector? _readerMetricsCollector;
    private MetricsCollector? _writerMetricsCollector;
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
    private IReadOnlyList<string>? _uniqueConnectionStringClaims;
    private IReadOnlyList<string>? _uniqueConnectionStringWarnRegistrations;
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
    public ReadWriteMode ReadWriteMode
    {
        get => _readWriteMode;
        set
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
    }

    /// <inheritdoc/>
    public string Name { get; private set; } = string.Empty;

    // Expose original requested mode for internal strategy decisions
    /// <inheritdoc/>
    public string ConnectionString => _redactedConnectionString;

    internal string RawConnectionString => _connectionString;

    internal string RawReaderConnectionString => _readerConnectionString;

    /// <inheritdoc/>
    public bool IsReadOnlyConnection => _isReadConnection && !_isWriteConnection;

    internal bool ShouldUseReadOnlyForReadIntent()
    {
        if (ReadWriteMode == ReadWriteMode.ReadOnly)
        {
            return true;
        }

        // Some embedded, single-writer-file-lock databases (DuckDB) can have a read-only
        // connection lock out a concurrent writer sharing the same file. This safety rule must be
        // evaluated before the explicit-connection-string check that used to follow it — an
        // explicit ReadOnlyConnectionString does not change that file-locking behavior, so it
        // must not bypass this guard.
        if (_dialect.ReadOnlyConnectionsCanBlockConcurrentWriters)
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


    internal ITypeMapRegistry TypeMapRegistry { get; private set; } = null!;

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


    public ProcWrappingStyle ProcWrappingStyle
    {
        get => _procWrappingStyle;
        set
        {
            _procWrappingStyle = value;
            _procWrappingStrategy = ProcWrappingStrategyFactory.Create(value);
        }
    }

    internal IProcWrappingStrategy ProcWrappingStrategy => _procWrappingStrategy;

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

        // CORE-026: if a governor timed out draining, a lease may still be genuinely
        // outstanding — do not dispose data sources or release uniqueness claims that
        // outstanding work may depend on. Leaked rather than corrupted is the safe default;
        // the context itself is still fully disposed either way.
        if (DisposePoolGovernors())
        {
            DisposeOwnedDataSources();
            UniqueConnectionStringRegistry.ReleaseAll(this, _uniqueConnectionStringClaims);
            _uniqueConnectionStringClaims = null;
            UniqueConnectionStringRegistry.UnregisterAllForWarning(this, _uniqueConnectionStringWarnRegistrations);
            _uniqueConnectionStringWarnRegistrations = null;
        }
        else
        {
            _logger.LogWarning(
                "Deferring data-source disposal and unique-connection-string claim release: a " +
                "pool governor did not drain before the disposal timeout, so a lease may still be " +
                "genuinely outstanding.");
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
        if (await DisposePoolGovernorsAsync().ConfigureAwait(false))
        {
            await DisposeOwnedDataSourcesAsync().ConfigureAwait(false);
            UniqueConnectionStringRegistry.ReleaseAll(this, _uniqueConnectionStringClaims);
            _uniqueConnectionStringClaims = null;
            UniqueConnectionStringRegistry.UnregisterAllForWarning(this, _uniqueConnectionStringWarnRegistrations);
            _uniqueConnectionStringWarnRegistrations = null;
        }
        else
        {
            _logger.LogWarning(
                "Deferring data-source disposal and unique-connection-string claim release: a " +
                "pool governor did not drain before the disposal timeout, so a lease may still be " +
                "genuinely outstanding.");
        }

        await base.DisposeManagedAsync().ConfigureAwait(false);
    }

    /// <returns>
    /// True if every governor drained (or was never engaged) within the timeout; false if at
    /// least one governor timed out — see CORE-026's remarks on <see cref="DisposeManaged"/>
    /// for why callers must treat that as "do not tear down shared resources yet."
    /// </returns>
    private bool DisposePoolGovernors()
    {
        var readerGovernor = _readerGovernor;
        var writerGovernor = _writerGovernor;
        _readerGovernor = null;
        _writerGovernor = null;

        // CORE-027: close admission on both governors before draining either. Without this, new
        // work could still be admitted while WaitForDrainAsync is waiting, making the drain only
        // best-effort rather than a real guarantee that _inUse can only decrease from here.
        writerGovernor?.Close();
        readerGovernor?.Close();

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

        writerGovernor?.Close();
        readerGovernor?.Close();

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
    /// CORE-025: rejects container creation on a disposed context as early as possible.
    /// Execution-time checks (see GetStandardConnectionWithExecutionType) still guard the actual
    /// connection-acquisition path for a container created before disposal completes.
    /// </summary>
    protected override void ValidateCanCreateContainer()
    {
        ThrowIfDisposed();
    }

    /// <inheritdoc/>
    public new ISqlDialect Dialect => _dialect;

    ISqlDialect ISqlDialectProvider.Dialect => _dialect;
}
