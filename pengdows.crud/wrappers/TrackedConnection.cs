// =============================================================================
// FILE: TrackedConnection.cs
// PURPOSE: Wraps DbConnection with lifecycle tracking, locking, and metrics.
//
// AI SUMMARY:
// - Implements ITrackedConnection wrapping underlying DbConnection.
// - Lifecycle tracking:
//   * WasOpened: Tracks if connection was ever opened
//   * onFirstOpen callback: For session settings application
//   * onStateChange callback: For metrics state tracking
//   * onDispose callback: For connection counting
// - Two-level locking strategy:
//   * Shared connections (persistent): RealAsyncLocker with SemaphoreSlim
//   * Ephemeral connections (per-op): NoOpAsyncLocker, zero overhead
// - GetLock(): Returns appropriate locker for connection type.
// - LocalState: Per-connection state for prepare behavior tracking.
// - Pool slot integration:
//   * AttachSlot(): Associates governor slot with connection
//   * ReleaseSlot(): Returns slot on dispose (once only)
// - Metrics collection:
//   * Open/close duration timing
//   * Connection hold duration
//   * State change tracking
// - Extends SafeAsyncDisposableBase for proper cleanup.
// - DisposeManaged/Async: Closes connection, invokes callbacks, releases permit.
// =============================================================================

