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

    private bool _committed;
    private bool _rolledBack;
    private int _completedState;
    private long _disposed;
    private int _semaphoreDisposed;

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

    public ProcWrappingStyle ProcWrappingStyle
    {
        get => _context.ProcWrappingStyle;
        set => throw new NotImplementedException();
    }

    public ITransactionContext BeginTransaction(IsolationProfile isolationProfile)
    {
        throw new InvalidOperationException("Cannot begin a nested transaction from TransactionContext.");
    }

    public ITransactionContext BeginTransaction(IsolationLevel? isolationLevel = null)
    {
        throw new InvalidOperationException("Cannot begin a nested transaction from TransactionContext.");
    }

    public void ReleaseConnection(ITrackedConnection? conn)
    {
        _context.ConnectionStrategy.ReleaseConnection(conn);
    }

    public ValueTask ReleaseConnectionAsync(ITrackedConnection? conn)
    {
        return _context.ConnectionStrategy.ReleaseConnectionAsync(conn);
    }

    public void Commit()
    {
        ThrowIfDisposed();
        _semaphoreSlim.Wait();
        try
        {
            if (Interlocked.Exchange(ref _completedState, 1) != 0)
            {
                throw new InvalidOperationException("Transaction already completed.");
            }

            _transaction.Commit();
            _committed = true;
        }
        finally
        {
            _context.ConnectionStrategy.ReleaseConnection(_connection);
            _semaphoreSlim.Release();
        }
    }

    public void Rollback()
    {
        ThrowIfDisposed();
        _semaphoreSlim.Wait();
        try
        {
            if (Interlocked.Exchange(ref _completedState, 1) != 0)
            {
                throw new InvalidOperationException("Transaction already completed.");
            }

            _transaction.Rollback();
            _rolledBack = true;
        }
        finally
        {
            _context.ConnectionStrategy.ReleaseConnection(_connection);
            _semaphoreSlim.Release();
        }
    }

    protected override void DisposeManaged()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _completedState, 0, 0) == 0)
        {
            _semaphoreSlim.Wait();
            try
            {
                if (Interlocked.Exchange(ref _completedState, 1) == 0)
                {
                    _transaction.Rollback();
                    _rolledBack = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rollback failed during Dispose.");
            }
            finally
            {
                _context.ConnectionStrategy.ReleaseConnection(_connection);
                _semaphoreSlim.Release();
            }
        }

        if (Interlocked.Exchange(ref _semaphoreDisposed, 1) == 0)
        {
            _semaphoreSlim.Dispose();
        }
    }

    protected override async ValueTask DisposeManagedAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _completedState, 0, 0) == 0)
        {
            _semaphoreSlim.Wait();
            try
            {
                if (Interlocked.Exchange(ref _completedState, 1) == 0)
                {
                    _transaction.Rollback();
                    _rolledBack = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Async rollback failed during DisposeAsync.");
            }
            finally
            {
                await _context.ConnectionStrategy.ReleaseConnectionAsync(_connection).ConfigureAwait(false);
                _semaphoreSlim.Release();
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

        if (Interlocked.Exchange(ref _semaphoreDisposed, 1) == 0)
        {
            _semaphoreSlim.Dispose();
        }
    }

    private void EnsureConnectionIsOpen()
    {
        if (_connection.State != ConnectionState.Open)
        {
            _connection.Open();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Interlocked.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TransactionContext));
        }
    }
}
