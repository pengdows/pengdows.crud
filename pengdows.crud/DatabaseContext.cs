#region

using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
// using pengdows.crud.strategies.connection; // superseded by strategies namespace
using pengdows.crud.dialects;
using pengdows.crud.@internal;
using pengdows.crud.infrastructure;
using pengdows.crud.isolation;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using pengdows.crud.strategies.connection;
using pengdows.crud.strategies.proc;
using pengdows.crud.metrics;

#endregion

namespace pengdows.crud;

/// <summary>
/// Primary database context for connection management, transaction handling, and SQL execution.
/// </summary>
/// <remarks>
/// <para><strong>Terminology:</strong></para>
/// <para>
/// <c>DatabaseContext</c> is not equivalent to Entity Framework's <c>DbContext</c>.
/// It is a <b>singleton execution coordinator</b> bound to a specific provider + connection string.
/// </para>
/// <para>
/// <strong>Concurrent callers are supported:</strong>
/// Standard mode: parallel operations using ephemeral connections.
/// KeepAlive/SingleWriter/SingleConnection modes: operations serialize on shared connection lock.
/// APIs returning <see cref="wrappers.ITrackedReader"/> hold a connection lease until disposed.
/// </para>
/// <para><strong>Lifetime:</strong></para>
/// <para>
/// Register <c>DatabaseContext</c> as a <b>singleton per unique connection string</b>.
/// This is required for modes that maintain persistent connections (e.g. SingleWriter/SingleConnection).
/// </para>
///
/// <para><strong>Concurrency contract:</strong></para>
/// <list type="bullet">
///   <item>
///     <description>
///     <b>Standard:</b> concurrent calls are allowed; each operation uses an ephemeral provider connection.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>KeepAlive:</b> concurrent calls are allowed; a pinned sentinel connection prevents unload, but work still uses
///     ephemeral provider connections.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>SingleWriter:</b> writes serialize on the pinned writer connection; non-transactional reads use ephemeral
///     read-only connections.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>SingleConnection:</b> all operations serialize on the single pinned connection.
///     </description>
///   </item>
/// </list>
///
/// <para><strong>Locking model:</strong></para>
/// <para>
/// The context itself does not act as the serialization primitive. Serialization happens at the <b>connection lock</b>
/// returned by the tracked connection. Shared connections use a real lock; ephemeral connections use a no-op lock.
/// </para>
///
/// <para><strong>Callbacks / re-entrancy:</strong></para>
/// <para>
/// Do not call back into the same <c>DatabaseContext</c> instance from metrics/event handlers. Treat callbacks as observers.
/// </para>
///
/// <para><strong>Version 2.0 Breaking Change:</strong></para>
/// <para>
/// <c>DatabaseContext</c> will be renamed to <c>DatabaseCoordinator</c> in version 2.0 to eliminate
/// confusion with Entity Framework's <c>DbContext</c>. A compatibility shim will be provided during
/// the transition period. See VERSION_2.0_PLANNING.md for migration details.
/// </para>
/// </remarks>
public partial class DatabaseContext : SafeAsyncDisposableBase, IDatabaseContext, IContextIdentity, ISqlDialectProvider, IMetricsCollectorAccessor
{
    private readonly DbProviderFactory? _factory;
    private readonly DbDataSource? _dataSource;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<IDatabaseContext> _logger;
    private IConnectionStrategy _connectionStrategy = null!;
    private IProcWrappingStrategy _procWrappingStrategy = null!;
    private ProcWrappingStyle _procWrappingStyle;
    private bool _applyConnectionSessionSettings;
    private ITrackedConnection? _connection = null;

    private long _connectionCount;
    private string _connectionString = string.Empty;
    private DataSourceInformation _dataSourceInfo = null!;
    private readonly SqlDialect _dialect = null!;
    private IIsolationResolver _isolationResolver = null!;
    private bool _isReadConnection = true;
    private bool _isWriteConnection = true;
    private long _maxNumberOfOpenConnections;
    
    // Additional performance counters for granular connection pool monitoring
    private long _totalConnectionsCreated;
    private long _totalConnectionsReused;
    private long _totalConnectionFailures;
    private long _totalConnectionTimeoutFailures;
    private string _connectionSessionSettings = string.Empty;
    private readonly bool? _forceManualPrepare;
    private readonly bool? _disablePrepare;
    private bool? _rcsiPrefetch;
    private bool? _snapshotIsolationPrefetch;
    private int _initializing; // 0 = false, 1 = true
    private bool _sessionSettingsAppliedOnOpen;
    private readonly MetricsCollector? _metricsCollector;
    private EventHandler<DatabaseMetrics>? _metricsUpdated;
    private int _metricsHasActivity;

    public Guid RootId { get; } = Guid.NewGuid();

    private static readonly char[] _parameterPrefixes = { '@', '?', ':' };

    [Obsolete("Use the constructor that takes DatabaseContextConfiguration instead.")]
    private ReadWriteMode _readWriteMode = ReadWriteMode.ReadWrite;
    public ReadWriteMode ReadWriteMode
    {
        get => _readWriteMode;
        set
        {
            _readWriteMode = value == ReadWriteMode.WriteOnly ? ReadWriteMode.ReadWrite : value;
            _isReadConnection = (_readWriteMode & ReadWriteMode.ReadOnly) == ReadWriteMode.ReadOnly ;
            _isWriteConnection = (_readWriteMode & ReadWriteMode.WriteOnly) == ReadWriteMode.WriteOnly;
            if (_isWriteConnection)
            {
                //write connection implies read connection
                _isWriteConnection = true;
            }
        }
    }

