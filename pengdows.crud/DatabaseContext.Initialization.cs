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
//   4. Initialize connection strategy (Standard, KeepAlive, etc.)
//   5. Set up metrics collector if enabled
// - Auto-detection of DbMode for embedded databases:
//   * SQLite :memory: -> SingleConnection
//   * SQLite file mode -> SingleWriter
//   * DuckDB in-memory -> appropriate mode
// - Pool governor setup for connection limiting
// - Application name handling for connection string
// - Session settings application (timeouts, isolation levels)
// =============================================================================

using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
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
        ITypeMapRegistry? typeMapRegistry = null,
        DbMode mode = DbMode.Best,
        ReadWriteMode readWriteMode = ReadWriteMode.ReadWrite,
        ILoggerFactory? loggerFactory = null)
        : this(
            new DatabaseContextConfiguration
            {
                ProviderName = providerFactory,
                ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString)),
                ReadWriteMode = readWriteMode,
                DbMode = mode
            },
            DbProviderFactories.GetFactory(providerFactory ?? throw new ArgumentNullException(nameof(providerFactory))),
            loggerFactory ?? NullLoggerFactory.Instance,
            typeMapRegistry)
    {
    }

    [Obsolete("Use the constructor that takes DatabaseContextConfiguration instead.")]
    public DatabaseContext(
        string connectionString,
        DbProviderFactory factory,
        ITypeMapRegistry? typeMapRegistry = null,
        DbMode mode = DbMode.Best,
        ReadWriteMode readWriteMode = ReadWriteMode.ReadWrite,
        ILoggerFactory? loggerFactory = null)
        : this(
            new DatabaseContextConfiguration
            {
                ConnectionString = connectionString,
                ReadWriteMode = readWriteMode,
                DbMode = mode
            },
            factory,
            loggerFactory ?? NullLoggerFactory.Instance,
            typeMapRegistry)
    {
    }

    // Convenience overloads for reflection-based tests and ease of use
    public DatabaseContext(string connectionString, DbProviderFactory factory)
        : this(new DatabaseContextConfiguration
            {
                ConnectionString = connectionString,
                DbMode = DbMode.Best,
                ReadWriteMode = ReadWriteMode.ReadWrite
            },
            factory,
            NullLoggerFactory.Instance,
            null)
    {
    }

    public DatabaseContext(string connectionString, DbProviderFactory factory, ITypeMapRegistry typeMapRegistry)
        : this(new DatabaseContextConfiguration
            {
                ConnectionString = connectionString,
                DbMode = DbMode.Best,
                ReadWriteMode = ReadWriteMode.ReadWrite
            },
            factory,
            NullLoggerFactory.Instance,
            typeMapRegistry)
    {
    }

    public DatabaseContext(
        IDatabaseContextConfiguration configuration,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory = null,
        ITypeMapRegistry? typeMapRegistry = null)
    {
        ILockerAsync? initLocker = null;
        try
        {
            initLocker = GetLock();
            initLocker.Lock();
            Interlocked.Exchange(ref _initializing, 1);
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (string.IsNullOrWhiteSpace(configuration.ConnectionString))
            {
                throw new ArgumentException("ConnectionString is required.", nameof(configuration.ConnectionString));
            }

            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger<IDatabaseContext>();
            if (TypeCoercionHelper.Logger is NullLogger)
            {
                TypeCoercionHelper.Logger =
                    _loggerFactory.CreateLogger(nameof(TypeCoercionHelper));
            }

            ReadWriteMode = configuration.ReadWriteMode;
            TypeMapRegistry = typeMapRegistry ?? global::pengdows.crud.TypeMapRegistry.Instance;
            ConnectionMode = configuration.DbMode;
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _dataSource = null;
            _readerDataSource = null;
            _dataSourceProvided = false;
            _forceManualPrepare = configuration.ForceManualPrepare;
            _disablePrepare = configuration.DisablePrepare;
            _poolAcquireTimeout = configuration.PoolAcquireTimeout;
            _modeLockTimeout = configuration.ModeLockTimeout;
            _enablePoolGovernor = configuration.EnablePoolGovernor;
            _configuredReadPoolSize = configuration.ReadPoolSize;
            _configuredWritePoolSize = configuration.WritePoolSize;
            if (configuration.EnableMetrics)
            {
                var options = configuration.MetricsOptions ?? MetricsOptions.Default;
                _metricsCollector = new MetricsCollector(options);
                _readerMetricsCollector = new MetricsCollector(options, _metricsCollector);
                _writerMetricsCollector = new MetricsCollector(options, _metricsCollector);
                _metricsCollector.MetricsChanged += OnMetricsCollectorUpdated;
            }

            var initialConnection = InitializeInternals(configuration);

            // Build strategies now that mode is final (moved from InitializeInternals)
            _connectionStrategy = ConnectionStrategyFactory.Create(this, ConnectionMode);
            _procWrappingStrategy = ProcWrappingStrategyFactory.Create(_procWrappingStyle);

            // Delegate dialect detection to the strategy
            var (dialect, dataSourceInfo) =
                _connectionStrategy.HandleDialectDetection(initialConnection, _factory, _loggerFactory);

            if (dialect != null && dataSourceInfo != null)
            {
                _dialect = (SqlDialect)dialect;
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

            // Apply pooling defaults now that we have the final mode and dialect
            var builder = GetFactoryConnectionStringBuilder(_connectionString);
            _connectionString = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
                _connectionString,
                Product,
                ConnectionMode,
                _dialect?.SupportsExternalPooling ?? false,
                _dialect?.PoolingSettingName,
                _dialect?.MinPoolSizeSettingName,
                builder);

            // Apply application name if configured
            _connectionString = ConnectionPoolingConfiguration.ApplyApplicationName(
                _connectionString,
                configuration.ApplicationName,
                _dialect?.ApplicationNameSettingName,
                builder);

            InitializeReadOnlyConnectionResources(configuration);
            InitializePoolGovernors();

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

            // Apply session settings for persistent connections now that dialect is initialized
            if (ConnectionMode != DbMode.Standard)
            {
                if (initialConnection != null)
                {
                    ApplyPersistentConnectionSessionSettings(initialConnection);
                }
                else if (PersistentConnection is not null)
                {
                    ApplyPersistentConnectionSessionSettings(PersistentConnection);
                }
            }

            // For Standard mode, dispose the connection after dialect initialization is complete
            if (ConnectionMode == DbMode.Standard && initialConnection != null)
            {
                initialConnection.Dispose();
                // Reset counters to "fresh" state after initialization probe
                Interlocked.Exchange(ref _connectionCount, 0);
                Interlocked.Exchange(ref _peakOpenConnections, 0);
            }

            _isolationResolver = new IsolationResolver(Product, RCSIEnabled, SnapshotIsolationEnabled);

            // Connection strategy is created in InitializeInternals(finally) via ConnectionStrategyFactory
        }
        catch (Exception e)
        {
            _logger?.LogError(e.Message);
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _initializing, 0);
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
    /// Creates a new DatabaseContext using a DbDataSource (e.g., NpgsqlDataSource).
    /// This provides better performance through shared prepared statement caching.
    /// </summary>
    /// <summary>
    /// Initializes a new DatabaseContext using a DbDataSource for connection creation.
    /// The DataSource provides better performance through shared prepared statement caching,
    /// while the factory is still required for creating parameters and other provider objects.
    /// </summary>
    /// <param name="configuration">Database configuration</param>
    /// <param name="dataSource">Data source for creating connections (e.g., NpgsqlDataSource)</param>
    /// <param name="factory">Provider factory for creating parameters and other objects</param>
    /// <param name="loggerFactory">Optional logger factory</param>
    /// <param name="typeMapRegistry">Optional type map registry</param>
    public DatabaseContext(
        IDatabaseContextConfiguration configuration,
        DbDataSource dataSource,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory = null,
        ITypeMapRegistry? typeMapRegistry = null)
    {
        ILockerAsync? initLocker = null;
        try
        {
            initLocker = GetLock();
            initLocker.Lock();
            Interlocked.Exchange(ref _initializing, 1);
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (string.IsNullOrWhiteSpace(configuration.ConnectionString))
            {
                throw new ArgumentException("ConnectionString is required.", nameof(configuration.ConnectionString));
            }

            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger<IDatabaseContext>();
            if (TypeCoercionHelper.Logger is NullLogger)
            {
                TypeCoercionHelper.Logger =
                    _loggerFactory.CreateLogger(nameof(TypeCoercionHelper));
            }

            ReadWriteMode = configuration.ReadWriteMode;
            TypeMapRegistry = typeMapRegistry ?? global::pengdows.crud.TypeMapRegistry.Instance;
            ConnectionMode = configuration.DbMode;
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _readerDataSource = _dataSource;
            _dataSourceProvided = true;
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _forceManualPrepare = configuration.ForceManualPrepare;
            _disablePrepare = configuration.DisablePrepare;
            _poolAcquireTimeout = configuration.PoolAcquireTimeout;
            _modeLockTimeout = configuration.ModeLockTimeout;
            _enablePoolGovernor = configuration.EnablePoolGovernor;
            _configuredReadPoolSize = configuration.ReadPoolSize;
            _configuredWritePoolSize = configuration.WritePoolSize;
            if (configuration.EnableMetrics)
            {
                var options = configuration.MetricsOptions ?? MetricsOptions.Default;
                _metricsCollector = new MetricsCollector(options);
                _readerMetricsCollector = new MetricsCollector(options, _metricsCollector);
                _writerMetricsCollector = new MetricsCollector(options, _metricsCollector);
                _metricsCollector.MetricsChanged += OnMetricsCollectorUpdated;
            }

            var initialConnection = InitializeInternals(configuration);

            // Build strategies now that mode is final (moved from InitializeInternals)
            _connectionStrategy = ConnectionStrategyFactory.Create(this, ConnectionMode);
            _procWrappingStrategy = ProcWrappingStrategyFactory.Create(_procWrappingStyle);

            // Delegate dialect detection to the strategy
            var (dialect, dataSourceInfo) =
                _connectionStrategy.HandleDialectDetection(initialConnection, _factory, _loggerFactory);

            if (dialect != null && dataSourceInfo != null)
            {
                _dialect = (SqlDialect)dialect;
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

            // Apply pooling defaults now that we have the final mode and dialect
            var builder = GetFactoryConnectionStringBuilder(_connectionString);
            _connectionString = ConnectionPoolingConfiguration.ApplyPoolingDefaults(
                _connectionString,
                Product,
                ConnectionMode,
                _dialect?.SupportsExternalPooling ?? false,
                _dialect?.PoolingSettingName,
                _dialect?.MinPoolSizeSettingName,
                builder);

            // Apply application name if configured
            _connectionString = ConnectionPoolingConfiguration.ApplyApplicationName(
                _connectionString,
                configuration.ApplicationName,
                _dialect?.ApplicationNameSettingName,
                builder);

            InitializeReadOnlyConnectionResources(configuration);
            InitializePoolGovernors();

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

            // Apply session settings for persistent connections now that dialect is initialized
            if (ConnectionMode != DbMode.Standard)
            {
                if (initialConnection != null)
                {
                    ApplyPersistentConnectionSessionSettings(initialConnection);
                }
                else if (PersistentConnection is not null)
                {
                    ApplyPersistentConnectionSessionSettings(PersistentConnection);
                }
            }

            // For Standard mode, dispose the connection after dialect initialization is complete
            if (ConnectionMode == DbMode.Standard && initialConnection != null)
            {
                initialConnection.Dispose();
                // Reset counters to "fresh" state after initialization probe
                Interlocked.Exchange(ref _connectionCount, 0);
                Interlocked.Exchange(ref _peakOpenConnections, 0);
            }

            _isolationResolver = new IsolationResolver(Product, RCSIEnabled, SnapshotIsolationEnabled);

            // Connection strategy is created in InitializeInternals(finally) via ConnectionStrategyFactory
        }
        catch (Exception e)
        {
            _logger?.LogError(e.Message);
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _initializing, 0);
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

    private ITrackedConnection? InitializeInternals(IDatabaseContextConfiguration config)
    {
        // 1) Persist config first
        var rawConnectionString =
            config.ConnectionString ?? throw new ArgumentNullException(nameof(config.ConnectionString));
        _connectionString = NormalizeConnectionString(rawConnectionString);
        ReadWriteMode = config.ReadWriteMode;

        ITrackedConnection? initConn = null;
        try
        {
            // 2) Create + open
            var initExecutionType = IsReadOnlyConnection ? ExecutionType.Read : ExecutionType.Write;
            initConn = FactoryCreateConnection(initExecutionType, _connectionString, true, IsReadOnlyConnection, null);
            try
            {
                initConn.Open();
            }
            catch (Exception)
            {
                // For Standard/Best with Unknown providers, allow constructor to proceed without an open
                // connection so dialect falls back to SQL-92 and operations surface errors later.
                if ((ConnectionMode == DbMode.Standard || ConnectionMode == DbMode.Best) &&
                    IsEmulatedUnknown(_connectionString))
                {
                    try
                    {
                        initConn.Dispose();
                    }
                    catch
                    { /* ignore */
                    }

                    initConn = null;
                }
                else
                {
                    throw new ConnectionFailedException("Failed to open database connection.");
                }
            }

            // 3) Detect product/capabilities once
            var product = DatabaseDetectionService.DetectProduct(initConn, _factory);
            var topology = DatabaseDetectionService.DetectTopology(product, _connectionString);
            var isLocalDb = topology.IsLocalDb;
            var isFirebirdEmbedded = topology.IsEmbedded;

            // Optional: RCSI prefetch (SQL Server only)
            var rcsi = false;
            var snapshotIsolation = false;
            if (initConn != null && product == SupportedDatabase.SqlServer)
            {
                try
                {
                    using var cmd = initConn.CreateCommand();
                    cmd.CommandText =
                        "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE name = DB_NAME()";
                    var v = cmd.ExecuteScalar();
                    rcsi = v switch
                    {
                        bool b => b, byte by => by != 0, short s => s != 0, int i => i != 0,
                        _ => Convert.ToInt32(v ?? 0) != 0
                    };
                }
                catch
                { /* ignore prefetch failures */
                }

                try
                {
                    using var cmd = initConn.CreateCommand();
                    cmd.CommandText = "SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()";
                    var value = cmd.ExecuteScalar();
                    var state = value switch
                    {
                        bool b => b ? 1 : 0, byte by => by, short s => s, int i => i,
                        _ => Convert.ToInt32(value ?? 0)
                    };
                    snapshotIsolation = state == 1;
                }
                catch
                { /* ignore prefetch failures */
                }
            }

            _rcsiPrefetch = rcsi;
            _snapshotIsolationPrefetch = snapshotIsolation;

            if (initConn != null && config.DbMode == DbMode.Standard)
            {
                // Only do inline detection for Standard mode; SingleWriter mode will detect via main constructor
                _dataSourceInfo = DataSourceInformation.Create(initConn, _factory!, _loggerFactory);
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
                if (ConnectionMode is DbMode.KeepAlive or DbMode.SingleConnection or DbMode.SingleWriter)
                {
                    ApplyPersistentConnectionSessionSettings(initConn);
                    SetPersistentConnection(initConn);
                    initConn = null; // context owns it now
                }
                else
                {
                    // Standard: no persistent connection to configure here
                }
            }

            // 7) Isolation resolver after product/RCSI known
            _isolationResolver = new IsolationResolver(product, RCSIEnabled, SnapshotIsolationEnabled);

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
            { /* ignore */
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

            return builder.ConnectionString;
        }
        catch
        {
            return connectionString;
        }
    }

    private void InitializePoolGovernors()
    {
        if (_dialect == null)
        {
            _effectivePoolGovernorEnabled = false;
            _readerGovernor = null;
            _writerGovernor = null;
            return;
        }

        _effectivePoolGovernorEnabled = ConnectionMode == DbMode.SingleConnection
            ? _enablePoolGovernor
            : true;

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

        var rawWriterMax = ResolveGovernorMax(_configuredWritePoolSize, writerConfig);
        var rawReaderMax = ResolveGovernorMax(_configuredReadPoolSize, readerConfig);

        var writerKey = ComputePoolKeyHash(writerConnectionString);
        var readerKey = ComputePoolKeyHash(readerConnectionString);
        var sharedPool = string.Equals(writerKey, readerKey, StringComparison.Ordinal);

        int? sharedMax = null;
        if (sharedPool)
        {
            sharedMax = ResolveSharedMax(rawWriterMax, rawReaderMax);
        }

        var writerLabelMax = rawWriterMax;
        var readerLabelMax = rawReaderMax;
        var readerDisabled = false;

        switch (ConnectionMode)
        {
            case DbMode.SingleConnection:
                writerLabelMax = 1;
                readerLabelMax = null;
                readerDisabled = true;
                break;
            case DbMode.SingleWriter:
                writerLabelMax = 1;
                if (sharedPool && readerLabelMax.HasValue)
                {
                    readerLabelMax = Math.Max(1, readerLabelMax.Value - 1);
                }

                break;
        }

        SemaphoreSlim? sharedSemaphore = null;
        if (sharedPool && sharedMax.HasValue && sharedMax.Value > 0)
        {
            sharedSemaphore = new SemaphoreSlim(sharedMax.Value, sharedMax.Value);
        }

        _writerGovernor = CreateGovernor(PoolLabel.Writer, writerKey, sharedMax ?? writerLabelMax, sharedSemaphore);
        _readerGovernor = readerDisabled
            ? CreateGovernor(PoolLabel.Reader, readerKey, null, null, true)
            : CreateGovernor(PoolLabel.Reader, readerKey, sharedMax ?? readerLabelMax, sharedSemaphore);

        if (ConnectionMode is DbMode.SingleConnection or DbMode.SingleWriter or DbMode.KeepAlive)
        {
            AttachPinnedPermitIfNeeded();
        }
    }

    private void InitializeReadOnlyConnectionResources(IDatabaseContextConfiguration configuration)
    {
        _readerConnectionString = BuildReaderConnectionString(configuration);

        if (UsesReadOnlyConnectionStringForReads() &&
            !AreConnectionStringsEquivalentIgnoringCredentials(
                _connectionString,
                _readerConnectionString,
                _dialect?.GetReadOnlyConnectionParameter(),
                _dialect?.ApplicationNameSettingName,
                ReadOnlyApplicationNameSuffix))
        {
            throw new InvalidOperationException(
                "Reader and writer connection strings must match except for user credentials.");
        }

        if (!_dataSourceProvided && _factory != null && _dataSource == null)
        {
            _dataSource = TryCreateDataSource(_factory, _connectionString);
        }

        _readerDataSource = _dataSource;

        if (!UsesReadOnlyConnectionStringForReads() ||
            string.Equals(_readerConnectionString, _connectionString, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_factory != null)
        {
            var readDataSource = TryCreateDataSource(_factory, _readerConnectionString);
            if (readDataSource != null)
            {
                _readerDataSource = readDataSource;
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
    }

    private string BuildReaderConnectionString(IDatabaseContextConfiguration configuration)
    {
        if (_dialect == null || !UsesReadOnlyConnectionStringForReads())
        {
            return _connectionString;
        }

        var readOnly = _dialect.GetReadOnlyConnectionString(_connectionString);
        if (string.IsNullOrWhiteSpace(readOnly) ||
            string.Equals(readOnly, _connectionString, StringComparison.OrdinalIgnoreCase))
        {
            readOnly = _connectionString;
        }

        return ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(
            readOnly,
            _dialect.ApplicationNameSettingName,
            ReadOnlyApplicationNameSuffix,
            configuration.ApplicationName);
    }

    private bool UsesReadOnlyConnectionStringForReads()
    {
        if (IsReadOnlyConnection)
        {
            return true;
        }

        return ConnectionMode == DbMode.SingleWriter;
    }

    private void AttachPinnedPermitIfNeeded()
    {
        if (!_enablePoolGovernor || _writerGovernor == null)
        {
            return;
        }

        if (PersistentConnection is TrackedConnection tracked)
        {
            var permit = _writerGovernor.Acquire();
            tracked.AttachPermit(permit);
        }
    }

    private PoolGovernor CreateGovernor(
        PoolLabel label,
        string poolKey,
        int? maxPermits,
        SemaphoreSlim? sharedSemaphore,
        bool disabled = false)
    {
        if (disabled || !maxPermits.HasValue || maxPermits.Value <= 0)
        {
            return new PoolGovernor(label, poolKey, maxPermits ?? 0, _poolAcquireTimeout, true);
        }

        return new PoolGovernor(
            label,
            poolKey,
            maxPermits.Value,
            _poolAcquireTimeout,
            false,
            sharedSemaphore);
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

        return true;
    }

    private static bool ShouldIgnoreKey(string key)
    {
        var lowered = key.ToLowerInvariant();
        return lowered switch
        {
            "password" => true,
            "pwd" => true,
            "user id" => true,
            "uid" => true,
            "user" => true,
            "username" => true,
            _ => lowered.Contains("password", StringComparison.OrdinalIgnoreCase)
                 || lowered.Contains("secret", StringComparison.OrdinalIgnoreCase)
        };
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
        var redacted = RedactConnectionString(connectionString);
        var input = $"{provider}|{redacted}";

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string RedactConnectionString(string connectionString)
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
                    builder[key] = "REDACTED";
                }
            }

            return builder.ConnectionString;
        }
        catch
        {
            return connectionString;
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

        if (!builder.TryGetValue("Data Source", out var raw) || builder.Count != 1)
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
            if (csb.ContainsKey("Data Source"))
            {
                return csb["Data Source"]?.ToString();
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
                // UNSAFE: Standard/KeepAlive (lock contention)
                // Safe but less functional: SingleConnection

                if (requested == DbMode.Best)
                {
                    var target = kind == InMemoryKind.Shared ? DbMode.SingleWriter : DbMode.SingleWriter;
                    LogModeOverride(requested, target, "SQLite/DuckDB: Best selects SingleWriter");
                    return target;
                }

                // Coerce UNSAFE modes (Standard, KeepAlive) to SingleWriter
                if (requested == DbMode.Standard || requested == DbMode.KeepAlive)
                {
                    LogModeOverride(requested, DbMode.SingleWriter,
                        "SQLite/DuckDB: Standard/KeepAlive unsafe, using SingleWriter");
                    return DbMode.SingleWriter;
                }

                // Honor safe but less functional modes (SingleConnection, SingleWriter)
                return requested;
            }

            case SupportedDatabase.Firebird when isFirebirdEmbedded:
            {
                // Embedded Firebird REQUIRES SingleConnection
                if (requested != DbMode.SingleConnection)
                {
                    LogModeOverride(requested, DbMode.SingleConnection, "Firebird embedded requires SingleConnection");
                }

                return DbMode.SingleConnection;
            }

            case SupportedDatabase.SqlServer when isLocalDb:
            {
                // LocalDB REQUIRES KeepAlive to prevent unload
                if (requested != DbMode.KeepAlive)
                {
                    LogModeOverride(requested, DbMode.KeepAlive, "LocalDB requires KeepAlive");
                }

                return DbMode.KeepAlive;
            }

            case SupportedDatabase.PostgreSql
                or SupportedDatabase.CockroachDb
                or SupportedDatabase.MySql
                or SupportedDatabase.MariaDb
                or SupportedDatabase.Oracle
                or SupportedDatabase.Firebird
                or SupportedDatabase.SqlServer:
            {
                // Full server databases: all modes are SAFE
                // Most functional: Standard
                // Safe but less functional: SingleWriter, SingleConnection, KeepAlive

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

    private static bool IsEmulatedUnknown(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        return connectionString.IndexOf("emulatedproduct=unknown", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private DbConnectionStringBuilder GetFactoryConnectionStringBuilder(string connectionString)
    {
        var input = string.IsNullOrEmpty(connectionString) ? _connectionString : connectionString;
        return ConnectionStringHelper.Create(_factory!, input);
    }

    private DbDataSource? TryCreateDataSource(DbProviderFactory factory, string connectionString)
    {
        try
        {
            var factoryType = factory.GetType();
            var dataSourceMethod =
                factoryType.GetMethod("CreateDataSource", new[] { typeof(DbConnectionStringBuilder) });
            if (dataSourceMethod != null)
            {
                var builder = factory.CreateConnectionStringBuilder() ?? new DbConnectionStringBuilder();
                builder.ConnectionString = connectionString;
                var dataSource = dataSourceMethod.Invoke(factory, new object?[] { builder }) as DbDataSource;
                if (dataSource != null)
                {
                    _logger.LogInformation("Using DbDataSource from provider factory: {FactoryType}",
                        factoryType.FullName);
                    return dataSource;
                }
            }

            dataSourceMethod = factoryType.GetMethod("CreateDataSource", new[] { typeof(string) });
            if (dataSourceMethod != null && dataSourceMethod.DeclaringType != typeof(DbProviderFactory))
            {
                var dataSource = dataSourceMethod.Invoke(factory, new object?[] { connectionString }) as DbDataSource;
                if (dataSource != null)
                {
                    _logger.LogInformation("Using DbDataSource from provider factory: {FactoryType}",
                        factoryType.FullName);
                    return dataSource;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to create DbDataSource for provider factory {FactoryType}.",
                factory.GetType().FullName);
            return null;
        }
    }

    #endregion
}
