// =============================================================================
// FILE: RealAsyncLocker.cs
// PURPOSE: Real semaphore-based locker for shared connection synchronization.
//
// AI SUMMARY:
// - Implements ILockerAsync with actual SemaphoreSlim-based locking.
// - Used for shared (persistent) connections in SingleWriter/SingleConnection modes.
// - Key methods:
//   * Lock(): Sync lock acquisition with optional timeout
//   * LockAsync(ct): Async lock acquisition with cancellation
//   * TryLockAsync(timeout, ct): Timeout-based acquisition attempt
// - Throws ModeContentionException on timeout (includes diagnostics snapshot).
// - Contention tracking:
//   * ModeContentionStats integration for wait metrics
//   * Records wait start/end, timeouts
// - Lock state tracking:
//   * _lockState pre-check (Volatile.Read) before entering semaphore wait — prevents
//     deadlock when called twice on same instance with SemaphoreSlim(1,1): the second
//     WaitAsync() would block indefinitely instead of reaching AcquireLockState().
//   * AcquireLockState(): CAS post-semaphore guard as final backstop for concurrent races
//   * ReleaseIfHeld(): Safe release on dispose
// - Logging: Trace-level lock acquisition/release events.
// - Extends SafeAsyncDisposableBase: auto-releases lock on dispose.
// =============================================================================

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.exceptions;
using pengdows.crud.metrics;

namespace pengdows.crud.threading;

internal sealed class RealAsyncLocker : SafeAsyncDisposableBase, ILockerAsync
{
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger<RealAsyncLocker> _logger;
    private readonly ModeContentionStats? _stats;
    private readonly DbMode _mode;
    private readonly TimeSpan? _lockTimeout;
    private int _lockState;

    public RealAsyncLocker(SemaphoreSlim semaphore, ILogger<RealAsyncLocker>? logger = null)
        : this(semaphore, null, DbMode.Standard, null, logger)
    {
    }

    public RealAsyncLocker(
        SemaphoreSlim semaphore,
        ModeContentionStats? stats,
        DbMode mode,
        TimeSpan? lockTimeout,
        ILogger<RealAsyncLocker>? logger = null)
    {
        _semaphore = semaphore ?? throw new ArgumentNullException(nameof(semaphore));
        _logger = logger ?? NullLogger<RealAsyncLocker>.Instance;
        _stats = stats;
        _mode = mode;
        _lockTimeout = lockTimeout;
    }