    public string Name { get; set; }

    // Expose original requested mode for internal strategy decisions
    public string ConnectionString => _connectionString;

    /// <summary>
    /// Gets the DbDataSource if one was provided (e.g., NpgsqlDataSource).
    /// When available, provides better performance through shared prepared statement caching.
    /// Null if using traditional DbProviderFactory approach.
    /// </summary>
    public DbDataSource? DataSource => _dataSource;

    public bool IsReadOnlyConnection => _isReadConnection && !_isWriteConnection;
    public bool RCSIEnabled { get; private set; }

    public bool SnapshotIsolationEnabled { get; private set; }

    /// <summary>
    /// Returns a no-op locker.
    /// </summary>
    /// <remarks>
    /// Context-level locking is intentionally a no-op. Serialization happens at the connection level:
    /// connections returned by <c>GetConnection(...)</c> provide the real lock when a mode uses shared/pinned connections.
    /// </remarks>
    public ILockerAsync GetLock()
    {
        ThrowIfDisposed();
        return NoOpAsyncLocker.Instance;
    }


    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
    public DbMode ConnectionMode { get; private set; }


    public ITypeMapRegistry TypeMapRegistry { get; }

    public IDataSourceInformation DataSourceInfo => _dataSourceInfo;
    public string SessionSettingsPreamble => _dialect.GetConnectionSessionSettings(this, IsReadOnlyConnection);

    public string CompositeIdentifierSeparator => _dataSourceInfo.CompositeIdentifierSeparator;
    public SupportedDatabase Product => _dataSourceInfo?.Product ?? SupportedDatabase.Unknown;
    // ProcWrappingStyle is defined below with a setter to update strategy
    public int MaxParameterLimit => _dataSourceInfo.MaxParameterLimit;
    public int MaxOutputParameters => _dataSourceInfo.MaxOutputParameters;
    public long MaxNumberOfConnections => Interlocked.Read(ref _maxNumberOfOpenConnections);
    public long NumberOfOpenConnections => Interlocked.Read(ref _connectionCount);

    public string QuotePrefix => _dialect.QuotePrefix;
    public string QuoteSuffix => _dialect.QuoteSuffix;
    public bool? ForceManualPrepare => _forceManualPrepare;
    public bool? DisablePrepare => _disablePrepare;

    public void AssertIsReadConnection()
    {
        if (!_isReadConnection)
        {
            throw new InvalidOperationException("The connection is not read connection.");
        }
    }

    public void AssertIsWriteConnection()
    {
        if (!_isWriteConnection)
        {
            throw new InvalidOperationException("The connection is not write connection.");
        }
    }


    public ProcWrappingStyle ProcWrappingStyle
    {
        get => _procWrappingStyle;
        set
        {
            _procWrappingStyle = value;
            _procWrappingStrategy = ProcWrappingStrategyFactory.Create(value);
        }
    }

    internal IProcWrappingStrategy ProcWrappingStrategy => _procWrappingStrategy;

    // Duplicates removed; properties already exist earlier in the class

    //
    // private int _disposed; // 0=false, 1=true
    //
    //
    // public void Dispose()
    // {
    //     Dispose(disposing: true);
    // }
    //
    // public async ValueTask DisposeAsync()
    // {
    //     await DisposeAsyncCore().ConfigureAwait(false);
    //     Dispose(disposing: false); // Finalizer path for unmanaged cleanup (if any)
    // }
    //
    // protected virtual async ValueTask DisposeAsyncCore()
    // {
    //     if (Interlocked.Exchange(ref _disposed, 1) != 0)
    //         return; // Already disposed
    //
    //     if (_connection is IAsyncDisposable asyncDisposable)
    //     {
    //         await asyncDisposable.DisposeAsync().ConfigureAwait(false);
    //     }
    //     else
    //     {
    //         _connection?.Dispose();
    //     }
    //
    //     _connection = null;
    // }
    //
    //
    // protected virtual void Dispose(bool disposing)
    // {
    //     if (Interlocked.Exchange(ref _disposed, 1) != 0)
    //         return; // Already disposed
    //
    //     if (disposing)
    //     {
    //         try
    //         {
    //             _connection?.Dispose();
    //         }
    //         catch
    //         {
    //             // Optional: log or suppress
    //         }
    //         finally
    //         {
    //             _connection = null;
    //             GC.SuppressFinalize(this); // Suppress only here
    //         }
    //     }
    //
    //     // unmanaged cleanup if needed (none currently)
    // }

    protected override void DisposeManaged()
    {
        if (_metricsCollector != null)
        {
            _metricsCollector.MetricsChanged -= OnMetricsCollectorUpdated;
        }
        try
        {
            _connection?.Dispose();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _connection = null;
        }
        base.DisposeManaged();
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        if (_metricsCollector != null)
        {
            _metricsCollector.MetricsChanged -= OnMetricsCollectorUpdated;
        }
        try
        {
            if (_connection is IAsyncDisposable ad)
            {
                await ad.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                _connection?.Dispose();
            }
        }
        finally
        {
            _connection = null;
        }
        await base.DisposeManagedAsync().ConfigureAwait(false);
    }

    public ISqlDialect Dialect => _dialect;
}
