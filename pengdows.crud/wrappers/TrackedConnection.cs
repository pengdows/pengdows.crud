#region

using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.connection;
using pengdows.crud.infrastructure;
using pengdows.crud.threading;

#endregion

namespace pengdows.crud.wrappers;

public class TrackedConnection : SafeAsyncDisposableBase, ITrackedConnection
{
    private readonly DbConnection _connection;
    private readonly bool _isSharedConnection;
    private readonly Func<ILockerAsync> _lockFactory;
    private readonly ILogger<TrackedConnection> _logger;
    private string _name;
    private readonly Action<DbConnection>? _onDispose;
    private readonly Action<DbConnection>? _onFirstOpen;
    private readonly StateChangeEventHandler? _onStateChange;
    private readonly SemaphoreSlim? _semaphoreSlim;

    private int _wasOpened;
    
    /// <summary>
    /// Per-connection state for prepare behavior tracking
    /// </summary>
    public ConnectionLocalState LocalState { get; } = new();


    protected internal TrackedConnection(
        DbConnection conn,
        StateChangeEventHandler? onStateChange = null,
        Action<DbConnection>? onFirstOpen = null,
        Action<DbConnection>? onDispose = null,
        ILogger<TrackedConnection>? logger = null,
        bool isSharedConnection = false
    )
    {
        _connection = conn ?? throw new ArgumentNullException(nameof(conn));
        _onStateChange = onStateChange;
        _onFirstOpen = onFirstOpen;
        _onDispose = onDispose;
        _logger = logger ?? NullLogger<TrackedConnection>.Instance;
        _name = Guid.NewGuid().ToString();
        if (isSharedConnection)
        {
            _isSharedConnection = true;
            _semaphoreSlim = new SemaphoreSlim(1, 1);
            _lockFactory = () => new RealAsyncLocker(_semaphoreSlim);
        }
        else
        {
            _lockFactory = () => NoOpAsyncLocker.Instance;
        }

        if (_onStateChange != null)
        {
            _connection.StateChange += _onStateChange;
        }
    }

    // Test convenience constructor: allow specifying a name and logger directly
    public TrackedConnection(DbConnection conn, string name, ILogger logger)
        : this(conn, null, null, null, logger as ILogger<TrackedConnection> ?? NullLogger<TrackedConnection>.Instance)
    {
        _name = name ?? _name;
    }

    public bool WasOpened => Interlocked.CompareExchange(ref _wasOpened, 0, 0) == 1;

    public void Open()
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _connection.Open();
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation("Connection opened in {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);
        }

        TriggerFirstOpen();
    }

    protected override void DisposeManaged()
    {
        _logger.LogDebug("Disposing connection {Name}", _name);

        if (_connection == null)
        {
            return;
        }

        try
        {
            if (_connection.State != ConnectionState.Closed)
            {
                _logger.LogWarning("Connection {Name} was still open during Dispose. Closing.", _name);
                _connection.Close();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while closing connection during Dispose.");
        }

        _onDispose?.Invoke(_connection);
        _connection.Dispose();
    }


    public ILockerAsync GetLock()
    {
        return _lockFactory();
    }

    public DataTable GetSchema()
    {
        return _connection.GetSchema();
    }

    private void TriggerFirstOpen()
    {
        if (Interlocked.Exchange(ref _wasOpened, 1) == 0)
        {
            _onFirstOpen?.Invoke(_connection);
        }
    }

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation("Connection opened in {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);
        }

        TriggerFirstOpen();
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        _logger.LogDebug("Async disposing connection {Name}", _name);

        if (_connection == null)
        {
            return;
        }

        if (_isSharedConnection && _semaphoreSlim != null)
        {
            // Avoid long waits during disposal to prevent hangs in tests/teardown
            var acquired = _semaphoreSlim.Wait(0);
            if (acquired)
            {
                try
                {
                    if (_connection.State != ConnectionState.Closed)
                    {
                        _logger.LogWarning("Connection {Name} was still open during DisposeAsync. Closing.", _name);
                        _connection.Close(); // Safe sync close
                    }

                    _onDispose?.Invoke(_connection);
                    await _connection.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    _semaphoreSlim.Release();
                }
            }
            else
            {
                _logger.LogError("TrackedConnection.DisposeAsync could not acquire lock; disposing without semaphore.");
                try
                {
                    if (_connection.State != ConnectionState.Closed)
                    {
                        _connection.Close();
                    }
                    _onDispose?.Invoke(_connection);
                    await _connection.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing connection without lock.");
                }
            }
        }
        else
        {
            if (_connection.State != ConnectionState.Closed)
            {
                _logger.LogWarning("Connection {Name} was still open during DisposeAsync. Closing.", _name);
                _connection.Close(); // Safe sync close
            }

            _onDispose?.Invoke(_connection);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    #region IDbConnection passthroughs

    public IDbTransaction BeginTransaction()
    {
        return _connection.BeginTransaction();
    }

    public IDbTransaction BeginTransaction(IsolationLevel isolationLevel)
    {
        return _connection.BeginTransaction(isolationLevel);
    }

    public void ChangeDatabase(string databaseName)
    {
        throw new NotImplementedException("ChangeDatabase is not supported.");
    }

    public void Close()
    {
        _connection.Close();
    }

    public IDbCommand CreateCommand()
    {
        return _connection.CreateCommand();
    }

    public string Database => _connection.Database;
    public ConnectionState State => _connection.State;
    public string DataSource => _connection.DataSource;
    public string ServerVersion => _connection.ServerVersion;
    public int ConnectionTimeout => _connection.ConnectionTimeout;

    public DataTable GetSchema(string dataSourceInformation)
    {
        return _connection.GetSchema(dataSourceInformation);
    }

    [AllowNull]
    public string ConnectionString
    {
        get => _connection.ConnectionString;
        set => _connection.ConnectionString = value;
    }

    #endregion
}
