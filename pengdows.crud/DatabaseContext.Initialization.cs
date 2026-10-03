// =============================================================================
// FILE: DatabaseContext.Initialization.cs
// PURPOSE: DatabaseContext constructors, initialization, and configuration.
//
// AI SUMMARY:
// - Contains all DatabaseContext constructors:
//   * (connectionString, providerName) - Uses DbProviderFactories
//   * (connectionString, DbProviderFactory) - Direct factory
//   * (IDatabaseContextConfiguration, factory) - Full configuration object
//   * (IDatabaseContextConfiguration, DbDataSource, factory) - Provider DataSource
// - Initialization flow:
//   1. Parse connection string for pool settings and mode hints
//   2. Detect database product (SQL Server, PostgreSQL, etc.)
//   3. Create appropriate SQL dialect
//   4. Initialize connection strategy (Standard, PreventDatabaseUnload, etc.)
//   5. Set up metrics collector if enabled
// - Auto-detection of DbMode for embedded databases:
//   * SQLite :memory: -> SingleConnection
//   * SQLite file mode -> SingleWriter
//   * DuckDB in-memory -> appropriate mode
// - Pool governor setup for connection limiting
// - Application name handling for connection string
// - Session settings application (timeouts, isolation levels)
// =============================================================================

using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.exceptions;
using pengdows.crud.@internal;
using pengdows.crud.isolation;
using pengdows.crud.metrics;
using pengdows.crud.strategies.connection;
using pengdows.crud.strategies.proc;
using pengdows.crud.threading;
using pengdows.crud.wrappers;

namespace pengdows.crud;

/// <summary>
/// DatabaseContext partial class: Constructors and initialization methods.
/// </summary>
/// <remarks>
/// This partial contains all the constructor overloads and the initialization
/// logic that sets up the database context including dialect detection,
/// connection strategy selection, and metrics configuration.
/// </remarks>
public partial class DatabaseContext
{
    #region Constructors