    /// <inheritdoc />
    public void Lock()
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _lockState) != 0)
        {
            throw new InvalidOperationException("Lock already acquired.");
        }

        _logger.LogTrace("Waiting for lock (sync)");

        // Try immediate acquisition first
        if (_semaphore.Wait(0))
        {
            AcquireLockState();
            _logger.LogTrace("Lock acquired (sync)");
            return;
        }

        _stats?.RecordWaitStart();
        var start = Stopwatch.GetTimestamp();
        var acquired = false;
        try
        {
            if (_lockTimeout.HasValue)
            {
                acquired = _semaphore.Wait(_lockTimeout.Value);
                if (!acquired)
                {
                    var waited = Stopwatch.GetTimestamp() - start;
                    _stats?.RecordTimeout(waited);
                    throw new ModeContentionException(_mode, _stats?.GetSnapshot() ?? default, _lockTimeout.Value);
                }
            }
            else
            {
                _semaphore.Wait();
                acquired = true;
            }

            var waitTicks = Stopwatch.GetTimestamp() - start;
            _stats?.RecordWaitEnd(waitTicks);
        }
        catch
        {
            if (acquired)
            {
                _semaphore.Release();
            }

            throw;
        }

        AcquireLockState();
        _logger.LogTrace("Lock acquired (sync)");
    }

    public ValueTask LockAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        // Pre-check: throw immediately if already locked on this instance.
        // Without this, a count=1 semaphore would deadlock rather than reaching AcquireLockState.
        if (Volatile.Read(ref _lockState) != 0)
        {
            throw new InvalidOperationException("Lock already acquired.");
        }

        _logger.LogTrace("Waiting for lock");
        if (_semaphore.Wait(0))
        {
            AcquireLockState();
            _logger.LogTrace("Lock acquired");
            return ValueTask.CompletedTask;
        }

        return LockAsyncSlow(cancellationToken);
    }

    private async ValueTask LockAsyncSlow(CancellationToken cancellationToken)
    {
        _stats?.RecordWaitStart();
        var start = Stopwatch.GetTimestamp();
        var acquired = false;
        var waitEnded = false;
        try
        {
            if (_lockTimeout.HasValue)
            {
                acquired = await _semaphore.WaitAsync(_lockTimeout.Value, cancellationToken).ConfigureAwait(false);
                if (!acquired)
                {
                    var waited = Stopwatch.GetTimestamp() - start;
                    _stats?.RecordTimeout(waited);
                    waitEnded = true;
                    throw new ModeContentionException(_mode, _stats?.GetSnapshot() ?? default, _lockTimeout.Value);
                }
            }
            else
            {
                await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired = true;
            }

            var waitTicks = Stopwatch.GetTimestamp() - start;
            _stats?.RecordWaitEnd(waitTicks);
            waitEnded = true;
        }
        catch
        {
            if (!waitEnded)
            {
                var waited = Stopwatch.GetTimestamp() - start;
                _stats?.RecordWaitEnd(waited);
            }

            if (acquired)
            {
                _semaphore.Release();
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw;
        }

        AcquireLockState();
        _logger.LogTrace("Lock acquired");
    }

    private void AcquireLockState()
    {
        if (Interlocked.CompareExchange(ref _lockState, 1, 0) != 0)
        {
            _semaphore.Release();
            throw new InvalidOperationException("Lock already acquired.");
        }
    }

    public ValueTask<bool> TryLockAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<bool>(cancellationToken);
        }

        // Pre-check: throw immediately if already locked on this instance.
        if (Volatile.Read(ref _lockState) != 0)
        {
            throw new InvalidOperationException("Lock already acquired.");
        }

        _logger.LogTrace("Attempting lock with timeout {Timeout}", timeout);
        if (_semaphore.Wait(0))
        {
            AcquireLockState();
            _logger.LogTrace("Lock acquired");
            return ValueTask.FromResult(true);
        }

        return TryLockAsyncSlow(timeout, cancellationToken);
    }

    private async ValueTask<bool> TryLockAsyncSlow(TimeSpan timeout, CancellationToken cancellationToken)
    {
        _stats?.RecordWaitStart();
        var start = Stopwatch.GetTimestamp();
        bool acquired;
        try
        {
            acquired = await _semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var waited = Stopwatch.GetTimestamp() - start;
            _stats?.RecordWaitEnd(waited);
            throw new OperationCanceledException(cancellationToken);
        }

        var waitTicks = Stopwatch.GetTimestamp() - start;
        if (acquired)
        {
            _stats?.RecordWaitEnd(waitTicks);
            AcquireLockState();
            _logger.LogTrace("Lock acquired");
            return true;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            _stats?.RecordWaitEnd(waitTicks);
            throw new OperationCanceledException(cancellationToken);
        }

        _stats?.RecordTimeout(waitTicks);
        _logger.LogTrace("Lock acquisition timed out");
        return false;
    }

    private void ReleaseIfHeld()
    {
        if (Interlocked.CompareExchange(ref _lockState, 0, 1) == 1)
        {
            _semaphore.Release();
            _logger.LogTrace("Lock released");
        }
    }

    protected override void DisposeManaged()
    {
        // Ensure synchronous disposal releases the semaphore as well.
        ReleaseIfHeld();
    }

    protected override ValueTask DisposeManagedAsync()
    {
        ReleaseIfHeld();
        return ValueTask.CompletedTask;
    }
}
