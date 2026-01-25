using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
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

    public async Task LockAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (cancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException();
        }

        _logger.LogTrace("Waiting for lock");
        if (_semaphore.Wait(0))
        {
            AcquireLockState();
            _logger.LogTrace("Lock acquired");
            return;
        }

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
                throw new TaskCanceledException();
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

    public async Task<bool> TryLockAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (cancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException();
        }

        _logger.LogTrace("Attempting lock with timeout {Timeout}", timeout);
        if (_semaphore.Wait(0))
        {
            AcquireLockState();
            _logger.LogTrace("Lock acquired");
            return true;
        }

        _stats?.RecordWaitStart();
        var start = Stopwatch.GetTimestamp();
        bool acquired;
        try
        {
            acquired = await _semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException();
        }

        if (acquired)
        {
            var waitTicks = Stopwatch.GetTimestamp() - start;
            _stats?.RecordWaitEnd(waitTicks);
            AcquireLockState();
            _logger.LogTrace("Lock acquired");
        }
        else
        {
            var waitTicks = Stopwatch.GetTimestamp() - start;
            _stats?.RecordTimeout(waitTicks);
            _logger.LogTrace("Lock acquisition timed out");
        }

        return acquired;
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