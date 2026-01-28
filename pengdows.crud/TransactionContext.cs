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

public class TransactionContext : SafeAsyncDisposableBase, ITransactionContext, IContextIdentity, ISqlDialectProvider,
    IMetricsCollectorAccessor
{
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
        _metricsCollector = (context as IMetricsCollectorAccessor)?.MetricsCollector;
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
        _connection = _context.GetConnection(executionType.Value, true);
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
    public long MaxNumberOfConnections => _context.MaxNumberOfConnections;
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
    public int MaxOutputParameters => (_dialect as SqlDialect)?.MaxOutputParameters ?? 0;
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

    /// <inheritdoc/>
    public ISqlContainer CreateSqlContainer(string? query = null)
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException("Cannot create a SQL container because the transaction is completed.");
        }

        // Try to reuse the parent context's logger factory if available so tests can capture logs
        ILogger<ISqlContainer>? logger = null;
        if (_context is DatabaseContext dbCtx)
        {
            logger = dbCtx.CreateSqlContainerLogger();
        }

        return SqlContainer.Create(this, query, logger);
    }

    /// <inheritdoc/>
    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value,
        ParameterDirection direction = ParameterDirection.Input)
    {
        var p = _context.CreateDbParameter(name, type, value);
        p.Direction = direction;
        return p;
    }

    // Back-compat overloads (interface surface)
    /// <inheritdoc/>
    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        return _context.CreateDbParameter(name, type, value);
    }

    /// <inheritdoc/>
    public DbParameter CreateDbParameter<T>(DbType type, T value,
        ParameterDirection direction = ParameterDirection.Input)
    {
        var p = _context.CreateDbParameter(type, value);
        p.Direction = direction;
        return p;
    }

    // Back-compat overload (interface surface)
    /// <inheritdoc/>
    public DbParameter CreateDbParameter<T>(DbType type, T value)
    {
        return _context.CreateDbParameter(type, value);
    }

    /// <inheritdoc/>
    public ITrackedConnection GetConnection(ExecutionType type, bool isShared = false)
    {
        return _connection;
    }

    /// <inheritdoc/>
    public string GenerateRandomName(int length = 5, int parameterNameMaxLength = 30)
    {
        return _context.GenerateRandomName(length, parameterNameMaxLength);
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
    public string QuotePrefix => _dialect.QuotePrefix;

    /// <inheritdoc/>
    public string QuoteSuffix => _dialect.QuoteSuffix;

    /// <inheritdoc/>
    public bool SupportsInsertReturning => _dialect.SupportsInsertReturning;

    /// <inheritdoc/>
    public bool? ForceManualPrepare => _context.ForceManualPrepare;

    /// <inheritdoc/>
    public bool? DisablePrepare => _context.DisablePrepare;

    /// <inheritdoc/>
    public string CompositeIdentifierSeparator => _dialect.CompositeIdentifierSeparator;

    /// <inheritdoc/>
    public string WrapObjectName(string name)
    {
        return _dialect.WrapObjectName(name);
    }

    MetricsCollector? IMetricsCollectorAccessor.MetricsCollector => _metricsCollector;

    /// <inheritdoc/>
    public string MakeParameterName(DbParameter dbParameter)
    {
        return _dialect.MakeParameterName(dbParameter);
    }

    /// <inheritdoc/>
    public string MakeParameterName(string parameterName)
    {
        return _dialect.MakeParameterName(parameterName);
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
        if (!_completionLock.Wait(TimeSpan.FromSeconds(30)))
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

    public ISqlDialect Dialect => _dialect;

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
