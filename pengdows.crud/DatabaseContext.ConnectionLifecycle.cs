// =============================================================================
// FILE: DatabaseContext.ConnectionLifecycle.cs
// PURPOSE: Connection acquisition, release, and lifecycle management.
//
// AI SUMMARY:
// - Manages the "open late, close early" connection philosophy.
// - Key methods:
//   * GetConnection(ExecutionType) - Acquires a connection (read or write)
//   * CloseAndDisposeConnection() - Returns connection to pool
//   * CloseAndDisposeConnectionAsync() - Async version
// - Delegates to IConnectionStrategy for mode-specific behavior:
//   * Standard - Creates ephemeral connections from pool
//   * PreventDatabaseUnload - Maintains passive sentinels + ephemeral work connections
//   * SingleWriter - Governor-serialized ephemeral writer + ephemeral readers
//   * SingleConnection - All operations on one connection
// - Pool governor integration for connection limiting/backpressure.
// - Session settings application (timeouts, read-only mode).
// - Internal helpers for strategy implementations:
//   * PersistentConnection - The pinned connection (if any)
//   * GetStandardConnection() - Creates new pooled connection
//   * AcquireSlot() - Gets pool slot with backpressure
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
using pengdows.crud.threading;
using pengdows.crud.wrappers;

namespace pengdows.crud;

