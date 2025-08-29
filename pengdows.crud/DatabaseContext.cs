#region

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.connection;
using pengdows.crud.dialects;
using pengdows.crud.infrastructure;
using pengdows.crud.isolation;
using pengdows.crud.threading;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

public class DatabaseContext : SafeAsyncDisposableBase, IDatabaseContext, IContextIdentity, ISqlDialectProvider
{
    private readonly DbProviderFactory _factory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<IDatabaseContext> _logger;
    private readonly IConnectionStrategy _connectionStrategy;

    private long _connectionCount;
    private string _connectionString;
    private DataSourceInformation _dataSourceInfo;
    private readonly SqlDialect _dialect;
    private IIsolationResolver _isolationResolver;
    private bool _isReadConnection = true;
    private bool _isWriteConnection = true;
    private long _maxNumberOfOpenConnections;
    private readonly bool _setDefaultSearchPath;
    private string _connectionSessionSettings;
    private bool _applyConnectionSessionSettings;
    private readonly DbMode _originalUserMode;

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

            var initialConnection = InitializeInternals(configuration);
            _dialect = SqlDialectFactory.CreateDialect(initialConnection, _factory, loggerFactory);
            _dataSourceInfo = new DataSourceInformation(_dialect);
            Name = _dataSourceInfo.DatabaseProductName;

            RCSIEnabled = _dialect.IsReadCommittedSnapshotOn(initialConnection);
            if (_originalUserMode != DbMode.Standard && 
                !(_dataSourceInfo.Product == SupportedDatabase.SqlServer && _originalUserMode == DbMode.SingleConnection))
            {
                _dialect.ApplyConnectionSettings(initialConnection);
            }

            switch (_dataSourceInfo.Product)
            {
                case SupportedDatabase.Sqlite:
                case SupportedDatabase.DuckDB:
                {
                    var csb = GetFactoryConnectionStringBuilder(string.Empty);
                    var ds = csb["Data Source"] as string;
                    ConnectionMode = ":memory:" == ds
                        ? DbMode.SingleConnection
                        : DbMode.SingleWriter;
                    break;
                }
                default:
                {
                    break;
                }
            }

            _isolationResolver = new IsolationResolver(Product, RCSIEnabled);

            var connFactory = () => FactoryCreateConnection(null, false, c => _dialect.ApplyConnectionSettings(c));
            _connectionStrategy = ConnectionMode switch
            {
                DbMode.Standard => new StandardConnectionStrategy(connFactory),
                DbMode.SingleConnection => new SingleConnectionStrategy(initialConnection!),
                DbMode.SingleWriter => new SingleWriterConnectionStrategy(initialConnection!, connFactory),
                DbMode.KeepAlive => new KeepAliveConnectionStrategy(connFactory),
                _ => throw new InvalidOperationException("Invalid connection mode."),
            };