    public DatabaseContext(
        string connectionString,
        string providerFactory,
        DbMode mode = DbMode.Best,
        ReadWriteMode readWriteMode = ReadWriteMode.ReadWrite,
        ILoggerFactory? loggerFactory = null,
        string? readOnlyConnectionString = null)
        : this(
            new DatabaseContextConfiguration
            {
                ProviderName = providerFactory,
                ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString)),
                ReadOnlyConnectionString = readOnlyConnectionString ?? string.Empty,
                ReadWriteMode = readWriteMode,
                DbMode = mode
            },
            DbProviderFactories.GetFactory(providerFactory ?? throw new ArgumentNullException(nameof(providerFactory))),
            loggerFactory ?? NullLoggerFactory.Instance,
            new TypeMapRegistry(),
            null)
    {
    }


    // Convenience overloads for reflection-based tests and ease of use
    public DatabaseContext(string connectionString, DbProviderFactory factory, string? readOnlyConnectionString = null)
        : this(new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            ReadOnlyConnectionString = readOnlyConnectionString ?? string.Empty,
            DbMode = DbMode.Best,
            ReadWriteMode = ReadWriteMode.ReadWrite
        },
            factory,
            NullLoggerFactory.Instance,
            new TypeMapRegistry(),
            null)
    {
    }

    internal DatabaseContext(string connectionString, DbProviderFactory factory, ITypeMapRegistry typeMapRegistry,
        string? readOnlyConnectionString = null)
        : this(new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            ReadOnlyConnectionString = readOnlyConnectionString ?? string.Empty,
            DbMode = DbMode.Best,
            ReadWriteMode = ReadWriteMode.ReadWrite
        },
            factory,
            NullLoggerFactory.Instance,
            typeMapRegistry,
            null)
    {
    }

    public DatabaseContext(
        IDatabaseContextConfiguration configuration,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory = null)
        : this(configuration, factory, loggerFactory, new TypeMapRegistry(), null)
    {
    }

    internal DatabaseContext(
        IDatabaseContextConfiguration configuration,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory,
        ITypeMapRegistry typeMapRegistry)
        : this(configuration, factory, loggerFactory, typeMapRegistry, null)
    {
    }

    internal DatabaseContext(
        string connectionString,
        DbProviderFactory factory,
        ITypeMapRegistry typeMapRegistry,
        ISqlDialect dialect)
        : this(new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            DbMode = DbMode.Best,
            ReadWriteMode = ReadWriteMode.ReadWrite
        },
            factory,
            NullLoggerFactory.Instance,
            typeMapRegistry,
            null)
    {
        _dialect = dialect as SqlDialect
            ?? throw new ArgumentException(
                $"Dialect must derive from SqlDialect; got {dialect?.GetType().Name ?? "null"}.",
                nameof(dialect));
    }

    private DatabaseContext(
        IDatabaseContextConfiguration configuration,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory,
        ITypeMapRegistry typeMapRegistry,
        DbDataSource? dataSource)
    {
        // BP-311: construction and CreateAsync share one initialization. With useAsync: false every
        // I/O step takes its blocking provider call, exactly as this constructor always did.
        InitializeCoreAsync(configuration, factory, loggerFactory, typeMapRegistry, dataSource, false,
            CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// An uninitialized instance for <see cref="CreateAsync(IDatabaseContextConfiguration, DbDataSource?, DbProviderFactory, ILoggerFactory?, ITypeMapRegistry, CancellationToken)"/>,
    /// which completes it with <see cref="InitializeAsync"/> before handing it out.
    /// </summary>
    private DatabaseContext()
    {
    }

    /// <summary>
    /// The asynchronous half of <see cref="CreateAsync(IDatabaseContextConfiguration, DbProviderFactory, ILoggerFactory?, CancellationToken)"/>:
    /// the same initialization as the constructor, opening connections and running detection
    /// probes asynchronously.
    /// </summary>
    private Task InitializeAsync(
        IDatabaseContextConfiguration configuration,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory,
        ITypeMapRegistry typeMapRegistry,
        DbDataSource? dataSource,
        CancellationToken cancellationToken)
    {
        return InitializeCoreAsync(configuration, factory, loggerFactory, typeMapRegistry, dataSource, true,
            cancellationToken);
    }

    /// <summary>
    /// The one initialization behind every constructor and <c>CreateAsync</c> (BP-311).
    /// <paramref name="useAsync"/> picks the blocking or asynchronous provider call at each I/O
    /// step (connection opens, detection probes, session settings, disposal); everything else is
    /// shared, so the two paths cannot drift apart.
    /// </summary>
    private async Task InitializeCoreAsync(
        IDatabaseContextConfiguration configuration,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory,
        ITypeMapRegistry typeMapRegistry,
        DbDataSource? dataSource,
        bool useAsync,
        CancellationToken cancellationToken)
    {
        // Outside the try: a rejected re-initialization must not run failure cleanup against the
        // live instance's resources.
        MarkInitializedOrThrow();

        ILockerAsync? initLocker = null;
        try
        {
            initLocker = GetLockInternal();
            if (useAsync)
            {
                await initLocker.LockAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                initLocker.Lock();
            }

            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (string.IsNullOrWhiteSpace(configuration.ConnectionString))
            {
                throw new ArgumentException("ConnectionString is required.", nameof(configuration.ConnectionString));
            }

            ValidateConfiguration(configuration);
            cancellationToken.ThrowIfCancellationRequested();

            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger<IDatabaseContext>();
            TypeCoercionHelper.SetLoggerIfUnset(_loggerFactory.CreateLogger(nameof(TypeCoercionHelper)));

            var normalizedReadWriteMode = configuration.ReadWriteMode;
            var normalizedReadPoolSize = configuration.MaxConcurrentReads;
            var normalizedWritePoolSize = configuration.MaxConcurrentWrites;
            NormalizePoolLimitConfiguration(
                configuration.DbMode,
                ref normalizedReadWriteMode,
                ref normalizedReadPoolSize,
                ref normalizedWritePoolSize);

            InitializeReadWriteMode(normalizedReadWriteMode);
            TypeMapRegistry = typeMapRegistry ?? throw new ArgumentNullException(nameof(typeMapRegistry));
            ConnectionMode = configuration.DbMode;
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _dataSource = dataSource;
            _readerDataSource = dataSource;
            _dataSourceProvided = dataSource != null;
            _disposeHandler = conn => { _logger.LogDebug("Connection disposed."); };
            _stateChangeHandler = (sender, args) =>
            {
                switch (args.CurrentState)
                {
                    case ConnectionState.Open:
                        _logger.LogDebug("Opening connection: " + Name);
                        UpdateMaxConnectionCount(Interlocked.Increment(ref _connectionCount));
                        break;
                    case ConnectionState.Closed when args.OriginalState != ConnectionState.Broken:
                    case ConnectionState.Broken:
                        _logger.LogDebug("Closed or broken connection: " + Name);
                        Interlocked.Decrement(ref _connectionCount);
                        break;
                }
            };
            // ExecuteSessionSettings/ExecuteSessionSettingsAsync handle their own exceptions
            // internally (log + return, or throw ConnectionException when FailClosed is
            // configured). No outer try-catch needed here for the sync handlers.
            _firstOpenHandlerRw = tc => ExecuteSessionSettings(tc, false);
            _firstOpenHandlerRo = tc => ExecuteSessionSettings(tc, true);
            // The async handlers keep a thin outer catch — NOT to re-catch session-settings
            // failures (ExecuteSessionSettingsAsync already handles those, including the
            // FailClosed throw), but purely as a safety net for the logging call inside its
            // own catch block throwing (e.g. a broken logging sink). OperationCanceledException
            // and ConnectionException (the FailClosed signal) must still propagate untouched.
            _firstOpenHandlerAsyncRw = async (tc, ct) =>
            {
                try
                {
                    await ExecuteSessionSettingsAsync(tc, false, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ConnectionException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to apply session settings on first open for {Name}", Name);
                }
            };
            _firstOpenHandlerAsyncRo = async (tc, ct) =>
            {
                try
                {
                    await ExecuteSessionSettingsAsync(tc, true, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ConnectionException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to apply session settings on first open for {Name}", Name);
                }
            };
            _prepareMode = configuration.PrepareMode;
            _readerPlanCacheSize = configuration.ReaderPlanCacheSize;
            _poolAcquireTimeout = configuration.PoolAcquireTimeout;
            _modeLockTimeout = configuration.ModeLockTimeout;
            _enableSingleWriterFairness = configuration.EnableSingleWriterFairness;
            // null keeps 2.0.5's best-effort behavior for every context on 2.0.x.
            _sessionInitializationFailureMode =
                configuration.SessionInitializationFailureMode ?? SessionInitializationFailureMode.BestEffort;
            _maxQueuedWrites = configuration.MaxQueuedWrites;
            _maxQueuedReads = configuration.MaxQueuedReads;
            _configuredReadPoolSize = normalizedReadPoolSize;
            _configuredWritePoolSize = normalizedWritePoolSize;
            if (configuration.EnableMetrics)
            {
                var options = configuration.MetricsOptions ?? MetricsOptions.Default;
                _metricsCollector = new MetricsCollector(options);
                _readerMetricsCollector = new MetricsCollector(options, _metricsCollector);
                _writerMetricsCollector = new MetricsCollector(options, _metricsCollector);
                _metricsCollector.MetricsChanged += OnMetricsCollectorUpdated;
            }

            var initialConnection = await InitializeInternalsAsync(configuration, useAsync, cancellationToken)
                .ConfigureAwait(false);

            // Build strategies now that mode is final (moved from InitializeInternals)
            _connectionStrategy = ConnectionStrategyFactory.Create(this, ConnectionMode);
            _procWrappingStrategy = ProcWrappingStrategyFactory.Create(_procWrappingStyle);

            // Delegate dialect detection to the strategy
            var (dialect, dataSourceInfo) = useAsync
                ? await _connectionStrategy.HandleDialectDetectionAsync(initialConnection, _factory, _loggerFactory,
                    cancellationToken).ConfigureAwait(false)
                : _connectionStrategy.HandleDialectDetection(initialConnection, _factory, _loggerFactory);

            if (dialect != null && dataSourceInfo != null)
            {
                _dialect = dialect as SqlDialect
                           ?? throw new InvalidOperationException(
                               $"Dialect returned by dialect detection must derive from SqlDialect; got {dialect.GetType().Name}.");
                _dataSourceInfo = (DataSourceInformation)dataSourceInfo;
            }
            else
            {
                // Fall back to a safe SQL-92 dialect when detection fails
                var logger = _loggerFactory.CreateLogger<SqlDialect>();
                _dialect = new Sql92Dialect(_factory, logger);
                _dialect.InitializeUnknownProductInfo();
                _dataSourceInfo = new DataSourceInformation(_dialect);
            }

            _sessionSettingsDetectionCompleted = true;

            Name = _dataSourceInfo.DatabaseProductName;
            _procWrappingStyle = _dataSourceInfo.ProcWrappingStyle;
            if (_dialect is SqlDialect { RequiresSerializedConnectionOpen: true })
            {
                RequiresSerializedOpen = true;
                _connectionOpenGate = new SemaphoreSlim(1, 1);
            }

            if (ConnectionMode == DbMode.SingleConnection)
            {
                // DbMode.SingleConnection shares one physical connection across the entire
                // context. A transaction acquires this gate for its whole lifetime (Begin through
                // Commit/Rollback/Dispose) so every other operation — another transaction attempt,
                // or an ordinary non-transactional command — correctly waits its turn instead of
                // racing directly against the connection or silently executing while a transaction
                // is mid-flight (risking absorption into that transaction's uncommitted scope).
                // Separate from the connection's own per-command RealAsyncLocker (TrackedConnection
                // .GetLock()) so a transaction holding this gate never deadlocks against its own
                // commands, which still acquire that other lock as normal.
                _singleConnectionTransactionGate = new SemaphoreSlim(1, 1);
            }

            // Apply pooling defaults now that we have the final mode and dialect
            var builder = GetFactoryConnectionStringBuilder(_connectionString);
            _connectionString = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
                _connectionString,
                Product,
                ConnectionMode,
                _dialect?.SupportsExternalPooling ?? false,
                _dialect?.PoolingSettingName,
                builder);

            var effectiveApplicationName = ResolveApplicationName(configuration.ApplicationName);

            // Apply application name if configured or required for read/write pool splitting
            _connectionString = ConnectionPoolingConfiguration.ApplyApplicationName(
                _connectionString,
                effectiveApplicationName,
                _dialect?.ApplicationNameSettingName,
                builder, logger: _logger);

            if (ConnectionMode is DbMode.SingleWriter or DbMode.SingleConnection)
            {
                _connectionString = ConnectionPoolingConfiguration.StripPoolingSetting(
                    _connectionString,
                    _dialect?.PoolingSettingName, logger: _logger);
            }

            InitializeReadOnlyConnectionResources(configuration, effectiveApplicationName);

            // PRE-COMPUTE SESSION SETTINGS: computed after app name is resolved so dialects that
            // embed the application name in session SQL (e.g. Oracle DBMS_APPLICATION_INFO) get
            // the correct rw/ro-suffixed name. GetFinalSessionSettings produces exactly ONE
            // optimized string per pool type (baseline + intent) for a single RTT on the hot path.
            var rwAppName = string.IsNullOrWhiteSpace(effectiveApplicationName)
                ? null
                : effectiveApplicationName + WriteApplicationNameSuffix;
            var roAppName = string.IsNullOrWhiteSpace(effectiveApplicationName)
                ? null
                : effectiveApplicationName + ReadOnlyApplicationNameSuffix;
            _cachedReadWriteSessionSettings = _dialect.GetFinalSessionSettings(readOnly: false, rwAppName);
            _cachedReadOnlySessionSettings  = _dialect.GetFinalSessionSettings(readOnly: true,  roAppName);

            // Validate read-only connection if an explicit RO connection string was provided
            if (!string.IsNullOrWhiteSpace(configuration.ReadOnlyConnectionString) &&
                HasDedicatedReadConnectionString())
            {
                await TestConnectAsync(_readerConnectionString, "ReadOnlyValidation", "ReadOnly", useAsync,
                    cancellationToken).ConfigureAwait(false);
            }

            if (useAsync)
            {
                await InitializePoolGovernorsAsync(true, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                InitializePoolGovernors();
            }

            if (initialConnection != null)
            {
                RCSIEnabled = _rcsiPrefetch ?? _dialect!.IsReadCommittedSnapshotOn(initialConnection);
                SnapshotIsolationEnabled =
                    _snapshotIsolationPrefetch ?? _dialect!.IsSnapshotIsolationOn(initialConnection);
            }
            else
            {
                RCSIEnabled = false;
                SnapshotIsolationEnabled = false;
            }

            // Special case: SingleConnection's pinned connection opened before detection.
            // PreventDatabaseUnload sentinel doesn't need settings — it's never used for work.
            if (ConnectionMode == DbMode.SingleConnection)
            {
                var target = initialConnection ?? PersistentConnection;
                if (target != null)
                {
                    try
                    {
                        if (useAsync)
                        {
                            await ExecuteSessionSettingsAsync(target, IsReadOnlyConnection, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            ExecuteSessionSettings(target, IsReadOnlyConnection);
                        }
                    }
                    catch
                    {
                        // A rejected/failed construction never returns an object for the caller
                        // to Dispose. For SingleConnection mode `target` is typically the
                        // already-open, already-owned PersistentConnection — undisposed, that's a
                        // real connection leak on every FailClosed session-settings failure.
                        target.Dispose();
                        if (ReferenceEquals(target, PersistentConnection))
                        {
                            SetPersistentConnection(null);
                        }

                        throw;
                    }
                }
            }

            // For Standard and SingleWriter modes, dispose the connection after dialect initialization is complete
            if (ConnectionMode is DbMode.Standard or DbMode.SingleWriter && initialConnection != null)
            {
                if (useAsync)
                {
                    await initialConnection.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    initialConnection.Dispose();
                }

                // Reset counters to "fresh" state after initialization probe
                Interlocked.Exchange(ref _connectionCount, 0);
                Interlocked.Exchange(ref _peakOpenConnections, 0);
            }

            _isolationResolver = new IsolationResolver(Product, RCSIEnabled, SnapshotIsolationEnabled);

            // BP-301: always-on duplicate warning, and the opt-in hard check. A failed claim
            // rolls back its own keys and throws; the catch below releases everything else.
            var connectionStringKeys = ComputeConnectionStringKeys(configuration);
            _uniqueConnectionStringWarnRegistrations =
                UniqueConnectionStringRegistry.RegisterAllForWarning(this, connectionStringKeys, _logger);
            if (configuration.EnforceUniqueConnectionString)
            {
                _uniqueConnectionStringClaims = UniqueConnectionStringRegistry.ClaimAll(this, connectionStringKeys);
            }
        }
        catch (Exception e)
        {
            _logger?.LogError(e, "DatabaseContext construction failed.");
            // A failed constructor never returns an object for the caller to Dispose, so release
            // what construction already opened or created: sentinels / the persistent connection
            // (CONFIRMED live on Db2: a failed PreventDatabaseUnload construction left its writer
            // sentinel open and exhausted a one-connection pool for every later context), owned
            // data sources, and governors.
            ReleaseResourcesAfterFailedConstruction();
            throw;
        }
        finally
        {
            if (initLocker is IAsyncDisposable iad)
            {
                if (useAsync)
                {
                    await iad.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    iad.DisposeAsync().GetAwaiter().GetResult();
                }
            }
            else if (initLocker is IDisposable id)
            {
                id.Dispose();
            }
        }
    }

    /// <summary>
    /// Initializes a new DatabaseContext using a DbDataSource for connection creation.
    /// The DataSource provides better performance through shared prepared statement caching,
    /// while the factory is still required for creating parameters and other provider objects.
    /// </summary>
    /// <param name="configuration">Database configuration</param>
    /// <param name="dataSource">Data source for creating connections (e.g., NpgsqlDataSource)</param>
    /// <param name="factory">Provider factory for creating parameters and other objects</param>
    /// <param name="loggerFactory">Optional logger factory</param>
    public DatabaseContext(
        IDatabaseContextConfiguration configuration,
        DbDataSource dataSource,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory = null)
        : this(configuration, factory, loggerFactory, new TypeMapRegistry(), dataSource ??
                                                                             throw new ArgumentNullException(
                                                                                 nameof(dataSource)))
    {
    }

    internal DatabaseContext(
        IDatabaseContextConfiguration configuration,
        DbDataSource dataSource,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory,
        ITypeMapRegistry typeMapRegistry)
        : this(configuration, factory, loggerFactory, typeMapRegistry, dataSource ??
                                                                       throw new ArgumentNullException(
                                                                           nameof(dataSource)))
    {
    }

    #endregion

    #region CreateAsync

    /// <summary>
    /// Asynchronously creates and initializes a new <see cref="DatabaseContext"/>: the same
    /// initialization as the matching constructor, with connection opening, product detection and
    /// session setup done asynchronously, so the calling thread is not blocked (BP-311).
    /// </summary>
    /// <param name="configuration">Context configuration.</param>
    /// <param name="factory">Provider factory.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="cancellationToken">Cancels initialization; resources it opened are released.</param>
    /// <returns>The initialized context.</returns>
    public static Task<DatabaseContext> CreateAsync(
        IDatabaseContextConfiguration configuration,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(configuration, null, factory, loggerFactory, new TypeMapRegistry(), cancellationToken);
    }

    /// <summary>
    /// Asynchronously creates and initializes a new <see cref="DatabaseContext"/> that opens
    /// connections through <paramref name="dataSource"/> (e.g. an <c>NpgsqlDataSource</c>).
    /// </summary>
    /// <param name="configuration">Context configuration.</param>
    /// <param name="dataSource">Data source for connection creation.</param>
    /// <param name="factory">Provider factory for parameters and other provider objects.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="cancellationToken">Cancels initialization.</param>
    /// <returns>The initialized context.</returns>
    public static Task<DatabaseContext> CreateAsync(
        IDatabaseContextConfiguration configuration,
        DbDataSource dataSource,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        if (dataSource is null)
        {
            throw new ArgumentNullException(nameof(dataSource));
        }

        return CreateAsync(configuration, dataSource, factory, loggerFactory, new TypeMapRegistry(), cancellationToken);
    }

    /// <summary>
    /// Asynchronous counterpart of
    /// <see cref="DatabaseContext(string, string, DbMode, ReadWriteMode, ILoggerFactory?, string?)"/>.
    /// </summary>
    /// <param name="connectionString">Database connection string.</param>
    /// <param name="providerFactory">Provider invariant name registered with <see cref="DbProviderFactories"/>.</param>
    /// <param name="mode">Connection mode.</param>
    /// <param name="readWriteMode">Read/write mode.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="readOnlyConnectionString">Optional read-only connection string.</param>
    /// <param name="cancellationToken">Cancels initialization.</param>
    /// <returns>The initialized context.</returns>
    public static Task<DatabaseContext> CreateAsync(
        string connectionString,
        string providerFactory,
        DbMode mode = DbMode.Best,
        ReadWriteMode readWriteMode = ReadWriteMode.ReadWrite,
        ILoggerFactory? loggerFactory = null,
        string? readOnlyConnectionString = null,
        CancellationToken cancellationToken = default)
    {
        if (connectionString is null)
        {
            throw new ArgumentNullException(nameof(connectionString));
        }

        if (providerFactory is null)
        {
            throw new ArgumentNullException(nameof(providerFactory));
        }

        var config = new DatabaseContextConfiguration
        {
            ProviderName = providerFactory,
            ConnectionString = connectionString,
            ReadOnlyConnectionString = readOnlyConnectionString ?? string.Empty,
            ReadWriteMode = readWriteMode,
            DbMode = mode
        };

        return CreateAsync(config, null, DbProviderFactories.GetFactory(providerFactory),
            loggerFactory ?? NullLoggerFactory.Instance, new TypeMapRegistry(), cancellationToken);
    }

    /// <summary>
    /// Asynchronous counterpart of <see cref="DatabaseContext(string, DbProviderFactory, string?)"/>.
    /// </summary>
    /// <param name="connectionString">Database connection string.</param>
    /// <param name="factory">Provider factory.</param>
    /// <param name="readOnlyConnectionString">Optional read-only connection string.</param>
    /// <param name="cancellationToken">Cancels initialization.</param>
    /// <returns>The initialized context.</returns>
    public static Task<DatabaseContext> CreateAsync(
        string connectionString,
        DbProviderFactory factory,
        string? readOnlyConnectionString = null,
        CancellationToken cancellationToken = default)
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            ReadOnlyConnectionString = readOnlyConnectionString ?? string.Empty,
            DbMode = DbMode.Best,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        return CreateAsync(config, null, factory, NullLoggerFactory.Instance, new TypeMapRegistry(), cancellationToken);
    }

    internal static async Task<DatabaseContext> CreateAsync(
        IDatabaseContextConfiguration configuration,
        DbDataSource? dataSource,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory,
        ITypeMapRegistry typeMapRegistry,
        CancellationToken cancellationToken = default)
    {
        var context = new DatabaseContext();
        // A failed initialization releases what it opened itself (ReleaseResourcesAfterFailedConstruction),
        // exactly as a failed constructor does; the instance is never handed out.
        await context.InitializeAsync(configuration, factory, loggerFactory, typeMapRegistry, dataSource,
            cancellationToken).ConfigureAwait(false);
        return context;
    }

    #endregion

    #region Initialization Helper Methods

    // 1 once InitializeCoreAsync has started on this instance (BP-311, see MarkInitializedOrThrow).
    private int _initialized;

    /// <summary>
    /// Claims this instance's one-time initialization. DatabaseContext is a long-lived singleton;
    /// the fields initialization assigns can't be <c>readonly</c> now that
    /// <see cref="InitializeAsync"/> assigns them outside a constructor, so this restores the
    /// single-assignment guarantee at run time: a second initialization throws before touching
    /// anything.
    /// </summary>
    private void MarkInitializedOrThrow()
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "This DatabaseContext instance has already been initialized. DatabaseContext is a " +
                "singleton per connection string; create a new instance instead of re-initializing one.");
        }
    }

    private void SetConnectionString(string value)
    {
        if (!string.IsNullOrWhiteSpace(_connectionString) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Connection string reset attempted.");
        }

        _connectionString = value;
    }

    private async ValueTask<ITrackedConnection?> InitializeInternalsAsync(IDatabaseContextConfiguration config,
        bool useAsync, CancellationToken cancellationToken)
    {
        // 1) Persist config first
        var rawConnectionString =
            config.ConnectionString ?? throw new ArgumentNullException(nameof(config.ConnectionString));
        _connectionString = NormalizeConnectionString(rawConnectionString);
        // Min Pool Size above Max Pool Size is corrected silently (PoolMaxMinValidationTests), and
        // must be before the detection connection: SqlClient and IBM.Data.Db2 reject it on assignment.
        _connectionString = PoolingConfigReader.ClampMinPoolSizeToMax(_connectionString);

        ITrackedConnection? initConn = null;
        try
        {
            // 2) Create + open
            var initExecutionType = IsReadOnlyConnection ? ExecutionType.Read : ExecutionType.Write;
            initConn = FactoryCreateConnection(initExecutionType, _connectionString, true);
            try
            {
                if (useAsync)
                {
                    await initConn.OpenAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    initConn.Open();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new ConnectionFailedException("Failed to open database connection.", ex)
                {
                    Phase = "InitConnect",
                    Role = "ReadWrite"
                };
            }

            // 3) Detect product/capabilities once
            var product = useAsync
                ? await DatabaseDetectionService.DetectProductAsync(initConn, _factory, cancellationToken)
                    .ConfigureAwait(false)
                : DatabaseDetectionService.DetectProduct(initConn, _factory);
            var topology = useAsync
                ? await DatabaseDetectionService.DetectTopologyAsync(product, _connectionString, initConn,
                    cancellationToken).ConfigureAwait(false)
                : DatabaseDetectionService.DetectTopology(product, _connectionString, initConn);
            var isLocalDb = topology.IsLocalDb;

            // Session capabilities the dialect reads before it is initialized (SQL Server's
            // read-committed snapshot and snapshot isolation); a no-op for other dialects.
            if (initConn != null)
            {
                var prefetchDialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(product, _factory,
                    _loggerFactory.CreateLogger<SqlDialect>());
                var prefetch = await prefetchDialect.DetectSessionCapabilitiesAsync(initConn, useAsync,
                    cancellationToken).ConfigureAwait(false);
                _rcsiPrefetch = prefetch.Rcsi;
                _snapshotIsolationPrefetch = prefetch.SnapshotIsolation;
            }

            if (initConn != null && config.DbMode == DbMode.Standard)
            {
                // Only do inline detection for an explicitly requested Standard mode; every other
                // requested mode (including Best) detects via the main constructor
                _dataSourceInfo = useAsync
                    ? await DataSourceInformation.CreateAsync(initConn, _factory, _loggerFactory, cancellationToken)
                        .ConfigureAwait(false)
                    : DataSourceInformation.Create(initConn, _factory, _loggerFactory);
                _procWrappingStyle = _dataSourceInfo.ProcWrappingStyle;
                Name = _dataSourceInfo.DatabaseProductName;
            }

            // 4) Coerce ConnectionMode based on product/topology
            var requestedMode = ConnectionMode;
            ConnectionMode = CoerceMode(requestedMode, product, topology);
            var inMemoryKind = DetectInMemoryKind(product, _connectionString);

            if (ConnectionMode == DbMode.SingleConnection
                && inMemoryKind != InMemoryKind.None
                && IsReadOnlyConnection)
            {
                throw new InvalidOperationException(
                    "In-memory databases that use SingleConnection mode require a read-write context.");
            }

            // Warn on mode/database mismatches (performance, not correctness)
            WarnOnModeMismatch(ConnectionMode, product, requestedMode != ConnectionMode, isLocalDb);

            // Pooling defaults will be applied after dialect detection

            // 5) Apply provider/session settings according to final mode
            if (initConn != null)
            {
                // Note: SingleWriter no longer uses persistent connections - it uses
                // Standard lifecycle with governor policy (WriteSlots=1 + turnstile fairness)
                if (ConnectionMode == DbMode.PreventDatabaseUnload)
                {
                    // The initialization connection becomes the sentinel for the pool it was
                    // opened for (reader on a read-only context, writer otherwise).
                    RegisterSentinel(initConn, IsReadOnlyConnection ? ExecutionType.Read : ExecutionType.Write);
                    initConn = null; // context owns it now
                }
                else if (ConnectionMode == DbMode.SingleConnection)
                {
                    SetPersistentConnection(initConn);
                    initConn = null; // context owns it now
                }
                else
                {
                    // Standard and SingleWriter: no persistent connection to configure here
                }
            }

            // 7) Isolation resolver is created in the outer constructor after RCSI/Snapshot detection.

            // 8) Return the open initConn for non-persistent modes (Standard, SingleWriter; caller
            // disposes). For persistent modes it was handed to the context and null is returned.
            return initConn;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize DatabaseContext: {Message}", ex.Message);
            // Ensure no leaked connection if we're bailing
            try
            {
                initConn?.Dispose();
            }
            catch
            {
                /* ignore */
            }

            throw;
        }
    }

    // Init probes run synchronously from the constructor, asynchronously from CreateAsync (BP-311).
    private static async ValueTask<object?> ExecuteInitScalarAsync(IDbCommand command, bool useAsync,
        CancellationToken cancellationToken)
    {
        if (useAsync && command is DbCommand dbCommand)
        {
            return await dbCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }

        return command.ExecuteScalar();
    }

    private string NormalizeConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        try
        {
            var builder = GetFactoryConnectionStringBuilder(connectionString);
            if (RepresentsRawConnectionString(builder, connectionString))
            {
                return connectionString;
            }

            var normalized = builder.ConnectionString;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return connectionString;
            }

            if (SensitiveValuesStripped(connectionString, normalized))
            {
                return connectionString;
            }

            return normalized;
        }
        catch
        {
            return connectionString;
        }
    }


    // 2.0.x keeps 2.0.5's admission behavior: with no MaxQueuedReads/MaxQueuedWrites a waiting caller
    // is bounded only by PoolAcquireTimeout, never rejected for queue depth. The cap is opt-in here;
    // 3.0 makes the governor's default cap the default.
    private const int UnboundedQueueDepth = int.MaxValue;

    // useAsync: false never awaits anything incomplete, so this completes synchronously.
    private void InitializePoolGovernors() =>
        InitializePoolGovernorsAsync(false, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    private async ValueTask InitializePoolGovernorsAsync(bool useAsync, CancellationToken cancellationToken)
    {
        if (_dialect == null)
        {
            _effectivePoolGovernorEnabled = false;
            _readerGovernor = null;
            _writerGovernor = null;
            return;
        }

        _effectivePoolGovernorEnabled = ConnectionMode != DbMode.SingleConnection;

        if (!_effectivePoolGovernorEnabled)
        {
            _readerGovernor = null;
            _writerGovernor = null;
            return;
        }

        var writerConnectionString = _connectionString;
        var readerConnectionString = string.IsNullOrWhiteSpace(_readerConnectionString)
            ? writerConnectionString
            : _readerConnectionString;

        var writerConfig = PoolingConfigReader.GetEffectivePoolConfig(_dialect, writerConnectionString);
        var readerConfig = PoolingConfigReader.GetEffectivePoolConfig(_dialect, readerConnectionString);

        var rawWriterMax = ApplyAbsolutePoolLimit(ResolveGovernorMax(_configuredWritePoolSize, writerConfig));
        var rawReaderMax = ApplyAbsolutePoolLimit(ResolveGovernorMax(_configuredReadPoolSize, readerConfig));

        // Validate explicit pool sizes — negative values are always invalid.
        if (rawWriterMax.HasValue && rawWriterMax.Value < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rawWriterMax), rawWriterMax.Value,
                "Write pool MaxPoolSize must be >= 0. Use 0 to forbid write connections.");
        }

        if (rawReaderMax.HasValue && rawReaderMax.Value < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rawReaderMax), rawReaderMax.Value,
                "Read pool MaxPoolSize must be >= 0. Use 0 to forbid read connections.");
        }

        // ReadOnly contexts have no writer pool. Apply this before minimum enforcement so the
        // disabled writer is not accidentally given a provider minimum or a sentinel.
        if (!_isWriteConnection)
        {
            rawWriterMax = 0;
        }

        // PreventDatabaseUnload needs one permit for its sentinel and one for useful work.
        // Raise an enabled pool below that floor so the sentinel cannot consume all capacity.
        if (ConnectionMode == DbMode.PreventDatabaseUnload)
        {
            rawWriterMax = EnsurePreventUnloadCapacity(rawWriterMax, "writer");
            rawReaderMax = EnsurePreventUnloadCapacity(rawReaderMax, "reader");
        }

        // Caller-supplied minimums are clamped to [0, MaxPoolSize] for every mode.
        // PreventDatabaseUnload additionally raises the provider minimum to two so the
        // sentinel's permit doesn't starve ordinary work of the one remaining slot. Other
        // modes never inject an implicit minimum.
        var minPoolSizeKey = _dialect?.MinPoolSizeSettingName;
        var writerBeforeMinimum = _connectionString;
        var readerBeforeMinimum = _readerConnectionString;
        var writerMinimum = ConnectionMode == DbMode.PreventDatabaseUnload && rawWriterMax != 0 ? 2 : 0;
        _connectionString = ConnectionPoolingConfiguration.EnsureMinimumPoolSize(
            _connectionString, minPoolSizeKey, writerConfig.MinPoolSize, rawWriterMax, writerMinimum, logger: _logger);
        if (!string.IsNullOrWhiteSpace(_readerConnectionString))
        {
            var readerMinimum = ConnectionMode == DbMode.PreventDatabaseUnload && rawReaderMax != 0 ? 2 : 0;
            _readerConnectionString = ConnectionPoolingConfiguration.EnsureMinimumPoolSize(
                _readerConnectionString, minPoolSizeKey, readerConfig.MinPoolSize, rawReaderMax, readerMinimum, logger: _logger);
        }

        RebuildOwnedDataSourcesForChangedConnectionStrings(writerBeforeMinimum, readerBeforeMinimum);

        var writerKey = ComputePoolKeyHash(writerConnectionString);
        var readerKey = ComputePoolKeyHash(readerConnectionString);

        var writerLabelMax = rawWriterMax;
        var readerLabelMax = rawReaderMax;

        // SingleWriter limits the write governor to 1 concurrent slot to serialize writes
        // (prevents SQLite file locking errors). Skip this override when the write pool is
        // forbidden (rawWriterMax=0) — overriding 0→1 would incorrectly allow writes on a
        // ReadOnly context or an explicitly disabled write pool.
        if (ConnectionMode == DbMode.SingleWriter && rawWriterMax != 0)
        {
            if (_isWriteConnection && rawWriterMax.HasValue && rawWriterMax.Value != 1)
            {
                _logger.LogWarning(
                    "SingleWriter coerced the write pool size from {Requested} to 1 so the provider pool and governor stay aligned.",
                    rawWriterMax.Value);
            }
            writerLabelMax = 1;
        }

        // SingleWriter mode: create a shared turnstile for writer-preference fairness.
        // The turnstile is only shared when reader and writer target the same connection pool.
        // When a dedicated read-only connection string points to a different server (e.g. a
        // read replica), sharing the turnstile would incorrectly gate replica reads behind
        // primary writes — those operations are independent and should not compete.
        // Also skip when writes are forbidden — no writes means no turnstile needed.
        //
        // NOTE: this is deliberately NOT a comparison of the writer/reader connection-string
        // hashes (writerKey/readerKey) — those strings are intentionally mutated differently
        // for reader vs. writer during InitializeReadOnlyConnectionResources (pooling stripped
        // from the reader, an "-rw" ApplicationName suffix + MaxPoolSize=1 on the writer) even
        // when the caller supplied only one connection string, so a hash comparison is always
        // false for the common single-connection-string SingleWriter case. Whether reader and
        // writer target the same physical server is determined by whether the caller supplied
        // an explicit ReadOnlyConnectionString, not by whether the derived strings still match.
        // BP-123: an explicit ReadOnlyConnectionString that happens to equal ConnectionString
        // verbatim (raw, pre-derivation) still targets the same physical database, so it must
        // not disable sharing either.
        var sharesTurnstile = !_explicitReadOnlyConnectionString || _readOnlyConnectionStringTargetsSameDatabase;

        SemaphoreSlim? turnstile = null;
        if (ConnectionMode == DbMode.SingleWriter && _enableSingleWriterFairness && sharesTurnstile
            && _isWriteConnection)
        {
            turnstile = new SemaphoreSlim(1, 1);
        }

        _writerGovernor = CreateGovernor(
            PoolLabel.Writer,
            writerKey,
            writerLabelMax,
            null,
            false,
            _metricsCollector != null,
            turnstile: turnstile,
            holdTurnstile: true,
            ownsTurnstile: turnstile != null, // Writers hold turnstile until slot released
            maxQueueDepth: _maxQueuedWrites ?? UnboundedQueueDepth);

        _readerGovernor = CreateGovernor(
            PoolLabel.Reader,
            readerKey,
            readerLabelMax,
            null,
            false,
            _metricsCollector != null,
            turnstile: turnstile,
            holdTurnstile: false,
            ownsTurnstile: false, // Readers touch-and-release turnstile
            maxQueueDepth: _maxQueuedReads ?? UnboundedQueueDepth);

        // PreventDatabaseUnload: every sentinel holds one permit from its own pool's governor, and
        // a dedicated reader pool gets its own sentinel so it cannot unload independently.
        if (ConnectionMode == DbMode.PreventDatabaseUnload)
        {
            AttachSentinelSlotsIfNeeded();

            if (_isWriteConnection && HasDedicatedReadConnectionString())
            {
                var readSentinel = CreateSentinelConnection(ExecutionType.Read);
                try
                {
                    if (useAsync)
                    {
                        await readSentinel.OpenAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        readSentinel.Open();
                    }

                    RegisterSentinel(readSentinel, ExecutionType.Read);
                }
                catch
                {
                    readSentinel.Dispose();
                    throw;
                }
            }
        }
    }

    private async ValueTask TestConnectAsync(string connectionString, string phase, string role, bool useAsync,
        CancellationToken cancellationToken)
    {
        var isReadOnly = role == "ReadOnly";
        var executionType = isReadOnly ? ExecutionType.Read : ExecutionType.Write;
        try
        {
            var conn = FactoryCreateConnection(executionType, connectionString, true);
            if (useAsync)
            {
                try
                {
                    await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await conn.DisposeAsync().ConfigureAwait(false);
                }
            }
            else
            {
                using (conn)
                {
                    conn.Open();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ConnectionFailedException(
                $"Failed to validate {role.ToLowerInvariant()} connection.", ex)
            {
                Phase = phase,
                Role = role
            };
        }
    }

    private void InitializeReadOnlyConnectionResources(IDatabaseContextConfiguration configuration,
        string effectiveApplicationName)
    {
        _explicitReadOnlyConnectionString = !string.IsNullOrWhiteSpace(configuration.ReadOnlyConnectionString);
        _readOnlyConnectionStringTargetsSameDatabase = _explicitReadOnlyConnectionString &&
            string.Equals(configuration.ReadOnlyConnectionString, configuration.ConnectionString,
                StringComparison.OrdinalIgnoreCase);
        // 1. Derive reader connection string BEFORE adding -rw to writer so the reader
        //    does not inherit the write suffix.
        _readerConnectionString = BuildReaderConnectionString(configuration, effectiveApplicationName);

        // Strip pooling from the reader connection string only when writes are active.
        // SingleWriter + ReadOnly is functionally identical to Standard + ReadOnly (no writers
        // at all), so the reader should use normal pooled connections in that case.
        // SingleConnection always strips reader pooling (in-memory SingleConnection + ReadOnly
        // is rejected earlier, in InitializeInternals).
        if (ConnectionMode == DbMode.SingleConnection ||
            (ConnectionMode == DbMode.SingleWriter && _isWriteConnection))
        {
            _readerConnectionString = ConnectionPoolingConfiguration.StripPoolingSetting(
                _readerConnectionString,
                _dialect?.PoolingSettingName, logger: _logger);
        }

        // 2. Finalize reader connection string: apply MaxPoolSize + provider-specific
        //    DataSource settings while it still differs from the writer.
        if (_dialect != null &&
            !string.Equals(_readerConnectionString, _connectionString, StringComparison.OrdinalIgnoreCase))
        {
            var readMaxPoolSize = ResolveEffectiveMaxPoolSize(_configuredReadPoolSize, _readerConnectionString, "reader");
            var readerBuilder = GetFactoryConnectionStringBuilder(_readerConnectionString);
            _readerConnectionString = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
                _readerConnectionString,
                readMaxPoolSize,
                _dialect.MaxPoolSizeSettingName,
                overrideExisting: true,
                readerBuilder, logger: _logger);
            _readerConnectionString = _dialect.PrepareConnectionStringForDataSource(_readerConnectionString, readOnly: true);
        }

        // 3. Finalize writer connection string: -rw suffix → MaxPoolSize → provider
        //    DataSource settings.  Must happen AFTER reader derivation so the reader
        //    is not polluted with -rw.
        _connectionString = ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(
            _connectionString,
            _dialect?.ApplicationNameSettingName,
            WriteApplicationNameSuffix,
            effectiveApplicationName, logger: _logger);

        var writerBuilder = GetFactoryConnectionStringBuilder(_connectionString);
        if (!_isWriteConnection)
        {
            // ReadOnly context: writes are forbidden by the governor. When no separate
            // ReadOnlyConnectionString is configured the reader shares _connectionString,
            // so stamp the resolved read pool size here — step 2 above was skipped for
            // equal strings. When a separate read connection string exists this stamps
            // the read size onto the write string too, which is harmless and keeps it
            // validated and normalized.
            var readPoolSizeForWriter = ResolveEffectiveMaxPoolSize(_configuredReadPoolSize, _connectionString, "reader");
            _connectionString = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
                _connectionString, readPoolSizeForWriter, _dialect?.MaxPoolSizeSettingName,
                overrideExisting: true, writerBuilder, logger: _logger);
        }
        else if (ConnectionMode == DbMode.SingleWriter)
        {
            // SingleWriter: force the writer pool to exactly 1 to prevent concurrent writes.
            // Readers use a separate pool (pooling is stripped from the reader connection string),
            // so only the write slot needs to be sized here.
            _connectionString = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
                _connectionString, 1, _dialect?.MaxPoolSizeSettingName,
                overrideExisting: true, writerBuilder, logger: _logger);
        }
        else
        {
            // Standard/PreventDatabaseUnload: reader and writer always use separate ADO.NET pools
            // (differentiated via ApplicationName suffix or a dialect-specific pool-discriminator setting).
            // Stamp the resolved write size so the governor and the provider pool agree.
            // Configuration wins over connection-string, which wins over the dialect default.
            var writeMax = ResolveEffectiveMaxPoolSize(_configuredWritePoolSize, _connectionString, "writer");
            _connectionString = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
                _connectionString, writeMax, _dialect?.MaxPoolSizeSettingName,
                overrideExisting: true, writerBuilder, logger: _logger);
        }

        if (_dialect != null)
        {
            _connectionString = _dialect.PrepareConnectionStringForDataSource(_connectionString, readOnly: !_isWriteConnection);
        }

        // If suffix application was a no-op, keep reader/writer aligned so pool-key
        // hashing and DataSource reuse remain consistent.
        if (string.Equals(_readerConnectionString, _connectionString, StringComparison.OrdinalIgnoreCase))
        {
            _readerConnectionString = _connectionString;
        }

        _connectionNamePrefixWrite = ExtractApplicationName(_connectionString);
        _connectionNamePrefixRead = ExtractApplicationName(_readerConnectionString);
        if (string.Equals(_readerConnectionString, _connectionString, StringComparison.OrdinalIgnoreCase))
        {
            _connectionNamePrefixRead = _connectionNamePrefixWrite;
        }

        // 4. Both connection strings are now complete — create DataSources.
        if (!_dataSourceProvided && _factory != null && _dataSource == null)
        {
            _dataSource = TryCreateDataSource(_factory, _connectionString);
        }

        // Set baked flags only for native provider DataSources.
        // GenericDbDataSource wraps a factory and does not send startup parameters, so
        // the baked Options have no effect and the per-checkout SET must still run.
        if (_dataSource is { } writerDs && writerDs is not GenericDbDataSource
            && (_dialect?.SessionSettingsBakedIntoDataSource ?? false))
        {
            if (_isWriteConnection)
            {
                _rwSettingsBakedIntoDataSource = true;
            }
            else
            {
                _roSettingsBakedIntoDataSource = true;
            }
        }

        _readerDataSource = _dataSource;
        RefreshRedactedConnectionStrings();

        if (string.Equals(_readerConnectionString, _connectionString, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_factory != null)
        {
            var readDataSource = TryCreateDataSource(_factory, _readerConnectionString);
            if (readDataSource != null)
            {
                _readerDataSource = readDataSource;
                // Reader DataSource is always used exclusively for read-only operations.
                if (readDataSource is not GenericDbDataSource
                    && (_dialect?.SessionSettingsBakedIntoDataSource ?? false))
                {
                    _roSettingsBakedIntoDataSource = true;
                }
                return;
            }

            if (_dataSourceProvided)
            {
                _readerDataSource = null;
                _logger.LogWarning(
                    "Read-only connection string differs, but no read-only DbDataSource could be created. Falling back to factory connections for read-only operations.");
            }

            return;
        }

        if (_dataSourceProvided)
        {
            _readerDataSource = null;
            _logger.LogWarning(
                "Read-only connection string differs, but no provider factory is available. Read-only operations will reuse the provided DbDataSource.");
        }

        RefreshRedactedConnectionStrings();
    }

    /// <summary>
    /// Validates configuration fields that cannot be caught at connection time.
    /// ConnectionString is validated before this call.
    /// </summary>
    private static void ValidateConfiguration(IDatabaseContextConfiguration config)
    {
        if (config.PoolAcquireTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.PoolAcquireTimeout),
                config.PoolAcquireTimeout,
                "PoolAcquireTimeout must be greater than zero.");
        }

        if (config.MaxConcurrentReads.HasValue && config.MaxConcurrentReads.Value < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.MaxConcurrentReads),
                config.MaxConcurrentReads.Value,
                "MaxConcurrentReads must be >= 0 when specified. Use 0 to forbid read connections.");
        }

        if (config.MaxConcurrentWrites.HasValue && config.MaxConcurrentWrites.Value < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.MaxConcurrentWrites),
                config.MaxConcurrentWrites.Value,
                "MaxConcurrentWrites must be >= 0 when specified. Use 0 to forbid write connections.");
        }

        if (config.ModeLockTimeout.HasValue && config.ModeLockTimeout.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.ModeLockTimeout),
                config.ModeLockTimeout.Value,
                "ModeLockTimeout must be greater than zero when specified (use null to wait indefinitely).");
        }
    }

    /// <summary>
    /// Resolves the effective max-pool-size for a connection string following the
    /// priority chain: context configuration → explicit value already in the connection
    /// string → dialect default. A mismatch is logged and the context configuration wins.
    /// </summary>
    private int ResolveEffectiveMaxPoolSize(int? configuredMax, string connectionString, string poolLabel)
    {
        // 1. Caller-supplied configuration — highest priority; wins over anything in the connection string.
        if (configuredMax.HasValue && configuredMax.Value > 0)
        {
            var connectionStringMax = PoolingConfigReader.GetExplicitMaxPoolSize(_dialect!, connectionString);
            if (connectionStringMax.HasValue && connectionStringMax.Value != configuredMax.Value)
            {
                _logger.LogWarning(
                    "Pool size mismatch for {Pool} pool: {ConfigurationSetting}={Configured} overrides connection-string {ConnectionStringSetting}={ConnectionStringValue}; effective governor and provider Max Pool Size is {Effective}.",
                    poolLabel,
                    poolLabel == "reader" ? nameof(DatabaseContextConfiguration.MaxConcurrentReads) : nameof(DatabaseContextConfiguration.MaxConcurrentWrites),
                    configuredMax.Value,
                    _dialect!.MaxPoolSizeSettingName,
                    connectionStringMax.Value,
                    configuredMax.Value);
            }

            return EnsurePreventUnloadCapacity(configuredMax.Value, poolLabel) ?? configuredMax.Value;
        }

        // 2. Already present in the connection string.
        if (_dialect != null)
        {
            var effectiveConfig = PoolingConfigReader.GetEffectivePoolConfig(_dialect, connectionString);
            if (effectiveConfig.Source == PoolConfigSource.ConnectionString &&
                effectiveConfig.MaxPoolSize is int csMaxPoolSize)
            {
                if (csMaxPoolSize < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        _dialect.MaxPoolSizeSettingName ?? "MaxPoolSize",
                        csMaxPoolSize,
                        "MaxPoolSize in the connection string must be >= 0. Use 0 to forbid connections.");
                }

                if (csMaxPoolSize == 0)
                {
                    return _dialect.DefaultMaxPoolSize;
                }

                var limited = ApplyAbsolutePoolLimit(
                    csMaxPoolSize,
                    "connection string");
                return EnsurePreventUnloadCapacity(limited, poolLabel) ?? limited;
            }
        }

        // 3. Dialect default.
        return ApplyAbsolutePoolLimit(
            _dialect?.DefaultMaxPoolSize ?? SqlDialect.FallbackMaxPoolSize,
            "dialect default");
    }

    private int? EnsurePreventUnloadCapacity(int? maxPoolSize, string poolLabel)
    {
        if (ConnectionMode != DbMode.PreventDatabaseUnload ||
            !maxPoolSize.HasValue || maxPoolSize.Value == 0 || maxPoolSize.Value >= 2)
        {
            return maxPoolSize;
        }

        _logger.LogWarning(
            "PreventDatabaseUnload raised the {Pool} pool maximum from {Requested} to 2 so one sentinel permit and one working permit remain available.",
            poolLabel, maxPoolSize.Value);
        return 2;
    }

    private void NormalizePoolLimitConfiguration(
        DbMode mode,
        ref ReadWriteMode readWriteMode,
        ref int? configuredReadPoolSize,
        ref int? configuredWritePoolSize)
    {
        configuredReadPoolSize = ApplyAbsolutePoolLimit(
            configuredReadPoolSize,
            nameof(DatabaseContextConfiguration.MaxConcurrentReads));
        configuredWritePoolSize = ApplyAbsolutePoolLimit(
            configuredWritePoolSize,
            nameof(DatabaseContextConfiguration.MaxConcurrentWrites));

        if (readWriteMode == ReadWriteMode.ReadOnly)
        {
            if (configuredWritePoolSize.HasValue && configuredWritePoolSize.Value != 0)
            {
                _logger.LogWarning(
                    "ReadOnly mode ignores {Setting}={Configured}; writes remain forbidden.",
                    nameof(DatabaseContextConfiguration.MaxConcurrentWrites),
                    configuredWritePoolSize.Value);
            }

            configuredWritePoolSize = 0;
            return;
        }

        if (configuredWritePoolSize.HasValue && configuredWritePoolSize.Value == 0)
        {
            _logger.LogWarning(
                "{Setting}=0 promotes the context to ReadOnly mode; writes remain forbidden.",
                nameof(DatabaseContextConfiguration.MaxConcurrentWrites));
            readWriteMode = ReadWriteMode.ReadOnly;
            configuredWritePoolSize = 0;
        }
    }

    private int ApplyAbsolutePoolLimit(int value, string sourceDescription)
    {
        if (value <= AbsoluteMaxPoolSize)
        {
            return value;
        }

        _logger.LogWarning(
            "{Source} requested pool size {Requested}, which exceeds the absolute limit of {Maximum}. Coercing to {CoercedMaximum}.",
            sourceDescription,
            value,
            AbsoluteMaxPoolSize,
            AbsoluteMaxPoolSize);
        return AbsoluteMaxPoolSize;
    }

    private int? ApplyAbsolutePoolLimit(int? value)
    {
        if (!value.HasValue || value.Value <= AbsoluteMaxPoolSize)
        {
            return value;
        }

        return AbsoluteMaxPoolSize;
    }

    private int? ApplyAbsolutePoolLimit(int? value, string sourceDescription)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return ApplyAbsolutePoolLimit(value.Value, sourceDescription);
    }

    private string BuildReaderConnectionString(IDatabaseContextConfiguration configuration,
        string effectiveApplicationName)
    {
        if (_dialect == null)
        {
            return _connectionString;
        }

        var rawReadOnlyConnectionString = configuration.ReadOnlyConnectionString;
        var baseReaderConnectionString = string.IsNullOrWhiteSpace(rawReadOnlyConnectionString)
            ? _connectionString
            : NormalizeConnectionString(rawReadOnlyConnectionString);

        if (ShouldUseReadOnlyForReadIntent())
        {
            var readOnly = _dialect.GetReadOnlyConnectionString(baseReaderConnectionString);
            var usesOriginalValue = string.IsNullOrWhiteSpace(readOnly) ||
                                    string.Equals(readOnly, baseReaderConnectionString,
                                        StringComparison.OrdinalIgnoreCase);
            baseReaderConnectionString = usesOriginalValue
                ? BuildReadOnlyConnectionStringFromBase(baseReaderConnectionString)
                : readOnly;
        }

        var readerResult = ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(
            baseReaderConnectionString,
            _dialect.ApplicationNameSettingName,
            ReadOnlyApplicationNameSuffix,
            effectiveApplicationName, logger: _logger);

        // For dialects without ApplicationNameSettingName (e.g., Oracle ODP.NET), the
        // suffix is a no-op and reader/writer end up with identical connection strings,
        // sharing a single connection pool. Apply a discriminator key/value so the strings
        // differ and the provider creates separate pools for reader and writer connections.
        // Skip when the caller supplied an explicit ReadOnlyConnectionString — they already
        // manage pool isolation themselves.
        if (string.IsNullOrWhiteSpace(_dialect.ApplicationNameSettingName) &&
            string.IsNullOrWhiteSpace(rawReadOnlyConnectionString))
        {
            readerResult = ConnectionPoolingConfiguration.ApplyPoolDiscriminator(
                readerResult,
                _dialect.ReadOnlyPoolDiscriminatorSettingName,
                _dialect.ReadOnlyPoolDiscriminatorSettingValue, logger: _logger);
        }

        return readerResult;
    }

    private string ResolveApplicationName(string? configuredApplicationName)
    {
        var configured = configuredApplicationName?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var existing = ExtractApplicationName(_connectionString);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        return CanAutoGenerateApplicationName(_connectionString)
            ? ResolveDefaultApplicationName()
            : string.Empty;
    }

    private static string ResolveDefaultApplicationName()
    {
        var entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name?.Trim();
        if (!string.IsNullOrWhiteSpace(entryAssemblyName))
        {
            return entryAssemblyName;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            var processName = process.ProcessName?.Trim();
            if (!string.IsNullOrWhiteSpace(processName))
            {
                return processName;
            }
        }
        catch
        {
            // ignore process inspection failures and fall back to the library name
        }

        return DefaultApplicationName;
    }

    private bool CanAutoGenerateApplicationName(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(_dialect?.ApplicationNameSettingName) ||
            string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            if (_factory?.CreateConnectionStringBuilder() is { } providerBuilder)
            {
                providerBuilder.ConnectionString = connectionString;
                return CanUseForApplicationName(providerBuilder, connectionString);
            }
        }
        catch
        {
            return false;
        }

        try
        {
            var genericBuilder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            return CanUseForApplicationName(genericBuilder, connectionString);
        }
        catch
        {
            return false;
        }
    }

    private static bool CanUseForApplicationName(DbConnectionStringBuilder builder, string connectionString)
    {
        if (RepresentsRawConnectionString(builder, connectionString))
        {
            return false;
        }

        var normalized = builder.ConnectionString;
        return !string.IsNullOrWhiteSpace(normalized) &&
               !SensitiveValuesStripped(connectionString, normalized);
    }

    private string? ExtractApplicationName(string connectionString)
    {
        var settingName = _dialect?.ApplicationNameSettingName;
        if (string.IsNullOrWhiteSpace(settingName) || string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        try
        {
            var builder = GetFactoryConnectionStringBuilder(connectionString);
            if (RepresentsRawConnectionString(builder, connectionString))
            {
                return null;
            }

            if (builder.TryGetValue(settingName, out var value))
            {
                var appName = Convert.ToString(value)?.Trim();
                return string.IsNullOrWhiteSpace(appName) ? null : appName;
            }
        }
        catch
        {
            // ignore parse errors - no application name available
        }

        return null;
    }

    private bool HasDedicatedReadConnectionString()
    {
        return !string.IsNullOrWhiteSpace(_readerConnectionString) &&
               !string.Equals(_readerConnectionString, _connectionString, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldUseReaderConnectionString(bool readOnly)
    {
        return readOnly && HasDedicatedReadConnectionString();
    }

    private void AttachSentinelSlotsIfNeeded()
    {
        if (!_effectivePoolGovernorEnabled)
        {
            return;
        }

        foreach (var (connection, executionType) in GetSentinelSnapshot())
        {
            if (connection is TrackedConnection tracked)
            {
                var slot = AcquireSentinelSlot(executionType);
                tracked.AttachSlot(slot);
            }
        }
    }

    private PoolGovernor CreateGovernor(
        PoolLabel label,
        string poolKey,
        int? maxSlots,
        SemaphoreSlim? sharedSemaphore,
        bool disabled = false,
        bool trackMetrics = false,
        SemaphoreSlim? turnstile = null,
        bool holdTurnstile = false,
        bool ownsTurnstile = false,
        int? maxQueueDepth = null)
    {
        if (disabled || !maxSlots.HasValue)
        {
            return new PoolGovernor(label, poolKey, 0, _poolAcquireTimeout,
                disabled: true, trackMetrics: trackMetrics);
        }

        if (maxSlots.Value == 0)
        {
            // MaxPoolSize=0 means this pool is explicitly forbidden — any Acquire throws.
            return new PoolGovernor(label, poolKey, 0, _poolAcquireTimeout,
                forbidden: true, trackMetrics: trackMetrics);
        }

        return new PoolGovernor(
            label,
            poolKey,
            maxSlots.Value,
            _poolAcquireTimeout,
            disabled: false,
            trackMetrics: trackMetrics,
            sharedSemaphore: sharedSemaphore,
            turnstile: turnstile,
            holdTurnstile: holdTurnstile,
            ownsTurnstile: ownsTurnstile,
            maxQueueDepth: maxQueueDepth);
    }

    private static int? ResolveSharedMax(int? writerMax, int? readerMax)
    {
        if (!writerMax.HasValue && !readerMax.HasValue)
        {
            return null;
        }

        if (!writerMax.HasValue)
        {
            return readerMax;
        }

        if (!readerMax.HasValue)
        {
            return writerMax;
        }

        return Math.Min(writerMax.Value, readerMax.Value);
    }

    private static int? ResolveGovernorMax(int? configuredMax, PoolConfig config)
    {
        return configuredMax ?? config switch
        {
            { MaxPoolSize: int max } => max,
            _ => null
        };
    }


    private static bool AreConnectionStringsEquivalentIgnoringCredentials(
        string primary,
        string secondary,
        string? readOnlyParameter,
        string? applicationNameSettingName,
        string readOnlySuffix)
    {
        if (string.IsNullOrWhiteSpace(primary) && string.IsNullOrWhiteSpace(secondary))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(primary) || string.IsNullOrWhiteSpace(secondary))
        {
            return false;
        }

        if (!TryParseReadOnlyParameter(readOnlyParameter, out var readOnlyKey, out var readOnlyValue))
        {
            readOnlyKey = null;
            readOnlyValue = null;
        }

        if (!TryBuildNormalizedConnectionMap(primary, readOnlyKey, readOnlyValue,
                applicationNameSettingName, readOnlySuffix, out var primaryMap))
        {
            return false;
        }

        if (!TryBuildNormalizedConnectionMap(secondary, readOnlyKey, readOnlyValue,
                applicationNameSettingName, readOnlySuffix, out var secondaryMap))
        {
            return false;
        }

        if (primaryMap.Count != secondaryMap.Count)
        {
            return false;
        }

        foreach (var entry in primaryMap)
        {
            if (!secondaryMap.TryGetValue(entry.Key, out var value))
            {
                return false;
            }

            if (!string.Equals(entry.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryBuildNormalizedConnectionMap(
        string connectionString,
        string? readOnlyKey,
        string? readOnlyValue,
        string? applicationNameSettingName,
        string readOnlySuffix,
        out Dictionary<string, string> normalized)
    {
        if (ConnectionStringNormalizationCache.TryGet(connectionString, readOnlyKey, readOnlyValue,
                applicationNameSettingName, readOnlySuffix, out normalized!))
        {
            return true;
        }

        normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DbConnectionStringBuilder builder;
        try
        {
            builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        }
        catch
        {
            return false;
        }

        foreach (var keyObj in builder.Keys)
        {
            var key = keyObj?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (ShouldIgnoreKey(key))
            {
                continue;
            }

            var value = builder[key]?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(readOnlyKey) &&
                string.Equals(key, readOnlyKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(value, readOnlyValue, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(applicationNameSettingName) &&
                string.Equals(key, applicationNameSettingName, StringComparison.OrdinalIgnoreCase) &&
                value.EndsWith(readOnlySuffix, StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - readOnlySuffix.Length);
            }

            normalized[key] = value;
        }

        _ = ConnectionStringNormalizationCache.TryAdd(connectionString, readOnlyKey, readOnlyValue,
            applicationNameSettingName, readOnlySuffix, normalized);
        return true;
    }

    private static bool ShouldIgnoreKey(string key)
    {
        return string.Equals(key, "password", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "pwd", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "user id", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "uid", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "user", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "username", StringComparison.OrdinalIgnoreCase)
               || key.Contains("password", StringComparison.OrdinalIgnoreCase)
               || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
               || key.Contains("token", StringComparison.OrdinalIgnoreCase)
               || key.Contains("access", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SensitiveValuesStripped(string original, string normalized)
    {
        if (string.IsNullOrWhiteSpace(original))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        if (string.Equals(original, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryExtractSensitiveValues(original, out var originalSensitive) ||
            originalSensitive.Count == 0)
        {
            return false;
        }

        if (!TryExtractSensitiveValues(normalized, out var normalizedSensitive))
        {
            return true;
        }

        // Compare by value, not key name: a typed provider builder may rewrite a credential synonym
        // to its canonical keyword (IBM.Data.Db2: UID -> User ID, PWD -> Password; confirmed live),
        // which keeps the credential but not the key the caller wrote.
        foreach (var entry in originalSensitive)
        {
            if (!normalizedSensitive.Values.Contains(entry.Value, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractSensitiveValues(
        string connectionString,
        out Dictionary<string, string> sensitiveValues)
    {
        sensitiveValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DbConnectionStringBuilder builder;
        try
        {
            builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        }
        catch
        {
            return false;
        }

        foreach (var keyObj in builder.Keys)
        {
            var key = keyObj?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!ShouldIgnoreKey(key))
            {
                continue;
            }

            var value = builder[key]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            sensitiveValues[key] = value;
        }

        return true;
    }

    private static bool TryParseReadOnlyParameter(
        string? readOnlyParameter,
        out string? key,
        out string? value)
    {
        key = null;
        value = null;

        if (string.IsNullOrWhiteSpace(readOnlyParameter))
        {
            return false;
        }

        var parts = readOnlyParameter.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        key = parts[0];
        value = parts[1];
        return !string.IsNullOrWhiteSpace(key);
    }

    private string ComputePoolKeyHash(string connectionString)
    {
        var provider = _factory?.GetType().FullName ?? "unknown";
        // Not RedactConnectionString: it collapses every secret to the same literal "REDACTED",
        // right for logs but wrong for a pool-identity key — two connection strings differing only
        // in credentials (distinct pools) would hash identically. Each secret is replaced by a
        // hash of itself instead: distinct secrets stay distinct, no plaintext is retained.
        var redacted = HashSensitiveConnectionStringValues(connectionString);
        var input = $"{provider}|{redacted}";

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string HashSensitiveConnectionStringValues(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            var keys = builder.Keys.Cast<object>().Select(k => k.ToString() ?? string.Empty).ToArray();
            foreach (var key in keys)
            {
                var lower = key.ToLowerInvariant();
                if (lower.Contains("password") || lower == "pwd" || lower.Contains("user id") || lower == "uid" ||
                    lower.Contains("token") || lower.Contains("secret") || lower.Contains("access"))
                {
                    var value = builder[key]?.ToString() ?? string.Empty;
                    var valueBytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
                    builder[key] = Convert.ToHexString(valueBytes)[..16].ToLowerInvariant();
                }
            }

            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            // Malformed: hash the whole string rather than risk two different ones colliding.
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connectionString)));
        }
    }

    /// <summary>
    /// The raw, caller-supplied connection string(s) — not the internally decorated reader/writer
    /// variants — as keys for <see cref="IDatabaseContextConfiguration.EnforceUniqueConnectionString"/>.
    /// </summary>
    private List<string> ComputeConnectionStringKeys(IDatabaseContextConfiguration configuration)
    {
        var keys = new List<string>(2) { ComputePoolKeyHash(configuration.ConnectionString) };

        if (!string.IsNullOrWhiteSpace(configuration.ReadOnlyConnectionString) &&
            !string.Equals(configuration.ReadOnlyConnectionString, configuration.ConnectionString,
                StringComparison.OrdinalIgnoreCase))
        {
            keys.Add(ComputePoolKeyHash(configuration.ReadOnlyConnectionString));
        }

        return keys;
    }

    private string BuildReadOnlyConnectionStringFromBase(string baseConnectionString)
    {
        var builder = GetFactoryConnectionStringBuilder(baseConnectionString);
        var processed = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            baseConnectionString,
            Product,
            ConnectionMode,
            _dialect?.SupportsExternalPooling ?? false,
            _dialect?.PoolingSettingName,
            builder);

        return processed;
    }

    internal static Action? RedactionHook;

    private void RefreshRedactedConnectionStrings()
    {
        _redactedConnectionString = RedactConnectionString(_connectionString);
        _redactedReaderConnectionString = RedactConnectionString(_readerConnectionString);
    }

    private static string RedactConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        RedactionHook?.Invoke();

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            var keys = builder.Keys.Cast<object>().Select(k => k.ToString() ?? string.Empty).ToArray();
            foreach (var key in keys)
            {
                var lower = key.ToLowerInvariant();
                if (lower.Contains("password") || lower == "pwd" || lower.Contains("user id") || lower == "uid" ||
                    lower.Contains("token") || lower.Contains("secret") || lower.Contains("access"))
                {
                    builder[key] = "REDACTED";
                }
            }

            return builder.ConnectionString;
        }
        catch
        {
            return "REDACTED_CONNECTION_STRING";
        }
    }

    private static DbConnectionStringBuilder GetFactoryConnectionStringBuilderStatic(string connectionString)
    {
        return ConnectionStringHelper.Create((DbConnectionStringBuilder?)null, connectionString);
    }

    private static bool RepresentsRawConnectionString(DbConnectionStringBuilder builder, string original)
    {
        if (builder == null)
        {
            return true;
        }

        if (!builder.TryGetValue(ConnectionStringHelper.DataSourceKey, out var raw) || builder.Count != 1)
        {
            return false;
        }

        return string.Equals(Convert.ToString(raw), original, StringComparison.Ordinal);
    }

    private static string? TryGetDataSourcePath(string connectionString)
    {
        try
        {
            var csb = new DbConnectionStringBuilder { ConnectionString = connectionString };
            if (csb.ContainsKey(ConnectionStringHelper.DataSourceKey))
            {
                return csb[ConnectionStringHelper.DataSourceKey]?.ToString();
            }
        }
        catch
        {
        }

        return connectionString;
    }

    private void ReleaseResourcesAfterFailedConstruction()
    {
        ReleaseUniqueConnectionStringRegistrations();
        DisposePersistentConnections();
        DisposeOwnedDataSources();

        _writerGovernor?.Dispose();
        _writerGovernor = null;
        _readerGovernor?.Dispose();
        _readerGovernor = null;
    }

    private DbMode CoerceMode(DbMode requested, SupportedDatabase product, DatabaseTopology topology)
    {
        // All per-database coercion policy (what Best resolves to, which explicit modes are unsafe
        // and get coerced, which are safe and honored as-is) lives on the dialect now — see
        // ISqlDialect.CoerceConnectionMode. A new client-server database needs no entry here at
        // all; only a database with real mode restrictions (embedded engines, LocalDB) overrides
        // the dialect's base implementation.
        var dialect = SqlDialectFactory.CreateDialectForType(product, _factory, _logger);
        var (mode, reason) = dialect is SqlDialect sqlDialect
            ? sqlDialect.CoerceConnectionMode(requested, _connectionString, topology)
            : dialect.CoerceConnectionMode(requested, _connectionString, topology.IsLocalDb);
        // Best must not override a caller's explicit one-connection pool to make room for a
        // PreventDatabaseUnload sentinel (CONFIRMED live on Db2: IBM's driver rejects the raised
        // pool size once the detection connection has created the pool at size 1).
        if (requested == DbMode.Best && mode == DbMode.PreventDatabaseUnload && dialect is SqlDialect poolDialect)
        {
            var configuredMax = _configuredWritePoolSize ??
                                PoolingConfigReader.GetEffectivePoolConfig(poolDialect, _connectionString).MaxPoolSize;
            if (configuredMax is < 2)
            {
                (mode, reason) = (DbMode.Standard,
                    $"Best would select PreventDatabaseUnload, but its sentinel needs a second pooled connection and the configured maximum pool size is {configuredMax}; using Standard");
            }
        }

        LogModeOverride(requested, mode, reason);
        if (dialect is SqlDialect topologyDialect && topologyDialect.DescribeUnsupportedTopology(topology) is { } unsupported)
        {
            _logger.LogWarning("{UnsupportedTopology}", unsupported);
        }

        return mode;
    }

    private void LogModeOverride(DbMode requested, DbMode resolved, string reason)
    {
        if (requested == resolved)
        {
            return;
        }

        if (requested == DbMode.Best)
        {
            _logger.LogInformation(
                "DbMode auto-selection: requested {requested}, resolved to {resolved} — reason: {reason}", requested,
                resolved, reason);
            return;
        }

        _logger.LogWarning(diagnostics.EventIds.ModeCoerced,
            "DbMode override: requested {requested}, coerced to {resolved} — reason: {reason}", requested, resolved,
            reason);
    }

    private void WarnOnModeMismatch(DbMode resolved, SupportedDatabase product, bool wasCoerced, bool isLocalDb)
    {
        // Don't warn if we auto-coerced (already logged by LogModeOverride)
        if (wasCoerced)
        {
            return;
        }

        var dialect = SqlDialectFactory.CreateDialectForType(product, _factory, _logger);

        // Pattern 1: Client-server database with overly restrictive mode
        if (dialect.IsClientServerDatabase)
        {
            if (resolved == DbMode.SingleConnection)
            {
                _logger.LogWarning(
                    diagnostics.EventIds.ModeMismatch,
                    "SingleConnection mode used with {Database}. " +
                    "Client-server databases support full concurrency; " +
                    "consider Standard mode for better throughput. " +
                    "SingleConnection serializes all operations and is designed for embedded databases.",
                    product
                );
            }
            else if (resolved == DbMode.SingleWriter)
            {
                _logger.LogWarning(
                    diagnostics.EventIds.ModeMismatch,
                    "SingleWriter mode used with {Database}. " +
                    "This mode is designed for embedded databases with single-writer constraints. " +
                    "Client-server databases support concurrent writers; consider Standard mode.",
                    product
                );
            }
        }

        // Pattern 2: an embedded single-writer engine (DuckDB, Access — SQLite stays hard-coerced
        // and never reaches this: see SqlDialect.CoerceEmbeddedSingleWriterMode's allowStandard
        // parameter) explicitly running Standard mode against a file-based database. The engine's
        // own documentation claims concurrent-connection/writer support; pengdows.crud honors the
        // explicit choice but surfaces the dialect's own evidence-backed risk description
        // (DescribeStandardModeRisk) instead of silently trusting that documentation. Deliberately
        // checks IsEmbeddedSingleWriterEngine rather than !IsClientServerDatabase — the latter is
        // also true for the Unknown-database fallback, which should not get this wording.
        if (dialect.IsEmbeddedSingleWriterEngine &&
            resolved == DbMode.Standard &&
            dialect.DetectInMemoryKind(_connectionString) == InMemoryKind.None)
        {
            var risk = (dialect as SqlDialect)?.DescribeStandardModeRisk() ??
                "This engine has single-writer constraints; concurrent writers may cause lock contention.";
            _logger.LogWarning(
                diagnostics.EventIds.ModeMismatch,
                "Standard mode used with file-based {Database}. {Risk}",
                product,
                risk
            );
        }

        // Pattern 3: SQL Server LocalDB explicitly running Standard mode. Purely a
        // performance/lifecycle tradeoff, not a correctness risk: LocalDB's auto-shutdown-after-idle
        // isn't masked by a PreventDatabaseUnload sentinel. Only relevant if the workload actually
        // goes idle long enough to trigger it — see SqlServerDialect.CoerceConnectionMode.
        if (isLocalDb && resolved == DbMode.Standard)
        {
            _logger.LogWarning(
                diagnostics.EventIds.ModeMismatch,
                "Standard mode used with SQL Server LocalDB. LocalDB automatically shuts down " +
                "after an idle period, which incurs a reconnect cost on next use; " +
                "PreventDatabaseUnload avoids that cost by holding one sentinel connection open. " +
                "This is irrelevant if your workload keeps the database busy continuously — " +
                "Standard mode is honored here because it was explicitly requested."
            );
        }
    }

    private bool IsClientServerDatabase(SupportedDatabase product)
    {
        // Delegates to the dialect's own ISqlDialect.IsClientServerDatabase instead of
        // maintaining a second SupportedDatabase switch here.
        return SqlDialectFactory.CreateDialectForType(product, _factory, _logger).IsClientServerDatabase;
    }

    private InMemoryKind DetectInMemoryKind(SupportedDatabase product, string? connectionString)
    {
        // Delegates to the dialect's own ISqlDialect.DetectInMemoryKind instead of maintaining a
        // second per-product connection-string parser here. Only SQLite, DuckDB, and Access
        // override the base (always-None) behavior.
        return SqlDialectFactory.CreateDialectForType(product, _factory, _logger).DetectInMemoryKind(connectionString);
    }

    private bool IsMemoryDataSource()
    {
        var ds = TryGetDataSourcePath(_connectionString) ?? string.Empty;
        return ds.IndexOf(":memory:", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private DbConnectionStringBuilder GetFactoryConnectionStringBuilder(string connectionString)
    {
        var input = string.IsNullOrEmpty(connectionString) ? _connectionString : connectionString;
        return ConnectionStringHelper.Create(_factory, input);
    }

    /// <summary>
    /// Returns the <c>CreateDataSource</c> method for <paramref name="parameterType"/> only if
    /// the provider actually overrides it. Methods inherited directly from
    /// <see cref="DbProviderFactory"/> (e.g. the base <c>NotSupportedException</c> stub) are
    /// excluded so we never invoke a no-op and mistake it for provider capability.
    /// </summary>
    private static MethodInfo? FindProviderCreateDataSourceMethod(Type factoryType, Type parameterType)
    {
        var method = factoryType.GetMethod("CreateDataSource", new[] { parameterType });
        if (method == null || method.DeclaringType == typeof(DbProviderFactory))
            return null;

        return method;
    }

    /// <summary>
    /// Attempts to obtain a provider-native <see cref="DbDataSource"/> by reflecting on the
    /// factory. Returns <c>null</c> on all failure paths — callers should fall back to
    /// <see cref="CreateGenericFallbackDataSource"/>.
    /// <para>
    /// Probe order: <c>string</c> overload first (avoids builder round-trip canonicalization),
    /// then <c>DbConnectionStringBuilder</c> overload.
    /// </para>
    /// </summary>
    private DbDataSource? TryCreateProviderDataSource(DbProviderFactory factory, string connectionString)
    {
        var factoryType = factory.GetType();
        try
        {
            // Priority 1: string overload — preferred because it avoids builder round-trip
            // canonicalization that can drop or reorder provider-specific keys.
            var stringMethod = FindProviderCreateDataSourceMethod(factoryType, typeof(string));
            if (stringMethod != null)
            {
                if (stringMethod.Invoke(factory, new object?[] { connectionString }) is DbDataSource ds)
                {
                    return ds;
                }
            }

            // Priority 2: DbConnectionStringBuilder overload — some providers only expose this.
            var builderMethod = FindProviderCreateDataSourceMethod(factoryType, typeof(DbConnectionStringBuilder));
            if (builderMethod != null)
            {
                var builder = factory.CreateConnectionStringBuilder() ?? new DbConnectionStringBuilder();
                builder.ConnectionString = connectionString;
                if (builderMethod.Invoke(factory, new object?[] { builder }) is DbDataSource ds)
                {
                    return ds;
                }
            }

            return null;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is NotSupportedException)
        {
            // Provider explicitly opts out of the DataSource pattern.
            _logger.LogDebug(
                "Provider {FactoryType} explicitly does not support DbDataSource.",
                factoryType.FullName);
            return null;
        }
        catch (Exception ex)
        {
            // Unexpected failure during probe — log at debug because fallback is always attempted.
            // A warning would be misleading since the context may still function correctly.
            _logger.LogDebug(
                ex,
                "Failed probing provider-native DbDataSource support for {FactoryType}.",
                factoryType.FullName);
            return null;
        }
    }

    /// <summary>
    /// Creates the <see cref="GenericDbDataSource"/> fallback wrapper.
    /// Overridable in tests to return <c>null</c> or a substitute without type-name sniffing.
    /// </summary>
    internal virtual DbDataSource? CreateGenericFallbackDataSource(DbProviderFactory factory, string connectionString)
        => new GenericDbDataSource(factory, connectionString);

    /// <summary>
    /// Resolves the best available <see cref="DbDataSource"/> for <paramref name="factory"/>:
    /// <list type="number">
    ///   <item>Provider-native data source (via reflected <c>CreateDataSource</c> override).</item>
    ///   <item><see cref="GenericDbDataSource"/> wrapper so the rest of the framework can always
    ///         use the DataSource path uniformly.</item>
    /// </list>
    /// Returns <c>null</c> only if both paths fail.
    /// </summary>
    /// <summary>
    /// The data sources are created before <see cref="InitializePoolGovernors"/> applies
    /// PreventDatabaseUnload's provider minimum, so a string changed there must also reach the
    /// data source its connections come from. Otherwise connections open from a pool keyed by the
    /// old string while everything that manages pools by string (governor keys, the Firebird DDL
    /// pool reset) targets the new one — confirmed live: the DDL reset cleared an unused pool and
    /// DROP TABLE failed with "object ... is in use". A caller-provided data source is left alone.
    /// The replaced data sources are retired, not disposed, because the construction-time
    /// connection (PreventDatabaseUnload's writer sentinel) may already be open on them; they are
    /// disposed with the context.
    /// </summary>
    private void RebuildOwnedDataSourcesForChangedConnectionStrings(string writerBefore, string readerBefore)
    {
        if (_dataSourceProvided || _factory == null)
        {
            return;
        }

        var writerChanged = !string.Equals(writerBefore, _connectionString, StringComparison.Ordinal);
        var readerChanged = !string.IsNullOrWhiteSpace(_readerConnectionString) &&
                            !string.Equals(readerBefore, _readerConnectionString, StringComparison.Ordinal);
        if (!writerChanged && !readerChanged)
        {
            return;
        }

        var oldWriter = _dataSource;
        var oldReader = _readerDataSource;
        var readerSharedWriter = ReferenceEquals(oldReader, oldWriter);

        if (writerChanged && oldWriter != null)
        {
            _dataSource = TryCreateDataSource(_factory, _connectionString);
            RetireDataSource(oldWriter);
        }

        if (readerSharedWriter)
        {
            _readerDataSource = string.Equals(_readerConnectionString, _connectionString, StringComparison.OrdinalIgnoreCase)
                ? _dataSource
                : oldReader == null ? null : TryCreateDataSource(_factory, _readerConnectionString);
        }
        else if (readerChanged && oldReader != null)
        {
            _readerDataSource = TryCreateDataSource(_factory, _readerConnectionString);
            RetireDataSource(oldReader);
        }

        RefreshRedactedConnectionStrings();
    }

    private void RetireDataSource(DbDataSource dataSource)
    {
        lock (_retiredDataSources)
        {
            _retiredDataSources.Add(dataSource);
        }
    }

    private DbDataSource? TryCreateDataSource(DbProviderFactory factory, string connectionString)
    {
        var nativeDataSource = TryCreateProviderDataSource(factory, connectionString);
        if (nativeDataSource != null)
        {
            var isProviderSpecific = nativeDataSource.GetType().Assembly != typeof(DbDataSource).Assembly;
            _logger.LogInformation(
                "Using {SourceType} DbDataSource from provider factory: {FactoryType}",
                isProviderSpecific ? "provider-specific" : "generic",
                factory.GetType().FullName);
            return nativeDataSource;
        }

        try
        {
            _logger.LogDebug(
                "Creating GenericDbDataSource wrapper for {FactoryType}",
                factory.GetType().FullName);
            return CreateGenericFallbackDataSource(factory, connectionString);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed creating GenericDbDataSource wrapper for {FactoryType}; DataSource path unavailable.",
                factory.GetType().FullName);
            return null;
        }
    }

    /// <inheritdoc />
    public TimeSpan? ModeLockTimeout => _modeLockTimeout;

    #endregion
}
