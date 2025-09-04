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
    private readonly bool _setDefaultSearchPath;
    private string _connectionSessionSettings = string.Empty;
    private readonly DbMode _originalUserMode;
    private readonly bool? _forceManualPrepare;
    private readonly bool? _disablePrepare;

    public Guid RootId { get; } = Guid.NewGuid();

    private static readonly char[] _parameterPrefixes = { '@', '?', ':' };

    [Obsolete("Use the constructor that takes DatabaseContextConfiguration instead.")]
    public DatabaseContext(
        string connectionString,
        string providerFactory,
        ITypeMapRegistry? typeMapRegistry = null,
        DbMode mode = DbMode.Standard,
        ReadWriteMode readWriteMode = ReadWriteMode.ReadWrite,
        ILoggerFactory? loggerFactory = null)
        : this(
            new DatabaseContextConfiguration
            {
                ProviderName = providerFactory,
                ConnectionString = connectionString,
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
        DbMode mode = DbMode.Standard,
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
                DbMode = DbMode.Standard,
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
                DbMode = DbMode.Standard,
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
        try
        {
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger<IDatabaseContext>();
            TypeCoercionHelper.Logger =
                _loggerFactory.CreateLogger(nameof(TypeCoercionHelper));
            ReadWriteMode = configuration.ReadWriteMode;
            TypeMapRegistry = typeMapRegistry ?? global::pengdows.crud.TypeMapRegistry.Instance;
            ConnectionMode = configuration.DbMode;
            _originalUserMode = configuration.DbMode;
            _factory = factory ?? throw new NullReferenceException(nameof(factory));
            _setDefaultSearchPath = configuration.SetDefaultSearchPath;
            _forceManualPrepare = configuration.ForceManualPrepare;
            _disablePrepare = configuration.DisablePrepare;

            // Pre-infer connection mode for in-memory providers only when the user did not
            // explicitly choose a non-standard mode. Respect explicit choices.
            try
            {
                if (configuration.DbMode == DbMode.Standard)
                {
                    var factoryName = _factory.GetType().Name.ToLowerInvariant();
                    var connStr = configuration.ConnectionString ?? string.Empty;
                    var connStrLower = connStr.ToLowerInvariant();

                    string? dsPre = null;
                    foreach (var key in new[] { "data source=", "datasource=" })
                    {
                        var idx = connStrLower.IndexOf(key, StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            var start = idx + key.Length;
                            var end = connStrLower.IndexOf(';', start);
                            dsPre = connStrLower.Substring(start, end >= 0 ? end - start : connStrLower.Length - start).Trim();
                            break;
                        }
                    }

                    var isDuck = factoryName.Contains("duckdb") || connStrLower.Contains("emulatedproduct=duckdb");
                    var isSqlite = factoryName.Contains("sqlite") || connStrLower.Contains("emulatedproduct=sqlite");

                    if (isDuck || isSqlite)
                    {
                        ConnectionMode = dsPre == ":memory:" ? DbMode.SingleConnection : DbMode.SingleWriter;
                    }
                }
            }
            catch { /* ignore pre-infer failures */ }

            var initialConnection = InitializeInternals(configuration);
            _dialect = SqlDialectFactory.CreateDialect(initialConnection!, _factory, _loggerFactory);
            _dataSourceInfo = new DataSourceInformation(_dialect);
            Name = _dataSourceInfo.DatabaseProductName;
            _procWrappingStyle = _dataSourceInfo.ProcWrappingStyle;

            RCSIEnabled = _dialect.IsReadCommittedSnapshotOn(initialConnection!);

            // Apply session settings for persistent connections now that dialect is initialized
            if (ConnectionMode != DbMode.Standard && initialConnection != null)
            {
                ApplyPersistentConnectionSessionSettings(initialConnection);
            }

            // For Standard mode, dispose the connection after dialect initialization is complete
            if (ConnectionMode == DbMode.Standard && initialConnection != null)
            {
                initialConnection.Dispose();
            }

            switch (_dataSourceInfo.Product)
            {
                // Ensure DuckDB defaults are applied even when provider factory is opaque,
                // but only if the user did not explicitly choose a non-standard mode.
                case SupportedDatabase.DuckDB:
                case SupportedDatabase.Sqlite:
                    try
                    {
                        var csb2 = GetFactoryConnectionStringBuilder(configuration.ConnectionString ?? string.Empty);
                        var ds2 = csb2["Data Source"] as string;
                        ConnectionMode = ":memory:" == ds2 ? DbMode.SingleConnection : DbMode.SingleWriter;
                    }
                    catch { /* ignore */ }

                    break;
            }

            _isolationResolver = new IsolationResolver(Product, RCSIEnabled);

            // Connection strategy is created in InitializeInternals(finally) via ConnectionStrategyFactory
        }
        catch (Exception e)
        {
            _logger?.LogError(e.Message);
            throw;
        }
    }


    private ReadWriteMode _readWriteMode = ReadWriteMode.ReadWrite;
    public ReadWriteMode ReadWriteMode
    {
        get => _readWriteMode;
        set
        {
            _readWriteMode = value == ReadWriteMode.WriteOnly ? ReadWriteMode.ReadWrite : value;
            _isReadConnection = (_readWriteMode & ReadWriteMode.ReadOnly) == ReadWriteMode.ReadOnly;
            _isWriteConnection = (_readWriteMode & ReadWriteMode.WriteOnly) == ReadWriteMode.WriteOnly;
        }
    }

    public string Name { get; set; }

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
    public string QuotePrefix => _dialect.QuotePrefix;
    public string QuoteSuffix => _dialect.QuoteSuffix;
    public bool? ForceManualPrepare => _forceManualPrepare;
    public bool? DisablePrepare => _disablePrepare;

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

        // Ensure session settings from the active dialect are applied on first open for all modes.
        Action<DbConnection>? firstOpenHandler = conn =>
        {
            try
            {
                // Apply session settings for all connection modes.
                // Prefer dialect-provided settings when available; fall back to precomputed string.
                var settings = (_dialect != null
                    ? _dialect.GetConnectionSessionSettings(this, readOnly)
                    : _connectionSessionSettings) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(settings))
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = settings;
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply session settings on first open for {Name}", Name);
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
                    case ConnectionState.Closed:
                        if (from == ConnectionState.Broken)
                        {
                            break;
                        }
                        _logger.LogDebug("Closed or broken connection: " + Name);
                        Interlocked.Decrement(ref _connectionCount);
                        break;
                    case ConnectionState.Broken:
                        _logger.LogDebug("Closed or broken connection: " + Name);
                        Interlocked.Decrement(ref _connectionCount);
                        break;
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
        var connectionString = config.ConnectionString;
        ReadWriteMode = config.ReadWriteMode;
        ITrackedConnection? conn = null;
        try
        {
            // this connection will be set as our single connection for any DbMode != DbMode.Standard
            // so we set it to shared.
            conn = FactoryCreateConnection(connectionString, true, IsReadOnlyConnection, null);
            try
            {
                conn.Open();
            }
            catch (Exception ex)
            {
                throw new ConnectionFailedException(ex.Message);
            }

            _dataSourceInfo = DataSourceInformation.Create(conn, _factory, _loggerFactory);
            _procWrappingStyle = _dataSourceInfo.ProcWrappingStyle;
            SetupConnectionSessionSettingsForProvider(conn);
            Name = _dataSourceInfo.DatabaseProductName;
            // No further provider-based overrides here

            // Enforce: full-blown servers must use Standard mode (no persistent single connection).
            // Applies to: PostgreSQL (and CockroachDB), MySQL (and MariaDB), Oracle, and SQL Server (except LocalDB/CE).
            // Firebird embedded is treated like a local engine and is exempt.
            if (ConnectionMode != DbMode.Standard)
            {
                var p = Product;
                var isSqlServer = p == SupportedDatabase.SqlServer;
                var connStrLower = (_connectionString ?? string.Empty).ToLowerInvariant();
                var isLocalDb = connStrLower.Contains("(localdb)") || connStrLower.Contains("localdb");
                var factoryNameLower = _factory.GetType().FullName?.ToLowerInvariant() ?? string.Empty;
                var isSqlCe = factoryNameLower.Contains("sqlserverce") || Name.ToLowerInvariant().Contains("compact");

                // If we're using the fakeDb/emulated provider (detected via connection string hint),
                // do NOT force Standard mode. Tests and dev scenarios rely on single-connection/single-writer semantics.
                var isEmulated = connStrLower.Contains("emulatedproduct=");

                // Firebird embedded: detect via connection string hints
                bool isFirebirdEmbedded = false;
                if (p == SupportedDatabase.Firebird)
                {
                    try
                    {
                        var csb = GetFactoryConnectionStringBuilder(_connectionString);
                        string GetVal(string key) => csb.ContainsKey(key) ? (csb[key]?.ToString() ?? string.Empty) : string.Empty;

                        var serverType = GetVal("ServerType").ToLowerInvariant();
                        var clientLib = GetVal("ClientLibrary").ToLowerInvariant();
                        var dataSource = GetVal("DataSource").ToLowerInvariant();
                        var database = GetVal("Database");

                        if (serverType.Contains("embedded") || clientLib.Contains("embed"))
                        {
                            isFirebirdEmbedded = true;
                        }
                        else if (string.IsNullOrWhiteSpace(dataSource))
                        {
                            var dbLower = database.ToLowerInvariant();
                            if (!string.IsNullOrWhiteSpace(dbLower) && (dbLower.Contains('/') || dbLower.Contains('\\') || dbLower.EndsWith(".fdb")))
                            {
                                isFirebirdEmbedded = true;
                            }
                        }
                    }
                    catch { /* heuristic only */ }
                }

                var isFullServer = p == SupportedDatabase.PostgreSql
                                   || p == SupportedDatabase.CockroachDb
                                   || p == SupportedDatabase.MySql
                                   || p == SupportedDatabase.MariaDb
                                   || p == SupportedDatabase.Oracle
                                   || (isSqlServer && !isLocalDb && !isSqlCe);

                if (!isEmulated && (isFullServer || (p == SupportedDatabase.Firebird && !isFirebirdEmbedded)))
                {
                    _logger.LogWarning(
                        "DbMode.{Mode} is not supported for {Product}; forcing Standard mode.",
                        ConnectionMode, p);
                    ConnectionMode = DbMode.Standard;
                }
            }

            _isolationResolver ??= new IsolationResolver(Product, RCSIEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize DatabaseContext: {Message}", ex.Message);
            throw;
        }
        finally
        {
            _isolationResolver ??= new IsolationResolver(Product, RCSIEnabled);
            _connectionStrategy = ConnectionStrategyFactory.Create(this, ConnectionMode);
            _procWrappingStrategy = ProcWrappingStrategyFactory.Create(ProcWrappingStyle);

            switch (ConnectionMode)
            {
                case DbMode.Standard:
                    // Don't dispose here - let the constructor handle disposal after dialect initialization
                    break;
                case DbMode.KeepAlive:
                case DbMode.SingleConnection:
                case DbMode.SingleWriter:
                    if (conn != null)
                    {
                        ApplyPersistentConnectionSessionSettings(conn);
                    }

                    SetPersistentConnection(conn);
                    break;
                default:
                    // Don't dispose here - let the constructor handle disposal after dialect initialization
                    break;
            }
        }

        return conn;
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
                SET search_path = public;
";
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

        _logger.LogInformation("Applying persistent connection session settings");

        // For persistent connections in SingleConnection/SingleWriter mode,
        // use the dialect's session settings which include read-only settings when appropriate
        var sessionSettings = _dialect.GetConnectionSessionSettings(this, IsReadOnlyConnection);

        if (!string.IsNullOrEmpty(sessionSettings))
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = sessionSettings;
                cmd.ExecuteNonQuery();
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
            // For transaction-scoped reads (shared=true), reuse the persistent connection
            // to avoid opening an extra ephemeral connection under SingleWriter mode.
            // For ad-hoc reads (shared=false), use a standard ephemeral connection configured as read-only.
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
