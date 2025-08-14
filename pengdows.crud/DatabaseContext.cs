#region

using System;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.connection;
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
    private bool _applyConnectionSessionSettings;

    private long _connectionCount;
    private string _connectionSessionSettings;
    private string _connectionString;
    private DataSourceInformation _dataSourceInfo;
    private readonly SqlDialect _dialect;
    private IIsolationResolver _isolationResolver;
    private bool _isReadConnection = true;
    private bool _isSqlServer;
    private bool _isWriteConnection = true;
    private long _maxNumberOfOpenConnections;

    public Guid RootId { get; } = Guid.NewGuid();

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
            (loggerFactory ?? NullLoggerFactory.Instance))
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
            (loggerFactory ?? NullLoggerFactory.Instance))
    {
    }

    public DatabaseContext(
        IDatabaseContextConfiguration configuration,
        DbProviderFactory factory,
        ILoggerFactory? loggerFactory = null)
    {
        try
        {
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger<IDatabaseContext>();
            TypeCoercionHelper.Logger =
                _loggerFactory.CreateLogger(nameof(TypeCoercionHelper));
            ReadWriteMode = configuration.ReadWriteMode;
            TypeMapRegistry = new TypeMapRegistry();
            ConnectionMode = configuration.DbMode;
            _factory = factory ?? throw new NullReferenceException(nameof(factory));

            var initialConnection = InitializeInternals(configuration);
            _dialect = new SqlDialect(_dataSourceInfo, _factory, GenerateRandomName);
            var connFactory = () => FactoryCreateConnection(null, false);
            _connectionStrategy = ConnectionMode switch
            {
                DbMode.Standard => new StandardConnectionStrategy(connFactory),
                DbMode.SingleConnection => new SingleConnectionStrategy(initialConnection!),
                DbMode.SingleWriter => new SingleWriterConnectionStrategy(initialConnection!, connFactory),
                DbMode.KeepAlive => new KeepAliveConnectionStrategy(connFactory),
                _ => throw new InvalidOperationException("Invalid connection mode."),
            };
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
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
    public bool RCSIEnabled { get; }

    public ILockerAsync GetLock()
    {
        ThrowIfDisposed();
        return NoOpAsyncLocker.Instance;
    }


    public DbMode ConnectionMode { get; private set; }


    public ITypeMapRegistry TypeMapRegistry { get; }

    public IDataSourceInformation DataSourceInfo => _dataSourceInfo;


    public string SessionSettingsPreamble => _connectionSessionSettings ?? "";


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

        return new TransactionContext(this, _dialect, isolationLevel.Value, executionType);
    }

    public ITransactionContext BeginTransaction(
        IsolationProfile isolationProfile,
        ExecutionType executionType = ExecutionType.Write)
    {
        var level = _isolationResolver.Resolve(isolationProfile);
        return BeginTransaction(level, executionType);
    }


    public SupportedDatabase Product => _dataSourceInfo.Product;

    public ISqlContainer CreateSqlContainer(string? query = null)
    {
        return new SqlContainer(this, _dialect, query);
    }

    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        return _dialect.CreateDbParameter(name, type, value);
    }


    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false)
    {
        return _connectionStrategy.GetConnection(executionType, isShared);
    }

    public string GenerateRandomName(int length = 5, int parameterNameMaxLength = 30)
    {
        var validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();
        var len = Math.Min(Math.Max(length, 2), parameterNameMaxLength);

        Span<char> buffer = stackalloc char[len];
        const int firstCharMax = 52; // a-zA-Z
        var anyOtherMax = validChars.Length;

        buffer[0] = validChars[Random.Shared.Next(firstCharMax)];
        for (var i = 1; i < len; i++) buffer[i] = validChars[Random.Shared.Next(anyOtherMax)];

        return new string(buffer);
    }


    public DbParameter CreateDbParameter<T>(DbType type, T value)
    {
        return _dialect.CreateDbParameter(type, value);
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


    public ProcWrappingStyle ProcWrappingStyle
    {
        get => _dataSourceInfo.ProcWrappingStyle;
        set => _dataSourceInfo.ProcWrappingStyle = value;
    }

    public int MaxParameterLimit => _dataSourceInfo.MaxParameterLimit;

    public long MaxNumberOfConnections => Interlocked.Read(ref _maxNumberOfOpenConnections);

    public long NumberOfOpenConnections => Interlocked.Read(ref _connectionCount);

    public string QuotePrefix => _dialect.QuotePrefix;

    public string QuoteSuffix => _dialect.QuoteSuffix;

    public string CompositeIdentifierSeparator => _dialect.CompositeIdentifierSeparator;

    SqlDialect ISqlDialectProvider.Dialect => _dialect;

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

    private ITrackedConnection FactoryCreateConnection(string? connectionString = null, bool isSharedConnection = false)
    {
        SanitizeConnectionString(connectionString);

        var connection = _factory.CreateConnection();
        connection.ConnectionString = ConnectionString;

        var tracked = new TrackedConnection(
            connection,
            (sender, args) => //StateChangeHandler
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
                    case ConnectionState.Broken:
                        _logger.LogDebug("Closed or broken connection: " + Name);
                        Interlocked.Decrement(ref _connectionCount);
                        break;
                }
            },
            onFirstOpen: ApplyConnectionSessionSettings,
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
                return; // no update needed

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

    private void CheckForSqlServerSettings(ITrackedConnection conn)
    {
        _isSqlServer =
            _dataSourceInfo.DatabaseProductName.StartsWith("Microsoft SQL Server", StringComparison.OrdinalIgnoreCase)
            && !_dataSourceInfo.DatabaseProductName.Contains("Compact", StringComparison.OrdinalIgnoreCase);

        if (!_isSqlServer)
        {
            return;
        }

        var settings = new Dictionary<string, string>
        {
            { "ANSI_NULLS", "ON" },
            { "ANSI_PADDING", "ON" },
            { "ANSI_WARNINGS", "ON" },
            { "ARITHABORT", "ON" },
            { "CONCAT_NULL_YIELDS_NULL", "ON" },
            { "QUOTED_IDENTIFIER", "ON" },
            { "NUMERIC_ROUNDABORT", "OFF" }
        };

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DBCC USEROPTIONS;";

        using var reader = cmd.ExecuteReader();
        var currentSettings = settings.ToDictionary(kvp => kvp.Key, kvp => "OFF");

        while (reader.Read())
        {
            var key = reader.GetString(0).ToUpperInvariant();
            if (settings.ContainsKey(key)) currentSettings[key] = reader.GetString(1) == "SET" ? "ON" : "OFF";
        }

        var sb = CompareResults(settings, currentSettings);


        if (sb.Length > 0)
        {
            sb.Insert(0, "SET NOCOUNT ON;\n");
            sb.AppendLine(";\nSET NOCOUNT OFF;");
            _connectionSessionSettings = sb.ToString();
        }
    }

    private StringBuilder CompareResults(Dictionary<string, string> expected, Dictionary<string, string> recorded)
    {
        //used for checking which connection/session settings are on or off for mssql
        var sb = new StringBuilder();
        foreach (var expectedKvp in expected)
        {
            recorded.TryGetValue(expectedKvp.Key, out var result);
            if (result != expectedKvp.Value)
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }

                sb.Append($"SET {expectedKvp.Key} {expectedKvp.Value}");
            }
        }

        return sb;
    }

    private ITrackedConnection? InitializeInternals(IDatabaseContextConfiguration config)
    {
        var connectionString = config.ConnectionString;
        var mode = config.DbMode;
        ReadWriteMode = config.ReadWriteMode;
        ITrackedConnection? conn = null;
        try
        {
            _isReadConnection = (ReadWriteMode & ReadWriteMode.ReadOnly) == ReadWriteMode.ReadOnly;
            _isWriteConnection = (ReadWriteMode & ReadWriteMode.WriteOnly) == ReadWriteMode.WriteOnly;
            // this connection will be set as our single connection for any DbMode != DbMode.Standard
            // so we set it to shared.
            conn = FactoryCreateConnection(connectionString, true);
            try
            {
                conn.Open();
            }
            catch (Exception ex)
            {
                throw new ConnectionFailedException(ex.Message);
            }

            _dataSourceInfo = DataSourceInformation.Create(conn, _loggerFactory);
            SetupConnectionSessionSettingsForProvider(conn);
            if (mode != DbMode.Standard)
            {
                ApplyConnectionSessionSettings(conn);
            }
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
                mode = ConnectionMode;
            }

            _isolationResolver ??= new IsolationResolver(Product, RCSIEnabled);
        }
        catch(Exception ex){
            _logger.LogError(ex, ex.Message);
            throw;
        }
        finally
        {
            _isolationResolver ??= new IsolationResolver(Product, RCSIEnabled);
            if (mode == DbMode.Standard)
            {
                //if it is standard mode, we can close it.
                conn?.Dispose();
                conn = null;
            }
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
        switch (_dataSourceInfo.Product)
        {
            case SupportedDatabase.SqlServer:
                //sets up only what is necessary
                CheckForSqlServerSettings(conn);
                break;

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
}
