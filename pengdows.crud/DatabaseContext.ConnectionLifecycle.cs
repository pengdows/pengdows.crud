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
//   * KeepAlive - Maintains sentinel + ephemeral work connections
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
using System.Threading;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;
using pengdows.crud.strategies.connection;
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

    /// <inheritdoc/>
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

    /// <summary>
    /// Registers a PreventDatabaseUnload sentinel for the pool identified by
    /// <paramref name="executionType"/>. The first sentinel also becomes <see cref="PersistentConnection"/>.
    /// </summary>
    internal void RegisterSentinel(ITrackedConnection connection, ExecutionType executionType)
    {
        lock (_sentinelLock)
        {
            foreach (var existing in _sentinels)
            {
                if (ReferenceEquals(existing.Connection, connection))
                {
                    return;
                }
            }

            // Preserve an already-installed persistent connection when a strategy is
            // initialized directly (outside the normal constructor path).
            if (_sentinels.Count == 0 && _connection != null && !ReferenceEquals(_connection, connection))
            {
                _sentinels.Add((_connection, executionType));
            }

            _sentinels.Add((connection, executionType));
            _connection ??= connection;
        }
    }

    /// <summary>
    /// Reloads the provider's type catalog on every data source this context owns (Npgsql's
    /// <c>NpgsqlDataSource.ReloadTypesAsync</c>, found by reflection so the core library takes no
    /// provider dependency). Called after DDL for which
    /// <see cref="SqlDialect.InvalidatesProviderTypeCache"/> is true. Best effort: a data source
    /// without the method is skipped and a failure is logged, never thrown.
    /// </summary>
    internal async ValueTask ReloadProviderTypesAsync(CancellationToken cancellationToken)
    {
        var dataSources = new[] { _dataSource, _readerDataSource }
            .Where(d => d != null)
            .Distinct()
            .ToList();
        foreach (var dataSource in dataSources)
        {
            try
            {
                var type = dataSource!.GetType();
                var asyncReload = type.GetMethod("ReloadTypesAsync", new[] { typeof(CancellationToken) });
                if (asyncReload?.Invoke(dataSource, new object[] { cancellationToken }) is Task task)
                {
                    await task.ConfigureAwait(false);
                    continue;
                }

                type.GetMethod("ReloadTypes", Type.EmptyTypes)?.Invoke(dataSource, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not reload provider types after a type-catalog DDL statement; connections from this data source may not recognize the new type until the application restarts.");
            }
        }
    }

    private int _sentinelSuspensions;

    /// <summary>True while a DDL statement has the PreventDatabaseUnload sentinels closed.</summary>
    internal bool SentinelsSuspended => Volatile.Read(ref _sentinelSuspensions) > 0;

    /// <summary>
    /// Closes every PreventDatabaseUnload sentinel ahead of a DDL statement on a dialect that
    /// requires a pool reset for DDL (Firebird), and keeps them closed until
    /// <see cref="ResumeSentinelsAfterDdl"/>. Confirmed live: any other attachment present while the
    /// DDL runs — a long-lived sentinel, or a fresh one reopened just before the DDL — makes
    /// DROP/ALTER of a table the application has used fail with "object ... is in use". Disposing
    /// releases each sentinel's pool permit; the strategy's lazy repair reopens fresh sentinels on
    /// the first connection acquisition after the DDL. Returns false (and suspends nothing) when
    /// the context has no sentinels.
    /// </summary>
    internal bool SuspendSentinelsForDdl(List<string>? sentinelPools = null)
    {
        var sentinels = GetSentinelSnapshot();
        if (sentinels.Count == 0)
        {
            return false;
        }

        Interlocked.Increment(ref _sentinelSuspensions);
        foreach (var (connection, _) in sentinels)
        {
            sentinelPools?.Add(connection.ConnectionString);
            try
            {
                connection.Dispose();
            }
            catch
            {
                // Best effort: a sentinel that fails to close is replaced by lazy repair anyway.
            }
        }

        return true;
    }

    /// <summary>
    /// Ends a <see cref="SuspendSentinelsForDdl"/> suspension and, when it was the last one,
    /// reopens the sentinels so unload protection is back as soon as the DDL finishes. Never
    /// throws (it runs in a finally): a failed reopen is logged and left to the strategy's lazy
    /// repair on the next connection acquisition.
    /// </summary>
    internal async ValueTask ResumeSentinelsAfterDdlAsync()
    {
        if (Interlocked.Decrement(ref _sentinelSuspensions) > 0 ||
            _connectionStrategy is not PreventDatabaseUnloadConnectionStrategy strategy)
        {
            return;
        }

        try
        {
            await strategy.RestoreSentinelsAfterDdlAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not reopen PreventDatabaseUnload sentinels after a DDL statement; they will be reopened on the next operation.");
        }
    }

    /// <summary>
    /// Compare-and-swap replacement of a broken sentinel. Returns false (and installs nothing)
    /// when <paramref name="previous"/> is no longer registered or the context is disposed; the
    /// caller then owns and must dispose <paramref name="replacement"/>.
    /// </summary>
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

    /// <summary>
    /// Creates (unopened) a replacement or additional sentinel connection for the given pool,
    /// holding one permit from that pool's governor — the same accounting the initial sentinel uses.
    /// </summary>
    internal ITrackedConnection CreateSentinelConnection(ExecutionType executionType)
    {
        var connectionString = executionType == ExecutionType.Read && !string.IsNullOrWhiteSpace(_readerConnectionString)
            ? _readerConnectionString
            : _connectionString;
        var slot = AcquireSentinelSlot(executionType);
        try
        {
            return FactoryCreateConnection(executionType, connectionString, true, slot);
        }
        catch
        {
            slot.Dispose();
            throw;
        }
    }

    private PoolSlot AcquireSentinelSlot(ExecutionType executionType)
    {
        if (!_effectivePoolGovernorEnabled)
        {
            return default;
        }

        var governor = executionType == ExecutionType.Read ? _readerGovernor : _writerGovernor;
        if (governor == null)
        {
            ThrowIfGovernorMissingAfterDisposal();
            return default;
        }

        if (governor.Forbidden)
        {
            return default;
        }

        return governor.Acquire();
    }

    private ITrackedConnection[] TakePersistentConnections()
    {
        lock (_sentinelLock)
        {
            ITrackedConnection[] connections;
            if (_sentinels.Count > 0)
            {
                connections = new ITrackedConnection[_sentinels.Count];
                for (var i = 0; i < _sentinels.Count; i++)
                {
                    connections[i] = _sentinels[i].Connection;
                }

                _sentinels.Clear();
            }
            else
            {
                connections = _connection != null ? new[] { _connection } : Array.Empty<ITrackedConnection>();
            }

            _connection = null;
            return connections;
        }
    }

    private void DisposePersistentConnections()
    {
        foreach (var connection in TakePersistentConnections())
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
        foreach (var connection in TakePersistentConnections())
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

    /// <summary>
    /// The same gate, taken by a transaction for its whole lifetime: while held it marks the
    /// connection as having an open transaction, so an ordinary read through this context is
    /// rejected (<see cref="ThrowIfSingleConnectionTransactionOpen"/>) instead of reaching the
    /// provider outside — or silently inside — that transaction.
    /// </summary>
    internal ILockerAsync GetSingleConnectionTransactionGateForTransaction()
    {
        var gate = GetSingleConnectionTransactionGate();
        return gate == NoOpAsyncLocker.Instance ? gate : new TransactionGateLocker(this, gate);
    }

    /// <summary>
    /// DbMode.SingleConnection: throws when a transaction is open on the shared connection. A read
    /// through the context at that point would run on the transaction's connection without it
    /// (providers either reject that or silently enlist the command), so it is rejected instead.
    /// </summary>
    internal void ThrowIfSingleConnectionTransactionOpen()
    {
        if (Volatile.Read(ref _singleConnectionTransactionOpen) != 0)
        {
            throw new InvalidOperationException(
                "Cannot read through the context while a transaction is open on its single shared " +
                "connection (DbMode.SingleConnection). Read through the transaction instead, or " +
                "complete the transaction first.");
        }
    }

    private int _singleConnectionTransactionOpen;

    private sealed class TransactionGateLocker : ILockerAsync
    {
        private readonly DatabaseContext _owner;
        private readonly ILockerAsync _inner;
        private int _held;

        public TransactionGateLocker(DatabaseContext owner, ILockerAsync inner)
        {
            _owner = owner;
            _inner = inner;
        }

        public void Lock()
        {
            _inner.Lock();
            MarkHeld();
        }

        public async ValueTask LockAsync(CancellationToken cancellationToken = default)
        {
            await _inner.LockAsync(cancellationToken).ConfigureAwait(false);
            MarkHeld();
        }

        public async ValueTask<bool> TryLockAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var acquired = await _inner.TryLockAsync(timeout, cancellationToken).ConfigureAwait(false);
            if (acquired)
            {
                MarkHeld();
            }

            return acquired;
        }

        private void MarkHeld()
        {
            Volatile.Write(ref _held, 1);
            Volatile.Write(ref _owner._singleConnectionTransactionOpen, 1);
        }

        private void ClearHeld()
        {
            if (Interlocked.Exchange(ref _held, 0) == 1)
            {
                Volatile.Write(ref _owner._singleConnectionTransactionOpen, 0);
            }
        }

        public void Dispose()
        {
            ClearHeld();
            _inner.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            ClearHeld();
            return _inner.DisposeAsync();
        }
    }

    internal ITrackedConnection GetStandardConnectionWithExecutionType(ExecutionType executionType,
        bool isShared = false)
    {
        // BP-110 (3.0 CORE-025): every connection acquisition path reaches here. Without this
        // check a disposed context's nulled-out governor fields made AcquireSlot silently return
        // an ungoverned default slot, letting a container open a fresh physical connection
        // completely outside admission control.
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
    /// Async counterpart of <see cref="GetStandardConnectionWithExecutionType"/> -- awaits pool
    /// slot acquisition via <see cref="AcquireSlotAsync"/> instead of blocking the calling thread
    /// on the synchronous <see cref="AcquireSlot"/>. <see cref="FactoryCreateConnection"/> only
    /// constructs the connection object (no I/O -- "open late" philosophy defers Open() to the
    /// caller), so it is safe to call synchronously here.
    /// </summary>
    internal async ValueTask<ITrackedConnection> GetStandardConnectionWithExecutionTypeAsync(
        ExecutionType executionType, bool isShared = false, CancellationToken cancellationToken = default)
    {
        // BP-110 (3.0 CORE-025): every connection acquisition path reaches here. Without this
        // check a disposed context's nulled-out governor fields made AcquireSlot silently return
        // an ungoverned default slot, letting a container open a fresh physical connection
        // completely outside admission control.
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
    /// Skips execution if session-settings detection has not completed.
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
                // by beginning transactions with ExecutionType.Read.
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
            // Default (SessionInitializationFailureMode.BestEffort): log the failure and return the
            // connection without marking settings applied. The connection proceeds in an unknown
            // session state. FailClosed throws ConnectionException instead (below).
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
    /// Overload of FactoryCreateConnection using read execution type.
    /// </summary>
    internal ITrackedConnection FactoryCreateConnection(string? connectionString = null,
        bool isSharedConnection = false)
    {
        return FactoryCreateConnection(ExecutionType.Read, connectionString, isSharedConnection);
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
            // Not yet disposed: the narrow bootstrap window before InitializePoolGovernors()
            // has run — ungoverned by design, matching pre-existing behavior.
            return default;
        }

        return governor.Acquire();
    }

    /// <summary>
    /// Async counterpart of <see cref="AcquireSlot"/> -- awaits <c>PoolGovernor.AcquireAsync</c>
    /// (SemaphoreSlim.WaitAsync) instead of blocking the calling thread on the synchronous
    /// <c>PoolGovernor.Acquire</c> (SemaphoreSlim.Wait). Every branch here mirrors AcquireSlot
    /// exactly, aside from the final await.
    /// </summary>
    private async ValueTask<PoolSlot> AcquireSlotAsync(ExecutionType executionType,
        CancellationToken cancellationToken)
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
            // Not yet disposed: the narrow bootstrap window before InitializePoolGovernors()
            // has run — ungoverned by design, matching pre-existing behavior.
            return default;
        }

        return await governor.AcquireAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// BP-110 (3.0 CORE-025): a null governor observed while the context is already disposed
    /// means DisposePoolGovernors() nulled the field out from under an in-flight acquire —
    /// silently returning an ungoverned default slot would bypass admission control, so fail
    /// loudly instead. A null governor while NOT disposed is the legitimate pre-initialization
    /// bootstrap window, so this only throws for the disposed case.
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
