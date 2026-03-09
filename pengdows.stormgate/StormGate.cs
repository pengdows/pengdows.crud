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
    private int _disposed;

    public StormGate(
        DbDataSource dataSource,
        int maxConcurrentOpens,
        TimeSpan acquireTimeout,
        ILogger? logger = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

        if (maxConcurrentOpens <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentOpens));

        if (acquireTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(acquireTimeout));

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
            return new PermitConnection(inner, _semaphore);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open connection after acquiring StormGate permit.");
            _semaphore.Release();
            throw;
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(StormGate));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _semaphore.Dispose();
        _dataSource.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _semaphore.Dispose();
        await _dataSource.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class PermitConnection : DbConnection
    {
        private readonly DbConnection _inner;
        private readonly SemaphoreSlim _semaphore;
        private int _released;
        private int _disposed;

        public PermitConnection(DbConnection inner, SemaphoreSlim semaphore)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _semaphore = semaphore ?? throw new ArgumentNullException(nameof(semaphore));
        }

        private void ReleasePermitOnce()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                _semaphore.Release();
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

        public override void Open() =>
            throw new InvalidOperationException("Connection already opened by StormGate.");

        public override Task OpenAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Connection already opened by StormGate.");

        public override void Close()
        {
            try
            {
                if (_inner.State != ConnectionState.Closed)
                    _inner.Close();
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
                    await _inner.CloseAsync().ConfigureAwait(false);
            }
            finally
            {
                ReleasePermitOnce();
            }
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            ThrowIfInnerClosed();
            return _inner.BeginTransaction(isolationLevel);
        }

        protected override DbCommand CreateDbCommand()
        {
            ThrowIfInnerClosed();
            return _inner.CreateCommand();
        }

        private void ThrowIfInnerClosed()
        {
            if (_inner.State == ConnectionState.Closed)
                throw new InvalidOperationException("Connection is closed.");
            
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(PermitConnection));
        }

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            if (disposing)
                _inner.Dispose();

            ReleasePermitOnce();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

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
}
