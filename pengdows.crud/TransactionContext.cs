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

#endregion

namespace pengdows.crud;

public class TransactionContext : SafeAsyncDisposableBase, ITransactionContext, IContextIdentity, ISqlDialectProvider
{
    private readonly ITrackedConnection _connection;
    private readonly IDatabaseContext _context;
    private readonly ISqlDialect _dialect;
    private readonly ILogger<TransactionContext> _logger;
    private readonly SemaphoreSlim _userLock;
    private readonly SemaphoreSlim _completionLock;
    private readonly IDbTransaction _transaction;
    private readonly IsolationLevel _resolvedIsolationLevel;

    private int _committed; // 0 = no, 1 = yes
    private int _rolledBack; // 0 = no, 1 = yes
    private int _completedState;

    public Guid RootId { get; }

    internal TransactionContext(
        IDatabaseContext context,
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        ExecutionType? executionType = null,
        ILogger<TransactionContext>? logger = null)
    {
        _logger = logger ?? new NullLogger<TransactionContext>();
        _context = context ?? throw new ArgumentNullException(nameof(context));
        var provider = context as ISqlDialectProvider
                       ?? throw new InvalidOperationException(
                           "IDatabaseContext must implement ISqlDialectProvider and expose a non-null Dialect.");
        _dialect = provider.Dialect;
        RootId = ((IContextIdentity)_context).RootId;

        executionType ??= _context.IsReadOnlyConnection ? ExecutionType.Read : ExecutionType.Write;

        if (_context.IsReadOnlyConnection && executionType != ExecutionType.Read)
        {
            throw new NotSupportedException("DatabaseContext is read-only");
        }

        if (_context.Product == SupportedDatabase.CockroachDb)
        {
            isolationLevel = IsolationLevel.Serializable;
        }

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
    }

    public Guid TransactionId { get; } = Guid.NewGuid();
    internal IDbTransaction Transaction => _transaction;

    public bool WasCommitted => Interlocked.CompareExchange(ref _completedState, 0, 0) != 0
                                 && Interlocked.CompareExchange(ref _committed, 0, 0) != 0;
    public bool WasRolledBack => Interlocked.CompareExchange(ref _completedState, 0, 0) != 0
                                  && Interlocked.CompareExchange(ref _rolledBack, 0, 0) != 0;
    public bool IsCompleted => Interlocked.CompareExchange(ref _completedState, 0, 0) != 0;
    public IsolationLevel IsolationLevel => _resolvedIsolationLevel;

    public long NumberOfOpenConnections => _context.NumberOfOpenConnections;
    public SupportedDatabase Product => _context.Product;
    public long MaxNumberOfConnections => _context.MaxNumberOfConnections;
    public bool IsReadOnlyConnection => _context.IsReadOnlyConnection;
    public bool RCSIEnabled => _context.RCSIEnabled;
    public int MaxParameterLimit => _context.MaxParameterLimit;
    public int MaxOutputParameters => (_dialect as pengdows.crud.dialects.SqlDialect)?.MaxOutputParameters ?? 0;
    public DbMode ConnectionMode => DbMode.SingleConnection;
    public ITypeMapRegistry TypeMapRegistry => _context.TypeMapRegistry;
    public IDataSourceInformation DataSourceInfo => _context.DataSourceInfo;
    public string SessionSettingsPreamble => _context.SessionSettingsPreamble;

    public ILockerAsync GetLock()
    {
        ThrowIfDisposed();
        if (IsCompleted)
        {
            throw new InvalidOperationException("Transaction already completed.");
        }

        return new RealAsyncLocker(_userLock);
    }

    public ISqlContainer CreateSqlContainer(string? query = null)
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException("Cannot create a SQL container because the transaction is completed.");
        }

        return new SqlContainer(this, query);
    }

    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value,
        ParameterDirection direction = ParameterDirection.Input)
    {
        var p = _context.CreateDbParameter(name, type, value);
        p.Direction = direction;
        return p;
    }

    // Back-compat overloads (interface surface)
    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        return _context.CreateDbParameter(name, type, value);
    }

    public DbParameter CreateDbParameter<T>(DbType type, T value,
        ParameterDirection direction = ParameterDirection.Input)
    {
        var p = _context.CreateDbParameter(type, value);
        p.Direction = direction;
        return p;
    }

    // Back-compat overload (interface surface)
    public DbParameter CreateDbParameter<T>(DbType type, T value)
    {
        return _context.CreateDbParameter(type, value);
    }

    public ITrackedConnection GetConnection(ExecutionType type, bool isShared = false)
    {
        return _connection;
    }

    public string GenerateRandomName(int length = 5, int parameterNameMaxLength = 30)
    {
        return _context.GenerateRandomName(length, parameterNameMaxLength);
    }

    public void AssertIsReadConnection()
    {
        _context.AssertIsReadConnection();
    }

    public void AssertIsWriteConnection()
    {
        _context.AssertIsWriteConnection();
    }

    public string QuotePrefix => _dialect.QuotePrefix;

    public string QuoteSuffix => _dialect.QuoteSuffix;

    public string CompositeIdentifierSeparator => _dialect.CompositeIdentifierSeparator;

    public string WrapObjectName(string name)
    {
        return _dialect.WrapObjectName(name);
    }

    public string MakeParameterName(DbParameter dbParameter)
    {
        return _dialect.MakeParameterName(dbParameter);
    }

    public string MakeParameterName(string parameterName)
    {
        return _dialect.MakeParameterName(parameterName);
    }

    public ProcWrappingStyle ProcWrappingStyle => _context.ProcWrappingStyle;

    ProcWrappingStyle IDatabaseContext.ProcWrappingStyle => _context.ProcWrappingStyle;

    ITransactionContext IDatabaseContext.BeginTransaction(IsolationProfile isolationProfile, ExecutionType executionType)
    {
        throw new InvalidOperationException("Cannot begin a nested transaction from TransactionContext.");
    }

    ITransactionContext IDatabaseContext.BeginTransaction(IsolationLevel? isolationLevel, ExecutionType executionType)
    {
        throw new InvalidOperationException("Cannot begin a nested transaction from TransactionContext.");
    }

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
        cmd.CommandText = $"SAVEPOINT {name}";
        if (cmd is DbCommand db)
        {
            await db.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            cmd.ExecuteNonQuery();
        }
    }

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
        cmd.CommandText = $"ROLLBACK TO SAVEPOINT {name}";
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

    private async Task CompleteTransactionWithWaitAsync(Func<Task> action, bool markCommitted, CancellationToken cancellationToken = default)
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
    }

    // Kept for backward compatibility with existing internal calls
    private Task RollbackAsync() => RollbackAsync(default);

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
                    _logger.LogError("TransactionContext.DisposeAsync could not acquire lock; skipping explicit rollback.");
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
    }

    private void EnsureConnectionIsOpen()
    {
        if (_connection.State != ConnectionState.Open)
        {
            _connection.Open();
        }
    }

    public ISqlDialect Dialect =>  _dialect;
}
