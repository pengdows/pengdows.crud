using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace pengdows.stormgate;

/// <summary>
/// Limits concurrently held database connection leases and ties permit release to connection
/// lifetime — see the <c>maxConcurrentOpens</c> remarks below for what "concurrent" actually
/// bounds here.
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
    private int _resourcesDisposed;

    /// <param name="dataSource">The provider's data source, used to physically open connections.</param>
    /// <param name="maxConcurrentOpens">
    /// The size of the admission budget. Despite the name, this does not merely cap simultaneous
    /// *opening* handshakes — a permit is held for the entire lifetime of an admitted connection
    /// lease, from acquisition until that connection closes, fails to open, is disposed, or
    /// transitions to <see cref="ConnectionState.Broken"/>. A long-running unit of work that keeps
    /// a connection open occupies its permit for that whole duration, the same as if it were still
    /// mid-open. Read this as "maximum concurrently open/in-use connections," not "maximum
    /// concurrent open attempts."
    /// </param>
    /// <param name="acquireTimeout">How long <see cref="OpenAsync"/>/<see cref="AcquirePermitAsync"/>/<see cref="AcquirePermit"/> wait for a free permit before throwing <see cref="TimeoutException"/>.</param>
    /// <param name="logger">Optional logger for saturation warnings and open-failure errors.</param>
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

    /// <summary>Builds a <see cref="DbDataSource"/> from <paramref name="factory"/>/<paramref name="connectionString"/> and wraps it in a new <see cref="StormGate"/> — see the constructor's <c>maxConcurrentOpens</c> remarks for what the budget actually bounds.</summary>
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
        var permit = await AcquirePermitAsync(ct).ConfigureAwait(false);

        try
        {
            var inner = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
            return new PermitConnection(inner, permit);
        }
        catch (OperationCanceledException)
        {
            permit.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open connection after acquiring StormGate permit.");
            permit.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Acquires one admission-control permit from this StormGate's shared budget without opening
    /// a connection. Lets a consumer that manages its own connection lifecycle elsewhere — e.g.
    /// <c>StormGateConnectionInterceptor</c>, which gates Entity Framework Core's own connection
    /// open/close events — enforce the exact same admission budget as <see cref="OpenAsync"/>,
    /// instead of implementing an independent one. Dispose the returned <see cref="StormGatePermit"/>
    /// exactly once, when the guarded unit of work completes, to release the slot.
    /// </summary>
    public async Task<StormGatePermit> AcquirePermitAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        // Reserve BEFORE waiting on the semaphore, not after — see the "outstanding acquire
        // attempt" remarks on RegisterLease for why this ordering is load-bearing, not cosmetic.
        RegisterLease();
        try
        {
            // Dispose may race with WaitAsync after the disposed check above.
            // In that case SemaphoreSlim may throw ObjectDisposedException.
            // That is acceptable: a disposed StormGate cannot hand out new permits.
            if (!await _semaphore.WaitAsync(_acquireTimeout, ct).ConfigureAwait(false))
            {
                _logger.LogWarning("StormGate saturation: timed out waiting for a connection permit after {Timeout}ms.", _acquireTimeout.TotalMilliseconds);
                throw new TimeoutException("Database is saturated (storm gate).");
            }
        }
        catch
        {
            // The wait itself failed — timed out, canceled, or the semaphore was disposed out
            // from under us. No slot was ever taken, so undo the reservation without releasing
            // a semaphore permit that was never acquired.
            ReleaseReservation();
            throw;
        }

        return new StormGatePermit(this);
    }

    /// <summary>Synchronous counterpart of <see cref="AcquirePermitAsync"/> — see its remarks.</summary>
    public StormGatePermit AcquirePermit(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        RegisterLease();
        try
        {
            if (!_semaphore.Wait(_acquireTimeout, ct))
            {
                _logger.LogWarning("StormGate saturation: timed out waiting for a connection permit after {Timeout}ms.", _acquireTimeout.TotalMilliseconds);
                throw new TimeoutException("Database is saturated (storm gate).");
            }
        }
        catch
        {
            ReleaseReservation();
            throw;
        }

        return new StormGatePermit(this);
    }

    /// <summary>
    /// A held admission-control permit from a <see cref="StormGate"/>'s shared budget. Dispose
    /// exactly once to release the slot back to the gate.
    /// </summary>
    public readonly struct StormGatePermit : IDisposable, IAsyncDisposable
    {
        // A struct value can be copied (assignment, pass-by-value) and each copy disposed
        // independently, and a single copy can itself have Dispose() called more than once.
        // Both are indistinguishable from a caller double-releasing the same slot, which would
        // otherwise call StormGate.ReleaseLease() an extra time — either throwing
        // SemaphoreFullException (gate otherwise fully available) or silently handing out a slot
        // beyond the configured concurrency limit. Routing release through a shared, reference-typed
        // state object lets every copy see the same "already released" flag.
        private readonly ReleaseState? _state;

        internal StormGatePermit(StormGate owner)
        {
            _state = new ReleaseState(owner);
        }

        // A default-initialized StormGatePermit (e.g. default(StormGatePermit), never returned by
        // AcquirePermit/AcquirePermitAsync) has no state to release — disposing it is a safe no-op
        // rather than a NullReferenceException.
        public void Dispose() => _state?.ReleaseOnce();

        public ValueTask DisposeAsync()
        {
            _state?.ReleaseOnce();
            return ValueTask.CompletedTask;
        }

        private sealed class ReleaseState
        {
            private readonly StormGate _owner;
            private int _released;

            public ReleaseState(StormGate owner)
            {
                _owner = owner;
            }

            public void ReleaseOnce()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0)
                {
                    _owner.ReleaseLease();
                }
            }
        }
    }

    /// <summary>
    /// Counts an acquire attempt as "outstanding" from the moment it commits to taking a slot —
    /// BEFORE the semaphore wait even begins, not after it succeeds. This is load-bearing, not
    /// cosmetic: Dispose()/DisposeAsync() only tear down the shared DbDataSource/SemaphoreSlim
    /// once <see cref="_activeLeases"/> reaches zero, so an attempt that has taken a slot (or is
    /// still waiting for one) but hasn't finished becoming a registered lease must still count —
    /// otherwise a concurrent Dispose() can see "zero active leases" and dispose shared state
    /// while that attempt is still using it, corrupting or crashing it (and potentially masking
    /// its real exception with an unrelated ObjectDisposedException from the release path).
    /// </summary>
    private void RegisterLease()
    {
        lock (_lifecycleLock)
        {
            _activeLeases++;
        }
    }

    /// <summary>Releases a genuinely-acquired semaphore slot back to the gate.</summary>
    private void ReleaseLease() => CompleteLease(releaseSemaphoreSlot: true);

    /// <summary>
    /// Undoes RegisterLease() for an attempt that never actually took a semaphore slot — the wait
    /// timed out, was canceled, or the semaphore had already been disposed by a concurrent
    /// Dispose()/DisposeAsync(). Must NOT call SemaphoreSlim.Release(): doing so would release a
    /// slot this attempt never held.
    /// </summary>
    private void ReleaseReservation() => CompleteLease(releaseSemaphoreSlot: false);

    private void CompleteLease(bool releaseSemaphoreSlot)
    {
        var shouldDisposeResources = false;

        lock (_lifecycleLock)
        {
            if (releaseSemaphoreSlot)
            {
                _semaphore.Release();
            }

            _activeLeases--;

            if (_activeLeases == 0 &&
                Volatile.Read(ref _disposed) != 0 &&
                _resourcesDisposed == 0)
            {
                _resourcesDisposed = 1;
                shouldDisposeResources = true;
            }
        }

        if (shouldDisposeResources)
        {
            _dataSource.Dispose();
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

        // Disposing _dataSource/_semaphore here unconditionally — the pre-fix behavior — raced
        // with any acquire attempt still in flight (see RegisterLease's remarks). Deferring both
        // until drained, exactly like the semaphore already was, closes that race for the data
        // source too.
        if (TryClaimResourceDisposal())
        {
            _dataSource.Dispose();
            _semaphore.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (TryClaimResourceDisposal())
        {
            await _dataSource.DisposeAsync().ConfigureAwait(false);
            _semaphore.Dispose();
        }
    }

    /// <summary>
    /// Atomically claims the right to actually dispose _dataSource/_semaphore, if — and only
    /// if — no acquire attempt or lease is currently outstanding. Whichever of Dispose(Async) or
    /// the last outstanding attempt's CompleteLease() call reaches zero active leases first wins
    /// this claim; the other is a no-op, so the resources are disposed exactly once regardless of
    /// which side the drain completes on.
    /// </summary>
    private bool TryClaimResourceDisposal()
    {
        lock (_lifecycleLock)
        {
            if (_activeLeases != 0 || _resourcesDisposed != 0)
            {
                return false;
            }

            _resourcesDisposed = 1;
            return true;
        }
    }

    private sealed class PermitConnection : DbConnection
    {
        private readonly DbConnection _inner;
        private readonly StormGatePermit _permit;
        private int _released;
        private int _disposed;

        public PermitConnection(DbConnection inner, StormGatePermit permit)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _permit = permit;

            // A caller can close the real inner connection without ever going through this
            // wrapper's own Close()/Dispose() — e.g. CommandBehavior.CloseConnection on a reader
            // closes cmd.Connection directly, which is the inner connection, not this wrapper.
            // Subscribing to the inner connection's own StateChange event catches every path to
            // Closed/Broken regardless of what triggered it, mirroring the same fix already used
            // by StormGateConnectionInterceptor (pengdows.stormgate.EntityFrameworkCore) for the
            // identical class of problem.
            _inner.StateChange += OnInnerStateChange;
        }

        private void OnInnerStateChange(object? sender, StateChangeEventArgs e)
        {
            if (e.CurrentState is ConnectionState.Closed or ConnectionState.Broken)
            {
                ReleasePermitOnce();
            }
        }

        private void ReleasePermitOnce()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _inner.StateChange -= OnInnerStateChange;
                _permit.Dispose();
            }
        }

        // Finalizer-safe permit release: unlike ReleasePermitOnce, this never touches _inner
        // (not even to unsubscribe the StateChange handler) — see the Dispose(bool) finalizer
        // branch below for why touching _inner from that path is unsafe. Disposing _permit is
        // safe because it only reaches the owning StormGate, which this permit itself keeps
        // reachable and which has no finalizer of its own to race against.
        private void ReleasePermitOnFinalize()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                try
                {
                    _permit.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // The owning StormGate had already torn down its semaphore; nothing to release.
                }
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
                // The permit itself is safe to release here (see ReleasePermitOnFinalize) —
                // without it, a PermitConnection abandoned without Close/Dispose would hold
                // its StormGate permit forever once finalized.
                ReleasePermitOnFinalize();
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

            public override bool SupportsSavepoints => Inner.SupportsSavepoints;

            public override void Save(string savepointName) => Inner.Save(savepointName);

            public override void Rollback(string savepointName) => Inner.Rollback(savepointName);

            public override void Release(string savepointName) => Inner.Release(savepointName);

            public override Task SaveAsync(string savepointName, CancellationToken cancellationToken = default) =>
                Inner.SaveAsync(savepointName, cancellationToken);

            public override Task RollbackAsync(string savepointName, CancellationToken cancellationToken = default) =>
                Inner.RollbackAsync(savepointName, cancellationToken);

            public override Task ReleaseAsync(string savepointName, CancellationToken cancellationToken = default) =>
                Inner.ReleaseAsync(savepointName, cancellationToken);

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
