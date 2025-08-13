using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.infrastructure;

namespace pengdows.crud.threading;

internal sealed class RealAsyncLocker : SafeAsyncDisposableBase, ILockerAsync
{
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger<RealAsyncLocker> _logger;
    private int _lockState;

    public RealAsyncLocker(SemaphoreSlim semaphore, ILogger<RealAsyncLocker>? logger = null)
    {
        _semaphore = semaphore ?? throw new ArgumentNullException(nameof(semaphore));
        _logger = logger ?? NullLogger<RealAsyncLocker>.Instance;
    }

    public async Task LockAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _logger.LogTrace("Waiting for lock");
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (Interlocked.CompareExchange(ref _lockState, 1, 0) != 0)
        {
            _semaphore.Release();
            throw new InvalidOperationException("Lock already acquired.");
        }

        _logger.LogTrace("Lock acquired");
    }

    public async Task<bool> TryLockAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _logger.LogTrace("Attempting lock with timeout {Timeout}", timeout);
        var acquired = await _semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        if (acquired)
        {
            if (Interlocked.CompareExchange(ref _lockState, 1, 0) != 0)
            {
                _semaphore.Release();
                throw new InvalidOperationException("Lock already acquired.");
            }

            _logger.LogTrace("Lock acquired");
        }
        else
        {
            _logger.LogTrace("Lock acquisition timed out");
        }

        return acquired;
    }

    protected override ValueTask DisposeManagedAsync()
    {
        if (Interlocked.CompareExchange(ref _lockState, 0, 1) == 1)
        {
            _semaphore.Release();
            _logger.LogTrace("Lock released");
        }

        return ValueTask.CompletedTask;
    }
}

