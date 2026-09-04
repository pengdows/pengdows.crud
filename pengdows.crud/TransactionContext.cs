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
    private readonly ILockerAsync _singleConnectionTransactionGate;

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
        ILogger<TransactionContext>? logger,
        ILockerAsync singleConnectionTransactionGate)
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

        // Already acquired (or NoOpAsyncLocker.Instance if not applicable) by the static creation
        // path before this constructor ran — this instance owns releasing it on completion.
        _singleConnectionTransactionGate = singleConnectionTransactionGate;
    }

    private TransactionContext(
        IDatabaseContext context,
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        ExecutionType? executionType = null,
        ILogger<TransactionContext>? logger = null)
        : this(context,
            CreateConnectionAndTransaction(context, ref isolationLevel, ref executionType,
                out var transaction, out var singleConnectionTransactionGate),
            transaction,
            isolationLevel,
            executionType!.Value,
            logger,
            singleConnectionTransactionGate)
    {
        if (_isReadOnly)
        {
            try
            {
                _dialect.TryEnterReadOnlyTransaction(this);
            }
            catch
            {
                // Fully tear down (rollback, close connection, dispose locks, finalize metrics) —
                // mirrors the async CreateAsync failure path (tx.DisposeAsync()). Do NOT dispose
                // the parent context, which is a singleton that must remain usable.
                Dispose();
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
        out IDbTransaction transaction,
        out ILockerAsync singleConnectionTransactionGate)
    {
        var (resolvedExecType, resolvedIsolation, connectionProvider) =
            ResolveCreationParameters(context, isolationLevel, executionType);
        executionType = resolvedExecType;
        isolationLevel = resolvedIsolation;

        // isShared=false: The TransactionContext's own _userLock serializes all operations
        // on this pinned connection. A second lock on the connection itself would be redundant
        // double-locking that adds measurable overhead (e.g., WriteStorm scenarios).
        var connection = connectionProvider.GetConnection(resolvedExecType, false);
        try
        {
            OpenConnectionWithOptionalLock(context, connection);
        }
        catch
        {
            // Open() can throw (e.g. ConnectionException from ExecuteSessionSettings under
            // SessionInitializationFailureMode.FailClosed) before the transaction-begin try/catch
            // below ever starts — without this, the connection's already-acquired PoolGovernor slot
            // is never released, leaking one slot per failure.
            context.CloseAndDisposeConnection(connection);
            throw;
        }

        var gate = AcquireSingleConnectionTransactionGate(context);

        // Some providers (DuckDB) reject an explicit IsolationLevel value. Use the provider
        // default there, but preserve the resolved isolation level for reporting and logic.
        try
        {
            transaction = context.Dialect.RejectsExplicitIsolationLevelOnBeginTransaction
                ? connection.BeginTransaction()
                : connection.BeginTransaction(resolvedIsolation);
        }
        catch (Exception ex)
        {
            gate.Dispose();
            context.CloseAndDisposeConnection(connection);
            throw new TransactionException(
                $"Failed to begin transaction on {context.Product}: {ex.Message}",
                context.Product, ex);
        }

        singleConnectionTransactionGate = gate;
        return connection;
    }

    /// <summary>
    /// DbMode.SingleConnection shares one physical connection across the entire context. Acquires
    /// the dedicated single-connection transaction gate (see
    /// <c>DatabaseContext.GetSingleConnectionTransactionGate()</c>) so this transaction has
    /// exclusive use of the connection until it completes — every other caller (another
    /// transaction attempt, or an ordinary non-transactional command) correctly waits its turn.
    /// Bounded by <c>ModeLockTimeout</c>; a no-op for every other mode.
    /// </summary>
    private static ILockerAsync AcquireSingleConnectionTransactionGate(IDatabaseContext context)
    {
        if (context is not DatabaseContext dbContext)
        {
            return NoOpAsyncLocker.Instance;
        }

        var gate = dbContext.GetSingleConnectionTransactionGate();
        gate.Lock();
        return gate;
    }

    private static async ValueTask<ILockerAsync> AcquireSingleConnectionTransactionGateAsync(
        IDatabaseContext context, CancellationToken cancellationToken)
    {
        if (context is not DatabaseContext dbContext)
        {
            return NoOpAsyncLocker.Instance;
        }

        var gate = dbContext.GetSingleConnectionTransactionGate();
        await gate.LockAsync(cancellationToken).ConfigureAwait(false);
        return gate;
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
    public int MaxParameterLimit => _context.MaxParameterLimit;

    /// <inheritdoc/>
    public DbMode ConnectionMode => _context.ConnectionMode;

    ITypeMapRegistry ITypeMapAccessor.TypeMapRegistry =>
        (_context as ITypeMapAccessor)?.TypeMapRegistry ??
        throw new InvalidOperationException("IDatabaseContext must expose a TypeMapRegistry.");

    /// <inheritdoc/>
    public IDataSourceInformation DataSourceInfo => _context.DataSourceInfo;

    // Compatibility member retained from the 2.0 API.
    public DbDataSource? DataSource => _context.DataSource;

    /// <inheritdoc/>
    public string GetBaseSessionSettings() => _context.GetBaseSessionSettings();

    /// <inheritdoc/>
    public string GetReadOnlySessionSettings() => _context.GetReadOnlySessionSettings();

    /// <inheritdoc/>
    public DatabaseMetrics Metrics => _context.Metrics;

    /// <inheritdoc/>
    public PoolStatisticsSnapshot GetPoolStatisticsSnapshot(PoolLabel label) => _context.GetPoolStatisticsSnapshot(label);

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

    /// <inheritdoc/>
    internal ValueTask<ITrackedConnection> GetConnectionAsync(ExecutionType type, bool isShared = false,
        CancellationToken cancellationToken = default)
    {
        // A transaction pins a single connection for its whole lifetime -- already acquired,
        // nothing to await here.
        return ValueTask.FromResult(GetConnection(type, isShared));
    }

    ITrackedConnection IInternalConnectionProvider.GetConnection(ExecutionType executionType, bool isShared)
    {
        return GetConnection(executionType, isShared);
    }

    ValueTask<ITrackedConnection> IInternalConnectionProvider.GetConnectionAsync(ExecutionType executionType,
        bool isShared, CancellationToken cancellationToken)
    {
        return GetConnectionAsync(executionType, isShared, cancellationToken);
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
            throw new exceptions.ReadOnlyAccessException("Transaction is read-only.");
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
        ExecutionType executionType, IsolationResolutionPolicy policy)
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
        ExecutionType executionType, CancellationToken cancellationToken, IsolationResolutionPolicy policy)
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
        return CompleteTransactionWithWaitAsync(() => CommitTransactionAsync(cancellationToken), true, cancellationToken);
    }

    /// <summary>
    /// Commits via <see cref="DbTransaction.CommitAsync"/> when the underlying transaction is a
    /// real <see cref="DbTransaction"/> (true for every ADO.NET provider), so a provider with a
    /// genuinely async/cancellable commit gets to use it and the cancellation token actually
    /// reaches the call, instead of always falling back to the plain sync Commit() under an
    /// async facade. Falls back to the sync IDbTransaction.Commit() for the rare non-DbTransaction
    /// implementation (e.g. a hand-rolled test double).
    /// </summary>
    private async ValueTask CommitTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is DbTransaction dbTransaction)
        {
            await dbTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _transaction.Commit();
        }
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
        return CompleteTransactionWithWaitAsync(() => RollbackTransactionAsync(cancellationToken), false, cancellationToken);
    }

    /// <summary>Async counterpart of <see cref="CommitTransactionAsync"/> — see its remarks.</summary>
    private async ValueTask RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is DbTransaction dbTransaction)
        {
            await dbTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _transaction.Rollback();
        }
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

        // CORE-023: a savepoint is a command against the shared connection like any other — it
        // must be serialized through the same reader-aware lock ordinary commands use, so it
        // fails fast (instead of racing a still-open reader on the same connection) exactly like
        // ExecuteNonQueryAsync already does.
        await _reusableLocker.LockAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
        finally
        {
            await _reusableLocker.DisposeAsync().ConfigureAwait(false);
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
            // Unlike SavepointAsync's no-op (creating a savepoint that's never used is harmless),
            // silently no-op-ing a rollback here would let the caller believe partial work was
            // undone when nothing happened — throw instead of lying about the outcome.
            throw new NotSupportedException(
                $"{_context.Product} does not support savepoints; RollbackToSavepointAsync is unavailable.");
        }

        // See SavepointAsync for why this must go through the same reader-aware lock.
        await _reusableLocker.LockAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
        finally
        {
            await _reusableLocker.DisposeAsync().ConfigureAwait(false);
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
            try
            {
                _completionLock.Release();
            }
            catch (ObjectDisposedException)
            {
            }
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
            try
            {
                _completionLock.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void CompleteTransaction(Action action, bool markCommitted)
    {
        // CORE-023: acquire the same reader-aware lock ordinary commands use, before touching
        // any completion state. If a reader opened on this transaction is still active,
        // ReusableAsyncLocker fails fast here (see MarkHeldByActiveReader/
        // ThrowIfBlockedBehindActiveReader) instead of letting completion dispose the
        // transaction/connection out from under it. Held for the entire completion so no other
        // caller can start a new operation on the connection while it is being torn down.
        // Acquired before flipping _completedState so a failed attempt (reader still open)
        // leaves the transaction fully retryable rather than permanently stuck.
        _reusableLocker.Lock();
        try
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
            catch (OperationCanceledException)
            {
                // OperationCanceledException is never wrapped, matching every other execution
                // path in the library — the finally below still runs unconditionally.
                throw;
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
                // Disposing the transaction here — not in DisposeManaged — guarantees it happens
                // exactly once, on whichever thread actually completed it, regardless of whether a
                // concurrent Dispose() lost the _completionLock race (see DisposeManaged).
                _transaction.Dispose();
                _context.CloseAndDisposeConnection(_connection);
                _singleConnectionTransactionGate.Dispose();
                CompleteTransactionMetrics();
            }
        }
        finally
        {
            // TrackDisposeState = false: this only releases the hold acquired above (a no-op if
            // Lock() threw before acquiring), it never permanently disposes the reusable locker.
            _reusableLocker.Dispose();
        }
    }

    private async ValueTask CompleteTransactionAsync(Func<ValueTask> action, bool markCommitted)
    {
        // See CompleteTransaction's sync counterpart for why this lock is acquired first.
        await _reusableLocker.LockAsync().ConfigureAwait(false);
        try
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
            catch (OperationCanceledException)
            {
                // OperationCanceledException is never wrapped, matching every other execution
                // path in the library — the finally below still runs unconditionally.
                throw;
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
                // See CompleteTransaction's sync counterpart for why disposal happens here rather
                // than in DisposeManagedAsync.
                if (_transaction is IAsyncDisposable asyncTx)
                {
                    await asyncTx.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    _transaction.Dispose();
                }

                await _context.CloseAndDisposeConnectionAsync(_connection).ConfigureAwait(false);
                await _singleConnectionTransactionGate.DisposeAsync().ConfigureAwait(false);
                CompleteTransactionMetrics();
            }
        }
        finally
        {
            await _reusableLocker.DisposeAsync().ConfigureAwait(false);
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
        // Delegates to the async implementation (same pattern as Commit()/Rollback() blocking on
        // their own async counterparts) rather than maintaining a second, independently-coded
        // sync copy of this logic — the two had drifted into near-duplicates that both needed
        // updating in lockstep for every fix. Every internal await here uses ConfigureAwait(false),
        // so blocking synchronously carries no captured-SynchronizationContext deadlock risk.
        DisposeManagedAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// CORE-023: disposing _userLock unconditionally would corrupt an active reader still
    /// holding it — the reader's own later cleanup calls Release() on it (via _reusableLocker),
    /// which throws ObjectDisposedException against an already-disposed SemaphoreSlim. Only
    /// dispose it when immediately acquirable (nobody currently holds it); otherwise leave it for
    /// the GC — matching the existing shouldDisposeLock precedent for _completionLock just above.
    /// </summary>
    private void DisposeUserLockUnlessHeld()
    {
        if (_userLock.Wait(0))
        {
            _userLock.Dispose();
        }
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
                        await CompleteTransactionAsync(() => RollbackTransactionAsync(CancellationToken.None), false)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        try
                        {
                            _completionLock.Release();
                        }
                        catch (ObjectDisposedException)
                        {
                            shouldDisposeLock = false;
                        }
                    }
                }
                else
                {
                    // Another thread is completing the transaction and still holds the lock —
                    // it is potentially still inside _transaction.Commit()/Rollback() right now.
                    // It will dispose the transaction and close the connection itself via
                    // CompleteTransactionAsync.finally once it finishes; disposing _transaction
                    // here would race with that in-flight call. Do NOT dispose _completionLock
                    // here either — the other thread still holds it and its Release() would
                    // throw ObjectDisposedException.
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

        DisposeUserLockUnlessHeld();
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

        var connection = await connectionProvider.GetConnectionAsync(resolvedExecType, false, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await OpenConnectionWithOptionalLockAsync(context, connection, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Mirrors the sync path in CreateConnectionAndTransaction: Open()/OpenAsync() can throw
            // (e.g. ConnectionException from ExecuteSessionSettingsAsync under
            // SessionInitializationFailureMode.FailClosed) before the transaction-begin try/catch
            // below ever starts — without this, the connection's already-acquired PoolGovernor slot
            // is never released, leaking one slot per failure.
            await context.CloseAndDisposeConnectionAsync(connection).ConfigureAwait(false);
            throw;
        }

        var gate = await AcquireSingleConnectionTransactionGateAsync(context, cancellationToken).ConfigureAwait(false);

        IDbTransaction transaction;
        try
        {
            transaction = context.Dialect.RejectsExplicitIsolationLevelOnBeginTransaction
                ? await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : await connection.BeginTransactionAsync(resolvedIsolation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await gate.DisposeAsync().ConfigureAwait(false);
            await context.CloseAndDisposeConnectionAsync(connection).ConfigureAwait(false);
            throw new TransactionException(
                $"Failed to begin transaction on {context.Product}: {ex.Message}",
                context.Product, ex);
        }

        var tx = new TransactionContext(context, connection, transaction, resolvedIsolation, resolvedExecType, logger, gate);

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
