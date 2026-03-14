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
//   * SingleWriter - Pinned writer + ephemeral readers
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

    /// <summary>
    /// Creates a standard (ephemeral) connection from the factory or data source.
    /// </summary>
    internal ITrackedConnection GetStandardConnection(bool isShared = false, bool readOnly = false)
    {
        return GetStandardConnectionWithExecutionType(ExecutionType.Read, isShared, readOnly);
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

    internal ITrackedConnection GetStandardConnectionWithExecutionType(ExecutionType executionType,
        bool isShared = false, bool readOnly = false)
    {
        var slot = AcquireSlot(executionType);
        try
        {
            var useReader = ShouldUseReaderConnectionString(readOnly);
            var connectionString = useReader ? _readerConnectionString : _connectionString;
            var conn = FactoryCreateConnection(executionType, connectionString, isShared, readOnly, slot);
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
        bool readOnly = false,
        PoolSlot? slot = null)
    {
        SanitizeConnectionString(connectionString);

        var activeConnectionString = string.IsNullOrWhiteSpace(connectionString)
            ? _connectionString
            : connectionString;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Preparing connection for {ExecutionType}", executionType);
        }

        var dataSource = ResolveDataSource(readOnly);

        // Prefer DataSource over Factory for better performance (shared prepared statement cache)
        DbConnection connection;
        if (dataSource != null)
        {
            connection = dataSource.CreateConnection();
            _dialect?.ConfigureProviderSpecificSettings(connection, this, readOnly);
        }
        else if (_factory != null)
        {
            connection = _factory.CreateConnection() ??
                         throw new InvalidOperationException("Factory returned null DbConnection.");
            if (_dialect != null)
            {
                _dialect.ApplyConnectionSettingsCore(connection, this, readOnly, activeConnectionString);
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
        var firstOpenHandler = readOnly ? _firstOpenHandlerRo : _firstOpenHandlerRw;
        var firstOpenHandlerAsync = readOnly ? _firstOpenHandlerAsyncRo : _firstOpenHandlerAsyncRw;

        var metricsCollector = executionType == ExecutionType.Read ? _readerMetricsCollector : _writerMetricsCollector;
        var useReaderPrefix = readOnly && ShouldUseReaderConnectionString(readOnly);
        var namePrefix = useReaderPrefix ? _connectionNamePrefixRead : _connectionNamePrefixWrite;
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
        bool isSharedConnection = false, bool readOnly = false)
    {
        return FactoryCreateConnection(ExecutionType.Read, connectionString, isSharedConnection, readOnly);
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
            return default;
        }

        return governor.Acquire();
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
