#region

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
// using pengdows.crud.strategies.connection; // superseded by strategies namespace
using pengdows.crud.dialects;
using pengdows.crud.infrastructure;
using pengdows.crud.isolation;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using pengdows.crud.strategies.connection;
using pengdows.crud.strategies.proc;

#endregion

namespace pengdows.crud;

public class DatabaseContext : SafeAsyncDisposableBase, IDatabaseContext, IContextIdentity, ISqlDialectProvider
{
    private readonly DbProviderFactory _factory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<IDatabaseContext> _logger;
    private IConnectionStrategy _connectionStrategy = null!;
    private IProcWrappingStrategy _procWrappingStrategy = null!;
    private ProcWrappingStyle _procWrappingStyle;
    private bool _applyConnectionSessionSettings;
    private ITrackedConnection? _connection = null;

    private long _connectionCount;
    private string _connectionString = string.Empty;
    private DataSourceInformation _dataSourceInfo = null!;
    private readonly SqlDialect _dialect;
    private IIsolationResolver _isolationResolver;
    private bool _isReadConnection = true;
    private bool _isWriteConnection = true;
    private long _maxNumberOfOpenConnections;
    
    // Additional performance counters for granular connection pool monitoring
    private long _totalConnectionsCreated;
    private long _totalConnectionsReused;
    private long _totalConnectionFailures;
    private long _totalConnectionTimeoutFailures;
    private readonly bool _setDefaultSearchPath;
    private string _connectionSessionSettings = string.Empty;
    private readonly DbMode _originalUserMode;
    private readonly bool? _forceManualPrepare;
    private readonly bool? _disablePrepare;
    private bool? _rcsiPrefetch;
    private int _initializing; // 0 = false, 1 = true
    private bool _sessionSettingsAppliedOnOpen;

    public Guid RootId { get; } = Guid.NewGuid();

    private static readonly char[] _parameterPrefixes = { '@', '?', ':' };

    [Obsolete("Use the constructor that takes DatabaseContextConfiguration instead.")]
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
            (loggerFactory ?? NullLoggerFactory.Instance),
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
            (loggerFactory ?? NullLoggerFactory.Instance),
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
            initLocker.LockAsync().GetAwaiter().GetResult();
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
            TypeCoercionHelper.Logger =
                _loggerFactory.CreateLogger(nameof(TypeCoercionHelper));
            ReadWriteMode = configuration.ReadWriteMode;
            TypeMapRegistry = typeMapRegistry ?? global::pengdows.crud.TypeMapRegistry.Instance;
            ConnectionMode = configuration.DbMode;
            _originalUserMode = configuration.DbMode;
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _setDefaultSearchPath = configuration.SetDefaultSearchPath;
            _forceManualPrepare = configuration.ForceManualPrepare;
            _disablePrepare = configuration.DisablePrepare;

            var initialConnection = InitializeInternals(configuration);
            if (initialConnection != null)
            {
                _dialect = SqlDialectFactory.CreateDialect(initialConnection, _factory, _loggerFactory);
                _dataSourceInfo = new DataSourceInformation(_dialect);
            }
            else if (PersistentConnection is not null)
            {
                // In persistent modes, InitializeInternals handed ownership to the context.
                // Use the persistent connection to detect product/dialect now.
                _dialect = SqlDialectFactory.CreateDialect(PersistentConnection, _factory, _loggerFactory);
                _dataSourceInfo = new DataSourceInformation(_dialect);
            }
            else
            {
                // Fall back to a safe SQL-92 dialect when no connection is opened (e.g., Standard mode with failing open)
                var logger = _loggerFactory.CreateLogger<SqlDialect>();
                _dialect = new Sql92Dialect(_factory, logger);
                _dialect.InitializeUnknownProductInfo();
                _dataSourceInfo = new DataSourceInformation(_dialect);
            }
            Name = _dataSourceInfo.DatabaseProductName;
            _procWrappingStyle = _dataSourceInfo.ProcWrappingStyle;

            if (initialConnection != null)
            {
                RCSIEnabled = _rcsiPrefetch ?? _dialect.IsReadCommittedSnapshotOn(initialConnection);
            }
            else
            {
                RCSIEnabled = false;
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
                Interlocked.Exchange(ref _maxNumberOfOpenConnections, 0);
            }

            _isolationResolver = new IsolationResolver(Product, RCSIEnabled);

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


    private ReadWriteMode _readWriteMode = ReadWriteMode.ReadWrite;
    public ReadWriteMode ReadWriteMode
    {
        get => _readWriteMode;
        set
        {
            _readWriteMode = value == ReadWriteMode.WriteOnly ? ReadWriteMode.ReadWrite : value;
            _isReadConnection = (_readWriteMode & ReadWriteMode.ReadOnly) == ReadWriteMode.ReadOnly ;
            _isWriteConnection = (_readWriteMode & ReadWriteMode.WriteOnly) == ReadWriteMode.WriteOnly;
            if (_isWriteConnection)
            {
                //write connection implies read connection
                _isWriteConnection = true;
            }
        }
    }

