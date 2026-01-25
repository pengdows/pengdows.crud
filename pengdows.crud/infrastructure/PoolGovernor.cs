using System.Diagnostics;
using pengdows.crud.exceptions;
using pengdows.crud.metrics;

namespace pengdows.crud.infrastructure;

internal sealed class PoolGovernor
{
    private readonly PoolLabel _label;
    private readonly string _poolKeyHash;
    private readonly SemaphoreSlim? _semaphore;
    private readonly TimeSpan _acquireTimeout;
    private readonly int _maxPermits;
    private readonly bool _disabled;

    private long _inUse;
    private long _peakInUse;
    private long _queued;
    private long _totalAcquired;
    private long _totalTimeouts;
    private long _totalCanceledWaits;

    public PoolGovernor(
        PoolLabel label,
        string poolKeyHash,
        int maxPermits,
        TimeSpan acquireTimeout,
        bool disabled = false,
        SemaphoreSlim? sharedSemaphore = null)
    {
        _label = label;
        _poolKeyHash = poolKeyHash;
        _acquireTimeout = acquireTimeout;
        _disabled = disabled;

        if (disabled)
        {
            _maxPermits = 0;
            _semaphore = null;
            return;
        }

        if (maxPermits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPermits), "Pool governor requires at least one permit.");
        }

        _maxPermits = maxPermits;
        _semaphore = sharedSemaphore ?? new SemaphoreSlim(maxPermits, maxPermits);
    }

    public PoolLabel Label => _label;
    public string PoolKeyHash => _poolKeyHash;

    public PoolPermit Acquire(CancellationToken cancellationToken = default)
    {
        if (_disabled)
        {
            return default;
        }

        if (_semaphore == null)
        {
            throw new InvalidOperationException("Pool governor is not initialized.");
        }

        if (_semaphore.Wait(0, cancellationToken))
        {
            return OnAcquired();
        }

        var waitStart = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _queued);

        try
        {
            try
            {
                var acquired = _semaphore.Wait(_acquireTimeout, cancellationToken);
                if (!acquired)
                {
                    Interlocked.Increment(ref _totalTimeouts);
                    throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
                }

                return OnAcquired();
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _totalCanceledWaits);
                throw;
            }
        }
        finally
        {
            Interlocked.Decrement(ref _queued);
            _ = waitStart; // keep for parity with async; no extra metrics today
        }
    }

    public async Task<PoolPermit> AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (_disabled)
        {
            return default;
        }

        if (_semaphore == null)
        {
            throw new InvalidOperationException("Pool governor is not initialized.");
        }

        if (_semaphore.Wait(0, cancellationToken))
        {
            return OnAcquired();
        }

        var waitStart = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _queued);

        try
        {
            try
            {
                var acquired = await _semaphore.WaitAsync(_acquireTimeout, cancellationToken).ConfigureAwait(false);
                if (!acquired)
                {
                    Interlocked.Increment(ref _totalTimeouts);
                    throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
                }

                return OnAcquired();
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _totalCanceledWaits);
                throw;
            }
        }
        finally
        {
            Interlocked.Decrement(ref _queued);
            _ = waitStart; // keep for parity with sync path
        }
    }

    private PoolPermit OnAcquired()
    {
        var inUse = Interlocked.Increment(ref _inUse);
        UpdatePeak(ref _peakInUse, inUse);
        Interlocked.Increment(ref _totalAcquired);
        return new PoolPermit(new PoolPermit.PoolPermitToken(this));
    }

    internal void Release()
    {
        Interlocked.Decrement(ref _inUse);
        _semaphore?.Release();
    }

    public PoolStatisticsSnapshot GetSnapshot()
    {
        return new PoolStatisticsSnapshot(
            _label,
            _poolKeyHash,
            _maxPermits,
            (int)Math.Clamp(Interlocked.Read(ref _inUse), 0L, int.MaxValue),
            (int)Math.Clamp(Interlocked.Read(ref _peakInUse), 0L, int.MaxValue),
            (int)Math.Clamp(Interlocked.Read(ref _queued), 0L, int.MaxValue),
            Interlocked.Read(ref _totalAcquired),
            Interlocked.Read(ref _totalTimeouts),
            Interlocked.Read(ref _totalCanceledWaits),
            _disabled);
    }

    private static void UpdatePeak(ref long peak, long current)
    {
        while (true)
        {
            var existing = Interlocked.Read(ref peak);
            if (current <= existing)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref peak, current, existing) == existing)
            {
                return;
            }
        }
    }
}