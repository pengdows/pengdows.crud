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
//   * AcquirePermit() - Gets pool permit with backpressure
// =============================================================================

using System.Data;
using System.Data.Common;
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

    internal ITrackedConnection GetStandardConnectionWithExecutionType(ExecutionType executionType,
        bool isShared = false, bool readOnly = false)
    {
        var permit = AcquirePermit(executionType);
        try
        {
            var useReader = ShouldUseReaderConnectionString(readOnly);
            var connectionString = useReader ? _readerConnectionString : _connectionString;
            var conn = FactoryCreateConnection(executionType, connectionString, isShared, readOnly, null, permit);
            return conn;
        }
        catch
        {
            permit.Dispose();
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
    /// Applies connection session settings for non-Standard modes.
    /// </summary>
    internal void ApplyConnectionSessionSettings(IDbConnection connection)
    {
        if (ConnectionMode == DbMode.Standard || !_sessionSettingsDetectionCompleted || _dialect == null)
        {
            return;
        }

        var tracked = connection as ITrackedConnection;
        if (tracked?.LocalState.SessionSettingsApplied == true)
        {
            return;
        }

        _logger.LogInformation("Applying connection session settings");
        var sessionSettings = _dialect.GetConnectionSessionSettings(this, IsReadOnlyConnection);
        if (string.IsNullOrWhiteSpace(sessionSettings))
        {
            if (tracked != null)
            {
                tracked.LocalState.SessionSettingsApplied = true;
            }

            return;
        }

        foreach (var part in sessionSettings.Split(';'))
        {
            var stmt = part.Trim();
            if (string.IsNullOrEmpty(stmt))
            {
                continue;
            }

            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = stmt; // no trailing ';'
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error setting session settings:" + ex.Message);
            }
        }

        if (tracked != null)
        {
            tracked.LocalState.SessionSettingsApplied = true;
        }
    }

    /// <summary>
    /// Applies persistent connection session settings (for KeepAlive/SingleWriter/SingleConnection modes).
    /// </summary>
    public void ApplyPersistentConnectionSessionSettings(IDbConnection connection)
    {
        if (ConnectionMode == DbMode.Standard || !_sessionSettingsDetectionCompleted)
        {
            return;
        }

        // Skip if dialect hasn't been initialized yet (happens during constructor)
        if (_dialect == null)
        {
            return;
        }

        var tracked = connection as ITrackedConnection;
        if (tracked?.LocalState.SessionSettingsApplied == true)
        {
            return;
        }

        _logger.LogInformation("Applying persistent connection session settings");

        // For persistent connections in SingleConnection/SingleWriter mode,
        // use the dialect's session settings which include read-only settings when appropriate
        var sessionSettings = _dialect.GetConnectionSessionSettings(this, IsReadOnlyConnection);

        if (string.IsNullOrWhiteSpace(sessionSettings))
        {
            if (tracked != null)
            {
                tracked.LocalState.SessionSettingsApplied = true;
            }

            return;
        }

        try
        {
            var parts = sessionSettings.Split(';');
            foreach (var part in parts)
            {
                var stmt = part.Trim();
                if (string.IsNullOrEmpty(stmt))
                {
                    continue;
                }

                using var cmd = connection.CreateCommand();
                cmd.CommandText = stmt; // no trailing ';'
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error setting session settings:" + ex.Message);
        }

        if (tracked != null)
        {
            tracked.LocalState.SessionSettingsApplied = true;
        }
    }

    /// <summary>
    /// Factory method to create a new tracked connection with state change monitoring and session settings.
    /// </summary>
    private ITrackedConnection FactoryCreateConnection(
        ExecutionType executionType,
        string? connectionString = null,
        bool isSharedConnection = false,
        bool readOnly = false,
        Action<DbConnection>? onFirstOpen = null,
        PoolPermit? permit = null)
    {
        SanitizeConnectionString(connectionString);

        var activeConnectionString = string.IsNullOrWhiteSpace(connectionString)
            ? _connectionString
            : connectionString;
        if (!string.IsNullOrWhiteSpace(activeConnectionString) &&
            activeConnectionString.IndexOf("password", StringComparison.OrdinalIgnoreCase) < 0)
        {
            _logger.LogWarning("Connection string missing password for {Name}: {ConnectionString}", Name,
                activeConnectionString);
        }
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Preparing connection for {ExecutionType} with string: {ConnectionString}", executionType,
                activeConnectionString);
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
            ILockerAsync? guard = null;
            try
            {
                guard = GetLock();
                guard.Lock();
                if (_sessionSettingsDetectionCompleted && _dialect != null)
                {
                    if (trackedConnection?.LocalState.SessionSettingsApplied != true)
                    {
                        _logger.LogInformation("Applying connection session settings");
                        var settings = _dialect.GetConnectionSessionSettings(this, readOnly) ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(settings))
                        {
                            // Execute one statement at a time. Do not auto-append semicolons.
                            var parts = settings.Split(';');
                            foreach (var part in parts)
                            {
                                var stmt = part.Trim();
                                if (string.IsNullOrEmpty(stmt))
                                {
                                    continue;
                                }

                                using var cmd = conn.CreateCommand();
                                cmd.CommandText = stmt; // no trailing ';'
                                cmd.ExecuteNonQuery();
                            }

                        }

                        if (trackedConnection != null)
                        {
                            trackedConnection.LocalState.SessionSettingsApplied = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply session settings on first open for {Name}", Name);
            }
            finally
            {
                if (guard is IAsyncDisposable gad)
                {
                    gad.DisposeAsync().GetAwaiter().GetResult();
                }
                else if (guard is IDisposable gd)
                {
                    gd.Dispose();
                }
            }

            // Invoke any additional callback provided by caller
            onFirstOpen?.Invoke(conn);
        };

        var metricsCollector = executionType == ExecutionType.Read ? _readerMetricsCollector : _writerMetricsCollector;
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
            conn => { _logger.LogDebug("Connection disposed."); },
            null,
            isSharedConnection,
            metricsCollector,
            _modeContentionStats,
            ConnectionMode,
            _modeLockTimeout,
            permit
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

    private PoolPermit AcquirePermit(ExecutionType executionType)
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
