#region

using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.connection;
using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;
using pengdows.crud.threading;

#endregion

namespace pengdows.crud.wrappers;

/// <summary>
/// Wraps a DbConnection with lifecycle tracking, async support, and connection-level locking.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Lock Semantics:</strong>
/// </para>
/// <para>
/// TrackedConnection provides connection-level locking through <see cref="GetLock()"/>. The lock type depends
/// on whether the connection is shared (persistent) or ephemeral:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>Shared connections (isSharedConnection=true):</b> Returns <see cref="RealAsyncLocker"/> backed by
///     <see cref="SemaphoreSlim"/>. This serializes access to the connection, ensuring only one operation
///     at a time can use it. Used in SingleWriter and SingleConnection modes.
///   </description></item>
///   <item><description>
///     <b>Ephemeral connections (isSharedConnection=false):</b> Returns <see cref="NoOpAsyncLocker"/>.
///     No locking overhead since each operation gets its own connection. Used in Standard and KeepAlive modes
///     (and for read connections in SingleWriter mode).
///   </description></item>
/// </list>
/// <para>
/// <strong>Lifecycle Management:</strong>
/// </para>
/// <para>
/// TrackedConnection manages connection state and fires callbacks at key lifecycle points:
/// </para>
/// <list type="bullet">
///   <item><description><b>First open:</b> onFirstOpen callback (used for session settings)</description></item>
///   <item><description><b>State changes:</b> onStateChange callback (used for metrics)</description></item>
///   <item><description><b>Disposal:</b> onDispose callback (used for connection counting)</description></item>
/// </list>
/// <para>
/// <strong>Shared vs Ephemeral:</strong>
/// </para>
/// <para>
/// The isSharedConnection parameter determines ownership and locking behavior:
/// </para>
/// <list type="bullet">
///   <item><description><b>Shared (true):</b> Connection is owned by DatabaseContext, stays open across operations,
///   requires synchronization (RealAsyncLocker), never returned to provider pool.</description></item>
///   <item><description><b>Ephemeral (false):</b> Connection is owned by the operation, closed after use,
///   no synchronization needed (NoOpAsyncLocker), returned to provider pool on disposal.</description></item>
/// </list>
/// </remarks>
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
    private static readonly TimeSpan SharedDisposeTimeout = TimeSpan.FromSeconds(5);

    private int _wasOpened;
    private readonly MetricsCollector? _metricsCollector;
    private readonly StateChangeEventHandler? _metricsHandler;
    private long _openTimestamp;
    private readonly ModeContentionStats? _modeContentionStats;
    private readonly DbMode _mode;
    private readonly TimeSpan? _modeLockTimeout;
    private PoolPermit _permit;
    private int _permitAttached;
    private int _permitReleased;
    
    /// <summary>
    /// Per-connection state for prepare behavior tracking
    /// </summary>
    public ConnectionLocalState LocalState { get; } = new();


    internal TrackedConnection(
        DbConnection conn,
        StateChangeEventHandler? onStateChange = null,
        Action<DbConnection>? onFirstOpen = null,
        Action<DbConnection>? onDispose = null,
        ILogger<TrackedConnection>? logger = null,
        bool isSharedConnection = false,
        MetricsCollector? metricsCollector = null,
        ModeContentionStats? modeContentionStats = null,
        DbMode mode = DbMode.Standard,
        TimeSpan? modeLockTimeout = null,
        PoolPermit? permit = null
    )
    {
        _connection = conn ?? throw new ArgumentNullException(nameof(conn));
        _onStateChange = onStateChange;
        _onFirstOpen = onFirstOpen;
        _onDispose = onDispose;
        _logger = logger ?? NullLogger<TrackedConnection>.Instance;
        _name = Guid.NewGuid().ToString();
        _metricsCollector = metricsCollector;
        _modeContentionStats = modeContentionStats;
        _mode = mode;
        _modeLockTimeout = modeLockTimeout;
        if (isSharedConnection)
        {
            _isSharedConnection = true;
            _semaphoreSlim = new SemaphoreSlim(1, 1);
            _lockFactory = () => new RealAsyncLocker(_semaphoreSlim, _modeContentionStats, _mode, _modeLockTimeout);
        }
        else
        {
            _lockFactory = () => NoOpAsyncLocker.Instance;
        }

        if (_onStateChange != null)
        {
            _connection.StateChange += _onStateChange;
        }

        if (_metricsCollector != null)
        {
            _metricsHandler = HandleMetricsStateChange;
            _connection.StateChange += _metricsHandler;
        }

        if (permit.HasValue)
        {
            AttachPermit(permit.Value);
        }
    }

    private void HandleMetricsStateChange(object? sender, StateChangeEventArgs args)
    {
        if (_metricsCollector == null)
        {
            return;
        }

        switch (args.CurrentState)
        {
            case ConnectionState.Open:
            {
                _metricsCollector.ConnectionOpened();
                Interlocked.Exchange(ref _openTimestamp, Stopwatch.GetTimestamp());
                break;
            }
            case ConnectionState.Closed:
            case ConnectionState.Broken:
            {
                var openedAt = Interlocked.Exchange(ref _openTimestamp, 0);
                var duration = openedAt == 0
                    ? 0d
                    : MetricsCollector.ToMilliseconds(Stopwatch.GetTimestamp() - openedAt);
                _metricsCollector.ConnectionClosed(duration);
                break;
            }
        }
    }

    // Test convenience constructor: allow specifying a name and logger directly
    internal TrackedConnection(DbConnection conn, string name, ILogger logger)
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
            _logger.LogDebug("Connection opened in {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);
            _metricsCollector?.RecordConnectionOpenDuration(stopwatch.ElapsedMilliseconds);
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

        if (_isSharedConnection && _semaphoreSlim != null)
        {
            DisposeSharedConnectionSynchronously();
            return;
        }

        DisposeConnectionSync();
    }


    /// <summary>
    /// Gets the connection-level lock for synchronization.
    /// </summary>
    /// <returns>
    /// <see cref="RealAsyncLocker"/> for shared connections (serializes access),
    /// <see cref="NoOpAsyncLocker"/> for ephemeral connections (no-op, zero overhead).
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is part of the two-level locking strategy:
    /// </para>
    /// <list type="number">
    ///   <item><description>Context lock (DatabaseContext.GetLock): Always NoOp</description></item>
    ///   <item><description>Connection lock (this method): Real or NoOp depending on shared vs ephemeral</description></item>
    /// </list>
    /// <para>
    /// <strong>Shared connections:</strong> Lock prevents concurrent use of the same physical connection.
    /// </para>
    /// <para>
    /// <strong>Ephemeral connections:</strong> Each operation has its own connection, no contention possible,
    /// lock is no-op for performance.
    /// </para>
    /// </remarks>
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
            _logger.LogDebug("Connection opened in {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);
            _metricsCollector?.RecordConnectionOpenDuration(stopwatch.ElapsedMilliseconds);
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
            await DisposeSharedConnectionAsync().ConfigureAwait(false);
            return;
        }

        await DisposeConnectionAsyncCore().ConfigureAwait(false);
    }

    private void DisposeSharedConnectionSynchronously()
    {
        Task.Run(async () =>
        {
            await DisposeSharedConnectionAsync().ConfigureAwait(false);
        }).GetAwaiter().GetResult();
    }

    private async Task DisposeSharedConnectionAsync()
    {
        if (_semaphoreSlim == null)
        {
            await DisposeConnectionAsyncCore().ConfigureAwait(false);
            return;
        }

        if (await _semaphoreSlim.WaitAsync(SharedDisposeTimeout).ConfigureAwait(false))
        {
            try
            {
                await DisposeConnectionAsyncCore().ConfigureAwait(false);
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }
        else
        {
            _logger.LogWarning("Timed out waiting to dispose shared connection {Name}; retrying once lock is released.", _name);
            await Task.Run(async () =>
            {
                await _semaphoreSlim.WaitAsync().ConfigureAwait(false);
                try
                {
                    await DisposeConnectionAsyncCore().ConfigureAwait(false);
                }
                finally
                {
                    _semaphoreSlim.Release();
                }
            }).ConfigureAwait(false);
        }
    }

    private void DisposeConnectionSync()
    {
        try
        {
            if (_connection.State != ConnectionState.Closed)
            {
                _logger.LogWarning("Connection {Name} was still open during Dispose. Closing.", _name);
                CloseWithMetrics();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while closing connection during Dispose.");
        }

        LocalState.Reset();
        _onDispose?.Invoke(_connection);
        _connection.Dispose();
        DetachMetricsHandler();
        ReleasePermit();
    }

    private async ValueTask DisposeConnectionAsyncCore()
    {
        if (_connection == null)
        {
            return;
        }

        try
        {
            if (_connection.State != ConnectionState.Closed)
            {
                _logger.LogWarning("Connection {Name} was still open during DisposeAsync. Closing.", _name);
                CloseWithMetrics();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while closing connection during DisposeAsync.");
        }

        LocalState.Reset();
        _onDispose?.Invoke(_connection);
        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while disposing connection asynchronously. Falling back to synchronous dispose.");
            try
            {
                _connection?.Dispose();
            }
            catch
            {
                // Connection is already disposed or in invalid state, ignore
            }
        }
        DetachMetricsHandler();
        ReleasePermit();
    }

    private void DetachMetricsHandler()
    {
        if (_metricsHandler != null)
        {
            _connection.StateChange -= _metricsHandler;
        }
    }

    internal void AttachPermit(PoolPermit permit)
    {
        _permit = permit;
        Interlocked.Exchange(ref _permitAttached, 1);
    }

    private void ReleasePermit()
    {
        if (Interlocked.Exchange(ref _permitReleased, 1) == 0 &&
            Interlocked.CompareExchange(ref _permitAttached, 0, 0) == 1)
        {
            _permit.Dispose();
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
        CloseWithMetrics();
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

    private void CloseWithMetrics()
    {
        if (_connection.State == ConnectionState.Closed)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            _connection.Close();
        }
        finally
        {
            stopwatch.Stop();
            _metricsCollector?.RecordConnectionCloseDuration(stopwatch.ElapsedMilliseconds);
        }
    }
}
