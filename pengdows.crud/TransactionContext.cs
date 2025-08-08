#region

using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.threading;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

public class TransactionContext : SafeAsyncDisposableBase, ITransactionContext
{
    private readonly ITrackedConnection _connection;
    private readonly IDatabaseContext _context;
    private readonly ILogger<TransactionContext> _logger;
    private readonly SemaphoreSlim _semaphoreSlim;
    private readonly IDbTransaction _transaction;

    private volatile bool _committed;
    private volatile bool _rolledBack;
    private int _completedState;

    internal TransactionContext(
        IDatabaseContext context,
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        ExecutionType? executionType = null,
        ILogger<TransactionContext>? logger = null)
    {
        _logger = logger ?? new NullLogger<TransactionContext>();
        _context = context ?? throw new ArgumentNullException(nameof(context));

        executionType ??= _context.IsReadOnlyConnection ? ExecutionType.Read : ExecutionType.Write;

        if (_context.IsReadOnlyConnection && executionType != ExecutionType.Read)
        {
            throw new NotSupportedException("DatabaseContext is read-only");
        }

        if (_context.Product == SupportedDatabase.CockroachDb)
        {
            isolationLevel = IsolationLevel.Serializable;
        }

        _connection = _context.ConnectionStrategy.GetConnection(executionType.Value, shared: true);
        EnsureConnectionIsOpen();
        _semaphoreSlim = new SemaphoreSlim(1, 1);

        _transaction = _connection.BeginTransaction(isolationLevel);
    }

    public Guid TransactionId { get; } = Guid.NewGuid();
    internal IDbTransaction Transaction => _transaction;

    public bool WasCommitted => _completedState == 1 && _committed;
    public bool WasRolledBack => _completedState == 1 && _rolledBack;
    public bool IsCompleted => Interlocked.CompareExchange(ref _completedState, 0, 0) != 0;
    public IsolationLevel IsolationLevel => _transaction.IsolationLevel;

    public long NumberOfOpenConnections => _context.NumberOfOpenConnections;
    public string QuotePrefix => _context.QuotePrefix;
    public string QuoteSuffix => _context.QuoteSuffix;
    public string CompositeIdentifierSeparator => _context.CompositeIdentifierSeparator;
    public SupportedDatabase Product => _context.Product;
    public long MaxNumberOfConnections => _context.MaxNumberOfConnections;
    public bool IsReadOnlyConnection => _context.IsReadOnlyConnection;
    public bool RCSIEnabled => _context.RCSIEnabled;
    public int MaxParameterLimit => _context.MaxParameterLimit;
    public DbMode ConnectionMode => DbMode.SingleConnection;
    public ITypeMapRegistry TypeMapRegistry => _context.TypeMapRegistry;
    public IDataSourceInformation DataSourceInfo => _context.DataSourceInfo;
    public string SessionSettingsPreamble => _context.SessionSettingsPreamble;

    public ILockerAsync GetLock()
    {
        ThrowIfDisposed();
        return new RealAsyncLocker(_semaphoreSlim);
    }

    public ISqlContainer CreateSqlContainer(string? query = null)
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException("Cannot create a SQL container because the transaction is completed.");
        }

        return new SqlContainer(this, query);
    }

    public DbParameter CreateDbParameter<T>(string name, DbType type, T value)
    {
        return _context.CreateDbParameter(name, type, value);
    }

    public DbParameter CreateDbParameter<T>(DbType type, T value)
    {
        return _context.CreateDbParameter(type, value);
    }

    public ITrackedConnection GetConnection(ExecutionType type, bool isShared = false)
    {
        return _connection;
    }

    public string WrapObjectName(string name)
    {
        return _context.WrapObjectName(name);
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

    public string MakeParameterName(string parameterName)
    {
        return _context.MakeParameterName(parameterName);
    }

    public string MakeParameterName(DbParameter dbParameter)
    {
        return _context.MakeParameterName(dbParameter);
    }

    public ProcWrappingStyle ProcWrappingStyle => _context.ProcWrappingStyle;

    ProcWrappingStyle IDatabaseContext.ProcWrappingStyle
    {
        get => _context.ProcWrappingStyle;
        set
        {
            ThrowIfDisposed();
            _context.ProcWrappingStyle = value;
        }
    }

    /// <summary>
    /// Nested transactions are not supported. Calling this method will always throw.
    /// </summary>
    public ITransactionContext BeginTransaction(IsolationProfile isolationProfile) =>
        throw new InvalidOperationException("Cannot begin a nested transaction from TransactionContext.");

    /// <summary>
    /// Nested transactions are not supported. Calling this method will always throw.
    /// </summary>
    public ITransactionContext BeginTransaction(IsolationLevel? isolationLevel = null) =>
        throw new InvalidOperationException("Cannot begin a nested transaction from TransactionContext.");

    public void ReleaseConnection(ITrackedConnection? conn)
    {
        ThrowIfDisposed();
        _context.ConnectionStrategy.ReleaseConnection(conn);
    }

    public ValueTask ReleaseConnectionAsync(ITrackedConnection? conn)
    {
        ThrowIfDisposed();
        return _context.ConnectionStrategy.ReleaseConnectionAsync(conn);
    }

    public void Commit()
    {
        ThrowIfDisposed();
        CompleteTransactionWithWait(() => _transaction.Commit(), true);
    }

    public void Rollback()
    {
        ThrowIfDisposed();
        CompleteTransactionWithWait(() => _transaction.Rollback(), false);
    }

    private void CompleteTransactionWithWait(Action action, bool markCommitted)
    {
        _semaphoreSlim.Wait();
        try
        {
            CompleteTransaction(action, markCommitted);
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    private async Task CompleteTransactionWithWaitAsync(Func<Task> action, bool markCommitted)
    {
        await _semaphoreSlim.WaitAsync().ConfigureAwait(false);
        try
        {
            await CompleteTransactionAsync(action, markCommitted).ConfigureAwait(false);
        }
        finally
        {
            _semaphoreSlim.Release();
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
            _committed = true;
        }
        else
        {
            _rolledBack = true;
        }

        _context.ConnectionStrategy.ReleaseConnection(_connection);
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
            _committed = true;
        }
        else
        {
            _rolledBack = true;
        }

        await _context.ConnectionStrategy.ReleaseConnectionAsync(_connection).ConfigureAwait(false);
    }

    private Task RollbackAsync()
    {
        return CompleteTransactionWithWaitAsync(() =>
        {
            _transaction.Rollback();
            return Task.CompletedTask;
        }, false);
    }

    protected override void DisposeManaged()
    {
        if (!IsCompleted)
        {
            try
            {
                Rollback();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rollback failed during Dispose.");
            }
        }

        _transaction.Dispose();
        _semaphoreSlim.Dispose();
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        if (!IsCompleted)
        {
            try
            {
                await RollbackAsync().ConfigureAwait(false);
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

        _semaphoreSlim.Dispose();
    }

    private void EnsureConnectionIsOpen()
    {
        if (_connection.State != ConnectionState.Open)
        {
            _connection.Open();
        }
    }
}