            if (ConnectionMode == DbMode.Standard)
            {
                initialConnection?.Dispose();
                initialConnection = null;
            }
        }
        catch (Exception e)
        {
            _logger?.LogError(e.Message);
            throw;
        }
    }
    

    public ReadWriteMode ReadWriteMode { get; set; }

    public string Name { get; set; }

    private string ConnectionString
    {
        get => _connectionString;
        set
        {
            //don't let it change
            if (!string.IsNullOrWhiteSpace(_connectionString) || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"Connection string reset attempted.");
            }

            _connectionString = value;
        }
    }


    public bool IsReadOnlyConnection => _isReadConnection && !_isWriteConnection;
    public bool RCSIEnabled { get; private set; }

    public ILockerAsync GetLock()
    {
        ThrowIfDisposed();
        return NoOpAsyncLocker.Instance;
    }


    public DbMode ConnectionMode { get; private set; }


    public ITypeMapRegistry TypeMapRegistry { get; }

    public IDataSourceInformation DataSourceInfo => _dataSourceInfo;
    public string SessionSettingsPreamble => _dialect.GetConnectionSessionSettings();

    public ITransactionContext BeginTransaction(
        IsolationLevel? isolationLevel = null,
        ExecutionType executionType = ExecutionType.Write)
    {
        if (executionType == ExecutionType.Read)
        {
            if (!_isReadConnection)
            {
                throw new InvalidOperationException("Context is not readable.");
            }

            if (isolationLevel is null)
            {
                isolationLevel = IsolationLevel.RepeatableRead;
            }

            if (isolationLevel != IsolationLevel.RepeatableRead)
            {
                throw new InvalidOperationException("Read-only transactions must use 'RepeatableRead'.");
            }
        }
        else
        {
            if (!_isWriteConnection)
            {
                throw new NotSupportedException("Context is read-only.");
            }

            isolationLevel ??= IsolationLevel.ReadCommitted;
        }

        return new TransactionContext(this, isolationLevel.Value, executionType);
    }

    public ITransactionContext BeginTransaction(
        IsolationProfile isolationProfile,
        ExecutionType executionType = ExecutionType.Write)
    {
        var level = _isolationResolver.Resolve(isolationProfile);
        return BeginTransaction(level, executionType);
    }

    public string CompositeIdentifierSeparator => _dataSourceInfo.CompositeIdentifierSeparator;
    public SupportedDatabase Product => _dataSourceInfo?.Product ?? SupportedDatabase.Unknown;
    public ProcWrappingStyle ProcWrappingStyle => _dataSourceInfo.ProcWrappingStyle;
    public int MaxParameterLimit => _dataSourceInfo.MaxParameterLimit;
    public int MaxOutputParameters => _dataSourceInfo.MaxOutputParameters;
    public long MaxNumberOfConnections => Interlocked.Read(ref _maxNumberOfOpenConnections);
    public long NumberOfOpenConnections => Interlocked.Read(ref _connectionCount);
    public string QuotePrefix => _dialect.QuotePrefix;
    public string QuoteSuffix => _dialect.QuoteSuffix;

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
        Action<DbConnection>? onFirstOpen = null)
    {
        SanitizeConnectionString(connectionString);

        var connection = _factory.CreateConnection();
        connection.ConnectionString = ConnectionString;

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
            onFirstOpen,
            onDispose: conn => { _logger.LogDebug("Connection disposed."); },
            null,
            isSharedConnection
        );
        return tracked;
    }

    private void SanitizeConnectionString(string? connectionString)
    {
        if (connectionString != null && string.IsNullOrWhiteSpace(ConnectionString))
        {
            //"Multiple Active Record Sets"
            var csb = GetFactoryConnectionStringBuilder(connectionString);
            var tmp = csb.ConnectionString;
            ConnectionString = tmp;
        }
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

    public ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? connection)
    {
        return _connectionStrategy.ReleaseConnectionAsync(connection);
    }

    private ITrackedConnection? InitializeInternals(IDatabaseContextConfiguration config)
    {
        var connectionString = config.ConnectionString;
        ReadWriteMode = config.ReadWriteMode;
        ITrackedConnection? conn = null;
        try
        {
            _isReadConnection = (ReadWriteMode & ReadWriteMode.ReadOnly) == ReadWriteMode.ReadOnly;
            _isWriteConnection = (ReadWriteMode & ReadWriteMode.WriteOnly) == ReadWriteMode.WriteOnly;
            // this connection will be set as our single connection for any DbMode != DbMode.Standard
            // so we set it to shared.
            conn = FactoryCreateConnection(connectionString, true, null);
            try
            {
                conn.Open();
            }
            catch (Exception ex)
            {
                throw new ConnectionFailedException(ex.Message);
            }

            _dataSourceInfo = DataSourceInformation.Create(conn, _factory, _loggerFactory);
            SetupConnectionSessionSettingsForProvider(conn);
            Name = _dataSourceInfo.DatabaseProductName;
            if (_dataSourceInfo.Product == SupportedDatabase.Sqlite)
            {
                // Determine correct mode based on connection string
                // ":memory:" needs a persistent connection to avoid data loss
                // file-based SQLite requires a single writer to avoid lock conflicts
                var csb = GetFactoryConnectionStringBuilder(String.Empty);
                var ds = csb["Data Source"] as string;
                ConnectionMode = ":memory:" == ds
                    ? DbMode.SingleConnection
                    : DbMode.SingleWriter;
            }

            if (config.DbMode != DbMode.Standard)
            {
                //
                //Interlocked.Increment(ref _connectionCount);
                // if the mode is anything but standard
                // we store it as our minimal connection
                // Session settings will be applied in the main constructor
                // Note: _connection field doesn't exist in current architecture
            }

            if (Product != SupportedDatabase.Unknown)
            {
                _isolationResolver ??= new IsolationResolver(Product, RCSIEnabled);
            }

            if (config.DbMode == DbMode.Standard)
            {
                //if it is standard mode, we can close it.
                conn?.Dispose();
            }
        }
        catch (Exception ex)
        {
            throw new ConnectionFailedException(ex.Message);
        }

        return conn;
    }

    private DbConnectionStringBuilder GetFactoryConnectionStringBuilder(string connectionString)
    {
        var csb = _factory.CreateConnectionStringBuilder() ?? new DbConnectionStringBuilder();
        csb.ConnectionString = string.IsNullOrEmpty(connectionString) ? _connectionString : connectionString;
        return csb;
    }

    private void SetupConnectionSessionSettingsForProvider(ITrackedConnection conn)
    {
        switch (Product)
        {
            case SupportedDatabase.MySql:
            case SupportedDatabase.MariaDb:
                _connectionSessionSettings =
                    "SET SESSION sql_mode = 'STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ZERO_DATE,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES';\n";
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


    private void ApplyConnectionSessionSettings(IDbConnection connection)
    {
        _logger.LogInformation("Applying connection session settings");
        if (_applyConnectionSessionSettings)
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

    private ITrackedConnection GetStandardConnection(bool isShared = false)
    {
        var conn = FactoryCreateConnection(null, isShared);
        return conn;
    }


    // Note: These methods were removed as they referenced fields that don't exist in the new architecture

    protected override void DisposeManaged()
    {
        _connectionStrategy.Dispose();
        base.DisposeManaged();
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        await _connectionStrategy.DisposeAsync().ConfigureAwait(false);
        await base.DisposeManagedAsync().ConfigureAwait(false);
    }

    public ISqlDialect Dialect => _dialect;
}
