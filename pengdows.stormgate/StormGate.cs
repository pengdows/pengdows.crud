using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace pengdows.stormgate;

/// <summary>
/// Limits concurrent database connection opens and ties permit release
/// to connection lifetime.
/// </summary>
public sealed class StormGate : IConnectionFactory, IDisposable, IAsyncDisposable
{
    private readonly DbDataSource _dataSource;
    private readonly SemaphoreSlim _semaphore;
    private readonly TimeSpan _acquireTimeout;
    private readonly ILogger _logger;
    private readonly object _lifecycleLock = new();
    private int _activeLeases;
    private int _disposed;
    private int _semaphoreDisposed;

    public StormGate(
        DbDataSource dataSource,
        int maxConcurrentOpens,
        TimeSpan acquireTimeout,
        ILogger? logger = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

        if (maxConcurrentOpens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentOpens));
        }

        if (acquireTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(acquireTimeout));
        }

        _semaphore = new SemaphoreSlim(maxConcurrentOpens, maxConcurrentOpens);
        _acquireTimeout = acquireTimeout;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    public static StormGate Create(
        DbProviderFactory factory,
        string connectionString,
        int maxConcurrentOpens,
        TimeSpan acquireTimeout,
        ILogger? logger = null)
    {
        var resolver = new DataSourceResolver(logger);
        var dataSource = resolver.CreateDataSource(factory, connectionString);

        return new StormGate(dataSource, maxConcurrentOpens, acquireTimeout, logger);
    }

    public async Task<DbConnection> OpenAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        // Dispose may race with WaitAsync after the disposed check above.
        // In that case SemaphoreSlim may throw ObjectDisposedException.
        // That is acceptable: a disposed StormGate cannot open new connections.
        if (!await _semaphore.WaitAsync(_acquireTimeout, ct).ConfigureAwait(false))
        {
            _logger.LogWarning("StormGate saturation: timed out waiting for a connection permit after {Timeout}ms.", _acquireTimeout.TotalMilliseconds);
            throw new TimeoutException("Database is saturated (storm gate).");
        }

        try
        {
            var inner = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
            RegisterLease();
            return new PermitConnection(inner, this);
        }
        catch (OperationCanceledException)
        {
            _semaphore.Release();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open connection after acquiring StormGate permit.");
            _semaphore.Release();
            throw;
        }
    }

    private void RegisterLease()
    {
        lock (_lifecycleLock)
        {
            _activeLeases++;
        }
    }

    private void ReleaseLease()
    {
        var disposeSemaphore = false;

        lock (_lifecycleLock)
        {
            _semaphore.Release();
            _activeLeases--;

            if (_activeLeases == 0 &&
                Volatile.Read(ref _disposed) != 0 &&
                _semaphoreDisposed == 0)
            {
                _semaphoreDisposed = 1;
                disposeSemaphore = true;
            }
        }

        if (disposeSemaphore)
        {
            _semaphore.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(StormGate));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _dataSource.Dispose();
        DisposeSemaphoreIfDrained();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _dataSource.DisposeAsync().ConfigureAwait(false);
        DisposeSemaphoreIfDrained();
    }

    private void DisposeSemaphoreIfDrained()
    {
        var disposeSemaphore = false;

        lock (_lifecycleLock)
        {
            if (_activeLeases == 0 && _semaphoreDisposed == 0)
            {
                _semaphoreDisposed = 1;
                disposeSemaphore = true;
            }
        }

        if (disposeSemaphore)
        {
            _semaphore.Dispose();
        }
    }

    private sealed class PermitConnection : DbConnection
    {
        private readonly DbConnection _inner;
        private readonly StormGate _owner;
        private int _released;
        private int _disposed;

        public PermitConnection(DbConnection inner, StormGate owner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        private void ReleasePermitOnce()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _owner.ReleaseLease();
            }
        }

        [AllowNull]
        public override string ConnectionString
        {
            get => _inner.ConnectionString;
            set => _inner.ConnectionString = value;
        }

        public override string Database => _inner.Database;
        public override string DataSource => _inner.DataSource;
        public override string ServerVersion => _inner.ServerVersion;
        public override ConnectionState State => _inner.State;

        public override void ChangeDatabase(string databaseName) =>
            _inner.ChangeDatabase(databaseName);

        // Return silently if already open — Dapper and EF Core call Open() defensively
        // on connections they didn't open. NotSupportedException signals that direct open
        // is not valid for this wrapper type (the BCL convention for "invalid on this type").
        // Check _disposed first to give callers a clear ObjectDisposedException.
        public override void Open()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(PermitConnection));
            }

            if (_inner.State == ConnectionState.Open)
            {
                return;
            }

            throw new NotSupportedException("PermitConnection cannot be opened directly; obtain connections via StormGate.OpenAsync().");
        }

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(PermitConnection));
            }

            if (_inner.State == ConnectionState.Open)
            {
                return Task.CompletedTask;
            }

            throw new NotSupportedException("PermitConnection cannot be opened directly; obtain connections via StormGate.OpenAsync().");
        }

        public override void Close()
        {
            try
            {
                if (_inner.State != ConnectionState.Closed)
                {
                    _inner.Close();
                }
            }
            finally
            {
                // The StormGate permit tracks the lifetime of this wrapper, not whether
                // the provider close path completed cleanly. Release exactly once either way.
                ReleasePermitOnce();
            }
        }

        public override async Task CloseAsync()
        {
            try
            {
                if (_inner.State != ConnectionState.Closed)
                {
                    await _inner.CloseAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                ReleasePermitOnce();
            }
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            ThrowIfInnerClosed();
            return new PermitTransaction(_inner.BeginTransaction(isolationLevel), this);
        }

        // Minor: override the async path to use the inner connection's native async transaction
        // start rather than falling back to the sync BeginDbTransaction default in DbConnection.
        // Providers such as Npgsql and MySqlConnector support truly async transaction begin.
        protected override async ValueTask<DbTransaction> BeginDbTransactionAsync(
            IsolationLevel isolationLevel,
            CancellationToken cancellationToken)
        {
            ThrowIfInnerClosed();
            var transaction = await _inner.BeginTransactionAsync(isolationLevel, cancellationToken)
                .ConfigureAwait(false);
            return new PermitTransaction(transaction, this);
        }

        protected override DbCommand CreateDbCommand()
        {
            ThrowIfInnerClosed();
            return new PermitCommand(_inner.CreateCommand(), this);
        }

        // Check _disposed first so methods throw ObjectDisposedException when appropriate.
        // Then check _released. If Close() threw, the finally block still released the
        // permit (_released = 1) but inner.State may remain Open. Without this check,
        // CreateCommand/BeginTransaction would succeed on a connection whose permit was returned.
        private void ThrowIfInnerClosed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(PermitConnection));
            }

            if (Volatile.Read(ref _released) != 0)
            {
                throw new InvalidOperationException("Connection permit has been released.");
            }

            if (_inner.State == ConnectionState.Closed)
            {
                throw new InvalidOperationException("Connection is closed.");
            }
        }

        // _released uses Volatile.Read for the fast-path check; mutations use Interlocked.Exchange
        // which acts as a full memory barrier. Do not add a lock here — calling into owner
        // under any lock would risk deadlock with StormGate's _lifecycleLock.
        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (disposing)
            {
                // Managed dispose path: close before disposing the inner connection.
                // DbConnection.Dispose(bool) does not call Close() in .NET 8+, so we
                // call it explicitly. Some providers are not idempotent if Close() is
                // called after Dispose(), so we close first. Use try/finally to
                // guarantee _inner.Dispose() runs even if Close() throws.
                try
                {
                    Close();
                }
                finally
                {
                    try
                    {
                        _inner.Dispose();
                    }
                    finally
                    {
                        base.Dispose(disposing);
                    }
                }
            }
            else
            {
                // Finalizer path: do not touch managed objects — they may already be
                // collected or invalid. An exception from a finalizer crashes the process.
                base.Dispose(disposing);
            }
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            // Close before dispose (same provider-idempotency reason as sync path).
            // Use nested try/finally so _inner.DisposeAsync() is guaranteed to run
            // even if CloseAsync() throws (permit is also always released).
            try
            {
                if (_inner.State != ConnectionState.Closed)
                {
                    await _inner.CloseAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    await _inner.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    ReleasePermitOnce();
                    await base.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private sealed class PermitCommand(DbCommand inner, PermitConnection connection) : DbCommand
        {
            private DbTransaction? _transaction;
            private int _disposed;

            private DbCommand Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

            [AllowNull]
            public override string CommandText
            {
                get => Inner.CommandText;
                set => Inner.CommandText = value;
            }

            public override int CommandTimeout
            {
                get => Inner.CommandTimeout;
                set => Inner.CommandTimeout = value;
            }

            public override CommandType CommandType
            {
                get => Inner.CommandType;
                set => Inner.CommandType = value;
            }

            public override bool DesignTimeVisible
            {
                get => Inner.DesignTimeVisible;
                set => Inner.DesignTimeVisible = value;
            }

            public override UpdateRowSource UpdatedRowSource
            {
                get => Inner.UpdatedRowSource;
                set => Inner.UpdatedRowSource = value;
            }

            [AllowNull]
            protected override DbConnection DbConnection
            {
                get => connection;
                set
                {
                    if (value is not null && !ReferenceEquals(value, connection))
                    {
                        throw new InvalidOperationException("Commands created by a gated connection cannot be reassigned.");
                    }
                }
            }

            protected override DbParameterCollection DbParameterCollection => Inner.Parameters;

            [AllowNull]
            protected override DbTransaction DbTransaction
            {
                get => _transaction!;
                set
                {
                    _transaction = value;
                    Inner.Transaction = value is null
                        ? null
                        : value is PermitTransaction transaction && ReferenceEquals(transaction.Connection, connection)
                            ? transaction.Inner
                            : throw new InvalidOperationException("The transaction must come from the same gated connection.");
                }
            }

            public override void Cancel() => Inner.Cancel();

            public override int ExecuteNonQuery() => Inner.ExecuteNonQuery();

            // Without this override, DbCommand's default ExecuteNonQueryAsync runs the
            // synchronous ExecuteNonQuery() on a thread-pool thread instead of using the
            // provider's real async I/O (e.g. SqlCommand/NpgsqlCommand/SqliteCommand).
            public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) =>
                Inner.ExecuteNonQueryAsync(cancellationToken);

            public override object? ExecuteScalar() => Inner.ExecuteScalar();

            // See ExecuteNonQueryAsync override above — same thread-pool-blocking fallback risk.
            public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) =>
                Inner.ExecuteScalarAsync(cancellationToken);

            public override void Prepare() => Inner.Prepare();

            public override Task PrepareAsync(CancellationToken cancellationToken = default) =>
                Inner.PrepareAsync(cancellationToken);

            protected override DbParameter CreateDbParameter() => Inner.CreateParameter();

            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
                Inner.ExecuteReader(behavior);

            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
                CommandBehavior behavior,
                CancellationToken cancellationToken) => Inner.ExecuteReaderAsync(behavior, cancellationToken);


            protected override void Dispose(bool disposing)
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                if (disposing)
                {
                    Inner.Dispose();
                }

                base.Dispose(disposing);
            }

            public override async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                await Inner.DisposeAsync().ConfigureAwait(false);
                await base.DisposeAsync().ConfigureAwait(false);
            }
        }

        private sealed class PermitTransaction(DbTransaction inner, PermitConnection connection) : DbTransaction
        {
            internal DbTransaction Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));
            private int _disposed;

            public override IsolationLevel IsolationLevel => Inner.IsolationLevel;

            protected override DbConnection DbConnection => connection;

            public override void Commit() => Inner.Commit();

            public override Task CommitAsync(CancellationToken cancellationToken = default) =>
                Inner.CommitAsync(cancellationToken);

            public override void Rollback() => Inner.Rollback();

            public override Task RollbackAsync(CancellationToken cancellationToken = default) =>
                Inner.RollbackAsync(cancellationToken);

            protected override void Dispose(bool disposing)
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                if (disposing)
                {
                    Inner.Dispose();
                }

                base.Dispose(disposing);
            }

            public override async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                await Inner.DisposeAsync().ConfigureAwait(false);
                await base.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
