// =============================================================================
// FILE: TransactionContext.cs
// PURPOSE: Represents an active database transaction with commit/rollback
//          control, savepoint support, and automatic cleanup.
//
// AI SUMMARY:
// - Created via DatabaseContext.BeginTransaction() - not directly instantiated.
// - Holds a pinned connection for the duration of the transaction.
// - Implements IDatabaseContext so it can be used with TableGateway/SqlContainer.
// - Key features:
//   * Commit() / Rollback() for explicit transaction control
//   * SavepointAsync() / RollbackToSavepointAsync() for partial rollbacks
//   * Auto-rollback on disposal if not committed
//   * Isolation level enforcement (promotes to minimum safe level)
//   * Read-only transaction support
// - Thread-safe: uses internal locks for concurrent access.
// - NOT for use with TransactionScope - pengdows.crud uses its own model.
// - Metrics: tracks transaction duration and commit/rollback counts.
// - CockroachDB note: forces Serializable isolation (only level supported).
// - All database operations within a TransactionContext use the same connection,
//   which is the key difference from non-transactional operations.
// =============================================================================

#region

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using pengdows.crud.@internal;
using pengdows.crud.metrics;

#endregion

namespace pengdows.crud;

/// <summary>
/// Represents an active database transaction with commit/rollback control and savepoint support.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Creation:</strong> Always create via <see cref="IDatabaseContext.BeginTransaction"/>
/// rather than direct instantiation.
/// </para>
/// <para>
/// <strong>Behavior:</strong> The transaction holds a pinned database connection for its entire
/// lifetime. All operations performed through this context use that same connection.
/// </para>
/// <para>
/// <strong>Cleanup:</strong> If the transaction is disposed without calling <see cref="ITransactionContext.Commit"/>,
/// it will be automatically rolled back.
/// </para>
/// <para>
/// <strong>Savepoints:</strong> Use <see cref="ITransactionContext.SavepointAsync"/> and
/// <see cref="ITransactionContext.RollbackToSavepointAsync"/> for partial rollback scenarios.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// await using var tx = await context.BeginTransaction();
/// try
/// {
///     await gateway.CreateAsync(entity1);
///     await gateway.CreateAsync(entity2);
///     await tx.Commit();
/// }
/// catch
/// {
///     // Auto-rollback on dispose if Commit() wasn't called
///     throw;
/// }
/// </code>
/// </example>
/// <seealso cref="ITransactionContext"/>
/// <seealso cref="IDatabaseContext.BeginTransaction"/>
public class TransactionContext : ContextBase, ITransactionContext, IContextIdentity, ISqlDialectProvider,
    IMetricsCollectorAccessor, IInternalConnectionProvider
{
    private const int CompletionLockTimeoutSeconds = 30;

    private readonly ITrackedConnection _connection;
    private readonly IDatabaseContext _context;
    private readonly ISqlDialect _dialect;
    private readonly ILogger<TransactionContext> _logger;
    private readonly SemaphoreSlim _userLock;
    private readonly SemaphoreSlim _completionLock;
    private readonly IDbTransaction _transaction;
    private readonly IsolationLevel _resolvedIsolationLevel;
    private readonly bool _isReadOnly;
    private readonly MetricsCollector? _metricsCollector;
    private readonly MetricsCollector? _readMetricsCollector;
    private readonly MetricsCollector? _writeMetricsCollector;
    private readonly long _transactionMetricsStart;
    private int _metricsCompleted;

    private int _committed; // 0 = no, 1 = yes
    private int _rolledBack; // 0 = no, 1 = yes
    private int _completedState;

    /// <inheritdoc/>
    public Guid RootId { get; }

    private TransactionContext(
        IDatabaseContext context,
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        ExecutionType? executionType = null,
        bool isReadOnly = false,
        ILogger<TransactionContext>? logger = null)
    {
        _logger = logger ?? new NullLogger<TransactionContext>();
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _isReadOnly = isReadOnly;
        var provider = context as ISqlDialectProvider
                       ?? throw new InvalidOperationException(
                           "IDatabaseContext must implement ISqlDialectProvider and expose a non-null Dialect.");
        _dialect = provider.Dialect;
        RootId = ((IContextIdentity)_context).RootId;
        var metricsAccessor = context as IMetricsCollectorAccessor;
        _metricsCollector = metricsAccessor?.MetricsCollector;
        _readMetricsCollector = metricsAccessor?.ReadMetricsCollector;
        _writeMetricsCollector = metricsAccessor?.WriteMetricsCollector;
        if (_metricsCollector != null)
        {
            _transactionMetricsStart = _metricsCollector.TransactionStarted();
        }

        executionType ??= _context.IsReadOnlyConnection || _isReadOnly ? ExecutionType.Read : ExecutionType.Write;

        if ((_context.IsReadOnlyConnection || _isReadOnly) && executionType != ExecutionType.Read)
        {
            throw new NotSupportedException("DatabaseContext is read-only");
        }

        if (_context.Product == SupportedDatabase.CockroachDb)
        {
            isolationLevel = IsolationLevel.Serializable;
        }

        // Defer all connection routing to the configured connection strategy.
        // The strategy (and DatabaseContext helpers it uses) handle in-memory,
        // SingleConnection, and SingleWriter cases.
        if (_context is not IInternalConnectionProvider connectionProvider)
        {
            throw new InvalidOperationException("IDatabaseContext must provide internal connection access.");
        }

        _connection = connectionProvider.GetConnection(executionType.Value, true);
        EnsureConnectionIsOpen();
        _userLock = new SemaphoreSlim(1, 1);
        _completionLock = new SemaphoreSlim(1, 1);

        _resolvedIsolationLevel = isolationLevel;

        // DuckDB's ADO.NET provider rejects explicit IsolationLevel values. Use provider default,
        // but preserve the resolved isolation level for reporting and logic.
        _transaction = _context.Product == SupportedDatabase.DuckDB
            ? _connection.BeginTransaction()
            : _connection.BeginTransaction(isolationLevel);

        if (_isReadOnly)
        {
            _dialect.TryEnterReadOnlyTransaction(this);
        }
    }

    /// <inheritdoc/>
    public Guid TransactionId { get; } = Guid.NewGuid();

    internal IDbTransaction Transaction => _transaction;

    /// <inheritdoc/>
    public bool WasCommitted => Interlocked.CompareExchange(ref _completedState, 0, 0) != 0
                                && Interlocked.CompareExchange(ref _committed, 0, 0) != 0;

    /// <inheritdoc/>
    public bool WasRolledBack => Interlocked.CompareExchange(ref _completedState, 0, 0) != 0
                                 && Interlocked.CompareExchange(ref _rolledBack, 0, 0) != 0;

    /// <inheritdoc/>
    public bool IsCompleted => Interlocked.CompareExchange(ref _completedState, 0, 0) != 0;

    /// <inheritdoc/>
    public IsolationLevel IsolationLevel => _resolvedIsolationLevel;

    /// <inheritdoc/>
    public long NumberOfOpenConnections => _context.NumberOfOpenConnections;

    /// <inheritdoc/>
    public SupportedDatabase Product => _context.Product;

    /// <inheritdoc/>
    public long PeakOpenConnections => _context.PeakOpenConnections;

    /// <inheritdoc/>
    public bool IsReadOnlyConnection => _context.IsReadOnlyConnection || _isReadOnly;

    /// <inheritdoc/>
    public bool RCSIEnabled => _context.RCSIEnabled;

    /// <inheritdoc/>
    public bool SnapshotIsolationEnabled => _context.SnapshotIsolationEnabled;

    /// <inheritdoc/>
    public string ConnectionString => _context.ConnectionString;

    /// <inheritdoc/>
    public string Name
    {
        get => _context.Name;
        set => _context.Name = value;
    }

    /// <inheritdoc/>
    public ReadWriteMode ReadWriteMode => _context.ReadWriteMode;

    /// <inheritdoc/>
    public DbDataSource? DataSource => _context.DataSource;

    /// <inheritdoc/>
    public int MaxParameterLimit => _context.MaxParameterLimit;

    /// <inheritdoc/>
    public DbMode ConnectionMode => _context.ConnectionMode;

    /// <inheritdoc/>
    public ITypeMapRegistry TypeMapRegistry => _context.TypeMapRegistry;

    /// <inheritdoc/>
    public IDataSourceInformation DataSourceInfo => _context.DataSourceInfo;

    /// <inheritdoc/>
    public string SessionSettingsPreamble => _context.SessionSettingsPreamble;

    /// <inheritdoc/>
    public DatabaseMetrics Metrics => _context.Metrics;

    /// <inheritdoc/>
    public event EventHandler<DatabaseMetrics> MetricsUpdated
    {
        add => _context.MetricsUpdated += value;
        remove => _context.MetricsUpdated -= value;
    }

    /// <inheritdoc/>
    public ILockerAsync GetLock()
    {
        ThrowIfDisposed();
        if (IsCompleted)
        {
            throw new InvalidOperationException("Transaction already completed.");
        }

        return new RealAsyncLocker(_userLock);
    }

    protected override void ValidateCanCreateContainer()
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException("Cannot create a SQL container because the transaction is completed.");
        }
    }

    protected override ILogger<ISqlContainer>? ResolveSqlContainerLogger()
    {
        return _context is DatabaseContext dbCtx ? dbCtx.CreateSqlContainerLogger() : null;
    }

    /// <inheritdoc/>
    internal ITrackedConnection GetConnection(ExecutionType type, bool isShared = false)
    {
        return _connection;
    }

    ITrackedConnection IInternalConnectionProvider.GetConnection(ExecutionType executionType, bool isShared)
    {
        return GetConnection(executionType, isShared);
    }

    /// <inheritdoc/>
    public void AssertIsReadConnection()
    {
        _context.AssertIsReadConnection();
    }

    /// <inheritdoc/>
    public void AssertIsWriteConnection()
    {
        if (_isReadOnly)
        {
            throw new InvalidOperationException("Transaction is read-only.");
        }

        _context.AssertIsWriteConnection();
    }

    /// <inheritdoc/>
    public bool? ForceManualPrepare => _context.ForceManualPrepare;

    /// <inheritdoc/>
    public bool? DisablePrepare => _context.DisablePrepare;

    MetricsCollector? IMetricsCollectorAccessor.MetricsCollector => _metricsCollector;
    MetricsCollector? IMetricsCollectorAccessor.ReadMetricsCollector => _readMetricsCollector;
    MetricsCollector? IMetricsCollectorAccessor.WriteMetricsCollector => _writeMetricsCollector;
    MetricsCollector? IMetricsCollectorAccessor.GetMetricsCollector(ExecutionType executionType)
    {
        return executionType == ExecutionType.Read ? _readMetricsCollector : _writeMetricsCollector;
    }

    /// <inheritdoc/>
    public ProcWrappingStyle ProcWrappingStyle => _context.ProcWrappingStyle;

    ProcWrappingStyle IDatabaseContext.ProcWrappingStyle => _context.ProcWrappingStyle;

    ITransactionContext IDatabaseContext.BeginTransaction(IsolationProfile isolationProfile,
        ExecutionType executionType, bool? readOnly)
    {
        throw new InvalidOperationException("Cannot begin a nested transaction from TransactionContext.");
    }

    ITransactionContext IDatabaseContext.BeginTransaction(IsolationLevel? isolationLevel, ExecutionType executionType,
        bool? readOnly)
    {
        throw new InvalidOperationException("Cannot begin a nested transaction from TransactionContext.");
    }

    /// <inheritdoc/>
    public void CloseAndDisposeConnection(ITrackedConnection? conn)
    {
        ThrowIfDisposed();
        if (conn is null)
        {
            return;
        }

        if (ReferenceEquals(conn, _connection))
        {
            return;
        }

        _context.CloseAndDisposeConnection(conn);
    }

    /// <inheritdoc/>
    public ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? conn)
    {
        ThrowIfDisposed();
        if (conn is null)
        {
            return ValueTask.CompletedTask;
        }

        if (ReferenceEquals(conn, _connection))
        {
            return ValueTask.CompletedTask;
        }

        return _context.CloseAndDisposeConnectionAsync(conn);
    }

    /// <inheritdoc/>
    public void Commit()
    {
        ThrowIfDisposed();
        // Use async core for consistent semaphore behavior
        CommitAsync().GetAwaiter().GetResult();
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return CompleteTransactionWithWaitAsync(() =>
        {
            _transaction.Commit();
            return Task.CompletedTask;
        }, true, cancellationToken);
    }

    /// <inheritdoc/>
    public void Rollback()
    {
        ThrowIfDisposed();
        RollbackAsync().GetAwaiter().GetResult();
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return CompleteTransactionWithWaitAsync(() =>
        {
            _transaction.Rollback();
            return Task.CompletedTask;
        }, false, cancellationToken);
    }

    /// <inheritdoc/>
    public Task SavepointAsync(string name)
    {
        return SavepointAsync(name, default);
    }

    public async Task SavepointAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_dialect.SupportsSavepoints)
        {
            return;
        }

        using var cmd = _connection.CreateCommand();
        cmd.Transaction = _transaction;
        cmd.CommandText = _dialect.GetSavepointSql(name);
        if (cmd is DbCommand db)
        {
            await db.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            cmd.ExecuteNonQuery();
        }
    }

    /// <inheritdoc/>
    public Task RollbackToSavepointAsync(string name)
    {
        return RollbackToSavepointAsync(name, default);
    }

    public async Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_dialect.SupportsSavepoints)
        {
            return;
        }

        using var cmd = _connection.CreateCommand();
        cmd.Transaction = _transaction;
        cmd.CommandText = _dialect.GetRollbackToSavepointSql(name);
        if (cmd is DbCommand db)
        {
            await db.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            cmd.ExecuteNonQuery();
        }
    }

    private void CompleteTransactionWithWait(Action action, bool markCommitted)
    {
        // Use internal completion lock only; do not contend with user lock
        if (!_completionLock.Wait(TimeSpan.FromSeconds(CompletionLockTimeoutSeconds)))
        {
            throw new InvalidOperationException("Transaction completion timed out waiting for internal lock.");
        }

        try
        {
            CompleteTransaction(action, markCommitted);
        }
        finally
        {
            _completionLock.Release();
        }
    }

    private async Task CompleteTransactionWithWaitAsync(Func<Task> action, bool markCommitted,
        CancellationToken cancellationToken = default)
    {
        await _completionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CompleteTransactionAsync(action, markCommitted).ConfigureAwait(false);
        }
        finally
        {
            _completionLock.Release();
        }
    }

    private void CompleteTransaction(Action action, bool markCommitted)
    {
        if (Interlocked.Exchange(ref _completedState, 1) != 0)
        {
            throw new InvalidOperationException("Transaction already completed.");
        }

        action();

        if (markCommitted)
        {
            Interlocked.Exchange(ref _committed, 1);
        }
        else
        {
            Interlocked.Exchange(ref _rolledBack, 1);
        }

        _context.CloseAndDisposeConnection(_connection);
        CompleteTransactionMetrics();
    }

    private async Task CompleteTransactionAsync(Func<Task> action, bool markCommitted)
    {
        if (Interlocked.Exchange(ref _completedState, 1) != 0)
        {
            throw new InvalidOperationException("Transaction already completed.");
        }

        await action().ConfigureAwait(false);

        if (markCommitted)
        {
            Interlocked.Exchange(ref _committed, 1);
        }
        else
        {
            Interlocked.Exchange(ref _rolledBack, 1);
        }

        await _context.CloseAndDisposeConnectionAsync(_connection).ConfigureAwait(false);
        CompleteTransactionMetrics();
    }

    private void CompleteTransactionMetrics()
    {
        if (_metricsCollector == null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _metricsCompleted, 1) != 0)
        {
            return;
        }

        _metricsCollector.TransactionCompleted(_transactionMetricsStart);
    }

    // Kept for backward compatibility with existing internal calls
    private Task RollbackAsync()
    {
        return RollbackAsync(default);
    }

    protected override void DisposeManaged()
    {
        if (!IsCompleted)
        {
            try
            {
                // Attempt immediate rollback using internal completion lock
                if (_completionLock.Wait(0))
                {
                    try
                    {
                        CompleteTransaction(() => _transaction.Rollback(), false);
                    }
                    finally
                    {
                        _completionLock.Release();
                    }
                }
                else
                {
                    _logger.LogError("TransactionContext.Dispose could not acquire lock; skipping explicit rollback.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rollback failed during Dispose.");
            }
        }

        _transaction.Dispose();
        _userLock.Dispose();
        _completionLock.Dispose();
        CompleteTransactionMetrics();
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        if (!IsCompleted)
        {
            try
            {
                // Avoid any wait on disposal to prevent hangs; rely on provider dispose if busy
                var acquired = _completionLock.Wait(0);
                if (acquired)
                {
                    try
                    {
                        await CompleteTransactionAsync(() =>
                        {
                            _transaction.Rollback();
                            return Task.CompletedTask;
                        }, false).ConfigureAwait(false);
                    }
                    finally
                    {
                        _completionLock.Release();
                    }
                }
                else
                {
                    _logger.LogError(
                        "TransactionContext.DisposeAsync could not acquire lock; skipping explicit rollback.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rollback failed during DisposeAsync.");
            }
        }

        if (_transaction is IAsyncDisposable asyncTx)
        {
            await asyncTx.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _transaction.Dispose();
        }

        _userLock.Dispose();
        _completionLock.Dispose();
        CompleteTransactionMetrics();
    }

    private void EnsureConnectionIsOpen()
    {
        if (_connection.State != ConnectionState.Open)
        {
            _connection.Open();
        }
    }

    protected override ISqlDialect DialectCore => _dialect;

    // Internal factory used by DatabaseContext
    internal static TransactionContext Create(
        IDatabaseContext context,
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        ExecutionType? executionType = null,
        bool isReadOnly = false,
        ILogger<TransactionContext>? logger = null)
    {
        return new TransactionContext(context, isolationLevel, executionType, isReadOnly, logger);
    }
}
