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
        ILockerAsync? initLocker = null;
        try
        {
            initLocker = GetLockInternal();
            initLocker.Lock();
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
            // ExecuteSessionSettings handles its own exceptions internally (logs + returns).
            // No outer try-catch needed here.
            _firstOpenHandlerRw = tc => ExecuteSessionSettings(tc, false);
            _firstOpenHandlerRo = tc => ExecuteSessionSettings(tc, true);
            _firstOpenHandlerAsyncRw = async (tc, ct) =>
            {
                try { await ExecuteSessionSettingsAsync(tc, false, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _logger.LogError(ex, "Failed to apply session settings on first open for {Name}", Name); }
            };
            _firstOpenHandlerAsyncRo = async (tc, ct) =>
            {
                try { await ExecuteSessionSettingsAsync(tc, true, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _logger.LogError(ex, "Failed to apply session settings on first open for {Name}", Name); }
            };
            _prepareMode = configuration.PrepareMode;
            _readerPlanCacheSize = configuration.ReaderPlanCacheSize;
            _poolAcquireTimeout = configuration.PoolAcquireTimeout;
            _modeLockTimeout = configuration.ModeLockTimeout;
            _enableSingleWriterFairness = configuration.EnableSingleWriterFairness;
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

            var initialConnection = InitializeInternals(configuration);

            // Build strategies now that mode is final (moved from InitializeInternals)
            _connectionStrategy = ConnectionStrategyFactory.Create(this, ConnectionMode);
            _procWrappingStrategy = ProcWrappingStrategyFactory.Create(_procWrappingStyle);

            // Delegate dialect detection to the strategy
            var (dialect, dataSourceInfo) =
                _connectionStrategy.HandleDialectDetection(initialConnection, _factory, _loggerFactory);

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

            // PRE-COMPUTE SESSION SETTINGS: Compute the "ready-to-go" session strings once
            // per context. GetFinalSessionSettings(bool) ensures that each dialect
            // returns exactly ONE optimized string (combining baseline + intent)
            // to ensure 1 RTT and 1 execution on the server on the hot path.
            _cachedReadWriteSessionSettings = _dialect.GetFinalSessionSettings(readOnly: false);
            _cachedReadOnlySessionSettings = _dialect.GetFinalSessionSettings(readOnly: true);

            Name = _dataSourceInfo.DatabaseProductName;
            _procWrappingStyle = _dataSourceInfo.ProcWrappingStyle;
            if (Product == SupportedDatabase.DuckDB)
            {
                RequiresSerializedOpen = true;
                _connectionOpenGate = new SemaphoreSlim(1, 1);
                _connectionOpenLocker = new ReusableAsyncLocker(_connectionOpenGate);
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
                builder);

            if (ConnectionMode is DbMode.SingleWriter or DbMode.SingleConnection)
            {
                _connectionString = ConnectionPoolingConfiguration.StripPoolingSetting(
                    _connectionString,
                    _dialect?.PoolingSettingName);
            }

            InitializeReadOnlyConnectionResources(configuration, effectiveApplicationName);

            // Validate read-only connection if an explicit RO connection string was provided
            if (!string.IsNullOrWhiteSpace(configuration.ReadOnlyConnectionString) &&
                HasDedicatedReadConnectionString())
            {
                TestConnect(_readerConnectionString, "ReadOnlyValidation", "ReadOnly");
            }

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

            // Special case: SingleConnection's pinned connection opened before detection.
            // KeepAlive sentinel doesn't need settings — it's never used for work.
            if (ConnectionMode == DbMode.SingleConnection)
            {
                var target = initialConnection ?? PersistentConnection;
                if (target != null)
                {
                    ExecuteSessionSettings(target, IsReadOnlyConnection);
                }
            }

            // For Standard and SingleWriter modes, dispose the connection after dialect initialization is complete
            if (ConnectionMode is DbMode.Standard or DbMode.SingleWriter && initialConnection != null)
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
            _logger?.LogError(e, "DatabaseContext construction failed.");
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
                        bool b => b,
                        byte by => by != 0,
                        short s => s != 0,
                        int i => i != 0,
                        _ => Convert.ToInt32(v ?? 0) != 0
                    };
                }
                catch
                {
                    /* ignore prefetch failures */
                }

                try
                {
                    using var cmd = initConn.CreateCommand();
                    cmd.CommandText = "SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()";
                    var value = cmd.ExecuteScalar();
                    var state = value switch
                    {
                        bool b => b ? 1 : 0,
                        byte by => by,
                        short s => s,
                        int i => i,
                        _ => Convert.ToInt32(value ?? 0)
                    };
                    snapshotIsolation = state == 1;
                }
                catch
                {
                    /* ignore prefetch failures */
                }
            }

            _rcsiPrefetch = rcsi;
            _snapshotIsolationPrefetch = snapshotIsolation;

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
                if (ConnectionMode is DbMode.KeepAlive or DbMode.SingleConnection)
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

    private void InitializePoolGovernors()
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

        // Silently clamp MinPoolSize to [0, MaxPoolSize] so the driver receives valid values.
        var minPoolSizeKey = _dialect?.MinPoolSizeSettingName;
        _connectionString = ConnectionPoolingConfiguration.ClampMinPoolSize(
            _connectionString, minPoolSizeKey, writerConfig.MinPoolSize, rawWriterMax);
        if (!string.IsNullOrWhiteSpace(_readerConnectionString))
        {
            _readerConnectionString = ConnectionPoolingConfiguration.ClampMinPoolSize(
                _readerConnectionString, minPoolSizeKey, readerConfig.MinPoolSize, rawReaderMax);
        }

        // ReadOnly context: the write pool is forbidden — no write connections permitted.
        if (!_isWriteConnection)
        {
            rawWriterMax = 0;
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
        var sharesTurnstile = string.Equals(writerKey, readerKey, StringComparison.Ordinal);

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
            ownsTurnstile: turnstile != null); // Writers hold turnstile until slot released

        _readerGovernor = CreateGovernor(
            PoolLabel.Reader,
            readerKey,
            readerLabelMax,
            null,
            false,
            _metricsCollector != null,
            turnstile: turnstile,
            holdTurnstile: false,
            ownsTurnstile: false); // Readers touch-and-release turnstile

        // Attach slot for modes with persistent connections.
        if (ConnectionMode == DbMode.KeepAlive)
        {
            AttachPinnedSlotIfNeeded();
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

    private void InitializeReadOnlyConnectionResources(IDatabaseContextConfiguration configuration,
        string effectiveApplicationName)
    {
        _explicitReadOnlyConnectionString = !string.IsNullOrWhiteSpace(configuration.ReadOnlyConnectionString);
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
            var readMaxPoolSize = ResolveEffectiveMaxPoolSize(_configuredReadPoolSize, _readerConnectionString);
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
            var readPoolSizeForWriter = ResolveEffectiveMaxPoolSize(_configuredReadPoolSize, _connectionString);
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
            // Standard/KeepAlive: reader and writer always use separate ADO.NET pools
            // (differentiated via ApplicationName suffix or Connection Timeout delta).
            // Stamp the resolved write size so the governor and the provider pool agree.
            // Configuration wins over connection-string, which wins over the dialect default.
            var writeMax = ResolveEffectiveMaxPoolSize(_configuredWritePoolSize, _connectionString);
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
    /// priority chain: explicit value already in the CS → context configuration →
    /// dialect default.
    /// </summary>
    private int ResolveEffectiveMaxPoolSize(int? configuredMax, string connectionString)
    {
        // 1. Caller-supplied configuration — highest priority; wins over anything in the connection string.
        if (configuredMax.HasValue && configuredMax.Value > 0)
        {
            return configuredMax.Value;
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

                return ApplyAbsolutePoolLimit(
                    csMaxPoolSize,
                    "connection string");
            }
        }

        // 3. Dialect default.
        return ApplyAbsolutePoolLimit(
            _dialect?.DefaultMaxPoolSize ?? SqlDialect.FallbackMaxPoolSize,
            "dialect default");
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

        if (mode == DbMode.SingleWriter &&
            configuredWritePoolSize.HasValue &&
            configuredWritePoolSize.Value == 0)
        {
            _logger.LogWarning(
                "SingleWriter with {Setting}=0 promotes the context to ReadOnly mode.",
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

    private void AttachPinnedSlotIfNeeded()
    {
        if (!_effectivePoolGovernorEnabled || _writerGovernor == null || _writerGovernor.Forbidden)
        {
            return;
        }

        if (PersistentConnection is TrackedConnection tracked)
        {
            var slot = _writerGovernor.Acquire();
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
        bool ownsTurnstile = false)
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
            ownsTurnstile: ownsTurnstile);
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
        var redacted = RedactConnectionString(connectionString);
        var input = $"{provider}|{redacted}";

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
                    // UNSAFE: Standard/KeepAlive (lock contention)
                    // Safe but less functional: SingleConnection

                    if (requested == DbMode.Best)
                    {
                        LogModeOverride(requested, DbMode.SingleWriter, "SQLite/DuckDB: Best selects SingleWriter");
                        return DbMode.SingleWriter;
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
