#region

using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.isolation;
using pengdows.crud.threading;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

public class DatabaseContext : SafeAsyncDisposableBase, IDatabaseContext
{
    private readonly DbProviderFactory _factory;
    private readonly ILogger<IDatabaseContext> _logger;
    private bool _applyConnectionSessionSettings;
    private ITrackedConnection? _connection = null;

    private long _connectionCount;
    private string _connectionSessionSettings;
    private string _connectionString;
    private DataSourceInformation _dataSourceInfo;
    private IIsolationResolver _isolationResolver;
    private bool _isReadConnection = true;
    private bool _isSqlServer;
    private bool _isWriteConnection = true;
    private long _maxNumberOfOpenConnections;

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
            loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = loggerFactory?.CreateLogger<IDatabaseContext>() ?? NullLogger<IDatabaseContext>.Instance;
            ReadWriteMode = configuration.ReadWriteMode;
            TypeMapRegistry = new TypeMapRegistry();
            ConnectionMode = configuration.DbMode;
            _factory = factory ?? throw new NullReferenceException(nameof(factory));

            InitializeInternals(configuration);
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            throw;
        }
    }

    public ReadWriteMode ReadWriteMode { get; }

    public string Name { get; set; }

    private string ConnectionString
    {
        get => _connectionString;
        set
        {
            //don't let it change
            if (!string.IsNullOrWhiteSpace(_connectionString) || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Connection string reset attempted.");

            _connectionString = value;
        }
    }


    private ITrackedConnection Connection => _connection ??
                                             throw new ObjectDisposedException(
                                                 "attempt to use single connection from the wrong mode.");


    public bool IsReadOnlyConnection => _isReadConnection && !_isWriteConnection;
    public bool RCSIEnabled { get; }

    public ILockerAsync GetLock()
    {
        return NoOpAsyncLocker.Instance;
    }


    public DbMode ConnectionMode { get; private set; }

    public ITypeMapRegistry TypeMapRegistry { get; }

    public IDataSourceInformation DataSourceInfo => _dataSourceInfo;


    public string SessionSettingsPreamble => _connectionSessionSettings ?? "";

    public string WrapObjectName(string name)
    {
        var qp = QuotePrefix;
        var qs = QuoteSuffix;
        var tmp = name?.Replace(qp, string.Empty)?.Replace(qs, string.Empty);
        if (string.IsNullOrEmpty(tmp)) return string.Empty;

        var ss = tmp.Split(CompositeIdentifierSeparator);

        var sb = new StringBuilder();
        foreach (var s in ss)
        {
            if (sb.Length > 0) sb.Append(CompositeIdentifierSeparator);

            sb.Append(qp);
            sb.Append(s);
            sb.Append(qs);
        }

        return sb.ToString();
    }


    public ITransactionContext BeginTransaction(IsolationLevel? isolationLevel = null)
    {
        if (!_isWriteConnection && isolationLevel is null) isolationLevel = IsolationLevel.RepeatableRead;

        isolationLevel ??= IsolationLevel.ReadCommitted;

        if (!_isWriteConnection && isolationLevel != IsolationLevel.RepeatableRead)
            throw new InvalidOperationException("Read-only transactions must use 'RepeatableRead'.");

        return new TransactionContext(this, isolationLevel.Value);
    }

    public ITransactionContext BeginTransaction(IsolationProfile isolationProfile)
    {
        return new TransactionContext(this, _isolationResolver.Resolve(isolationProfile));
    }


    public string CompositeIdentifierSeparator => _dataSourceInfo.CompositeIdentifierSeparator;
    public SupportedDatabase Product => _dataSourceInfo.Product;

    public ISqlContainer CreateSqlContainer(string? query = null)
    {
        return new SqlContainer(this, query);
    }

    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        var p = _factory.CreateParameter() ?? throw new InvalidOperationException("Failed to create parameter.");

        if (string.IsNullOrWhiteSpace(name)) name = GenerateRandomName();

        var valueIsNull = Utils.IsNullOrDbNull(value);
        p.ParameterName = name;
        p.DbType = type;
        p.Value = valueIsNull ? DBNull.Value : value;
        if (!valueIsNull && p.DbType == DbType.String && value is string s) p.Size = Math.Max(s.Length, 1);

        return p;
    }


    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false)
    {
        switch (ConnectionMode)
        {
            case DbMode.Standard:
            case DbMode.KeepAlive:
                return GetStandardConnection(isShared);
            case DbMode.SingleWriter:
                return GetSingleWriterConnection(executionType);
            case DbMode.SingleConnection:
                return GetSingleConnection();
            default:
                throw new InvalidOperationException("Invalid connection mode.");
        }
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
        return CreateDbParameter(null, type, value);
    }

    public void AssertIsReadConnection()
    {
        if (!_isReadConnection) throw new InvalidOperationException("The connection is not read connection.");
    }

    public void AssertIsWriteConnection()
    {
        if (!_isWriteConnection) throw new InvalidOperationException("The connection is not write connection.");
    }


    public void CloseAndDisposeConnection(ITrackedConnection? connection)
    {
        if (connection == null) return;

        _logger.LogInformation($"Connection mode is: {ConnectionMode}");
        switch (ConnectionMode)
        {
            case DbMode.SingleConnection:
            case DbMode.SingleWriter:
            case DbMode.KeepAlive:
                if (_connection != connection)
                {
                    //never close our single write connection
                    _logger.LogInformation("Not our single connection, closing");
                    connection.Dispose();
                }

                break;
            case DbMode.Standard:
                _logger.LogInformation("Closing a standard connection");
                try
                {
                    connection.Dispose();
                }
                catch
                {
                    _logger.LogInformation("Connection closed");
                }

                break;
            default:
                throw new NotSupportedException("Unsupported connection mode.");
        }
    }

    public string MakeParameterName(DbParameter dbParameter)
    {
        return MakeParameterName(dbParameter.ParameterName);
    }

    public string MakeParameterName(string parameterName)
    {
        return !_dataSourceInfo.SupportsNamedParameters
            ? "?"
            : $"{_dataSourceInfo.ParameterMarker}{parameterName}";
    }


    public ProcWrappingStyle ProcWrappingStyle
    {
        get => _dataSourceInfo.ProcWrappingStyle;
        set => _dataSourceInfo.ProcWrappingStyle = value;
    }

    public int MaxParameterLimit => _dataSourceInfo.MaxParameterLimit;

    public long MaxNumberOfConnections => Interlocked.Read(ref _maxNumberOfOpenConnections);

    public long NumberOfOpenConnections => Interlocked.Read(ref _connectionCount);

    public string QuotePrefix => DataSourceInfo.QuotePrefix;

    public string QuoteSuffix => DataSourceInfo.QuoteSuffix;

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

    public async ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? connection)
    {
        if (connection == null)
            return;

        _logger.LogInformation($"Async Closing Connection in mode: {ConnectionMode}");

        if (connection is IAsyncDisposable asyncConnection)
            await asyncConnection.DisposeAsync().ConfigureAwait(false);
        else
            connection.Dispose();
    }

    private void CheckForSqlServerSettings(ITrackedConnection conn)
    {
        _isSqlServer =
            _dataSourceInfo.DatabaseProductName.StartsWith("Microsoft SQL Server", StringComparison.OrdinalIgnoreCase)
            && !_dataSourceInfo.DatabaseProductName.Contains("Compact", StringComparison.OrdinalIgnoreCase);

        if (!_isSqlServer) return;

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
                if (sb.Length > 0) sb.AppendLine();

                sb.Append($"SET {expectedKvp.Key} {expectedKvp.Value}");
            }
        }

        return sb;
    }

    private void InitializeInternals(IDatabaseContextConfiguration config)
    {
        var connectionString = config.ConnectionString;
        var mode = config.DbMode;
        var readWriteMode = config.ReadWriteMode;
        ITrackedConnection conn = null;
        try
        {
            _isReadConnection = (readWriteMode & ReadWriteMode.ReadOnly) == ReadWriteMode.ReadOnly;
            _isWriteConnection = (readWriteMode & ReadWriteMode.WriteOnly) == ReadWriteMode.WriteOnly;
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

            _dataSourceInfo = DataSourceInformation.Create(conn);
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
                mode = ConnectionMode;
            }

            if (mode != DbMode.Standard)
            {
                //
                //Interlocked.Increment(ref _connectionCount);
                // if the mode is anything but standard
                // we store it as our minimal connection
                ApplyConnectionSessionSettings(conn);
                _connection = conn;
            }
            _isolationResolver ??= new IsolationResolver(Product, RCSIEnabled);
        }
        catch(Exception ex){
            Console.WriteLine(ex.Message);
            throw;
        }
        finally
        {
            _isolationResolver ??= new IsolationResolver(Product, RCSIEnabled);
            if (mode == DbMode.Standard)
                //if it is standard mode, we can close it.
                conn?.Dispose();
        }
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

    private ITrackedConnection GetStandardConnection(bool isShared = false)
    {
        var conn = FactoryCreateConnection(null, isShared);
        return conn;
    }


    private ITrackedConnection GetSingleConnection()
    {
        return Connection;
    }

    private ITrackedConnection GetSingleWriterConnection(ExecutionType type, bool isShared = false)
    {
        if (ExecutionType.Read == type) return GetStandardConnection(isShared);

        return GetSingleConnection();
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
        _connection?.Dispose();
        _connection = null;
        base.DisposeManaged();
    }
}