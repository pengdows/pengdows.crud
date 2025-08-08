#region

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
    private readonly DatabaseContext _context;
    private readonly ILogger<TransactionContext> _logger;

    private readonly SemaphoreSlim _semaphoreSlim;
    private readonly IDbTransaction _transaction;

    private bool _committed;

    private int _completedState; // 0 = not completed, 1 = committed or rolled back
    private bool _rolledBack;

    internal TransactionContext(IDatabaseContext context,
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        ExecutionType? executionType = null,
        ILogger<TransactionContext>? logger = null)
    {
        _logger = logger ?? new NullLogger<TransactionContext>();
        _context = context as DatabaseContext ?? throw new ArgumentNullException(nameof(context));
        executionType ??= IsReadOnlyConnection ? ExecutionType.Read : ExecutionType.Write;

        if (IsReadOnlyConnection && executionType != ExecutionType.Read)
            throw new NotSupportedException("DatabaseContext is read only");

        if (_context.Product == SupportedDatabase.CockroachDb) isolationLevel = IsolationLevel.Serializable;

        //var executionType = GetExecutionAndSetIsolationTypes(ref isolationLevel);
        // IsolationLevel = isolationLevel;

        _connection = _context.GetConnection(executionType.Value, true);
        EnsureConnectionIsOpen();
        _semaphoreSlim = new SemaphoreSlim(1, 1);

        _transaction = _connection.BeginTransaction(isolationLevel);
    }

    public Guid TransactionId { get; } = Guid.NewGuid();

    internal IDbTransaction Transaction => _transaction;

    public bool WasCommitted => _completedState == 1 && _committed;
    public bool WasRolledBack => _completedState == 1 && _rolledBack;

    public bool IsCompleted => Interlocked.CompareExchange(ref _completedState, 0, 0) != 0;

    // private ExecutionType GetExecutionAndSetIsolationTypes(ref IsolationLevel isolationLevel)
    // {
    //     var executionType = ExecutionType.Write;
    //     switch (_context.ReadWriteMode)
    //     {
    //         case ReadWriteMode.ReadWrite:
    //         case ReadWriteMode.WriteOnly:
    //             //leave the default "write" selection
    //             if (isolationLevel < IsolationLevel.ReadCommitted)
    //             {
    //                 isolationLevel = IsolationLevel.ReadCommitted;
    //             }
    //
    //             break;
    //         case ReadWriteMode.ReadOnly:
    //             executionType = ExecutionType.Read;
    //             if (isolationLevel < IsolationLevel.RepeatableRead)
    //             {
    //                 isolationLevel = IsolationLevel.RepeatableRead;
    //             }
    //
    //             break;
    //         default:
    //             throw new ArgumentOutOfRangeException();
    //     }
    //
    //     return executionType;
    // }

    public IsolationLevel IsolationLevel => _transaction.IsolationLevel;

    // Delegated context properties
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
            throw new InvalidOperationException("Cannot create a SQL container because the transaction is completed.");

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

    public ITransactionContext BeginTransaction(IsolationProfile isolationProfile)
    {
        throw new InvalidOperationException("Cannot begin a nested transaction from TransactionContext.");
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

    public void CloseAndDisposeConnection(ITrackedConnection? conn)
    {
        _context.CloseAndDisposeConnection(conn);
    }

    public string MakeParameterName(DbParameter dbParameter)
    {
        return _context.MakeParameterName(dbParameter);
    }

    public ProcWrappingStyle ProcWrappingStyle => _context.ProcWrappingStyle;

    ProcWrappingStyle IDatabaseContext.ProcWrappingStyle
    {
        get => _context.ProcWrappingStyle;
        set => _context.ProcWrappingStyle = value;
    }

    public ITransactionContext BeginTransaction(IsolationLevel? isolationLevel = null)
    {
        throw new InvalidOperationException("Cannot begin a nested transaction from TransactionContext.");
    }

    public void Commit()
    {
        ThrowIfDisposed();
        CompleteTransaction(t => t.Commit(), true);
    }

    public void Rollback()
    {
        ThrowIfDisposed();
        CompleteTransaction(t => t.Rollback(), false);
    }

    protected override void DisposeManaged()
    {
        if (!IsCompleted)
        {
            try
            {
                CompleteTransaction(t => t.Rollback(), false, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rollback failed during Dispose.");
            }
        }

        _semaphoreSlim.Dispose();
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        if (!IsCompleted)
        {
            try
            {
                await CompleteTransactionAsync(t => t.Rollback(), false, false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Async rollback failed during DisposeAsync.");
            }
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

    private void CompleteTransaction(Action<IDbTransaction> action, bool markCommitted, bool throwIfAlreadyCompleted = true)
    {
        _semaphoreSlim.Wait();
        try
        {
            if (Interlocked.Exchange(ref _completedState, 1) != 0)
            {
                if (throwIfAlreadyCompleted)
                {
                    throw new InvalidOperationException("Transaction already completed.");
                }

                return;
            }

            action(_transaction);

            if (markCommitted)
            {
                _committed = true;
            }
            else
            {
                _rolledBack = true;
            }

            _transaction.Dispose();
            _context.CloseAndDisposeConnection(_connection);
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    private async ValueTask CompleteTransactionAsync(Action<IDbTransaction> action, bool markCommitted, bool throwIfAlreadyCompleted = true)
    {
        await _semaphoreSlim.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Interlocked.Exchange(ref _completedState, 1) != 0)
            {
                if (throwIfAlreadyCompleted)
                {
                    throw new InvalidOperationException("Transaction already completed.");
                }

                return;
            }

            action(_transaction);

            if (markCommitted)
            {
                _committed = true;
            }
            else
            {
                _rolledBack = true;
            }

            if (_transaction is IAsyncDisposable asyncTx)
            {
                await asyncTx.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                _transaction.Dispose();
            }

            await _context.CloseAndDisposeConnectionAsync(_connection).ConfigureAwait(false);
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}