/// <summary>
/// DatabaseContext partial class: Connection lifecycle management methods.
/// </summary>
/// <remarks>
/// This partial implements the connection acquisition and release patterns,
/// delegating to the configured <see cref="strategies.connection.IConnectionStrategy"/>
/// for mode-specific behavior.
/// </remarks>
public partial class DatabaseContext
{
    /// <inheritdoc/>
    internal ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false)
    {
        return _connectionStrategy.GetConnection(executionType, isShared);
    }

    ITrackedConnection IInternalConnectionProvider.GetConnection(ExecutionType executionType, bool isShared)
    {
        return GetConnection(executionType, isShared);
    }

    /// <summary>
    /// Async counterpart of <see cref="GetConnection"/>. Genuinely non-blocking under pool
    /// contention (see <see cref="GetStandardConnectionWithExecutionTypeAsync"/>/
    /// <see cref="AcquireSlotAsync"/>) — callers on an async execution path (command execution,
    /// async transaction creation) must use this instead of the sync <see cref="GetConnection"/>,
    /// which blocks the calling thread inside <c>PoolGovernor.Acquire()</c>'s <c>SemaphoreSlim.Wait</c>
    /// while waiting for a slot.
    /// </summary>
    internal ValueTask<ITrackedConnection> GetConnectionAsync(ExecutionType executionType, bool isShared = false,
        CancellationToken cancellationToken = default)
    {
        return _connectionStrategy.GetConnectionAsync(executionType, isShared, cancellationToken);
    }

    ValueTask<ITrackedConnection> IInternalConnectionProvider.GetConnectionAsync(ExecutionType executionType,
        bool isShared, CancellationToken cancellationToken)
    {
        return GetConnectionAsync(executionType, isShared, cancellationToken);
    }

    internal void CloseAndDisposeConnectionInternal(ITrackedConnection? connection)
    {
        _connectionStrategy.ReleaseConnection(connection);
    }

    internal async ValueTask CloseAndDisposeConnectionAsyncInternal(ITrackedConnection? connection)
    {
        await _connectionStrategy.ReleaseConnectionAsync(connection).ConfigureAwait(false);
    }

    void IInternalConnectionProvider.CloseAndDisposeConnection(ITrackedConnection? connection)
    {
        CloseAndDisposeConnectionInternal(connection);
    }

    ValueTask IInternalConnectionProvider.CloseAndDisposeConnectionAsync(ITrackedConnection? connection)
    {
        return CloseAndDisposeConnectionAsyncInternal(connection);
    }

    /// <summary>
    /// Internal property exposing the persistent connection for strategies.
    /// </summary>
    internal ITrackedConnection? PersistentConnection => _connection;

    /// <summary>
    /// Sets the persistent connection reference.
    /// </summary>
    internal void SetPersistentConnection(ITrackedConnection? connection)
    {
        _connection = connection;
    }

    internal IReadOnlyList<(ITrackedConnection Connection, ExecutionType ExecutionType)> GetSentinelSnapshot()
    {
        lock (_sentinelLock)
        {
            return _sentinels.ToArray();
        }
    }

    internal void RegisterSentinel(ITrackedConnection connection, ExecutionType executionType)
    {
        lock (_sentinelLock)
        {
            if (_sentinels.Any(s => ReferenceEquals(s.Connection, connection)))
            {
                return;
            }

            // Preserve an already-installed persistent connection when a strategy is
            // initialized directly (some internal callers do this outside the normal
            // constructor path). Normal PreventDatabaseUnload initialization starts
            // with no persistent connection, so this does not create an extra sentinel.
            if (_sentinels.Count == 0 && _connection != null &&
                !ReferenceEquals(_connection, connection))
            {
                _sentinels.Add((_connection, executionType));
            }

            _sentinels.Add((connection, executionType));
            _connection ??= connection;
        }
    }

    /// <summary>
    /// Best-effort cleanup for constructor failures, where the caller has no context
    /// instance available to dispose normally.
    /// </summary>
    internal void DisposePersistentConnectionsForInitializationFailure()
    {
        DisposePersistentConnections();
    }

    internal bool ReplaceSentinel(ITrackedConnection previous, ITrackedConnection replacement,
        ExecutionType executionType)
    {
        lock (_sentinelLock)
        {
            var index = _sentinels.FindIndex(s => ReferenceEquals(s.Connection, previous));
            if (index < 0 || IsDisposed)
            {
                return false;
            }

            _sentinels[index] = (replacement, executionType);
            if (ReferenceEquals(_connection, previous))
            {
                _connection = replacement;
            }

            return true;
        }
    }

    private void DisposePersistentConnections()
    {
        ITrackedConnection[] connections;
        lock (_sentinelLock)
        {
            connections = _sentinels.Select(s => s.Connection).ToArray();
            _sentinels.Clear();
            if (connections.Length > 0)
            {
                _connection = null;
            }
        }

        if (connections.Length == 0 && _connection != null)
        {
            connections = [_connection];
            _connection = null;
        }

        foreach (var connection in connections)
        {
            try
            {
                connection.Dispose();
            }
            catch
            {
                // best-effort cleanup during context disposal
            }
        }
    }

    private async ValueTask DisposePersistentConnectionsAsync()
    {
        ITrackedConnection[] connections;
        lock (_sentinelLock)
        {
            connections = _sentinels.Select(s => s.Connection).ToArray();
            _sentinels.Clear();
            if (connections.Length > 0)
            {
                _connection = null;
            }
        }

        if (connections.Length == 0 && _connection != null)
        {
            connections = [_connection];
            _connection = null;
        }

        foreach (var connection in connections)
        {
            try
            {
                if (connection is IAsyncDisposable asyncConnection)
                {
                    await asyncConnection.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    connection.Dispose();
                }
            }
            catch
            {
                // best-effort cleanup during context disposal
            }
        }
    }

    /// <summary>
    /// Creates a standard (ephemeral) connection from the factory or data source.
    /// </summary>
    internal ITrackedConnection GetStandardConnection(ExecutionType executionType, bool isShared = false)
    {
        return GetStandardConnectionWithExecutionType(executionType, isShared);
    }

    internal ILockerAsync GetConnectionOpenLock()
    {
        ThrowIfDisposed();
        if (!RequiresSerializedOpen || _connectionOpenGate == null)
        {
            return NoOpAsyncLocker.Instance;
        }

        return _connectionOpenLocker ?? (ILockerAsync)new RealAsyncLocker(_connectionOpenGate);
    }

    /// <summary>
    /// DbMode.SingleConnection shares one physical connection across the entire context. This gate
    /// serializes exclusive use of that connection for a transaction's entire span (Begin through
    /// Commit/Rollback/Dispose) against every other caller — another transaction attempt, or an
    /// ordinary non-transactional command. Bounded by <see cref="ModeLockTimeout"/> (the same
    /// timeout already used elsewhere for mode-related lock waits), so a caller blocks but does not
    /// wait forever; exceeding it throws <see cref="pengdows.crud.exceptions.ModeContentionException"/>.
    /// Separate from the connection's own per-command lock (<c>TrackedConnection.GetLock()</c>) so a
    /// transaction holding this gate never deadlocks against its own commands, which still acquire
    /// that other lock as normal.
    /// </summary>
    internal ILockerAsync GetSingleConnectionTransactionGate()
    {
        ThrowIfDisposed();
        if (_singleConnectionTransactionGate == null)
        {
            return NoOpAsyncLocker.Instance;
        }

        return new RealAsyncLocker(_singleConnectionTransactionGate, _modeContentionStats, ConnectionMode, _modeLockTimeout);
    }

    internal ITrackedConnection GetStandardConnectionWithExecutionType(ExecutionType executionType,
        bool isShared = false)
    {
        // CORE-025: this is the entry point every connection acquisition path (ordinary
        // commands, readers, sentinel creation) ultimately reaches. Without this check, a
        // disposed context's nulled-out governor fields made AcquireSlot silently return an
        // ungoverned default slot instead of failing — a container created after disposal could
        // open a fresh physical connection and execute completely outside admission control.
        ThrowIfDisposed();
        var slot = AcquireSlot(executionType);
        try
        {
            var roIntent = executionType == ExecutionType.Read;
            var useReader = roIntent && ShouldUseReadOnlyForReadIntent() && HasDedicatedReadConnectionString();
            var connectionString = useReader ? _readerConnectionString : _connectionString;
            var conn = FactoryCreateConnection(executionType, connectionString, isShared, slot);
            return conn;
        }
        catch
        {
            slot.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Async counterpart of <see cref="GetStandardConnectionWithExecutionType"/> — awaits the
    /// pool slot via <see cref="AcquireSlotAsync"/> (PoolGovernor.AcquireAsync/WaitAsync) instead
    /// of blocking the calling thread in the sync <c>PoolGovernor.Acquire</c>/SemaphoreSlim.Wait.
    /// <see cref="FactoryCreateConnection(ExecutionType,string?,bool,PoolSlot?)"/> itself performs
    /// no I/O (it only constructs the ADO.NET connection object; opening happens later, already
    /// asynchronously, via <c>OpenConnectionAsync</c>), so it is safe to call synchronously here
    /// once the slot has been acquired.
    /// </summary>
    internal async ValueTask<ITrackedConnection> GetStandardConnectionWithExecutionTypeAsync(
        ExecutionType executionType, bool isShared, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var slot = await AcquireSlotAsync(executionType, cancellationToken).ConfigureAwait(false);
        try
        {
            var roIntent = executionType == ExecutionType.Read;
            var useReader = roIntent && ShouldUseReadOnlyForReadIntent() && HasDedicatedReadConnectionString();
            var connectionString = useReader ? _readerConnectionString : _connectionString;
            var conn = FactoryCreateConnection(executionType, connectionString, isShared, slot);
            return conn;
        }
        catch
        {
            slot.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the persistent single connection (for SingleConnection mode).
    /// </summary>
    internal ITrackedConnection GetSingleConnection()
    {
        return _connection!;
    }

    /// <summary>
    /// Executes session settings on the given connection as a single command.
    /// Skips execution if detection has not completed or dialect is null.
    /// </summary>
    internal void ExecuteSessionSettings(IDbConnection connection, bool readOnly)
    {
        if (!_sessionSettingsDetectionCompleted)
        {
            return;
        }

        // If this DataSource has session settings baked into its PostgreSQL startup Options
        // parameter, the pool-return RESET ALL already restored the correct values — skip.
        if (_dataSource != null && (readOnly ? _roSettingsBakedIntoDataSource : _rwSettingsBakedIntoDataSource))
        {
            if (connection is ITrackedConnection bakedTc)
            {
                bakedTc.LocalState.MarkSessionSettingsApplied();
            }
            return;
        }

        var settingsToApply = readOnly
            ? _cachedReadOnlySessionSettings
            : _cachedReadWriteSessionSettings;

        if (string.IsNullOrWhiteSpace(settingsToApply))
        {
            if (readOnly)
            {
                // Some dialects (e.g. Oracle) have no session-level read-only SQL equivalent.
                // Oracle enforces read-only at the transaction level via SET TRANSACTION READ ONLY,
                // not at the connection level. A consumer who configures a read-only context for
                // Oracle will not get connection-level enforcement — the intent must be honoured
                // by always beginning transactions with readOnly: true.
                _logger.LogDebug(
                    "Dialect {Dialect} does not emit session-level read-only SQL; " +
                    "read-only intent must be enforced at the transaction level for {Name}.",
                    Dialect?.GetType().Name ?? "unknown", Name);
            }

            if (connection is ITrackedConnection t)
            {
                t.LocalState.MarkSessionSettingsApplied();
            }
            return;
        }

        _logger.LogDebug("Applying session settings for {Name} (ReadOnly: {ReadOnly})",
            Name, readOnly);

        var sessionInitStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = settingsToApply;
            cmd.ExecuteNonQuery();
            var sessionInitMs = MetricsCollector.ToMilliseconds(
                System.Diagnostics.Stopwatch.GetTimestamp() - sessionInitStart);
            _metricsCollector?.RecordSessionInitDuration(sessionInitMs);
        }
        catch (Exception ex)
        {
            // Best-effort: log the failure and return the connection without marking settings
            // applied. The connection proceeds in an unknown session state.
            //
            // Intentional trade-off: failing hard here would surface every transient SET
            // failure (e.g., a momentary DB hiccup) as a connection acquisition exception.
            // Instead, callers that require strict read-only enforcement should verify
            // the transaction isolation level and not rely solely on session settings.
            //
            // MarkSessionSettingsApplied() is NOT called, so a second checkout of this
            // logical connection will retry the SET on next first-open. For StandardMode
            // (ephemeral connections) each TrackedConnection is fresh anyway.
            _logger.LogError(ex, "Failed to apply session settings for {Name}", Name);
            if (_sessionInitializationFailureMode == SessionInitializationFailureMode.FailClosed)
            {
                throw new ConnectionException(
                    $"Failed to apply session settings for connection '{Name}' and SessionInitializationFailureMode.FailClosed is configured.",
                    Product, ex);
            }
            return;
        }

        if (connection is ITrackedConnection tc)
        {
            tc.LocalState.MarkSessionSettingsApplied();
        }
    }

    internal async ValueTask ExecuteSessionSettingsAsync(
        IDbConnection connection,
        bool readOnly,
        CancellationToken cancellationToken = default)
    {
        if (!_sessionSettingsDetectionCompleted)
        {
            return;
        }

        // If this DataSource has session settings baked into its PostgreSQL startup Options
        // parameter, the pool-return RESET ALL already restored the correct values — skip.
        if (_dataSource != null && (readOnly ? _roSettingsBakedIntoDataSource : _rwSettingsBakedIntoDataSource))
        {
            if (connection is ITrackedConnection bakedTc)
            {
                bakedTc.LocalState.MarkSessionSettingsApplied();
            }
            return;
        }

        var settingsToApply = readOnly
            ? _cachedReadOnlySessionSettings
            : _cachedReadWriteSessionSettings;

        if (string.IsNullOrWhiteSpace(settingsToApply))
        {
            if (readOnly)
            {
                _logger.LogDebug(
                    "Dialect {Dialect} does not emit session-level read-only SQL; " +
                    "read-only intent must be enforced at the transaction level for {Name}.",
                    Dialect?.GetType().Name ?? "unknown", Name);
            }

            if (connection is ITrackedConnection t)
            {
                t.LocalState.MarkSessionSettingsApplied();
            }
            return;
        }

        _logger.LogDebug("Applying session settings for {Name} (ReadOnly: {ReadOnly})",
            Name, readOnly);

        var sessionInitStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = settingsToApply;
            if (cmd is DbCommand dbCommand)
            {
                await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                cmd.ExecuteNonQuery();
            }

            var sessionInitMs = MetricsCollector.ToMilliseconds(
                System.Diagnostics.Stopwatch.GetTimestamp() - sessionInitStart);
            _metricsCollector?.RecordSessionInitDuration(sessionInitMs);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply session settings for {Name}", Name);
            if (_sessionInitializationFailureMode == SessionInitializationFailureMode.FailClosed)
            {
                throw new ConnectionException(
                    $"Failed to apply session settings for connection '{Name}' and SessionInitializationFailureMode.FailClosed is configured.",
                    Product, ex);
            }
            return;
        }

        if (connection is ITrackedConnection tc)
        {
            tc.LocalState.MarkSessionSettingsApplied();
        }
    }

    /// <summary>
    /// Factory method to create a new tracked connection with state change monitoring and session settings.
    /// </summary>
    [SuppressMessage("Security", "cs/clear-text-storage-of-sensitive-information",
        Justification = "Connection strings are redacted via RedactConnectionString() before logging. " +
                        "The raw connection string is only used for DbConnection.ConnectionString assignment.")]
    private ITrackedConnection FactoryCreateConnection(
        ExecutionType executionType,
        string? connectionString = null,
        bool isSharedConnection = false,
        PoolSlot? slot = null)
    {
        SanitizeConnectionString(connectionString);

        var roIntent = executionType == ExecutionType.Read;
        var useReader = roIntent && ShouldUseReadOnlyForReadIntent() && HasDedicatedReadConnectionString();

        var activeConnectionString = string.IsNullOrWhiteSpace(connectionString)
            ? (useReader ? _readerConnectionString : _connectionString)
            : connectionString;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Preparing connection for {ExecutionType}", executionType);
        }

        var dataSource = ResolveDataSource(useReader);

        // Prefer DataSource over Factory for better performance (shared prepared statement cache)
        DbConnection connection;
        if (dataSource != null)
        {
            connection = dataSource.CreateConnection();
            _dialect?.ConfigureProviderSpecificSettings(connection, this, roIntent);
        }
        else if (_factory != null)
        {
            connection = _factory.CreateConnection() ??
                         throw new InvalidOperationException("Factory returned null DbConnection.");
            if (_dialect != null)
            {
                _dialect.ApplyConnectionSettingsCore(connection, this, roIntent, activeConnectionString);
            }
            else
            {
                connection.ConnectionString = activeConnectionString;
            }
        }
        else
        {
            throw new InvalidOperationException("Neither DataSource nor Factory is available.");
        }

        // Increment total connections created counter when a new connection is actually created
        Interlocked.Increment(ref _totalConnectionsCreated);

        // Use pre-built per-context handlers — zero allocation per connection checkout
        var firstOpenHandler = roIntent ? _firstOpenHandlerRo : _firstOpenHandlerRw;
        var firstOpenHandlerAsync = roIntent ? _firstOpenHandlerAsyncRo : _firstOpenHandlerAsyncRw;

        var metricsCollector = executionType == ExecutionType.Read ? _readerMetricsCollector : _writerMetricsCollector;
        var namePrefix = useReader ? _connectionNamePrefixRead : _connectionNamePrefixWrite;
        return new TrackedConnection(
            connection,
            _stateChangeHandler,
            firstOpenHandler,
            _disposeHandler,
            null,
            isSharedConnection,
            metricsCollector,
            _modeContentionStats,
            ConnectionMode,
            _modeLockTimeout,
            slot,
            namePrefix,
            firstOpenHandlerAsync
        );
    }

    /// <summary>
    /// Overload of FactoryCreateConnection using read execution type. Every current caller of
    /// this overload (and its 3-arg sibling below) creates an infrastructure connection —
    /// dialect detection, TestConnect validation, or PreventDatabaseUnload sentinel
    /// creation/repair — never an application-issued read or write, so slot acquisition here
    /// must not be attributed to Metrics.ReadRequests/WriteRequests. The genuine
    /// application-facing path is GetStandardConnectionWithExecutionType, which calls AcquireSlot
    /// directly and passes its own pre-acquired slot into the 4-arg FactoryCreateConnection below.
    /// </summary>
    internal ITrackedConnection FactoryCreateConnection(string? connectionString = null,
        bool isSharedConnection = false)
    {
        return FactoryCreateConnection(ExecutionType.Read, connectionString, isSharedConnection);
    }

    internal ITrackedConnection FactoryCreateConnection(ExecutionType executionType,
        string? connectionString, bool isSharedConnection)
    {
        return FactoryCreateConnection(
            executionType,
            connectionString,
            isSharedConnection,
            AcquireInfrastructureSlot(executionType));
    }

    private DbDataSource? ResolveDataSource(bool readOnly)
    {
        if (_dataSource == null)
        {
            return null;
        }

        if (ShouldUseReaderConnectionString(readOnly) && _readerDataSource != null)
        {
            return _readerDataSource;
        }

        // If a DataSource was injected at construction (e.g., NpgsqlDataSource) but no
        // dedicated reader DataSource exists, fall back to the factory path so the reader
        // connection string is honoured. _dataSourceProvided is only true in DataSource-
        // injected construction; in the factory-only path this branch is never reached.
        if (ShouldUseReaderConnectionString(readOnly) && _dataSourceProvided && _readerDataSource == null)
        {
            return null;
        }

        return _dataSource;
    }

    private PoolSlot AcquireSlot(ExecutionType executionType)
    {
        if (!_effectivePoolGovernorEnabled)
        {
            return default;
        }

        if (executionType == ExecutionType.Read)
        {
            _attributionStats.RecordReadRequest();
        }
        else
        {
            _attributionStats.RecordWriteRequest();
        }

        var governor = executionType == ExecutionType.Read ? _readerGovernor : _writerGovernor;
        if (governor == null)
        {
            ThrowIfGovernorMissingAfterDisposal();
            // Not yet disposed: this is the narrow bootstrap window before
            // InitializePoolGovernors() has run (_effectivePoolGovernorEnabled defaults to
            // true so the very first, pre-governor connection during construction still
            // reaches here) — ungoverned by design, matching pre-existing behavior.
            return default;
        }

        return governor.Acquire();
    }

    /// <summary>
    /// Async counterpart of <see cref="AcquireSlot"/> — awaits <c>PoolGovernor.AcquireAsync</c>
    /// (WaitAsync) instead of blocking the calling thread in the sync <c>PoolGovernor.Acquire</c>
    /// (SemaphoreSlim.Wait). See <see cref="GetStandardConnectionWithExecutionTypeAsync"/> for why
    /// this matters: an async caller waiting for a saturated pool must not pin a real CLR
    /// ThreadPool worker thread for the duration of that wait.
    /// </summary>
    private async ValueTask<PoolSlot> AcquireSlotAsync(ExecutionType executionType, CancellationToken cancellationToken)
    {
        if (!_effectivePoolGovernorEnabled)
        {
            return default;
        }

        if (executionType == ExecutionType.Read)
        {
            _attributionStats.RecordReadRequest();
        }
        else
        {
            _attributionStats.RecordWriteRequest();
        }

        var governor = executionType == ExecutionType.Read ? _readerGovernor : _writerGovernor;
        if (governor == null)
        {
            ThrowIfGovernorMissingAfterDisposal();
            // Not yet disposed: this is the narrow bootstrap window before
            // InitializePoolGovernors() has run (_effectivePoolGovernorEnabled defaults to
            // true so the very first, pre-governor connection during construction still
            // reaches here) — ungoverned by design, matching pre-existing behavior.
            return default;
        }

        return await governor.AcquireAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Acquires a governor slot for infrastructure connections — dialect detection, TestConnect
    /// validation, and PreventDatabaseUnload sentinel creation/repair — that consume pool
    /// capacity but are never application-issued reads or writes. Identical to AcquireSlot except
    /// it does not record into _attributionStats, so Metrics.ReadRequests/WriteRequests reflect
    /// only requests the application actually made.
    /// </summary>
    private PoolSlot AcquireInfrastructureSlot(ExecutionType executionType)
    {
        if (!_effectivePoolGovernorEnabled)
        {
            return default;
        }

        var governor = executionType == ExecutionType.Read ? _readerGovernor : _writerGovernor;
        if (governor == null)
        {
            ThrowIfGovernorMissingAfterDisposal();
            // See AcquireSlot's identical branch above for why a null governor is otherwise
            // the legitimate pre-InitializePoolGovernors() bootstrap state, not an error.
            return default;
        }

        return governor.Acquire();
    }

    /// <summary>
    /// CORE-025: InitializePoolGovernors always assigns a real PoolGovernor (possibly
    /// disabled/forbidden internally, never a null reference) once it has run. A null governor
    /// observed while the context is already disposed means DisposePoolGovernors() nulled the
    /// field out from under an in-flight acquire — silently returning an ungoverned default slot
    /// would let that connection bypass admission control entirely, so fail loudly instead. A
    /// null governor observed while NOT disposed is the legitimate pre-initialization bootstrap
    /// window instead (see callers), so this only throws for the disposed case.
    /// </summary>
    private void ThrowIfGovernorMissingAfterDisposal()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(DatabaseContext));
        }
    }

    /// <summary>
    /// Sanitizes and normalizes the connection string if needed.
    /// </summary>
    private void SanitizeConnectionString(string? connectionString)
    {
        if (connectionString != null && string.IsNullOrWhiteSpace(_connectionString))
        {
            try
            {
                var csb = GetFactoryConnectionStringBuilder(connectionString);
                var normalized = RepresentsRawConnectionString(csb, connectionString)
                    ? connectionString
                    : csb.ConnectionString;
                SetConnectionString(normalized);
            }
            catch
            {
                SetConnectionString(connectionString);
            }
        }
    }

    /// <summary>
    /// Updates the max connection count using thread-safe compare-and-swap.
    /// </summary>
    private void UpdateMaxConnectionCount(long current)
    {
        long previous;
        do
        {
            previous = Interlocked.Read(ref _peakOpenConnections);
            if (current <= previous)
            {
                return; // no update needed
            }

            // try to update only if no one else has changed it
        } while (Interlocked.CompareExchange(
                     ref _peakOpenConnections,
                     current,
                     previous) != previous);
    }
}