using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.connection;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
using pengdows.crud.metrics;
using pengdows.crud.threading;

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
///     at a time can use it. Used for SingleConnection's persistent connection and the
///     PreventDatabaseUnload sentinel.
///   </description></item>
///   <item><description>
///     <b>Ephemeral connections (isSharedConnection=false):</b> Returns <see cref="NoOpAsyncLocker"/>.
///     No locking overhead since each operation gets its own connection. Used for per-operation connections
///     in Standard, PreventDatabaseUnload, and SingleWriter modes.
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
internal class TrackedConnection : SafeAsyncDisposableBase, ITrackedConnection, IInternalConnectionWrapper,
    IConnectionLocalState
{
    private readonly DbConnection _connection;

    /// <inheritdoc />
    DbConnection IInternalConnectionWrapper.UnderlyingConnection => _connection;

    private readonly bool _isSharedConnection;
    private readonly ILogger<TrackedConnection> _logger;
    internal static Action? OpenTimingHook;
    private static long _nameCounter;
    private string? _name;
    private readonly string? _namePrefix;
    private readonly Action<DbConnection>? _onDispose;
    private readonly Action<ITrackedConnection>? _onFirstOpen;
    private readonly Func<ITrackedConnection, CancellationToken, Task>? _onFirstOpenAsync;
    private readonly StateChangeEventHandler? _onStateChange;
    private readonly SemaphoreSlim? _semaphoreSlim;
    private static readonly TimeSpan SharedDisposeTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _sharedDisposeTimeout;

    private int _wasOpened;
    private readonly MetricsCollector? _metricsCollector;
    // The one StateChange handler this wrapper subscribes (see OnConnectionStateChange).
    private readonly StateChangeEventHandler _stateHandler;
    private long _openTimestamp;
    // TEST-014: a connection that breaks (Open -> Broken) already has its close counted by the
    // Broken case below. DisposeConnectionSync/DisposeConnectionAsyncCore's own "not already
    // Closed" guard treats Broken as still needing a close, so disposal calls the underlying
    // connection's Close() again — a real ADO.NET state transition (Broken -> Closed), not a
    // no-op, so it fires a second StateChange and would double-count the close without this
    // flag. Reset on Open, consumed by the first Closed-or-Broken transition per open cycle.
    private int _closeAlreadyCountedForThisOpen;
    private readonly ModeContentionStats? _modeContentionStats;
    private readonly DbMode _mode;
    private readonly TimeSpan? _modeLockTimeout;
    private PoolSlot _slot;
    private int _slotAttached;
    private int _slotReleased;

    // IConnectionLocalState — inlined to eliminate one heap allocation per connection checkout

    /// <inheritdoc/>
    public bool PrepareDisabled { get; set; }

    /// <inheritdoc/>
    public bool SessionSettingsApplied { get; set; }

    // A checkout usually prepares one statement, so the first shape lives in a field and the
    // collections are built only for a second (REL-005: they cost about 1.5 KB per checkout).
    // Once built they hold every shape, the first included, oldest first.
    private string? _firstPrepared;
    private ConcurrentDictionary<string, byte>? _prepared;
    private ConcurrentQueue<string>? _order;
    private readonly object _preparedGate = new();
    private const int _maxPrepared = 32;

    /// <inheritdoc/>
    public void DisablePrepare() => PrepareDisabled = true;

    /// <inheritdoc/>
    public void MarkSessionSettingsApplied() => SessionSettingsApplied = true;

    /// <inheritdoc/>
    public bool IsAlreadyPreparedForShape(string shapeHash)
    {
        var prepared = Volatile.Read(ref _prepared);
        return prepared != null
            ? prepared.ContainsKey(shapeHash)
            : string.Equals(Volatile.Read(ref _firstPrepared), shapeHash, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public (bool Added, int Evicted) MarkShapePrepared(string shapeHash)
    {
        if (Volatile.Read(ref _prepared) == null)
        {
            var first = Interlocked.CompareExchange(ref _firstPrepared, shapeHash, null);
            if (first == null)
            {
                return (true, 0);
            }

            if (string.Equals(first, shapeHash, StringComparison.Ordinal))
            {
                return (false, 0);
            }

            lock (_preparedGate)
            {
                if (_prepared == null)
                {
                    var order = new ConcurrentQueue<string>();
                    var dictionary = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
                    dictionary.TryAdd(first, 0);
                    order.Enqueue(first);
                    _order = order;
                    Volatile.Write(ref _prepared, dictionary);
                }
            }
        }

        var prepared = _prepared!;
        if (prepared.TryAdd(shapeHash, 0))
        {
            _order!.Enqueue(shapeHash);
            var evicted = 0;
            while (prepared.Count > _maxPrepared && _order.TryDequeue(out var old))
            {
                if (prepared.TryRemove(old, out _))
                {
                    evicted++;
                }
            }

            return (true, evicted);
        }

        return (false, 0);
    }

    /// <inheritdoc/>
    public void Reset()
    {
        lock (_preparedGate)
        {
            Volatile.Write(ref _prepared, null);
            _order = null;
            Volatile.Write(ref _firstPrepared, null);
        }

        // Don't reset PrepareDisabled - that should persist for the physical connection
        SessionSettingsApplied = false;
    }

    /// <summary>
    /// Per-connection state (this instance implements IConnectionLocalState directly).
    /// </summary>
    public IConnectionLocalState LocalState => this;


    internal TrackedConnection(
        DbConnection conn,
        StateChangeEventHandler? onStateChange = null,
        Action<ITrackedConnection>? onFirstOpen = null,
        Action<DbConnection>? onDispose = null,
        ILogger<TrackedConnection>? logger = null,
        bool isSharedConnection = false,
        MetricsCollector? metricsCollector = null,
        ModeContentionStats? modeContentionStats = null,
        DbMode mode = DbMode.Standard,
        TimeSpan? modeLockTimeout = null,
        PoolSlot? slot = null,
        string? namePrefix = null,
        Func<ITrackedConnection, CancellationToken, Task>? onFirstOpenAsync = null,
        TimeSpan? sharedDisposeTimeout = null
    )
    {
        _connection = conn ?? throw new ArgumentNullException(nameof(conn));
        _onStateChange = onStateChange;
        _onFirstOpen = onFirstOpen;
        _onFirstOpenAsync = onFirstOpenAsync;
        _onDispose = onDispose;
        _logger = logger ?? NullLogger<TrackedConnection>.Instance;
        _metricsCollector = metricsCollector;
        _namePrefix = string.IsNullOrWhiteSpace(namePrefix) ? null : namePrefix;
        _modeContentionStats = modeContentionStats;
        _mode = mode;
        _modeLockTimeout = modeLockTimeout;
        _sharedDisposeTimeout = sharedDisposeTimeout ?? SharedDisposeTimeout;
        if (isSharedConnection)
        {
            _isSharedConnection = true;
            _semaphoreSlim = new SemaphoreSlim(1, 1);
        }

        // One subscription instead of three (REL-005): each += / -= of a separate handler built a
        // new delegate or multicast chain on every checkout.
        _stateHandler = OnConnectionStateChange;
        _connection.StateChange += _stateHandler;

        if (slot.HasValue)
        {
            AttachSlot(slot.Value);
        }
    }

    // Some drivers never raise StateChange (CONFIRMED: Snowflake.Data 4.8.0's SnowflakeDbConnection has
    // no OnStateChange call), and pengdows counts open connections and connection metrics from that
    // event. So Open/Close note whether the driver reported the transition, and report it to pengdows'
    // own handlers themselves when it didn't; drivers that do report it are not counted twice.
    private int _driverReportedStateChange;

    // Runs the context's handler, then metrics, then notes that the driver reported the transition:
    // the order the three separate subscriptions ran in.
    private void OnConnectionStateChange(object? sender, StateChangeEventArgs args)
    {
        _onStateChange?.Invoke(sender!, args);
        if (_metricsCollector != null)
        {
            HandleMetricsStateChange(sender, args);
        }

        Interlocked.Exchange(ref _driverReportedStateChange, 1);
    }

    private void ResetDriverReportedStateChange()
    {
        Interlocked.Exchange(ref _driverReportedStateChange, 0);
    }

    private void ReportTransitionIfDriverDidNot(ConnectionState from, ConnectionState to)
    {
        if (Interlocked.Exchange(ref _driverReportedStateChange, 0) != 0 || _connection.State != to)
        {
            return;
        }

        var args = new StateChangeEventArgs(from, to);
        _onStateChange?.Invoke(_connection, args);
        if (_metricsCollector != null)
        {
            HandleMetricsStateChange(_connection, args);
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
                    Interlocked.Exchange(ref _closeAlreadyCountedForThisOpen, 0);
                    break;
                }
            case ConnectionState.Closed:
            case ConnectionState.Broken:
                {
                    if (Interlocked.Exchange(ref _closeAlreadyCountedForThisOpen, 1) != 0)
                    {
                        // Already counted this open cycle's close — e.g. the connection broke
                        // (counted then) and is now separately transitioning Broken -> Closed
                        // during disposal. One physical open/close cycle must count as one.
                        break;
                    }

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
        if (!string.IsNullOrWhiteSpace(name))
        {
            _name = name;
        }
    }

    public bool WasOpened => Interlocked.CompareExchange(ref _wasOpened, 0, 0) == 1;

    private string GetName()
    {
        var existing = Volatile.Read(ref _name);
        if (existing != null)
        {
            return existing;
        }

        var counter = Interlocked.Increment(ref _nameCounter);
        var generated = _namePrefix == null
            ? string.Concat("c", counter.ToString(CultureInfo.InvariantCulture))
            : string.Concat(_namePrefix, ":", counter.ToString(CultureInfo.InvariantCulture));
        var set = Interlocked.CompareExchange(ref _name, generated, null);
        return set ?? generated;
    }

    public void Open()
    {
        // Idempotent by design: a caller that already holds an open TrackedConnection (e.g. one
        // handed out pre-opened by a connection strategy) may legitimately call Open() again
        // without checking state first. Real ADO.NET providers throw InvalidOperationException on
        // a genuine double-open, so this guard — not provider leniency — is what makes that safe.
        if (_connection.State == ConnectionState.Open)
        {
            return;
        }

        var debugEnabled = _logger.IsEnabled(LogLevel.Debug);
        var shouldTime = debugEnabled || _metricsCollector != null;
        // A timestamp, not a Stopwatch per open, and fractional milliseconds: a pooled open takes
        // microseconds, which ElapsedMilliseconds recorded as 0 and the metric dropped (COR-010).
        var startedAt = 0L;
        if (shouldTime)
        {
            OpenTimingHook?.Invoke();
            startedAt = Stopwatch.GetTimestamp();
        }

        ResetDriverReportedStateChange();
        try
        {
            _connection.Open();
        }
        finally
        {
            if (shouldTime)
            {
                var elapsedMs = MetricsCollector.ToMilliseconds(Stopwatch.GetTimestamp() - startedAt);
                if (debugEnabled)
                {
                    _logger.LogDebug("Connection opened in {ElapsedMilliseconds} ms", elapsedMs);
                }

                _metricsCollector?.RecordConnectionOpenDuration(elapsedMs);
            }
        }

        ReportTransitionIfDriverDidNot(ConnectionState.Closed, ConnectionState.Open);
        TriggerFirstOpen();
    }

    protected override void DisposeManaged()
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Disposing connection {Name}", GetName());
        }

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
    public ILockerAsync GetLock() => _isSharedConnection
        ? new RealAsyncLocker(_semaphoreSlim!, _modeContentionStats, _mode, _modeLockTimeout)
        : NoOpAsyncLocker.Instance;

    public DataTable GetSchema()
    {
        return _connection.GetSchema();
    }

    private void TriggerFirstOpen()
    {
        if (Interlocked.Exchange(ref _wasOpened, 1) == 0)
        {
            if (_onFirstOpen != null)
                _onFirstOpen.Invoke(this);
            else if (_onFirstOpenAsync != null)
                // Invariant: callers that register an async-only handler must never call
                // the synchronous Open() path (e.g. must use OpenAsync). Violating this
                // would block a thread-pool thread via GetAwaiter().GetResult() and risk
                // deadlock on contexts with a synchronization context.
                throw new InvalidOperationException(
                    "A synchronous Open() was called on a TrackedConnection that has only an " +
                    "async first-open handler registered. Use OpenAsync() instead.");
        }
    }

    private async ValueTask TriggerFirstOpenAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _wasOpened, 1) != 0)
        {
            return;
        }

        if (_onFirstOpenAsync != null)
            await _onFirstOpenAsync(this, cancellationToken).ConfigureAwait(false);
        else
            _onFirstOpen?.Invoke(this);
    }

    public async ValueTask OpenAsync(CancellationToken cancellationToken = default)
    {
        // See the sync Open() guard above for the full rationale — same idempotency contract.
        if (_connection.State == ConnectionState.Open)
        {
            return;
        }

        var debugEnabled = _logger.IsEnabled(LogLevel.Debug);
        var shouldTime = debugEnabled || _metricsCollector != null;
        // A timestamp, not a Stopwatch per open, and fractional milliseconds: a pooled open takes
        // microseconds, which ElapsedMilliseconds recorded as 0 and the metric dropped (COR-010).
        var startedAt = 0L;
        if (shouldTime)
        {
            OpenTimingHook?.Invoke();
            startedAt = Stopwatch.GetTimestamp();
        }

        ResetDriverReportedStateChange();
        try
        {
            await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (shouldTime)
            {
                var elapsedMs = MetricsCollector.ToMilliseconds(Stopwatch.GetTimestamp() - startedAt);
                if (debugEnabled)
                {
                    _logger.LogDebug("Connection opened in {ElapsedMilliseconds} ms", elapsedMs);
                }

                _metricsCollector?.RecordConnectionOpenDuration(elapsedMs);
            }
        }

        ReportTransitionIfDriverDidNot(ConnectionState.Closed, ConnectionState.Open);
        await TriggerFirstOpenAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Async disposing connection {Name}", GetName());
        }

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

    // Called only from the synchronous DisposeManaged() path — never from DisposeAsync().
    // Blocking via Task.Run here is intentional: the synchronous Dispose() must complete
    // the async cleanup on the thread-pool rather than leaving a dangling async operation.
    private void DisposeSharedConnectionSynchronously()
    {
        Task.Run(async () => { await DisposeSharedConnectionAsync().ConfigureAwait(false); }).GetAwaiter().GetResult();
    }

    private async Task DisposeSharedConnectionAsync()
    {
        if (_semaphoreSlim == null)
        {
            await DisposeConnectionAsyncCore().ConfigureAwait(false);
            return;
        }

        if (await _semaphoreSlim.WaitAsync(_sharedDisposeTimeout).ConfigureAwait(false))
        {
            try
            {
                await DisposeConnectionAsyncCore().ConfigureAwait(false);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

            return;
        }

        if (_logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(
                "Timed out waiting to dispose shared connection {Name}; retrying once more before giving up.",
                GetName());
        }

        // Second attempt is ALSO bounded — never wait unboundedly here. Every other acquirer of
        // this same semaphore (RealAsyncLocker, used for ordinary command execution) is bounded
        // and fails loudly; disposal must not be the one exception that can hang a caller's
        // Dispose()/DisposeAsync() forever if whatever is holding the lock never releases it
        // (e.g. a hung command with no CommandTimeout).
        if (await _semaphoreSlim.WaitAsync(_sharedDisposeTimeout).ConfigureAwait(false))
        {
            try
            {
                await DisposeConnectionAsyncCore().ConfigureAwait(false);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

            return;
        }

        // Give up. Leak the connection rather than dispose/release a lock this call was never
        // actually granted: whatever still holds the lock keeps a working connection instead of
        // crashing with an ObjectDisposedException, and this method returns instead of hanging
        // its caller forever.
        if (_logger.IsEnabled(LogLevel.Error))
        {
            _logger.LogError(
                "Giving up waiting to dispose shared connection {Name} after {TotalSeconds}s total; leaking the connection instead of disposing it while its lock is still held by another operation.",
                GetName(), _sharedDisposeTimeout.TotalSeconds * 2);
        }
    }

    private void DisposeConnectionSync()
    {
        try
        {
            if (_connection.State != ConnectionState.Closed)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("Connection {Name} was still open during Dispose. Closing.", GetName());
                }

                CloseWithMetrics();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while closing connection during Dispose.");
        }

        LocalState.Reset();
        _onDispose?.Invoke(_connection);
        try
        {
            _connection.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while disposing connection.");
        }
        finally
        {
            DetachMetricsHandler();
            ReleaseSlot();
        }
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
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("Connection {Name} was still open during DisposeAsync. Closing.", GetName());
                }

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
            _logger.LogError(ex,
                "Error while disposing connection asynchronously. Falling back to synchronous dispose.");
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
        ReleaseSlot();
    }

    private void DetachMetricsHandler()
    {
        DetachStateChangeHandler(_stateHandler);
    }

    // Runs after the connection is disposed, so a close raised during Dispose is still counted. Some
    // drivers refuse StateChange access once disposed (CONFIRMED: AdoNetCore.AseClient 0.19.2's
    // AseConnection throws ObjectDisposedException); the disposed connection raises no more events, so
    // there is nothing left to detach from.
    private void DetachStateChangeHandler(StateChangeEventHandler handler)
    {
        try
        {
            _connection.StateChange -= handler;
        }
        catch (ObjectDisposedException)
        {
        }
    }

    internal void AttachSlot(PoolSlot slot)
    {
        _slot = slot;
        Interlocked.Exchange(ref _slotAttached, 1);
    }

    private void ReleaseSlot()
    {
        if (Interlocked.Exchange(ref _slotReleased, 1) == 0 &&
            Interlocked.CompareExchange(ref _slotAttached, 0, 0) == 1)
        {
            _slot.Dispose();
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

    public ValueTask<IDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return BeginTransactionAsync(IsolationLevel.Unspecified, cancellationToken);
    }

    public async ValueTask<IDbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default)
    {
        return await _connection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
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

        var startedAt = Stopwatch.GetTimestamp();
        ResetDriverReportedStateChange();
        try
        {
            _connection.Close();
        }
        finally
        {
            _metricsCollector?.RecordConnectionCloseDuration(
                MetricsCollector.ToMilliseconds(Stopwatch.GetTimestamp() - startedAt));
        }

        ReportTransitionIfDriverDidNot(ConnectionState.Open, ConnectionState.Closed);
    }
}
