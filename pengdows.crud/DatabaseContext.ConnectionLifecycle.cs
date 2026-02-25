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
using pengdows.crud.@internal;
using pengdows.crud.infrastructure;
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
    public void CloseAndDisposeConnection(ITrackedConnection? connection)
    {
        _connectionStrategy.ReleaseConnection(connection);
    }

    /// <inheritdoc/>
    public async ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? connection)
    {
        await _connectionStrategy.ReleaseConnectionAsync(connection).ConfigureAwait(false);
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

        return new RealAsyncLocker(_connectionOpenGate);
    }

    internal ITrackedConnection GetStandardConnectionWithExecutionType(ExecutionType executionType,
        bool isShared = false, bool readOnly = false)
    {
        var slot = AcquireSlot(executionType);
        try
        {
            var useReader = ShouldUseReaderConnectionString(readOnly);
            var connectionString = useReader ? _readerConnectionString : _connectionString;
            var conn = FactoryCreateConnection(executionType, connectionString, isShared, readOnly, null, slot);
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
    /// Skips execution if detection has not completed, dialect is null,
    /// or the connection has already had settings applied.
    /// </summary>
    internal void ExecuteSessionSettings(IDbConnection connection, bool readOnly)
    {
        if (!_sessionSettingsDetectionCompleted || _dialect == null)
        {
            return;
        }

        // Get the real physical connection from any wrapper (like TrackedConnection)
        // to check if we've already initialized THIS physical instance in the pool.
        var current = (object)connection;
        while (current is IInternalConnectionWrapper wrapper)
        {
            current = wrapper.UnderlyingConnection;
        }

        if (current is not DbConnection physicalConnection)
        {
            // If it's not a DbConnection (unlikely), we can't track it via ConditionalWeakTable safely.
            return;
        }

        var settings = GetCachedSessionSettings(readOnly);
        var settingsKey = settings ?? string.Empty;

        // ConditionalWeakTable uses reference equality on the physical object.
        if (_initializedConnections.TryGetValue(physicalConnection, out var lastSettings))
        {
            if (string.Equals(lastSettings, settingsKey, StringComparison.Ordinal))
            {
                if (connection is ITrackedConnection tc)
                {
                    tc.LocalState.MarkSessionSettingsApplied();
                }
                return;
            }
            
            // If we are here, the physical connection was last used with DIFFERENT settings
            // (e.g. it was Read and now it's Write). We MUST re-initialize.
        }

        var applied = false;
        if (!string.IsNullOrWhiteSpace(settings))
        {
            _logger.LogInformation("Applying session settings for {Name}", Name);
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = settings;
                cmd.ExecuteNonQuery();
                applied = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply session settings for {Name}", Name);
            }
        }
        else
        {
            applied = true;
        }

        if (applied)
        {
            _initializedConnections.AddOrUpdate(physicalConnection, settingsKey);
            if (connection is ITrackedConnection tc)
            {
                tc.LocalState.MarkSessionSettingsApplied();
            }
        }
    }

    private string GetCachedSessionSettings(bool readOnly)
    {
        if (readOnly)
        {
            if (Volatile.Read(ref _cachedReadOnlySessionSettingsComputed) == 1)
            {
                return _cachedReadOnlySessionSettings ?? string.Empty;
            }

            var settings = _dialect.GetConnectionSessionSettings(this, true);
            _cachedReadOnlySessionSettings = settings;
            Volatile.Write(ref _cachedReadOnlySessionSettingsComputed, 1);
            return settings;
        }

        if (Volatile.Read(ref _cachedReadWriteSessionSettingsComputed) == 1)
        {
            return _cachedReadWriteSessionSettings ?? string.Empty;
        }

        var readWriteSettings = _dialect.GetConnectionSessionSettings(this, false);
        _cachedReadWriteSessionSettings = readWriteSettings;
        Volatile.Write(ref _cachedReadWriteSessionSettingsComputed, 1);
        return readWriteSettings;
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
        Action<DbConnection>? onFirstOpen = null,
        PoolSlot? slot = null)
    {
        SanitizeConnectionString(connectionString);

        var activeConnectionString = string.IsNullOrWhiteSpace(connectionString)
            ? _connectionString
            : connectionString;

        var isCustomConnectionString = !string.IsNullOrWhiteSpace(connectionString);
        var useReaderCS = ShouldUseReaderConnectionString(readOnly);

        if (_logger.IsEnabled(LogLevel.Warning) &&
            !string.IsNullOrWhiteSpace(activeConnectionString) &&
            activeConnectionString.IndexOf("password", StringComparison.OrdinalIgnoreCase) < 0)
        {
            var redacted = isCustomConnectionString
                ? RedactConnectionString(activeConnectionString)
                : (useReaderCS ? _redactedReaderConnectionString : _redactedConnectionString);

            _logger.LogWarning("Connection string missing password for {Name}: {ConnectionString}", Name, redacted);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var redacted = isCustomConnectionString
                ? RedactConnectionString(activeConnectionString)
                : (useReaderCS ? _redactedReaderConnectionString : _redactedConnectionString);

            _logger.LogDebug("Preparing connection for {ExecutionType} with string: {ConnectionString}", executionType, redacted);
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

        TrackedConnection? trackedConnection = null;

        // Ensure session settings from the active dialect are applied on first open for all modes.
        Action<DbConnection>? firstOpenHandler = conn =>
        {
            try
            {
                if (trackedConnection != null)
                {
                    ExecuteSessionSettings(trackedConnection, readOnly);
                }
                else
                {
                    ExecuteSessionSettings(conn, readOnly);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply session settings on first open for {Name}", Name);
            }

            // Invoke any additional callback provided by caller
            onFirstOpen?.Invoke(conn);
        };

        var metricsCollector = executionType == ExecutionType.Read ? _readerMetricsCollector : _writerMetricsCollector;
        var useReaderPrefix = readOnly && ShouldUseReaderConnectionString(readOnly);
        var namePrefix = useReaderPrefix ? _connectionNamePrefixRead : _connectionNamePrefixWrite;
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
                    case ConnectionState.Closed when from != ConnectionState.Broken:
                    case ConnectionState.Broken:
                    {
                        _logger.LogDebug("Closed or broken connection: " + Name);
                        Interlocked.Decrement(ref _connectionCount);
                        break;
                    }
                }
            },
            firstOpenHandler,
            _disposeHandler,
            null,
            isSharedConnection,
            metricsCollector,
            _modeContentionStats,
            ConnectionMode,
            _modeLockTimeout,
            slot,
            namePrefix
        );
        trackedConnection = tracked;
        return tracked;
    }

    /// <summary>
    /// Overload of FactoryCreateConnection without custom first-open handler.
    /// </summary>
    internal ITrackedConnection FactoryCreateConnection(string? connectionString = null,
        bool isSharedConnection = false, bool readOnly = false)
    {
        return FactoryCreateConnection(ExecutionType.Read, connectionString, isSharedConnection, readOnly, null);
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