    public string Name { get; set; }

    // Expose original requested mode for internal strategy decisions
    internal DbMode OriginalUserMode => _originalUserMode;

    public string ConnectionString => _connectionString;

    private void SetConnectionString(string value)
    {
        if (!string.IsNullOrWhiteSpace(_connectionString) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Connection string reset attempted.");
        }

        _connectionString = value;
    }


    public bool IsReadOnlyConnection => _isReadConnection && !_isWriteConnection;
    public bool RCSIEnabled { get; private set; }

    public ILockerAsync GetLock()
    {
        ThrowIfDisposed();
        return NoOpAsyncLocker.Instance;
    }


    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
    public DbMode ConnectionMode { get; private set; }


    public ITypeMapRegistry TypeMapRegistry { get; }

    public IDataSourceInformation DataSourceInfo => _dataSourceInfo;
    public string SessionSettingsPreamble => _dialect.GetConnectionSessionSettings(this, IsReadOnlyConnection);

    public ITransactionContext BeginTransaction(
        IsolationLevel? isolationLevel = null,
        ExecutionType executionType = ExecutionType.Write,
        bool? readOnly = null)
    {
        var ro = readOnly ?? (executionType == ExecutionType.Read);
        if (ro)
        {
            executionType = ExecutionType.Read;
            if (!_isReadConnection)
            {
                throw new InvalidOperationException("Context is not readable.");
            }

            if (isolationLevel is null)
            {
                isolationLevel = _isolationResolver.Resolve(IsolationProfile.SafeNonBlockingReads);
            }
            else
            {
                _isolationResolver.Validate(isolationLevel.Value);
            }
        }
        else
        {
            if (!_isWriteConnection)
            {
                throw new NotSupportedException("Context is read-only.");
            }

            if (executionType == ExecutionType.Read)
            {
                throw new InvalidOperationException("Write transaction requested with read execution type.");
            }

            if (isolationLevel is null)
            {
                var supported = _isolationResolver.GetSupportedLevels();
                if (supported.Contains(IsolationLevel.ReadCommitted))
                {
                    isolationLevel = IsolationLevel.ReadCommitted;
                }
                else if (supported.Contains(IsolationLevel.Serializable))
                {
                    isolationLevel = IsolationLevel.Serializable;
                }
                else
                {
                    isolationLevel = supported.First();
                }
            }
        }

        return new TransactionContext(this, isolationLevel.Value, executionType, ro);
    }

    public ITransactionContext BeginTransaction(
        IsolationProfile isolationProfile,
        ExecutionType executionType = ExecutionType.Write,
        bool? readOnly = null)
    {
        var level = _isolationResolver.Resolve(isolationProfile);
        return BeginTransaction(level, executionType, readOnly);
    }

    public string CompositeIdentifierSeparator => _dataSourceInfo.CompositeIdentifierSeparator;
    public SupportedDatabase Product => _dataSourceInfo?.Product ?? SupportedDatabase.Unknown;
    // ProcWrappingStyle is defined below with a setter to update strategy
    public int MaxParameterLimit => _dataSourceInfo.MaxParameterLimit;
    public int MaxOutputParameters => _dataSourceInfo.MaxOutputParameters;
    public long MaxNumberOfConnections => Interlocked.Read(ref _maxNumberOfOpenConnections);
    public long NumberOfOpenConnections => Interlocked.Read(ref _connectionCount);
    
    /// <summary>
    /// Gets the total number of connections created during the lifetime of this context.
    /// This includes both reused and newly created connections.
    /// </summary>
    public long TotalConnectionsCreated => Interlocked.Read(ref _totalConnectionsCreated);
    
    /// <summary>
    /// Gets the total number of connections that were reused from the connection pool.
    /// </summary>
    public long TotalConnectionsReused => Interlocked.Read(ref _totalConnectionsReused);
    
    /// <summary>
    /// Gets the total number of connection failures that occurred.
    /// </summary>
    public long TotalConnectionFailures => Interlocked.Read(ref _totalConnectionFailures);
    
    /// <summary>
    /// Gets the total number of connection timeout failures specifically.
    /// </summary>
    public long TotalConnectionTimeoutFailures => Interlocked.Read(ref _totalConnectionTimeoutFailures);
    
    /// <summary>
    /// Gets the connection pool efficiency ratio (reused / total created).
    /// Returns 0 if no connections have been created.
    /// </summary>
    public double ConnectionPoolEfficiency
    {
        get
        {
            var total = TotalConnectionsCreated;
            return total == 0 ? 0.0 : (double)TotalConnectionsReused / total;
        }
    }
    
