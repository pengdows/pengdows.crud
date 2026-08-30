// =============================================================================
// FILE: DatabaseContext.Initialization.cs
// PURPOSE: DatabaseContext constructors, initialization, and configuration.
//
// AI SUMMARY:
// - Contains all DatabaseContext constructors:
//   * (connectionString, providerName) - Uses DbProviderFactories
//   * (connectionString, DbProviderFactory) - Direct factory
//   * (IDatabaseContextConfiguration, factory) - Full configuration object
// - Initialization flow:
//   1. Parse connection string for pool settings and mode hints
//   2. Detect database product (SQL Server, PostgreSQL, etc.)
//   3. Create appropriate SQL dialect
//   4. Initialize connection strategy (Standard, PreventDatabaseUnload, etc.)
//   5. Set up metrics collector if enabled
// - Auto-detection of DbMode for embedded databases:
//   * SQLite :memory: -> SingleConnection
//   * SQLite file mode -> SingleWriter
//   * Firebird embedded -> PreventDatabaseUnload
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

#pragma warning disable CS8618
    private DatabaseContext()
    {
    }
#pragma warning restore CS8618

    private DatabaseContext(
        IDatabaseContextConfiguration configuration,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory,
        ITypeMapRegistry typeMapRegistry,
        DbDataSource? dataSource)
    {
        ILockerAsync? initLocker = null;
        try
        {
            initLocker = GetLockInternal();
            initLocker.Lock();

            SetupFields(configuration, factory, loggerFactory, typeMapRegistry, dataSource);
            var initialConnection = InitializeInternals(configuration);

            _connectionStrategy = ConnectionStrategyFactory.Create(this, ConnectionMode);
            _procWrappingStrategy = ProcWrappingStrategyFactory.Create(_procWrappingStyle);

            var (dialect, dataSourceInfo) =
                _connectionStrategy.HandleDialectDetection(initialConnection, _factory, _loggerFactory);

            FinishInitialization(configuration, initialConnection, dialect, dataSourceInfo);
        }
        catch (Exception e)
        {
            UnwindInitializationFailure(e);
            throw;
        }
        finally
        {
            if (initLocker is IAsyncDisposable iad)
            {
                iad.DisposeAsync().GetAwaiter().GetResult();
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
    /// Asynchronously creates and initializes a new <see cref="DatabaseContext"/> instance.
    /// </summary>
    /// <param name="configuration">Context configuration.</param>
    /// <param name="factory">Provider factory.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task returning the initialized database context.</returns>
    public static Task<DatabaseContext> CreateAsync(
        IDatabaseContextConfiguration configuration,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(configuration, null, factory, loggerFactory, new TypeMapRegistry(), cancellationToken);
    }

    /// <summary>
    /// Asynchronously creates and initializes a new <see cref="DatabaseContext"/> instance with a native <see cref="DbDataSource"/>.
    /// </summary>
    /// <param name="configuration">Context configuration.</param>
    /// <param name="dataSource">Data source for connection creation.</param>
    /// <param name="factory">Provider factory.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task returning the initialized database context.</returns>
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
    /// Asynchronously creates and initializes a new <see cref="DatabaseContext"/> instance.
    /// </summary>
    /// <param name="connectionString">Database connection string.</param>
    /// <param name="providerFactory">Provider invariant name.</param>
    /// <param name="mode">Database connection mode.</param>
    /// <param name="readWriteMode">Read/write mode.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="readOnlyConnectionString">Optional read-only connection string.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task returning the initialized database context.</returns>
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

        var factory = DbProviderFactories.GetFactory(providerFactory);
        return CreateAsync(config, null, factory, loggerFactory, new TypeMapRegistry(), cancellationToken);
    }

    /// <summary>
    /// Asynchronously creates and initializes a new <see cref="DatabaseContext"/> instance.
    /// </summary>
    /// <param name="connectionString">Database connection string.</param>
    /// <param name="factory">Provider factory.</param>
    /// <param name="readOnlyConnectionString">Optional read-only connection string.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task returning the initialized database context.</returns>
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

    internal static Task<DatabaseContext> CreateAsync(
        IDatabaseContextConfiguration configuration,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory,
        ITypeMapRegistry typeMapRegistry,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(configuration, null, factory, loggerFactory, typeMapRegistry, cancellationToken);
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
        await context.InitializeAsync(configuration, factory, loggerFactory, typeMapRegistry, dataSource, cancellationToken)
            .ConfigureAwait(false);
        return context;
    }

    #endregion

    #region Initialization Helper Methods

    private void SetConnectionString(string value)
    {
        if (!string.IsNullOrWhiteSpace(_connectionString) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Connection string reset attempted.");
        }

        _connectionString = value;
    }

    private void SetupFields(
        IDatabaseContextConfiguration configuration,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory,
        ITypeMapRegistry typeMapRegistry,
        DbDataSource? dataSource)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (string.IsNullOrWhiteSpace(configuration.ConnectionString))
        {
            throw new ArgumentException("ConnectionString is required.", nameof(configuration.ConnectionString));
        }

        ValidateConfiguration(configuration);

        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<IDatabaseContext>();
        if (TypeCoercionHelper.Logger is NullLogger)
        {
            TypeCoercionHelper.Logger =
                _loggerFactory.CreateLogger(nameof(TypeCoercionHelper));
        }

        var normalizedReadWriteMode = configuration.ReadWriteMode;
        var normalizedReadPoolSize = configuration.MaxConcurrentReads;
        var normalizedWritePoolSize = configuration.MaxConcurrentWrites;
        NormalizePoolLimitConfiguration(
            configuration.DbMode,
            ref normalizedReadWriteMode,
            ref normalizedReadPoolSize,
            ref normalizedWritePoolSize);

        ReadWriteMode = normalizedReadWriteMode;
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
        _firstOpenHandlerRw = tc => ExecuteSessionSettings(tc, false);
        _firstOpenHandlerRo = tc => ExecuteSessionSettings(tc, true);
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
        _sessionInitializationFailureMode = configuration.SessionInitializationFailureMode;
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
    }

    private async Task InitializeAsync(
        IDatabaseContextConfiguration configuration,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory,
        ITypeMapRegistry typeMapRegistry,
        DbDataSource? dataSource,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ILockerAsync? initLocker = null;
        try
        {
            initLocker = GetLockInternal();
            await initLocker.LockAsync(cancellationToken).ConfigureAwait(false);

            SetupFields(configuration, factory, loggerFactory, typeMapRegistry, dataSource);
            var initialConnection = await InitializeInternalsAsync(configuration, cancellationToken).ConfigureAwait(false);

            _connectionStrategy = ConnectionStrategyFactory.Create(this, ConnectionMode);
            _procWrappingStrategy = ProcWrappingStrategyFactory.Create(_procWrappingStyle);

            var (dialect, dataSourceInfo) = await _connectionStrategy
                .HandleDialectDetectionAsync(initialConnection, _factory, _loggerFactory, cancellationToken)
                .ConfigureAwait(false);

            await FinishInitializationAsync(
                configuration,
                initialConnection,
                dialect,
                dataSourceInfo,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            UnwindInitializationFailure(e);
            throw;
        }
        finally
        {
            if (initLocker is IAsyncDisposable iad)
            {
                await iad.DisposeAsync().ConfigureAwait(false);
            }
            else if (initLocker is IDisposable id)
            {
                id.Dispose();
            }
        }
    }

    private void UnwindInitializationFailure(Exception e)
    {
        UniqueConnectionStringRegistry.UnregisterAllForWarning(this, _uniqueConnectionStringWarnRegistrations);
        _uniqueConnectionStringWarnRegistrations = null;
        try
        {
            DisposePersistentConnectionsForInitializationFailure();
            _writerGovernor?.Dispose();
            _writerGovernor = null;
            _readerGovernor?.Dispose();
            _readerGovernor = null;
            DisposeOwnedDataSources();
        }
        catch
        {
            // Preserve the original construction exception.
        }
        _logger?.LogError(e, "DatabaseContext construction failed.");
    }

    private void SetupDialectAndGates(ISqlDialect? dialect, IDataSourceInformation? dataSourceInfo)
    {
        if (dialect != null && dataSourceInfo != null)
        {
            _dialect = dialect as SqlDialect
                       ?? throw new InvalidOperationException(
                           $"Dialect returned by dialect detection must derive from SqlDialect; got {dialect.GetType().Name}.");
            _dataSourceInfo = (DataSourceInformation)dataSourceInfo;
        }
        else
        {
            var logger = _loggerFactory.CreateLogger<SqlDialect>();
            _dialect = new Sql92Dialect(_factory, logger);
            _dialect.InitializeUnknownProductInfo();
            _dataSourceInfo = new DataSourceInformation(_dialect);
        }

        _sessionSettingsDetectionCompleted = true;
        Name = _dataSourceInfo.DatabaseProductName;
        _procWrappingStyle = _dataSourceInfo.ProcWrappingStyle;
        if (_dialect.RequiresSerializedConnectionOpen)
        {
            RequiresSerializedOpen = true;
            _connectionOpenGate = new SemaphoreSlim(1, 1);
            _connectionOpenLocker = new ReusableAsyncLocker(_connectionOpenGate);
        }

        if (ConnectionMode == DbMode.SingleConnection)
        {
            _singleConnectionTransactionGate = new SemaphoreSlim(1, 1);
        }
    }

    private void SetupConnectionStringsAndSessionSettings(
        IDatabaseContextConfiguration configuration)
    {
        var effectiveApplicationName = ResolveApplicationName(configuration.ApplicationName);

        var builder = GetFactoryConnectionStringBuilder(_connectionString);
        _connectionString = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
            _connectionString,
            Product,
            ConnectionMode,
            _dialect?.SupportsExternalPooling ?? false,
            _dialect?.PoolingSettingName,
            builder);

        _connectionString = ConnectionPoolingConfiguration.ApplyApplicationName(
            _connectionString,
            effectiveApplicationName,
            _dialect?.ApplicationNameSettingName,
            builder);

        if (ConnectionMode is DbMode.SingleWriter or DbMode.SingleConnection)
        {
            _connectionString = ConnectionPoolingConfiguration.StripPoolingSetting(
                _connectionString,
                _dialect?.PoolingSettingName);
        }

        InitializeReadOnlyConnectionResources(configuration, effectiveApplicationName);

        var rwAppName = string.IsNullOrWhiteSpace(effectiveApplicationName)
            ? null
            : effectiveApplicationName + WriteApplicationNameSuffix;
        var roAppName = string.IsNullOrWhiteSpace(effectiveApplicationName)
            ? null
            : effectiveApplicationName + ReadOnlyApplicationNameSuffix;
        _cachedReadWriteSessionSettings = _dialect?.GetFinalSessionSettings(readOnly: false, rwAppName) ?? string.Empty;
        _cachedReadOnlySessionSettings  = _dialect?.GetFinalSessionSettings(readOnly: true,  roAppName) ?? string.Empty;
    }

    private void FinalizeRegistrations(IDatabaseContextConfiguration configuration)
    {
        _isolationResolver = new IsolationResolver(_dialect!, RCSIEnabled, SnapshotIsolationEnabled);
        _uniqueConnectionStringWarnRegistrations = RegisterConnectionStringsForDuplicateWarning(configuration);

        if (configuration.EnforceUniqueConnectionString)
        {
            try
            {
                _uniqueConnectionStringClaims = ClaimUniqueConnectionStrings(configuration);
            }
            catch
            {
                _writerGovernor?.Dispose();
                _writerGovernor = null;
                _readerGovernor?.Dispose();
                _readerGovernor = null;
                DisposePersistentConnectionsForInitializationFailure();
                DisposeOwnedDataSources();
                throw;
            }
        }
    }

    private void FinishInitialization(
        IDatabaseContextConfiguration configuration,
        ITrackedConnection? initialConnection,
        ISqlDialect? dialect,
        IDataSourceInformation? dataSourceInfo)
    {
        SetupDialectAndGates(dialect, dataSourceInfo);
        SetupConnectionStringsAndSessionSettings(configuration);

        if (!string.IsNullOrWhiteSpace(configuration.ReadOnlyConnectionString) &&
            HasDedicatedReadConnectionString())
        {
            TestConnect(_readerConnectionString, "ReadOnlyValidation", "ReadOnly");
        }

        InitializePoolGovernors();
        RefreshRedactedConnectionStrings();

        RCSIEnabled = _rcsiPrefetch;
        SnapshotIsolationEnabled = _snapshotIsolationPrefetch;

        if (ConnectionMode is DbMode.SingleConnection or DbMode.PreventDatabaseUnload)
        {
            var target = initialConnection ?? PersistentConnection;
            if (target != null)
            {
                try
                {
                    ExecuteSessionSettings(target, IsReadOnlyConnection);
                }
                catch
                {
                    DisposePersistentConnectionsForInitializationFailure();
                    throw;
                }
            }
        }

        if (ConnectionMode is DbMode.Standard or DbMode.SingleWriter && initialConnection != null)
        {
            initialConnection.Dispose();
            Interlocked.Exchange(ref _connectionCount, 0);
            Interlocked.Exchange(ref _peakOpenConnections, 0);
        }

        FinalizeRegistrations(configuration);
    }

    private async Task FinishInitializationAsync(
        IDatabaseContextConfiguration configuration,
        ITrackedConnection? initialConnection,
        ISqlDialect? dialect,
        IDataSourceInformation? dataSourceInfo,
        CancellationToken cancellationToken)
    {
        SetupDialectAndGates(dialect, dataSourceInfo);
        SetupConnectionStringsAndSessionSettings(configuration);

        if (!string.IsNullOrWhiteSpace(configuration.ReadOnlyConnectionString) &&
            HasDedicatedReadConnectionString())
        {
            await TestConnectAsync(_readerConnectionString, "ReadOnlyValidation", "ReadOnly", cancellationToken)
                .ConfigureAwait(false);
        }

        await InitializePoolGovernorsAsync(cancellationToken).ConfigureAwait(false);
        RefreshRedactedConnectionStrings();

        RCSIEnabled = _rcsiPrefetch;
        SnapshotIsolationEnabled = _snapshotIsolationPrefetch;

        if (ConnectionMode is DbMode.SingleConnection or DbMode.PreventDatabaseUnload)
        {
            var target = initialConnection ?? PersistentConnection;
            if (target != null)
            {
                try
                {
                    await ExecuteSessionSettingsAsync(target, IsReadOnlyConnection, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    DisposePersistentConnectionsForInitializationFailure();
                    throw;
                }
            }
        }

        if (ConnectionMode is DbMode.Standard or DbMode.SingleWriter && initialConnection != null)
        {
            if (initialConnection is IAsyncDisposable ad)
            {
                await ad.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                initialConnection.Dispose();
            }
            Interlocked.Exchange(ref _connectionCount, 0);
            Interlocked.Exchange(ref _peakOpenConnections, 0);
        }

        FinalizeRegistrations(configuration);
    }

    private ITrackedConnection? InitializeInternals(IDatabaseContextConfiguration config)
    {
        // 1) Persist config first
        var rawConnectionString =
            config.ConnectionString ?? throw new ArgumentNullException(nameof(config.ConnectionString));
        _connectionString = NormalizeConnectionString(rawConnectionString);

        ITrackedConnection? initConn = null;
        try
        {
            // 2) Create + open
            var initExecutionType = IsReadOnlyConnection ? ExecutionType.Read : ExecutionType.Write;
            initConn = FactoryCreateConnection(initExecutionType, _connectionString, true);
            try
            {
                initConn.Open();
            }
            catch (Exception ex)
            {
                throw new ConnectionFailedException("Failed to open database connection.", ex)
                {
                    Phase = "InitConnect",
                    Role = "ReadWrite"
                };
            }

            // 3) Detect product/capabilities once
            var product = DatabaseDetectionService.DetectProduct(initConn, _factory);
            var topology = DatabaseDetectionService.DetectTopology(product, _connectionString);
            var isLocalDb = topology.IsLocalDb;
            var isFirebirdEmbedded = topology.IsEmbedded;

            // Best-effort, provider-specific session-capability prefetch (currently only
            // meaningful for SQL Server's RCSI/snapshot isolation database options) — delegated
            // to the dialect rather than special-cased here. Every non-SQL-Server dialect's base
            // implementation is a true no-op (no connection round-trip), so this is free for
            // every other product.
            if (initConn != null)
            {
                var prefetchLogger = _loggerFactory.CreateLogger<SqlDialect>();
                var prefetchDialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(product, _factory, prefetchLogger);
                var prefetch = prefetchDialect.DetectSessionCapabilities(initConn);
                _rcsiPrefetch = prefetch.Rcsi;
                _snapshotIsolationPrefetch = prefetch.SnapshotIsolation;
            }

            if (initConn != null && config.DbMode == DbMode.Standard)
            {
                // Only do inline detection for Standard mode; SingleWriter mode will detect via main constructor
                _dataSourceInfo = DataSourceInformation.Create(initConn, _factory, _loggerFactory);
                _procWrappingStyle = _dataSourceInfo.ProcWrappingStyle;
                Name = _dataSourceInfo.DatabaseProductName;
            }

            // 4) Coerce ConnectionMode based on product/topology
            var requestedMode = ConnectionMode;
            ConnectionMode = CoerceMode(requestedMode, product, isLocalDb, isFirebirdEmbedded);
            var inMemoryKind = DetectInMemoryKind(product, _connectionString);

            if (ConnectionMode == DbMode.SingleConnection
                && inMemoryKind != InMemoryKind.None
                && IsReadOnlyConnection)
            {
                throw new InvalidOperationException(
                    "In-memory databases that use SingleConnection mode require a read-write context.");
            }

            // Warn on mode/database mismatches (performance, not correctness)
            WarnOnModeMismatch(ConnectionMode, product, requestedMode != ConnectionMode);

            // Pooling defaults will be applied after dialect detection

            // 5) Apply provider/session settings according to final mode
            if (initConn != null)
            {
                // Note: SingleWriter no longer uses persistent connections - it uses
                // Standard lifecycle with governor policy (WriteSlots=1 + turnstile fairness)
                if (ConnectionMode == DbMode.PreventDatabaseUnload)
                {
                    RegisterSentinel(initConn, initExecutionType);
                    initConn = null; // context owns the sentinel now
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

            // 8) Return the open initConn only for Standard (caller disposes). For persistent modes we returned null.
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

    private async Task<ITrackedConnection?> InitializeInternalsAsync(
        IDatabaseContextConfiguration config,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rawConnectionString =
            config.ConnectionString ?? throw new ArgumentNullException(nameof(config.ConnectionString));
        _connectionString = NormalizeConnectionString(rawConnectionString);

        ITrackedConnection? initConn = null;
        try
        {
            var initExecutionType = IsReadOnlyConnection ? ExecutionType.Read : ExecutionType.Write;
            initConn = FactoryCreateConnection(initExecutionType, _connectionString, true);
            try
            {
                await initConn.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new ConnectionFailedException("Failed to open database connection.", ex)
                {
                    Phase = "InitConnect",
                    Role = "ReadWrite"
                };
            }

            var product = await DatabaseDetectionService.DetectProductAsync(initConn, _factory, cancellationToken)
                .ConfigureAwait(false);
            var topology = DatabaseDetectionService.DetectTopology(product, _connectionString);
            var isLocalDb = topology.IsLocalDb;
            var isFirebirdEmbedded = topology.IsEmbedded;

            if (initConn != null)
            {
                var prefetchLogger = _loggerFactory.CreateLogger<SqlDialect>();
                var prefetchDialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(product, _factory, prefetchLogger);
                var prefetch = await prefetchDialect.DetectSessionCapabilitiesAsync(initConn, cancellationToken)
                    .ConfigureAwait(false);
                _rcsiPrefetch = prefetch.Rcsi;
                _snapshotIsolationPrefetch = prefetch.SnapshotIsolation;
            }

            if (initConn != null && config.DbMode == DbMode.Standard)
            {
                _dataSourceInfo = (DataSourceInformation)await DataSourceInformation.CreateAsync(initConn, _factory, _loggerFactory, cancellationToken)
                    .ConfigureAwait(false);
                _procWrappingStyle = _dataSourceInfo.ProcWrappingStyle;
                Name = _dataSourceInfo.DatabaseProductName;
            }

            var requestedMode = ConnectionMode;
            ConnectionMode = CoerceMode(requestedMode, product, isLocalDb, isFirebirdEmbedded);
            var inMemoryKind = DetectInMemoryKind(product, _connectionString);

            if (ConnectionMode == DbMode.SingleConnection
                && inMemoryKind != InMemoryKind.None
                && IsReadOnlyConnection)
            {
                throw new InvalidOperationException(
                    "In-memory databases that use SingleConnection mode require a read-write context.");
            }

            WarnOnModeMismatch(ConnectionMode, product, requestedMode != ConnectionMode);

            if (initConn != null)
            {
                if (ConnectionMode == DbMode.PreventDatabaseUnload)
                {
                    RegisterSentinel(initConn, initExecutionType);
                    initConn = null;
                }
                else if (ConnectionMode == DbMode.SingleConnection)
                {
                    SetPersistentConnection(initConn);
                    initConn = null;
                }
            }

            return initConn;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize DatabaseContext: {Message}", ex.Message);
            try
            {
                if (initConn is IAsyncDisposable ad)
                {
                    await ad.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    initConn?.Dispose();
                }
            }
            catch
            {
                /* ignore */
            }

            throw;
        }
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

    private void InitializePoolGovernorsCore()
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

        // Caller-supplied minimums are preserved as-is for every mode. PreventDatabaseUnload is
        // the sole exception: its sentinel already keeps one physical connection open, but the
        // provider pool still needs a minimum of two so the sentinel's permit doesn't starve
        // ordinary work of the one remaining slot. Standard/SingleWriter/KeepAlive never inject
        // an implicit minimum — DbConnectionStringBuilder omits Min Pool Size entirely unless
        // the caller (or PreventDatabaseUnload above) asked for one.
        var minPoolSizeKey = _dialect?.MinPoolSizeSettingName;
        var writerMinimum = ConnectionMode == DbMode.PreventDatabaseUnload && rawWriterMax != 0 ? 2 : 0;
        _connectionString = ConnectionPoolingConfiguration.EnsureMinimumPoolSize(
            _connectionString, minPoolSizeKey, writerConfig.MinPoolSize, rawWriterMax, writerMinimum);
        if (!string.IsNullOrWhiteSpace(_readerConnectionString))
        {
            var readerMinimum = ConnectionMode == DbMode.PreventDatabaseUnload && rawReaderMax != 0 ? 2 : 0;
            _readerConnectionString = ConnectionPoolingConfiguration.EnsureMinimumPoolSize(
                _readerConnectionString, minPoolSizeKey, readerConfig.MinPoolSize, rawReaderMax, readerMinimum);
        }

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
            maxQueueDepth: _maxQueuedWrites);

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
            maxQueueDepth: _maxQueuedReads);
    }

    private void InitializePoolGovernors()
    {
        InitializePoolGovernorsCore();

        // Attach the slot that belongs to the initialization sentinel. Additional sentinels are
        // created through FactoryCreateConnection below, which acquires and attaches their own
        // pool slot as part of the normal connection path.
        if (ConnectionMode == DbMode.PreventDatabaseUnload)
        {
            AttachInitialSentinelSlotsIfNeeded();

            if (_isWriteConnection && HasDedicatedReadConnectionString())
            {
                var readSentinel = FactoryCreateConnection(
                    ExecutionType.Read, _readerConnectionString, isSharedConnection: true);
                try
                {
                    readSentinel.Open();
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

    private async Task InitializePoolGovernorsAsync(CancellationToken cancellationToken)
    {
        InitializePoolGovernorsCore();

        if (ConnectionMode == DbMode.PreventDatabaseUnload)
        {
            AttachInitialSentinelSlotsIfNeeded();

            if (_isWriteConnection && HasDedicatedReadConnectionString())
            {
                var readSentinel = FactoryCreateConnection(
                    ExecutionType.Read, _readerConnectionString, isSharedConnection: true);
                try
                {
                    await readSentinel.OpenAsync(cancellationToken).ConfigureAwait(false);
                    RegisterSentinel(readSentinel, ExecutionType.Read);
                }
                catch
                {
                    if (readSentinel is IAsyncDisposable ad)
                    {
                        await ad.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        readSentinel.Dispose();
                    }
                    throw;
                }
            }
        }
    }

    private void TestConnect(string connectionString, string phase, string role)
    {
        var isReadOnly = role == "ReadOnly";
        var executionType = isReadOnly ? ExecutionType.Read : ExecutionType.Write;
        try
        {
            using var conn = FactoryCreateConnection(executionType, connectionString, true);
            conn.Open();
        }
        catch (Exception ex)
        {
            throw new ConnectionFailedException(
                $"Failed to validate {role.ToLowerInvariant()} connection.", ex)
            {
                Phase = phase,
                Role = role
            };
        }
    }

    private async Task TestConnectAsync(string connectionString, string phase, string role, CancellationToken cancellationToken)
    {
        var isReadOnly = role == "ReadOnly";
        var executionType = isReadOnly ? ExecutionType.Read : ExecutionType.Write;
        try
        {
            var conn = FactoryCreateConnection(executionType, connectionString, true);
            if (conn is IAsyncDisposable ad)
            {
                await using (ad.ConfigureAwait(false))
                {
                    await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                using (conn)
                {
                    await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
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
        // SingleConnection + ReadOnly is rejected earlier in the constructor, so that path
        // is never reached here.
        if (ConnectionMode == DbMode.SingleConnection ||
            (ConnectionMode == DbMode.SingleWriter && _isWriteConnection))
        {
            _readerConnectionString = ConnectionPoolingConfiguration.StripPoolingSetting(
                _readerConnectionString,
                _dialect?.PoolingSettingName);
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
                readerBuilder);
            _readerConnectionString = _dialect.PrepareConnectionStringForDataSource(_readerConnectionString, readOnly: true);
        }

        // 3. Finalize writer connection string: -rw suffix → MaxPoolSize → provider
        //    DataSource settings.  Must happen AFTER reader derivation so the reader
        //    is not polluted with -rw.
        _connectionString = ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(
            _connectionString,
            _dialect?.ApplicationNameSettingName,
            WriteApplicationNameSuffix,
            effectiveApplicationName);

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
                overrideExisting: true, writerBuilder);
        }
        else if (ConnectionMode == DbMode.SingleWriter)
        {
            // SingleWriter: force the writer pool to exactly 1 to prevent concurrent writes.
            // Readers use a separate pool (pooling is stripped from the reader connection string),
            // so only the write slot needs to be sized here.
            _connectionString = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
                _connectionString, 1, _dialect?.MaxPoolSizeSettingName,
                overrideExisting: true, writerBuilder);
        }
        else
        {
            // Standard/PreventDatabaseUnload: reader and writer always use separate ADO.NET pools
            // (differentiated via ApplicationName suffix or Connection Timeout delta).
            // Stamp the resolved write size so the governor and the provider pool agree.
            // Configuration wins over connection-string, which wins over the dialect default.
            var writeMax = ResolveEffectiveMaxPoolSize(_configuredWritePoolSize, _connectionString, "writer");
            _connectionString = ConnectionPoolingConfiguration.ApplyMaxPoolSize(
                _connectionString, writeMax, _dialect?.MaxPoolSizeSettingName,
                overrideExisting: true, writerBuilder);
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
        if (configuredMax.HasValue)
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

                return EnsurePreventUnloadCapacity(
                    ApplyAbsolutePoolLimit(csMaxPoolSize, "connection string"), poolLabel)
                    ?? ApplyAbsolutePoolLimit(csMaxPoolSize, "connection string");
            }
        }

        // 3. Dialect default.
        return ApplyAbsolutePoolLimit(
            _dialect?.DefaultMaxPoolSize ?? SqlDialect.FallbackMaxPoolSize,
            "dialect default");
    }

    private int? EnsurePreventUnloadCapacity(int? maxPoolSize, string poolLabel)
    {
        if (!maxPoolSize.HasValue || maxPoolSize.Value == 0 || maxPoolSize.Value >= 2)
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
            effectiveApplicationName);

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
                _dialect.ReadOnlyPoolDiscriminatorSettingValue);
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

    internal void AttachInitialSentinelSlotsIfNeeded()
    {
        if (!_effectivePoolGovernorEnabled)
        {
            return;
        }

        foreach (var (connection, executionType) in GetSentinelSnapshot())
        {
            if (connection is not TrackedConnection tracked)
            {
                continue;
            }

            var slot = AcquireInfrastructureSlot(executionType);
            tracked.AttachSlot(slot);
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
        if (ConnectionStringNormalizationCache.TryGet(connectionString, out normalized!))
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

        _ = ConnectionStringNormalizationCache.TryAdd(connectionString, normalized);
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

        foreach (var entry in originalSensitive)
        {
            if (!normalizedSensitive.TryGetValue(entry.Key, out var value) ||
                string.IsNullOrWhiteSpace(value))
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
        // Deliberately NOT RedactConnectionString here: that method collapses every sensitive
        // value to the same literal "REDACTED" for display/logging, which is correct for logs but
        // wrong for a pool-identity key — two tenants sharing a server+database with DIFFERENT
        // credentials (a real, distinct connection pool each) would hash identically and either
        // wrongly throw (EnforceUniqueConnectionString) or wrongly warn (the always-on duplicate
        // check). HashSensitiveConnectionStringValues preserves distinctness between different
        // secret values while still never retaining the plaintext secret in the hashed input.
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
        catch
        {
            return "UNPARSEABLE_CONNECTION_STRING";
        }
    }

    /// <summary>
    /// Claims the raw, caller-supplied connection string(s) — not the internally-decorated
    /// reader/writer variants — for <see cref="IDatabaseContextConfiguration.EnforceUniqueConnectionString"/>.
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

    private IReadOnlyList<string> ClaimUniqueConnectionStrings(IDatabaseContextConfiguration configuration)
    {
        return UniqueConnectionStringRegistry.ClaimAll(this, ComputeConnectionStringKeys(configuration));
    }

    private IReadOnlyList<string> RegisterConnectionStringsForDuplicateWarning(
        IDatabaseContextConfiguration configuration)
    {
        return UniqueConnectionStringRegistry.RegisterAllForWarning(this, ComputeConnectionStringKeys(configuration),
            _logger);
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

    private DbMode CoerceMode(DbMode requested, SupportedDatabase product, bool isLocalDb, bool isFirebirdEmbedded)
    {
        // Key principle:
        // 1. COERCE when requested mode is UNSAFE for the provider
        // 2. HONOR when requested mode is SAFE but less functional (for testing)
        // 3. Best mode always selects most functional safe mode

        switch (product)
        {
            case SupportedDatabase.Sqlite or SupportedDatabase.DuckDB:
                {
                    var kind = DetectInMemoryKind(product, _connectionString);

                    // Isolated in-memory REQUIRES SingleConnection (no other mode works)
                    if (kind == InMemoryKind.Isolated)
                    {
                        if (requested != DbMode.SingleConnection)
                        {
                            LogModeOverride(requested, DbMode.SingleConnection,
                                "Isolated in-memory requires SingleConnection");
                        }

                        return DbMode.SingleConnection;
                    }

                    // For shared in-memory and file-based SQLite/DuckDB:
                    // Most functional: SingleWriter
                    // UNSAFE: Standard/PreventDatabaseUnload (lock contention)
                    // Safe but less functional: SingleConnection

                    if (requested == DbMode.Best)
                    {
                        LogModeOverride(requested, DbMode.SingleWriter, "SQLite/DuckDB: Best selects SingleWriter");
                        return DbMode.SingleWriter;
                    }

                    // Coerce UNSAFE modes (Standard, PreventDatabaseUnload) to SingleWriter
                    if (requested == DbMode.Standard || requested == DbMode.PreventDatabaseUnload)
                    {
                        LogModeOverride(requested, DbMode.SingleWriter,
                            "SQLite/DuckDB: Standard/PreventDatabaseUnload unsafe, using SingleWriter");
                        return DbMode.SingleWriter;
                    }

                    // Honor safe but less functional modes (SingleConnection, SingleWriter)
                    return requested;
                }

            case SupportedDatabase.Firebird when isFirebirdEmbedded:
                {
                    // Embedded Firebird supports multiple simultaneous attachments. Keep one
                    // passive attachment alive without forcing all application work through it.
                    if (requested != DbMode.PreventDatabaseUnload)
                    {
                        LogModeOverride(requested, DbMode.PreventDatabaseUnload,
                            "Firebird embedded supports multiple attachments; using PreventDatabaseUnload");
                    }

                    return DbMode.PreventDatabaseUnload;
                }

            case SupportedDatabase.SqlServer when isLocalDb:
                {
                    // LocalDB REQUIRES PreventDatabaseUnload to prevent unload
                    if (requested != DbMode.PreventDatabaseUnload)
                    {
                        LogModeOverride(requested, DbMode.PreventDatabaseUnload,
                            "LocalDB requires PreventDatabaseUnload");
                    }

                    return DbMode.PreventDatabaseUnload;
                }

            case SupportedDatabase.PostgreSql
                or SupportedDatabase.CockroachDb
                or SupportedDatabase.MySql
                or SupportedDatabase.MariaDb
                or SupportedDatabase.Oracle
                or SupportedDatabase.Firebird
                or SupportedDatabase.SqlServer
                or SupportedDatabase.Db2:
                {
                    // Full server databases: all modes are SAFE
                    // Most functional: Standard
                    // Safe but less functional: SingleWriter, SingleConnection, PreventDatabaseUnload

                    if (requested == DbMode.Best)
                    {
                        LogModeOverride(requested, DbMode.Standard, "Full server: Best selects Standard");
                        return DbMode.Standard;
                    }

                    // Honor ANY explicit choice - all modes are safe on full servers
                    // Users can force less functional modes for testing
                    return requested;
                }

            default:
                {
                    // Unknown provider
                    if (requested == DbMode.Best)
                    {
                        LogModeOverride(requested, DbMode.Standard, "Unknown provider: Best defaults to Standard");
                        return DbMode.Standard;
                    }

                    return requested;
                }
        }
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

    private void WarnOnModeMismatch(DbMode resolved, SupportedDatabase product, bool wasCoerced)
    {
        // Don't warn if we auto-coerced (already logged that with EventIds.ModeCoerced)
        if (wasCoerced)
        {
            return;
        }

        // Pattern 1: Client-server database with overly restrictive mode
        if (IsClientServerDatabase(product))
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

        // Pattern 2: SQLite/DuckDB file with Standard (potential lock contention)
        if ((product == SupportedDatabase.Sqlite || product == SupportedDatabase.DuckDB) &&
            resolved == DbMode.Standard &&
            DetectInMemoryKind(product, _connectionString) == InMemoryKind.None)
        {
            _logger.LogWarning(
                diagnostics.EventIds.ModeMismatch,
                "Standard mode used with file-based {Database}. " +
                "File-based SQLite has single-writer constraints which may cause lock contention (SQLITE_BUSY errors). " +
                "Consider SingleWriter mode for better write coordination, or enable WAL mode (PRAGMA journal_mode=WAL) " +
                "for improved read/write concurrency.",
                product
            );
        }
    }

    private bool IsClientServerDatabase(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.PostgreSql => true,
            SupportedDatabase.CockroachDb => true,
            SupportedDatabase.SqlServer => true,
            SupportedDatabase.MySql => true,
            SupportedDatabase.MariaDb => true,
            SupportedDatabase.Oracle => true,
            SupportedDatabase.Firebird => true, // Usually client-server; embedded is rare
            SupportedDatabase.Db2 => true,
            _ => false
        };
    }

    private enum InMemoryKind
    {
        None,
        Isolated,
        Shared
    }

    private static InMemoryKind DetectInMemoryKind(SupportedDatabase product, string? connectionString)
    {
        var cs = (connectionString ?? string.Empty).Trim();
        var s = cs.ToLowerInvariant();
        var normalized = s.Replace(" ", string.Empty);
        if (product == SupportedDatabase.Sqlite)
        {
            var dataSource = TryGetDataSourcePath(connectionString ?? string.Empty) ?? string.Empty;
            var dataSourceLower = dataSource.ToLowerInvariant();
            var dataSourceIsMemory = dataSourceLower.Contains(":memory:");
            var modeMem = normalized.Contains("mode=memory") ||
                          normalized.Contains("filename=:memory:") ||
                          normalized.Contains("datasource=:memory:") ||
                          dataSourceIsMemory;
            if (!modeMem)
            {
                return InMemoryKind.None;
            }

            var cacheShared = normalized.Contains("cache=shared");
            var dsIsLiteralMem = dataSourceIsMemory ||
                                 normalized.Contains("datasource=:memory:") ||
                                 normalized.Contains("filename=:memory:");
            if (cacheShared && !dsIsLiteralMem)
            {
                return InMemoryKind.Shared; // e.g., file:name?mode=memory&cache=shared
            }

            return InMemoryKind.Isolated;
        }

        if (product == SupportedDatabase.DuckDB)
        {
            if (!s.Contains("data source=:memory:"))
            {
                return InMemoryKind.None;
            }

            return s.Contains("cache=shared") ? InMemoryKind.Shared : InMemoryKind.Isolated;
        }

        return InMemoryKind.None;
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
