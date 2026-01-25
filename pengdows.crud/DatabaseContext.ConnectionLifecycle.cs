using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.@internal;
using pengdows.crud.infrastructure;
using pengdows.crud.threading;
using pengdows.crud.wrappers;

namespace pengdows.crud;

/// <summary>
/// DatabaseContext partial class: Connection lifecycle management methods
/// </summary>
public partial class DatabaseContext
{
    /// <summary>
    /// Gets a tracked connection for the specified execution type.
    /// </summary>
    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false)
    {
        return _connectionStrategy.GetConnection(executionType, isShared);
    }

    /// <summary>
    /// Closes and disposes a tracked connection, returning it to the connection pool.
    /// </summary>
    public void CloseAndDisposeConnection(ITrackedConnection? connection)
    {
        _connectionStrategy.ReleaseConnection(connection);
    }

    /// <summary>
    /// Asynchronously closes and disposes a tracked connection, returning it to the connection pool.
    /// </summary>
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
            var conn = FactoryCreateConnection(null, isShared, readOnly, null, permit);
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
    /// Gets a connection for SingleWriter mode.
    /// </summary>
    internal ITrackedConnection GetSingleWriterConnection(ExecutionType type, bool isShared = false)
    {
        if (ExecutionType.Read == type)
        {
            // Embedded in-memory providers: distinguish isolated vs shared memory
            if (Product == SupportedDatabase.Sqlite || Product == SupportedDatabase.DuckDB)
            {
                var memKind = DetectInMemoryKind(Product, _connectionString);
                if (memKind == InMemoryKind.Isolated)
                {
                    // Isolated memory: reuse pinned connection for all reads
                    return GetSingleConnection();
                }

                // Shared memory: ephemeral read-only connections using the same CS
                return isShared
                    ? GetSingleConnection()
                    : GetStandardConnectionWithExecutionType(ExecutionType.Read, isShared, true);
            }

            // Non-embedded: ephemeral read connection (unless shared within a transaction)
            return isShared
                ? GetSingleConnection()
                : GetStandardConnectionWithExecutionType(ExecutionType.Read, isShared, true);
        }

        return GetSingleConnection();
    }

    /// <summary>
    /// Applies connection session settings for non-Standard modes.
    /// </summary>
    internal void ApplyConnectionSessionSettings(IDbConnection connection)
    {
        if (ConnectionMode == DbMode.Standard)
        {
            return;
        }

        _logger.LogInformation("Applying connection session settings");
        if (_applyConnectionSessionSettings)
        {
            var success = SessionSettingsConfigurator.ApplySessionSettings(connection, _connectionSessionSettings);
            if (!success)
            {
                _logger.LogError("Error setting session settings");
                _applyConnectionSessionSettings = false;
            }
        }
    }

    /// <summary>
    /// Applies persistent connection session settings (for KeepAlive/SingleWriter/SingleConnection modes).
    /// </summary>
    public void ApplyPersistentConnectionSessionSettings(IDbConnection connection)
    {
        if (ConnectionMode == DbMode.Standard)
        {
            return;
        }

        // Skip if dialect hasn't been initialized yet (happens during constructor)
        if (_dialect == null)
        {
            return;
        }

        // If session settings were already applied on the persistent connection, avoid double
        // application only for that same connection; still allow explicit application to other connections
        if (_sessionSettingsAppliedOnOpen && ReferenceEquals(connection, _connection))
        {
            return;
        }

        _logger.LogInformation("Applying persistent connection session settings");

        // For persistent connections in SingleConnection/SingleWriter mode,
        // use the dialect's session settings which include read-only settings when appropriate
        var sessionSettings = _dialect.GetConnectionSessionSettings(this, IsReadOnlyConnection);

        if (!string.IsNullOrEmpty(sessionSettings))
        {
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
        }
    }

    /// <summary>
    /// Factory method to create a new tracked connection with state change monitoring and session settings.
    /// </summary>
    private ITrackedConnection FactoryCreateConnection(
        string? connectionString = null,
        bool isSharedConnection = false,
        bool readOnly = false,
        Action<DbConnection>? onFirstOpen = null,
        PoolPermit? permit = null)
    {
        SanitizeConnectionString(connectionString);

        // Prefer DataSource over Factory for better performance (shared prepared statement cache)
        DbConnection connection;
        if (_dataSource != null)
        {
            connection = _dataSource.CreateConnection();
            // Connection string is already configured in the DataSource
        }
        else if (_factory != null)
        {
            connection = _factory.CreateConnection() ??
                         throw new InvalidOperationException("Factory returned null DbConnection.");
            connection.ConnectionString = ConnectionString;
            _dialect?.ApplyConnectionSettings(connection, this, readOnly);
        }
        else
        {
            throw new InvalidOperationException("Neither DataSource nor Factory is available.");
        }

        // Increment total connections created counter when a new connection is actually created
        Interlocked.Increment(ref _totalConnectionsCreated);

        // Ensure session settings from the active dialect are applied on first open for all modes.
        Action<DbConnection>? firstOpenHandler = conn =>
        {
            ILockerAsync? guard = null;
            try
            {
                guard = GetLock();
                guard.Lock();
                // Apply session settings for all connection modes.
                // Prefer dialect-provided settings when available; fall back to precomputed string.
                string settings;
                if (_dialect != null)
                {
                    settings = _dialect.GetConnectionSessionSettings(this, readOnly) ?? string.Empty;
                }
                else
                {
                    // Dialect not initialized yet (constructor path). Derive lightweight settings
                    // from the opened connection's product metadata for first application.
                    try
                    {
                        var schema = conn.GetSchema(DbMetaDataCollectionNames.DataSourceInformation);
                        var productName = schema.Rows.Count > 0
                            ? schema.Rows[0].Field<string>("DataSourceProductName")
                            : null;
                        var lower = (productName ?? string.Empty).ToLowerInvariant();
                        if (lower.Contains("mysql") || lower.Contains("mariadb"))
                        {
                            settings =
                                "SET SESSION sql_mode = 'STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ZERO_DATE,NO_ZERO_IN_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION,ANSI_QUOTES,NO_BACKSLASH_ESCAPES';";
                            if (readOnly)
                            {
                                settings += "\nSET SESSION TRANSACTION READ ONLY;";
                            }
                        }
                        else if (lower.Contains("oracle"))
                        {
                            settings = "ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY-MM-DD';";
                            if (readOnly)
                            {
                                settings += "\nALTER SESSION SET READ ONLY;";
                            }
                        }
                        else if (lower.Contains("postgres"))
                        {
                            settings = "SET standard_conforming_strings = on;\nSET client_min_messages = warning;";
                        }
                        else if (lower.Contains("sqlite"))
                        {
                            settings = "PRAGMA foreign_keys = ON;";
                            if (readOnly)
                            {
                                settings += "\nPRAGMA query_only = ON;";
                            }
                        }
                        else if (lower.Contains("duckdb") || lower.Contains("duck db"))
                        {
                            settings = readOnly ? "PRAGMA read_only = 1;" : string.Empty;
                        }
                        else
                        {
                            settings = _connectionSessionSettings ?? string.Empty;
                        }
                    }
                    catch
                    {
                        settings = _connectionSessionSettings ?? string.Empty;
                    }
                }

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

                    _sessionSettingsAppliedOnOpen = true;
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
            _metricsCollector,
            _modeContentionStats,
            ConnectionMode,
            _modeLockTimeout,
            permit
        );
        return tracked;
    }

    /// <summary>
    /// Overload of FactoryCreateConnection without custom first-open handler.
    /// </summary>
    internal ITrackedConnection FactoryCreateConnection(string? connectionString = null,
        bool isSharedConnection = false, bool readOnly = false)
    {
        return FactoryCreateConnection(connectionString, isSharedConnection, readOnly, null);
    }

    private PoolPermit AcquirePermit(ExecutionType executionType)
    {
        if (!_enablePoolGovernor)
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
            previous = Interlocked.Read(ref _maxNumberOfOpenConnections);
            if (current <= previous)
            {
                return; // no update needed
            }

            // try to update only if no one else has changed it
        } while (Interlocked.CompareExchange(
                     ref _maxNumberOfOpenConnections,
                     current,
                     previous) != previous);
    }
}