    /// <summary>
    /// Tracks a connection failure for monitoring purposes.
    /// </summary>
    /// <param name="exception">The exception that caused the failure</param>
    internal void TrackConnectionFailure(Exception exception)
    {
        Interlocked.Increment(ref _totalConnectionFailures);
        
        // Track specific timeout failures
        if (IsTimeoutException(exception))
        {
            Interlocked.Increment(ref _totalConnectionTimeoutFailures);
        }
        
        _logger.LogWarning(exception, "Connection failure tracked: {ExceptionType}", exception.GetType().Name);
    }
    
    /// <summary>
    /// Tracks a connection reuse for monitoring purposes.
    /// </summary>
    internal void TrackConnectionReuse()
    {
        Interlocked.Increment(ref _totalConnectionsReused);
    }
    
    private static bool IsTimeoutException(Exception exception)
    {
        return exception is TimeoutException ||
               exception.GetType().Name.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
    }
    public string QuotePrefix => _dialect.QuotePrefix;
    public string QuoteSuffix => _dialect.QuoteSuffix;
    public bool? ForceManualPrepare => _forceManualPrepare;
    public bool? DisablePrepare => _disablePrepare;
    public bool SetDefaultSearchPath => _setDefaultSearchPath;

    public ISqlContainer CreateSqlContainer(string? query = null)
    {
        return new SqlContainer(this, query);
    }

    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value,
        ParameterDirection direction = ParameterDirection.Input)
    {
        var p = _dialect.CreateDbParameter(name, type, value);
        p.Direction = direction;
        return p;
    }

    // Back-compat overloads (interface surface)
    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        return CreateDbParameter(name, type, value, ParameterDirection.Input);
    }


    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false)
    {
        return _connectionStrategy.GetConnection(executionType, isShared);
    }

    public string GenerateRandomName(int length = 5, int parameterNameMaxLength = 30)
    {
       return _dialect.GenerateRandomName(length, parameterNameMaxLength);
    }


    public DbParameter CreateDbParameter<T>(DbType type, T value,
        ParameterDirection direction = ParameterDirection.Input)
    {
        return CreateDbParameter(null, type, value, direction);
    }

    // Back-compat overload (interface surface)
    public DbParameter CreateDbParameter<T>(DbType type, T value)
    {
        return CreateDbParameter(type, value, ParameterDirection.Input);
    }

    public void AssertIsReadConnection()
    {
        if (!_isReadConnection)
        {
            throw new InvalidOperationException("The connection is not read connection.");
        }
    }

    public void AssertIsWriteConnection()
    {
        if (!_isWriteConnection)
        {
            throw new InvalidOperationException("The connection is not write connection.");
        }
    }


    public void CloseAndDisposeConnection(ITrackedConnection? connection)
    {
        _connectionStrategy.ReleaseConnection(connection);
    }


    public string WrapObjectName(string name)
    {
        return _dialect.WrapObjectName(name);
    }

    public string MakeParameterName(DbParameter dbParameter)
    {
        return _dialect.MakeParameterName(dbParameter);
    }

    public string MakeParameterName(string parameterName)
    {
        return _dialect.MakeParameterName(parameterName);
    }

    private ITrackedConnection FactoryCreateConnection(
        string? connectionString = null,
        bool isSharedConnection = false,
        bool readOnly = false,
        Action<DbConnection>? onFirstOpen = null)
    {
        SanitizeConnectionString(connectionString);

        var connection = _factory.CreateConnection() ??
                         throw new InvalidOperationException("Factory returned null DbConnection.");
        connection.ConnectionString = ConnectionString;
        _dialect?.ApplyConnectionSettings(connection, this, readOnly);
        
        // Increment total connections created counter when a new connection is actually created
        Interlocked.Increment(ref _totalConnectionsCreated);

        // Ensure session settings from the active dialect are applied on first open for all modes.
        Action<DbConnection>? firstOpenHandler = conn =>
        {
            ILockerAsync? guard = null;
            try
            {
                guard = GetLock();
                guard.LockAsync().GetAwaiter().GetResult();
                // Apply session settings for all connection modes.
                // Prefer dialect-provided settings when available; fall back to precomputed string.
                string settings;
                if (Interlocked.CompareExchange(ref _initializing, 0, 0) == 1 && ConnectionMode == DbMode.Standard && !readOnly)
                {
                    // During constructor probe in Standard mode, skip applying settings.
                    return;
                }
                if (_dialect != null)
                {
                    settings = _dialect.GetConnectionSessionSettings(this, readOnly) ?? string.Empty;
                }
                else
                {
                    // Dialect not initialized yet (constructor path). Derive lightweight settings
                    // from the opened connection's product metadata for first application.
                    try
                    {
                        var schema = conn.GetSchema(DbMetaDataCollectionNames.DataSourceInformation);
                        var productName = schema.Rows.Count > 0 ? schema.Rows[0].Field<string>("DataSourceProductName") : null;
                        var lower = (productName ?? string.Empty).ToLowerInvariant();
                        if (lower.Contains("mysql") || lower.Contains("mariadb"))
                        {
                            settings = "SET SESSION sql_mode = 'STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ZERO_DATE,NO_ZERO_IN_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES,NO_BACKSLASH_ESCAPES';";
                            if (readOnly)
                            {
                                settings += "\nSET SESSION TRANSACTION READ ONLY;";
                            }
                        }
                        else if (lower.Contains("oracle"))
                        {
                            settings = "ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY-MM-DD';";
                            if (readOnly)
                            {
                                settings += "\nALTER SESSION SET READ ONLY;";
                            }
                        }
                        else if (lower.Contains("postgres"))
                        {
                            settings = "SET standard_conforming_strings = on;\nSET client_min_messages = warning;";
                            if (_setDefaultSearchPath)
                            {
                                settings += "\nSET search_path = public;";
                            }
                        }
                        else if (lower.Contains("sqlite"))
                        {
                            settings = "PRAGMA foreign_keys = ON;";
                            if (readOnly)
                            {
                                settings += "\nPRAGMA query_only = ON;";
                            }
                        }
                        else if (lower.Contains("duckdb") || lower.Contains("duck db"))
                        {
                            settings = readOnly ? "PRAGMA read_only = 1;" : string.Empty;
                        }
                        else
                        {
                            settings = _connectionSessionSettings ?? string.Empty;
                        }
                    }
                    catch
                    {
                        settings = _connectionSessionSettings ?? string.Empty;
                    }
                }
                if (!string.IsNullOrWhiteSpace(settings))
                {
                    // Execute each statement separately to ensure provider compatibility
                    var parts = settings.Split(';');
                    foreach (var part in parts)
                    {
                        var stmt = part.Trim();
                        if (string.IsNullOrEmpty(stmt))
                        {
                            continue;
                        }

                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = stmt + ';';
                        cmd.ExecuteNonQuery();
                    }
                    _sessionSettingsAppliedOnOpen = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply session settings on first open for {Name}", Name);
            }
            finally
            {
                if (guard is IAsyncDisposable gad)
                {
                    gad.DisposeAsync().GetAwaiter().GetResult();
                }
                else if (guard is IDisposable gd)
                {
                    gd.Dispose();
                }
            }

            // Invoke any additional callback provided by caller
            onFirstOpen?.Invoke(conn);
        };

        var tracked = new TrackedConnection(
            connection,
            (sender, args) =>
            {
                var to = args.CurrentState;
                var from = args.OriginalState;
                switch (to)
                {
                    case ConnectionState.Open:
                    {
                        _logger.LogDebug("Opening connection: " + Name);
                        var now = Interlocked.Increment(ref _connectionCount);
                        UpdateMaxConnectionCount(now);
                        break;
                    }
                    case ConnectionState.Closed when from != ConnectionState.Broken:
                    case ConnectionState.Broken:
                    {
                        _logger.LogDebug("Closed or broken connection: " + Name);
                        Interlocked.Decrement(ref _connectionCount);
                        break;
                    }
                }
            },
            firstOpenHandler,
            onDispose: conn => { _logger.LogDebug("Connection disposed."); },
            null,
            isSharedConnection
        );
        return tracked;
    }

    private void SanitizeConnectionString(string? connectionString)
    {
        if (connectionString != null && string.IsNullOrWhiteSpace(_connectionString))
        {
            try
            {
                var csb = GetFactoryConnectionStringBuilder(connectionString);
                var tmp = csb.ConnectionString;
                SetConnectionString(tmp);
            }
            catch
            {
                SetConnectionString(connectionString);
            }
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

    // Duplicates removed; properties already exist earlier in the class

    internal ITrackedConnection FactoryCreateConnection(string? connectionString = null, bool isSharedConnection = false, bool readOnly = false)
    {
        return FactoryCreateConnection(connectionString, isSharedConnection, readOnly, null);
    }


    private void UpdateMaxConnectionCount(long current)
    {
        long previous;
        do
        {
            previous = Interlocked.Read(ref _maxNumberOfOpenConnections);
            if (current <= previous)
            {
                return; // no update needed
            }

            // try to update only if no one else has changed it
        } while (Interlocked.CompareExchange(
                     ref _maxNumberOfOpenConnections,
                     current,
                     previous) != previous);
    }

    public async ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? connection)
    {
        await _connectionStrategy.ReleaseConnectionAsync(connection).ConfigureAwait(false);
    }

    private ITrackedConnection? InitializeInternals(IDatabaseContextConfiguration config)
    {
        // 1) Persist config first
        _connectionString = config.ConnectionString ?? throw new ArgumentNullException(nameof(config.ConnectionString));
        ReadWriteMode = config.ReadWriteMode;

        ITrackedConnection? initConn = null;
        try
        {
            // 2) Create + open
            initConn = FactoryCreateConnection(_connectionString, true, IsReadOnlyConnection, null);
            try
            {
                initConn.Open();
            }
            catch (Exception)
            {
                // For Standard/Best with Unknown providers, allow constructor to proceed without an open
                // connection so dialect falls back to SQL-92 and operations surface errors later.
                if ((ConnectionMode == DbMode.Standard || ConnectionMode == DbMode.Best) && IsEmulatedUnknown(_connectionString))
                {
                    try { initConn.Dispose(); } catch { /* ignore */ }
                    initConn = null;
                }
                else
                {
                    throw new ConnectionFailedException("Failed to open database connection.");
                }
            }

            // 3) Detect product/capabilities once
            var (product, isFirebirdEmbedded, isLocalDb) = DetectProductAndTopology(initConn, _connectionString);

            // Optional: RCSI prefetch (SQL Server only)
            bool rcsi = false;
            if (product == SupportedDatabase.SqlServer)
            {
                try
                {
                    using var cmd = initConn.CreateCommand();
                    cmd.CommandText = "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE name = DB_NAME()";
                    var v = cmd.ExecuteScalar();
                    rcsi = v switch { bool b => b, byte by => by != 0, short s => s != 0, int i => i != 0, _ => Convert.ToInt32(v ?? 0) != 0 };
                }
                catch { /* ignore prefetch failures */ }
            }
            _rcsiPrefetch = rcsi;

            if (initConn != null)
            {
                _dataSourceInfo = DataSourceInformation.Create(initConn, _factory, _loggerFactory);
                _procWrappingStyle = _dataSourceInfo.ProcWrappingStyle;
                Name = _dataSourceInfo.DatabaseProductName;
            }

            // 4) Coerce ConnectionMode based on product/topology
            ConnectionMode = CoerceMode(ConnectionMode, product, isLocalDb, isFirebirdEmbedded);

            // 5) Build strategies now that mode is final
            _connectionStrategy = ConnectionStrategyFactory.Create(this, ConnectionMode);
            _procWrappingStrategy = ProcWrappingStrategyFactory.Create(_procWrappingStyle);

            // 6) Apply provider/session settings according to final mode
            if (ConnectionMode is DbMode.KeepAlive or DbMode.SingleConnection or DbMode.SingleWriter)
            {
                ApplyPersistentConnectionSessionSettings(initConn);
                SetPersistentConnection(initConn);
                initConn = null; // context owns it now
            }
            else
            {
                // Standard: apply per-connection session hints that must be present during dialect init
                SetupConnectionSessionSettingsForProvider(initConn);
                // Do NOT SetPersistentConnection
            }

            // 7) Isolation resolver after product/RCSI known
            _isolationResolver = new IsolationResolver(product, RCSIEnabled);

            // 8) Return the open initConn only for Standard (caller disposes). For persistent modes we returned null.
            return initConn;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize DatabaseContext: {Message}", ex.Message);
            // Ensure no leaked connection if we’re bailing
            try { initConn?.Dispose(); } catch { /* ignore */ }
            throw;
        }
    }

    /// <summary>Detects DB product/topology with a single parse.</summary>
    private (SupportedDatabase product, bool isFirebirdEmbedded, bool isLocalDb) DetectProductAndTopology(ITrackedConnection conn, string connectionString)
    {
        var product = InferProduct(conn);
        bool isLocalDb = false;
        bool isFirebirdEmbedded = false;

        var lower = (connectionString ?? string.Empty).ToLowerInvariant();
        if (product == SupportedDatabase.SqlServer)
        {
            isLocalDb = lower.Contains("(localdb)") || lower.Contains("localdb");
        }

        if (product == SupportedDatabase.Firebird)
        {
            try
            {
                var csb = GetFactoryConnectionStringBuilderStatic(conn, connectionString);
                string GetVal(string key) => csb.ContainsKey(key) ? csb[key]?.ToString() ?? string.Empty : string.Empty;
                var serverType = GetVal("ServerType").ToLowerInvariant();
                var clientLib = GetVal("ClientLibrary").ToLowerInvariant();
                var dataSource = GetVal("DataSource").ToLowerInvariant();
                var database = GetVal("Database").ToLowerInvariant();

                isFirebirdEmbedded =
                    serverType.Contains("embedded") ||
                    clientLib.Contains("embed") ||
                    (string.IsNullOrWhiteSpace(dataSource) &&
                     (!string.IsNullOrWhiteSpace(database) &&
                      (database.Contains('/') || database.Contains('\\') || database.EndsWith(".fdb"))));
            }
            catch { /* heuristic only */ }
        }

        return (product, isFirebirdEmbedded, isLocalDb);
    }

    private SupportedDatabase InferProduct(ITrackedConnection conn)
    {
        try
        {
            var schema = conn.GetSchema(DbMetaDataCollectionNames.DataSourceInformation);
            if (schema.Rows.Count > 0)
            {
                var name = schema.Rows[0].Field<string>("DataSourceProductName") ?? string.Empty;
                var lower = name.ToLowerInvariant();
                if (lower.Contains("sql server"))
                {
                    return SupportedDatabase.SqlServer;
                }

                if (lower.Contains("mariadb"))
                {
                    return SupportedDatabase.MariaDb;
                }

                if (lower.Contains("mysql"))
                {
                    return SupportedDatabase.MySql;
                }

                if (lower.Contains("cockroach"))
                {
                    return SupportedDatabase.CockroachDb;
                }

                if (lower.Contains("postgres"))
                {
                    return SupportedDatabase.PostgreSql;
                }

                if (lower.Contains("oracle"))
                {
                    return SupportedDatabase.Oracle;
                }

                if (lower.Contains("sqlite"))
                {
                    return SupportedDatabase.Sqlite;
                }

                if (lower.Contains("firebird"))
                {
                    return SupportedDatabase.Firebird;
                }

                if (lower.Contains("duckdb") || lower.Contains("duck db"))
                {
                    return SupportedDatabase.DuckDB;
                }
            }
        }
        catch { }
        try
        {
            var typeName = _factory.GetType().Name.ToLowerInvariant();
            if (typeName.Contains("sqlserver") || typeName.Contains("system.data.sqlclient"))
            {
                return SupportedDatabase.SqlServer;
            }

            if (typeName.Contains("npgsql"))
            {
                return SupportedDatabase.PostgreSql;
            }

            if (typeName.Contains("mysql"))
            {
                return SupportedDatabase.MySql;
            }

            if (typeName.Contains("sqlite"))
            {
                return SupportedDatabase.Sqlite;
            }

            if (typeName.Contains("oracle"))
            {
                return SupportedDatabase.Oracle;
            }

            if (typeName.Contains("firebird"))
            {
                return SupportedDatabase.Firebird;
            }

            if (typeName.Contains("duckdb"))
            {
                return SupportedDatabase.DuckDB;
            }
        }
        catch { }
        return SupportedDatabase.Unknown;
    }

    private static DbConnectionStringBuilder GetFactoryConnectionStringBuilderStatic(ITrackedConnection conn, string connectionString)
    {
        // Use a tolerant builder; if it fails, fall back to carrying raw string under Data Source
        var csb = new DbConnectionStringBuilder();
        try
        {
            csb.ConnectionString = connectionString;
        }
        catch
        {
            try { csb["Data Source"] = connectionString; } catch { /* ignore */ }
        }
        return csb;
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
        catch { }
        return connectionString;
    }

    private DbMode CoerceMode(DbMode requested, SupportedDatabase product, bool isLocalDb, bool isFirebirdEmbedded)
    {
        // Embedded engines: handle in-memory specially (isolated vs shared)
        if (product is SupportedDatabase.Sqlite or SupportedDatabase.DuckDB)
        {
            var kind = DetectInMemoryKind(product, _connectionString);
            if (kind == InMemoryKind.Isolated)
            {
                if (requested != DbMode.SingleConnection)
                {
                    _logger.LogWarning("DbMode override: requested {requested}, coerced to {resolved} — reason: {reason}", requested, DbMode.SingleConnection, "Isolated in-memory requires SingleConnection");
                }

                return DbMode.SingleConnection;
            }
            if (kind == InMemoryKind.Shared)
            {
                // Shared in-memory: upgrade Standard to SingleWriter, otherwise honor explicit choice
                if (requested == DbMode.Standard)
                {
                    _logger.LogWarning("DbMode override: requested {requested}, coerced to {resolved} — reason: {reason}", requested, DbMode.SingleWriter, "Shared in-memory prefers SingleWriter");
                    return DbMode.SingleWriter;
                }
                return requested;
            }

            // File-backed or none: previous behavior — prefer SingleWriter for Standard/KeepAlive;
            if (requested is DbMode.Standard or DbMode.KeepAlive)
            {
                _logger.LogWarning("DbMode override: requested {requested}, coerced to {resolved} — reason: {reason}", requested, DbMode.SingleWriter, "SQLite/DuckDB file-based prefer persistent coordination");
                return DbMode.SingleWriter;
            }
            if (requested is DbMode.Best)
            {
                return DbMode.SingleWriter;
            }
            return requested;
        }

        if (product == SupportedDatabase.Firebird && isFirebirdEmbedded)
        {
            if (requested != DbMode.SingleConnection)
            {
                _logger.LogWarning("DbMode override: requested {requested}, coerced to {resolved} — reason: {reason}", requested, DbMode.SingleConnection, "Firebird embedded requires SingleConnection");
            }

            return DbMode.SingleConnection;
        }

        // Full servers
        bool isFull =
            product is SupportedDatabase.PostgreSql
            or SupportedDatabase.CockroachDb
            or SupportedDatabase.MySql
            or SupportedDatabase.MariaDb
            or SupportedDatabase.Oracle
            or SupportedDatabase.SqlServer;

        if (product == SupportedDatabase.SqlServer && isLocalDb)
        {
            if (requested != DbMode.KeepAlive)
            {
                _logger.LogWarning("DbMode override: requested {requested}, coerced to {resolved} — reason: {reason}", requested, DbMode.KeepAlive, "LocalDb requires KeepAlive");
            }

            return DbMode.KeepAlive;
        }

        if (isFull && !(product == SupportedDatabase.SqlServer && isLocalDb))
        {
            // For full server engines:
            // - Best resolves to Standard (do not auto-select SingleWriter)
            // - Explicit SingleWriter is allowed
            // - Standard stays Standard
            // - Other modes are coerced to Standard
            if (requested == DbMode.Best)
            {
                return DbMode.Standard;
            }
            if (requested == DbMode.SingleWriter)
            {
                return DbMode.SingleWriter;
            }
            if (requested == DbMode.Standard)
            {
                return DbMode.Standard;
            }

            _logger.LogWarning("DbMode override: requested {requested}, coerced to {resolved} — reason: {reason}", requested, DbMode.Standard, "Full servers prefer Standard unless SingleWriter explicitly requested");
            return DbMode.Standard;
        }

        // Unknown databases: Best resolves to Standard for safety
        if (requested == DbMode.Best)
        {
            return DbMode.Standard;
        }

        return requested;
    }

    private enum InMemoryKind { None, Isolated, Shared }

    private static InMemoryKind DetectInMemoryKind(SupportedDatabase product, string? connectionString)
    {
        var cs = (connectionString ?? string.Empty).Trim();
        var s = cs.ToLowerInvariant();
        if (product == SupportedDatabase.Sqlite)
        {
            bool modeMem = s.Contains("mode=memory") || s.Contains("filename=:memory:") || s.Contains("data source=:memory:");
            if (!modeMem)
            {
                return InMemoryKind.None;
            }

            bool cacheShared = s.Contains("cache=shared");
            bool dsIsLiteralMem = s.Contains("data source=:memory:") || s.Contains("filename=:memory:");
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
        var csb = _factory.CreateConnectionStringBuilder() ?? new DbConnectionStringBuilder();
        try
        {
            csb.ConnectionString = input;
        }
        catch
        {
            // Fall back to a tolerant builder that carries the raw string in a common key
            var fallback = new DbConnectionStringBuilder();
            try
            {
                // Prefer a commonly recognized key when possible
                fallback["Data Source"] = input;
            }
            catch
            {
                // As a last resort, set ConnectionString on the generic builder which tolerates raw values
                try { fallback.ConnectionString = input; } catch { /* ignore */ }
            }

            csb = fallback;
        }

        return csb;
    }

    private void SetupConnectionSessionSettingsForProvider(ITrackedConnection conn)
    {
        switch (Product)
        {
            case SupportedDatabase.MySql:
            case SupportedDatabase.MariaDb:
                _connectionSessionSettings = ConnectionMode == DbMode.Standard
                    ? string.Empty
                    : "SET SESSION sql_mode = 'STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES';\n";
                break;

            case SupportedDatabase.PostgreSql:
            case SupportedDatabase.CockroachDb:
                _connectionSessionSettings = @"
                SET standard_conforming_strings = on;
                SET client_min_messages = warning;
";
                if (_setDefaultSearchPath)
                {
                    _connectionSessionSettings += @"
                SET search_path = public;
";
                }
                break;

            case SupportedDatabase.Oracle:
                _connectionSessionSettings = @"
                ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY-MM-DD';
";
//                ALTER SESSION SET CURRENT_SCHEMA = your_schema;
                break;

            case SupportedDatabase.Sqlite:
                _connectionSessionSettings = "PRAGMA foreign_keys = ON;";
                break;

            case SupportedDatabase.Firebird:
                // _connectionSessionSettings = "SET NAMES UTF8;";
                // has to be done in connection string, not session;
                break;

            //DB 2 can't be supported under modern .net
            //             case SupportedDatabase.Db2:
            //                 _connectionSessionSettings = @"
            //                  SET CURRENT DEGREE = 'ANY';
            // ";
            //                break;

            default:
                _connectionSessionSettings = string.Empty;
                break;
        }

        _applyConnectionSessionSettings = _connectionSessionSettings?.Length > 0;
    }


    internal void ApplyConnectionSessionSettings(IDbConnection connection)
    {
        if (ConnectionMode == DbMode.Standard)
        {
            return;
        }
        _logger.LogInformation("Applying connection session settings");
        if (_applyConnectionSessionSettings)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = _connectionSessionSettings;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error setting session settings:" + ex.Message);
                _applyConnectionSessionSettings = false;
            }
        }
    }

    public void ApplyPersistentConnectionSessionSettings(IDbConnection connection)
    {
        if (ConnectionMode == DbMode.Standard)
        {
            return;
        }

        // Skip if dialect hasn't been initialized yet (happens during constructor)
        if (_dialect == null)
        {
            return;
        }

        // If session settings were already applied on first open, avoid double application
        if (_sessionSettingsAppliedOnOpen)
        {
            return;
        }

        _logger.LogInformation("Applying persistent connection session settings");

        // For persistent connections in SingleConnection/SingleWriter mode,
        // use the dialect's session settings which include read-only settings when appropriate
        var sessionSettings = _dialect.GetConnectionSessionSettings(this, IsReadOnlyConnection);

        if (!string.IsNullOrEmpty(sessionSettings))
        {
            try
            {
                var parts = sessionSettings.Split(';');
                foreach (var part in parts)
                {
                    var stmt = part.Trim();
                    if (string.IsNullOrEmpty(stmt))
                    {
                        continue;
                    }

                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = stmt + ';';
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error setting session settings:" + ex.Message);
            }
        }
    }

    internal ITrackedConnection GetStandardConnection(bool isShared = false, bool readOnly = false)
    {
        var conn = FactoryCreateConnection(null, isShared, readOnly);
        return conn;
    }


    internal ITrackedConnection GetSingleConnection()
    {
        return _connection!;
    }

    internal ITrackedConnection GetSingleWriterConnection(ExecutionType type, bool isShared = false)
    {
        if (ExecutionType.Read == type)
        {
            // Embedded in-memory providers: distinguish isolated vs shared memory
            if (Product == SupportedDatabase.Sqlite || Product == SupportedDatabase.DuckDB)
            {
                var memKind = DetectInMemoryKind(Product, _connectionString);
                if (memKind == InMemoryKind.Isolated)
                {
                    // Isolated memory: reuse pinned connection for all reads
                    return GetSingleConnection();
                }
                // Shared memory: ephemeral read-only connections using the same CS
                return isShared ? GetSingleConnection() : GetStandardConnection(isShared, true);
            }

            // Non-embedded: ephemeral read connection (unless shared within a transaction)
            return isShared ? GetSingleConnection() : GetStandardConnection(isShared, true);
        }

        return GetSingleConnection();
    }

    internal ITrackedConnection? PersistentConnection => _connection;

    internal void SetPersistentConnection(ITrackedConnection? connection)
    {
        _connection = connection;
    }
    //
    // private int _disposed; // 0=false, 1=true
    //
    //
    // public void Dispose()
    // {
    //     Dispose(disposing: true);
    // }
    //
    // public async ValueTask DisposeAsync()
    // {
    //     await DisposeAsyncCore().ConfigureAwait(false);
    //     Dispose(disposing: false); // Finalizer path for unmanaged cleanup (if any)
    // }
    //
    // protected virtual async ValueTask DisposeAsyncCore()
    // {
    //     if (Interlocked.Exchange(ref _disposed, 1) != 0)
    //         return; // Already disposed
    //
    //     if (_connection is IAsyncDisposable asyncDisposable)
    //     {
    //         await asyncDisposable.DisposeAsync().ConfigureAwait(false);
    //     }
    //     else
    //     {
    //         _connection?.Dispose();
    //     }
    //
    //     _connection = null;
    // }
    //
    //
    // protected virtual void Dispose(bool disposing)
    // {
    //     if (Interlocked.Exchange(ref _disposed, 1) != 0)
    //         return; // Already disposed
    //
    //     if (disposing)
    //     {
    //         try
    //         {
    //             _connection?.Dispose();
    //         }
    //         catch
    //         {
    //             // Optional: log or suppress
    //         }
    //         finally
    //         {
    //             _connection = null;
    //             GC.SuppressFinalize(this); // Suppress only here
    //         }
    //     }
    //
    //     // unmanaged cleanup if needed (none currently)
    // }

    protected override void DisposeManaged()
    {
        try
        {
            _connection?.Dispose();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _connection = null;
        }
        base.DisposeManaged();
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        try
        {
            if (_connection is IAsyncDisposable ad)
            {
                await ad.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                _connection?.Dispose();
            }
        }
        finally
        {
            _connection = null;
        }
        await base.DisposeManagedAsync().ConfigureAwait(false);
    }

    public ISqlDialect Dialect => _dialect;
}
