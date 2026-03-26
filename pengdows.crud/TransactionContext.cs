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

using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using pengdows.crud.@internal;
using pengdows.crud.metrics;

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
    IMetricsCollectorAccessor, IInternalConnectionProvider, ITypeMapAccessor
{
    private readonly ITrackedConnection _connection;
    private readonly IDatabaseContext _context;
    private readonly ISqlDialect _dialect;
    private readonly ILogger<TransactionContext> _logger;
    private readonly SemaphoreSlim _userLock;
    private readonly ReusableAsyncLocker _reusableLocker;
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

    /// <summary>
    /// Common initialization shared by sync and async creation paths.
    /// Returns the resolved execution type, isolation level, and connection provider.
    /// </summary>
    private static (ExecutionType executionType, IsolationLevel isolationLevel, IInternalConnectionProvider provider)
        ResolveCreationParameters(
            IDatabaseContext context,
            IsolationLevel isolationLevel,
            ExecutionType? executionType)
    {
        executionType ??= context.IsReadOnlyConnection ? ExecutionType.Read : ExecutionType.Write;

        if (context.IsReadOnlyConnection && executionType != ExecutionType.Read)
        {
            throw new NotSupportedException("DatabaseContext is read-only");
        }
        if (context.Product == SupportedDatabase.CockroachDb)
        {
            isolationLevel = IsolationLevel.Serializable;
        }

        if (context is not IInternalConnectionProvider connectionProvider)
        {
            throw new InvalidOperationException("IDatabaseContext must provide internal connection access.");
        }

        return (executionType.Value, isolationLevel, connectionProvider);
    }

    /// <summary>
    /// Initializes fields common to both sync and async creation.
    /// </summary>
    private TransactionContext(
        IDatabaseContext context,
        ITrackedConnection connection,
        IDbTransaction transaction,
        IsolationLevel isolationLevel,
        ExecutionType executionType,
        ILogger<TransactionContext>? logger)
    {
        _logger = logger ?? new NullLogger<TransactionContext>();
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _isReadOnly = executionType == ExecutionType.Read;
        _dialect = context.GetDialect();
        RootId = ((IContextIdentity)_context).RootId;
        Name = _context.Name;
        var metricsAccessor = context as IMetricsCollectorAccessor;
        _metricsCollector = metricsAccessor?.MetricsCollector;
        _readMetricsCollector = metricsAccessor?.ReadMetricsCollector;
        _writeMetricsCollector = metricsAccessor?.WriteMetricsCollector;
        if (_metricsCollector != null)
        {
            _transactionMetricsStart = _metricsCollector.TransactionStarted();
        }

        _connection = connection;
        _transaction = transaction;
        _resolvedIsolationLevel = isolationLevel;
        _userLock = new SemaphoreSlim(1, 1);
        _reusableLocker = new ReusableAsyncLocker(_userLock);
        _completionLock = new SemaphoreSlim(1, 1);
    }

    private TransactionContext(
        IDatabaseContext context,
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        ExecutionType? executionType = null,
        ILogger<TransactionContext>? logger = null)
        : this(context,
            CreateConnectionAndTransaction(context, ref isolationLevel, ref executionType,
                out var transaction),
            transaction,
            isolationLevel,
            executionType!.Value,
            logger)
    {
        if (_isReadOnly)
        {
            try
            {
                _dialect.TryEnterReadOnlyTransaction(this);
            }
            catch
            {
                _transaction.Rollback();
                _context.CloseAndDisposeConnection(_connection);
                throw;
            }
        }
    }

    /// <summary>
    /// Helper for the sync constructor chain — resolves parameters, gets connection,
    /// opens it, and begins the transaction. Returns the connection; outputs the transaction.
    /// </summary>
    private static ITrackedConnection CreateConnectionAndTransaction(
        IDatabaseContext context,
        ref IsolationLevel isolationLevel,
        ref ExecutionType? executionType,
        out IDbTransaction transaction)
    {
        var (resolvedExecType, resolvedIsolation, connectionProvider) =
            ResolveCreationParameters(context, isolationLevel, executionType);
        executionType = resolvedExecType;
        isolationLevel = resolvedIsolation;

        // isShared=false: The TransactionContext's own _userLock serializes all operations
        // on this pinned connection. A second lock on the connection itself would be redundant
        // double-locking that adds measurable overhead (e.g., WriteStorm scenarios).
        var connection = connectionProvider.GetConnection(resolvedExecType, false);
        OpenConnectionWithOptionalLock(context, connection);

        // DuckDB's ADO.NET provider rejects explicit IsolationLevel values. Use provider default,
        // but preserve the resolved isolation level for reporting and logic.
        try
        {
            transaction = context.Product == SupportedDatabase.DuckDB
                ? connection.BeginTransaction()
                : connection.BeginTransaction(resolvedIsolation);
        }
        catch (Exception ex)
        {
            context.CloseAndDisposeConnection(connection);
            throw new TransactionException(
                $"Failed to begin transaction on {context.Product}: {ex.Message}",
                context.Product, ex);
        }

        return connection;
    }

    private static void OpenConnectionWithOptionalLock(IDatabaseContext context, ITrackedConnection connection)
    {
        if (connection.State == ConnectionState.Open)
        {
            return;
        }

        if (context is DatabaseContext dbContext && dbContext.RequiresSerializedOpen)
        {
            using var openLock = dbContext.GetConnectionOpenLock();
            openLock.Lock();
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            return;
        }

        connection.Open();
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
    public CommandPrepareMode PrepareMode => _context.PrepareMode;

    /// <inheritdoc/>
    public long PeakOpenConnections => _context.PeakOpenConnections;

    /// <inheritdoc/>
    public bool IsReadOnlyConnection => _context.IsReadOnlyConnection || _isReadOnly;

    /// <inheritdoc/>
    public bool RCSIEnabled => _context.RCSIEnabled;

    /// <inheritdoc/>
    public bool SnapshotIsolationEnabled => _context.SnapshotIsolationEnabled;

    /// <inheritdoc/>
    public IReadOnlySet<IsolationLevel> GetSupportedIsolationLevels() => _context.GetSupportedIsolationLevels();

    /// <inheritdoc/>
    public string ConnectionString => _context.ConnectionString;

    internal string RawConnectionString => InternalConnectionStringAccess.GetRawConnectionString(_context);

    /// <inheritdoc/>
    public string Name { get; init; }

    /// <inheritdoc/>
    public ReadWriteMode ReadWriteMode => _context.ReadWriteMode;

    /// <inheritdoc/>
    public DbDataSource? DataSource => _context.DataSource;

    /// <inheritdoc/>
    public int MaxParameterLimit => _context.MaxParameterLimit;

    /// <inheritdoc/>
    public DbMode ConnectionMode => _context.ConnectionMode;

    ITypeMapRegistry ITypeMapAccessor.TypeMapRegistry =>
        (_context as ITypeMapAccessor)?.TypeMapRegistry ??
        throw new InvalidOperationException("IDatabaseContext must expose a TypeMapRegistry.");

    /// <inheritdoc/>
    public IDataSourceInformation DataSourceInfo => _context.DataSourceInfo;

    /// <inheritdoc/>
    public string GetBaseSessionSettings() => _context.GetBaseSessionSettings();

    /// <inheritdoc/>
    public string GetReadOnlySessionSettings() => _context.GetReadOnlySessionSettings();

    /// <inheritdoc/>
    public DatabaseMetrics Metrics => _context.Metrics;

    /// <inheritdoc/>
    public event EventHandler<DatabaseMetrics> MetricsUpdated
    {
        add => _context.MetricsUpdated += value;
        remove => _context.MetricsUpdated -= value;
    }

    internal ILockerAsync GetLockInternal()
    {
        ThrowIfDisposed();
        if (IsCompleted)
        {
            throw new InvalidOperationException("Transaction already completed.");
        }

        return _reusableLocker;
    }

    ILockerAsync IInternalConnectionProvider.GetLock()
    {
        return GetLockInternal();
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

    internal void ExecuteSessionNonQuery(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    internal async ValueTask ExecuteSessionNonQueryAsync(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        if (cmd is DbCommand db)
        {
            await db.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        else
        {
            cmd.ExecuteNonQuery();
        }
    }

    private void TryResetReadOnlySession()
    {
        if (_isReadOnly && _dialect is SqlDialect sd)
        {
            var resetSql = sd.GetReadOnlyTransactionResetSql();
            if (!string.IsNullOrEmpty(resetSql))
            {
                try
                {
                    ExecuteSessionNonQuery(resetSql);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to reset read-only session settings.");
                }
            }
        }
    }

    private async ValueTask TryResetReadOnlySessionAsync()
    {
        if (_isReadOnly && _dialect is SqlDialect sd)
        {
            var resetSql = sd.GetReadOnlyTransactionResetSql();
            if (!string.IsNullOrEmpty(resetSql))
            {
                try
                {
                    await ExecuteSessionNonQueryAsync(resetSql).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to reset read-only session settings.");
                }
            }
        }
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

    internal void AssertIsReadConnection()
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
        ExecutionType executionType)
    {
        throw new InvalidOperationException("Cannot begin a nested transaction from TransactionContext.");
    }

    ITransactionContext IDatabaseContext.BeginTransaction(IsolationLevel? isolationLevel, ExecutionType executionType)
    {
        throw new InvalidOperationException("Cannot begin a nested transaction from TransactionContext.");
    }

    ValueTask<ITransactionContext> IDatabaseContext.BeginTransactionAsync(IsolationLevel? isolationLevel,
        ExecutionType executionType, CancellationToken cancellationToken)
    {
        return ValueTask.FromException<ITransactionContext>(
            new InvalidOperationException("Cannot begin a nested transaction from TransactionContext."));
    }

    ValueTask<ITransactionContext> IDatabaseContext.BeginTransactionAsync(IsolationProfile isolationProfile,
        ExecutionType executionType, CancellationToken cancellationToken)
    {
        return ValueTask.FromException<ITransactionContext>(
            new InvalidOperationException("Cannot begin a nested transaction from TransactionContext."));
    }

    private void CloseAndDisposeConnectionInternal(ITrackedConnection? conn)
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

    private ValueTask CloseAndDisposeConnectionAsyncInternal(ITrackedConnection? conn)
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

    void IInternalConnectionProvider.CloseAndDisposeConnection(ITrackedConnection? conn)
    {
        CloseAndDisposeConnectionInternal(conn);
    }

    ValueTask IInternalConnectionProvider.CloseAndDisposeConnectionAsync(ITrackedConnection? conn)
    {
        return CloseAndDisposeConnectionAsyncInternal(conn);
    }

    /// <inheritdoc/>
    public void Commit()
    {
        ThrowIfDisposed();
        // Use async core for consistent semaphore behavior
        CommitAsync().GetAwaiter().GetResult();
    }

    public ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return CompleteTransactionWithWaitAsync(() =>
        {
            _transaction.Commit();
            return ValueTask.CompletedTask;
        }, true, cancellationToken);
    }

    /// <inheritdoc/>
    public void Rollback()
    {
        ThrowIfDisposed();
        RollbackAsync().GetAwaiter().GetResult();
    }

    public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return CompleteTransactionWithWaitAsync(() =>
        {
            _transaction.Rollback();
            return ValueTask.CompletedTask;
        }, false, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask SavepointAsync(string name)
    {
        return SavepointAsync(name, default);
    }

    public async ValueTask SavepointAsync(string name, CancellationToken cancellationToken = default)
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
    public ValueTask RollbackToSavepointAsync(string name)
    {
        return RollbackToSavepointAsync(name, default);
    }

    public async ValueTask RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default)
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
        if (!_completionLock.Wait(_context.ModeLockTimeout ?? Timeout.InfiniteTimeSpan))
        {
            throw new InvalidOperationException("Transaction completion timed out waiting for internal lock.");
        }

        try
        {
            CompleteTransaction(action, markCommitted);
        }
        finally
        {
            // Guard against ObjectDisposedException: if Dispose() races with this Release()
            // it may have already called _completionLock.Dispose(). Swallowing the exception
            // here is safe — the connection was already closed in CompleteTransaction.finally.
            try { _completionLock.Release(); }
            catch (ObjectDisposedException) { }
        }
    }

    private async ValueTask CompleteTransactionWithWaitAsync(Func<ValueTask> action, bool markCommitted,
        CancellationToken cancellationToken = default)
    {
        if (!await _completionLock.WaitAsync(_context.ModeLockTimeout ?? Timeout.InfiniteTimeSpan, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException("Transaction completion timed out waiting for internal lock.");
        }

        try
        {
            await CompleteTransactionAsync(action, markCommitted).ConfigureAwait(false);
        }
        finally
        {
            // Guard against ObjectDisposedException: same race as the sync path.
            try { _completionLock.Release(); }
            catch (ObjectDisposedException) { }
        }
    }

    private void CompleteTransaction(Action action, bool markCommitted)
    {
        if (Interlocked.Exchange(ref _completedState, 1) != 0)
        {
            throw new InvalidOperationException("Transaction already completed.");
        }

        try
        {
            action();

            if (markCommitted)
            {
                Interlocked.Exchange(ref _committed, 1);
            }
            else
            {
                Interlocked.Exchange(ref _rolledBack, 1);
            }
        }
        catch (Exception ex)
        {
            // Do NOT reset _completedState — connection is already closed in finally.
            // Leaving it as 1 (completed) prevents Dispose from attempting rollback on a dead connection.
            throw new TransactionException(
                $"Transaction {(markCommitted ? "commit" : "rollback")} failed on {_context.Product}: {ex.Message}",
                _context.Product, ex);
        }
        finally
        {
            TryResetReadOnlySession();
            _context.CloseAndDisposeConnection(_connection);
            CompleteTransactionMetrics();
        }
    }

    private async ValueTask CompleteTransactionAsync(Func<ValueTask> action, bool markCommitted)
    {
        if (Interlocked.Exchange(ref _completedState, 1) != 0)
        {
            throw new InvalidOperationException("Transaction already completed.");
        }

        try
        {
            await action().ConfigureAwait(false);

            if (markCommitted)
            {
                Interlocked.Exchange(ref _committed, 1);
            }
            else
            {
                Interlocked.Exchange(ref _rolledBack, 1);
            }
        }
        catch (Exception ex)
        {
            // Do NOT reset _completedState — connection is already closed in finally.
            // Leaving it as 1 (completed) prevents Dispose from attempting rollback on a dead connection.
            throw new TransactionException(
                $"Transaction {(markCommitted ? "commit" : "rollback")} failed on {_context.Product}: {ex.Message}",
                _context.Product, ex);
        }
        finally
        {
            await TryResetReadOnlySessionAsync().ConfigureAwait(false);
            await _context.CloseAndDisposeConnectionAsync(_connection).ConfigureAwait(false);
            CompleteTransactionMetrics();
        }
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

        if (Volatile.Read(ref _committed) == 1)
        {
            _metricsCollector.TransactionCommitted(_transactionMetricsStart);
        }
        else
        {
            _metricsCollector.TransactionRolledBack(_transactionMetricsStart);
        }
    }

    // Kept for backward compatibility with existing internal calls
    private ValueTask RollbackAsync()
    {
        return RollbackAsync(default);
    }

    protected override void DisposeManaged()
    {
        var shouldDisposeLock = true;
        if (!IsCompleted)
        {
            try
            {
                // Attempt immediate rollback using internal completion lock.
                if (_completionLock.Wait(0))
                {
                    try
                    {
                        CompleteTransaction(() => _transaction.Rollback(), false);
                    }
                    finally
                    {
                        try { _completionLock.Release(); }
                        catch (ObjectDisposedException) { shouldDisposeLock = false; }
                    }
                }
                else
                {
                    // Another thread is completing the transaction and still holds the lock.
                    // It will close the connection via CompleteTransaction.finally.
                    // Do NOT dispose _completionLock here — the other thread still holds it
                    // and its Release() would throw ObjectDisposedException.
                    shouldDisposeLock = false;
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
        if (shouldDisposeLock)
        {
            _completionLock.Dispose();
        }
        CompleteTransactionMetrics();
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        var shouldDisposeLock = true;
        if (!IsCompleted)
        {
            try
            {
                // Avoid any wait on disposal to prevent hangs; rely on provider dispose if busy.
                var acquired = _completionLock.Wait(0);
                if (acquired)
                {
                    try
                    {
                        await CompleteTransactionAsync(() =>
                        {
                            _transaction.Rollback();
                            return ValueTask.CompletedTask;
                        }, false).ConfigureAwait(false);
                    }
                    finally
                    {
                        try { _completionLock.Release(); }
                        catch (ObjectDisposedException) { shouldDisposeLock = false; }
                    }
                }
                else
                {
                    // Another thread is completing the transaction and still holds the lock.
                    // It will close the connection via CompleteTransaction.finally.
                    // Do NOT dispose _completionLock here — the other thread still holds it
                    // and its Release() would throw ObjectDisposedException.
                    shouldDisposeLock = false;
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
        if (shouldDisposeLock)
        {
            _completionLock.Dispose();
        }
        CompleteTransactionMetrics();
    }

    protected override ISqlDialect DialectCore => _dialect;

    /// <inheritdoc/>
    public new ISqlDialect Dialect => _dialect;

    ISqlDialect ISqlDialectProvider.Dialect => _dialect;

    /// <inheritdoc />
    public TimeSpan? ModeLockTimeout => _context.ModeLockTimeout;

    // Internal factory used by DatabaseContext
    internal static TransactionContext Create(
        IDatabaseContext context,
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        ExecutionType? executionType = null,
        ILogger<TransactionContext>? logger = null)
    {
        return new TransactionContext(context, isolationLevel, executionType, logger);
    }

    // Internal async factory used by DatabaseContext
    internal static async ValueTask<TransactionContext> CreateAsync(
        IDatabaseContext context,
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        ExecutionType? executionType = null,
        ILogger<TransactionContext>? logger = null,
        CancellationToken cancellationToken = default)
    {
        var (resolvedExecType, resolvedIsolation, connectionProvider) =
            ResolveCreationParameters(context, isolationLevel, executionType);

        var connection = connectionProvider.GetConnection(resolvedExecType, false);
        await OpenConnectionWithOptionalLockAsync(context, connection, cancellationToken).ConfigureAwait(false);

        IDbTransaction transaction;
        try
        {
            transaction = context.Product == SupportedDatabase.DuckDB
                ? await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : await connection.BeginTransactionAsync(resolvedIsolation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await context.CloseAndDisposeConnectionAsync(connection).ConfigureAwait(false);
            throw new TransactionException(
                $"Failed to begin transaction on {context.Product}: {ex.Message}",
                context.Product, ex);
        }

        var tx = new TransactionContext(context, connection, transaction, resolvedIsolation, resolvedExecType, logger);

        if (tx.IsReadOnlyConnection)
        {
            try
            {
                await tx._dialect.TryEnterReadOnlyTransactionAsync(tx, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Release the pinned connection and roll back — do NOT dispose the parent context,
                // which is a singleton that must remain usable after a failed BeginTransactionAsync.
                // (The sync constructor path only closes the connection; this matches that behaviour.)
                await tx.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        return tx;
    }

    private static async ValueTask OpenConnectionWithOptionalLockAsync(IDatabaseContext context,
        ITrackedConnection connection,
        CancellationToken cancellationToken)
    {
        if (connection.State == ConnectionState.Open)
        {
            return;
        }

        if (context is DatabaseContext dbContext && dbContext.RequiresSerializedOpen)
        {
            await using var openLock = dbContext.GetConnectionOpenLock();
            await openLock.LockAsync(cancellationToken).ConfigureAwait(false);
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

}
