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
            return;
        }

        // Lazily compute the baseline SQL key — call GetBaseSessionSettings() exactly once
        // per context so repeated connection opens don't re-invoke the dialect method.
        if (Volatile.Read(ref _cachedBaselineKeyComputed) == 0)
        {
            _cachedBaselineKey = _dialect.GetBaseSessionSettings();
            Volatile.Write(ref _cachedBaselineKeyComputed, 1);
        }

        var baselineKey = _cachedBaselineKey ?? string.Empty;

        // Compute the desired intent key for this connection.
        //
        // For write connections, only emit the write-reset SQL when transitioning FROM a
        // read-only session state. Fresh connections are already in read-write mode and need
        // no intent SQL. This avoids emitting a no-op (or invalid) reset command on every
        // new connection obtained from the pool.
        //
        // We detect "was read-only" by inspecting the cached intent string: if the previous
        // intent is non-empty AND is not the write-reset SQL itself, the connection was in a
        // read-only session state and needs the reset. We deliberately do NOT call
        // GetReadOnlySessionSettings() in the write path — the read-only key is cached from
        // the first readOnly=true invocation and reused for comparison in the write path.
        string? intent;
        if (readOnly)
        {
            // Lazily compute the read-only intent key — call GetReadOnlySessionSettings()
            // exactly once per context.
            if (Volatile.Read(ref _cachedReadOnlyIntentKeyComputed) == 0)
            {
                _cachedReadOnlyIntentKey = _dialect.GetReadOnlySessionSettings();
                Volatile.Write(ref _cachedReadOnlyIntentKeyComputed, 1);
            }

            intent = _cachedReadOnlyIntentKey;
        }
        else
        {
            var writeResetSql = _dialect.GetReadOnlyTransactionResetSql();
            if (!string.IsNullOrEmpty(writeResetSql) &&
                _initializedConnections.TryGetValue(physicalConnection, out var prevState))
            {
                var prevParts = prevState.Split('|');
                var prevIntent = prevParts.Length == 2 ? prevParts[1] : string.Empty;
                // Non-empty prevIntent that is not already the write-reset SQL means the
                // connection was placed in read-only session mode and needs to be reset.
                intent = !string.IsNullOrEmpty(prevIntent) &&
                         !string.Equals(prevIntent, writeResetSql, StringComparison.Ordinal)
                    ? writeResetSql
                    : null;
            }
            else
            {
                intent = null;
            }
        }

        var intentKey = intent ?? string.Empty;

        var needsBaseline = true;
        var needsIntent = true;

        if (_initializedConnections.TryGetValue(physicalConnection, out var lastFullSettings))
        {
            // Format: "baseline|intent"
            var parts = lastFullSettings.Split('|');
            if (parts.Length == 2)
            {
                if (string.Equals(parts[0], baselineKey, StringComparison.Ordinal))
                {
                    needsBaseline = false;
                }
                if (string.Equals(parts[1], intentKey, StringComparison.Ordinal))
                {
                    needsIntent = false;
                }
            }
        }

        if (!needsBaseline && !needsIntent)
        {
            if (connection is ITrackedConnection tc)
            {
                tc.LocalState.MarkSessionSettingsApplied();
            }
            return;
        }

        var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        try
        {
            if (needsBaseline && !string.IsNullOrWhiteSpace(baselineKey))
            {
                sb.Append(baselineKey);
            }

            if (needsIntent && !string.IsNullOrWhiteSpace(intentKey))
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(intentKey);
            }

            if (sb.Length > 0)
            {
                var settingsToApply = sb.ToString();
                _logger.LogInformation("Applying session settings for {Name} (Baseline: {Baseline}, Intent: {Intent})",
                    Name, needsBaseline, needsIntent);

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
                    _logger.LogError(ex, "Failed to apply session settings for {Name}", Name);
                    return;
                }
            }

            // Update the cache with the new composite state
            _initializedConnections.AddOrUpdate(physicalConnection, $"{baselineKey}|{intentKey}");
            
            if (connection is ITrackedConnection tc)
            {
                tc.LocalState.MarkSessionSettingsApplied();
            }
        }
        finally
        {
            sb.Dispose();
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
