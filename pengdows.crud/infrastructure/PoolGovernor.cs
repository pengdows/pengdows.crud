// =============================================================================
// FILE: PoolGovernor.cs
// PURPOSE: Semaphore-based pool governor limiting concurrent connection usage.
//
// AI SUMMARY:
// - Controls maximum concurrent connections via SemaphoreSlim.
// - Thread-safe: all counters use Interlocked operations.
// - Key methods:
//   * Acquire(ct): Sync permit acquisition with timeout
//   * AcquireAsync(ct): Async permit acquisition with timeout (uses WaitAsync throughout)
//   * Release(): Returns permit to pool (called by PoolPermit)
//   * GetSnapshot(): Returns current pool statistics
// - Throws PoolSaturatedException when timeout expires waiting for permit.
// - Tracks: inUse, peakInUse, queued, totalAcquired, totalTimeouts.
// - Can be disabled (returns default permits without blocking).
// - Shared semaphore support:
//   * OwnsSemaphore: true if governor created its own semaphore
//   * When using shared semaphore, caller must ensure maxPermits matches actual capacity
//   * Telemetry uses maxPermits as reported capacity (not verified at runtime)
// - Turnstile fairness support (optional):
//   * Prevents writer starvation under reader pressure
//   * Writers (holdTurnstile=true): acquire turnstile before slot, release on permit dispose
//   * Readers (holdTurnstile=false): touch-and-release turnstile, then acquire slot
// - PoolPermit: RAII struct ensuring permit release on dispose.
// =============================================================================

using System.Diagnostics;
using pengdows.crud.exceptions;
using pengdows.crud.metrics;

namespace pengdows.crud.infrastructure;

internal sealed class PoolGovernor : IDisposable
{
    private const string NotInitializedMessage = "Pool governor is not initialized.";

    private readonly PoolLabel _label;
    private readonly string _poolKeyHash;
    private readonly SemaphoreSlim? _semaphore;
    private readonly TimeSpan _acquireTimeout;
    private readonly int _maxPermits;
    private readonly bool _disabled;
    private readonly bool _ownsSemaphore;
    private readonly bool _ownsTurnstile;
    private readonly object _drainLock = new();
    private TaskCompletionSource<bool> _drainSignal;

    // Turnstile fairness support: prevents writer starvation under reader pressure
    private readonly SemaphoreSlim? _turnstile;
    private readonly bool _holdTurnstile;

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
        SemaphoreSlim? sharedSemaphore = null,
        SemaphoreSlim? turnstile = null,
        bool holdTurnstile = false,
        bool ownsTurnstile = false)
    {
        _label = label;
        _poolKeyHash = poolKeyHash;
        _acquireTimeout = acquireTimeout;
        _disabled = disabled;
        _turnstile = turnstile;
        _holdTurnstile = holdTurnstile;
        _ownsTurnstile = ownsTurnstile;
        _drainSignal = CreateDrainSignal(completed: true);

        if (disabled)
        {
            _maxPermits = 0;
            _semaphore = null;
            _ownsSemaphore = false;
            return;
        }

        if (maxPermits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPermits), "Pool governor requires at least one permit.");
        }

        _maxPermits = maxPermits;

        if (sharedSemaphore != null)
        {
            // Shared semaphore: caller is responsible for ensuring maxPermits matches
            // the semaphore's actual capacity. We cannot verify this at runtime since
            // SemaphoreSlim does not expose its max count. Telemetry will use maxPermits
            // as the reported capacity - caller must ensure consistency.
            _semaphore = sharedSemaphore;
            _ownsSemaphore = false;
        }
        else
        {
            _semaphore = new SemaphoreSlim(maxPermits, maxPermits);
            _ownsSemaphore = true;
        }
    }

    /// <summary>
    /// Whether this governor owns its semaphore (vs using a shared one).
    /// When false, telemetry maxPermits may not reflect actual semaphore capacity.
    /// </summary>
    internal bool OwnsSemaphore => _ownsSemaphore;

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
            throw new InvalidOperationException(NotInitializedMessage);
        }

        // Turnstile fairness: acquire turnstile first to prevent starvation
        var turnstileAcquired = false;
        if (_turnstile != null)
        {
            if (!_turnstile.Wait(_acquireTimeout, cancellationToken))
            {
                Interlocked.Increment(ref _totalTimeouts);
                throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
            }

            turnstileAcquired = true;

            // Readers touch-and-release; writers hold until permit released
            if (!_holdTurnstile)
            {
                _turnstile.Release();
                turnstileAcquired = false;
            }
        }

        try
        {
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
        catch
        {
            // On failure, release turnstile if we're still holding it (writers only)
            if (turnstileAcquired && _turnstile != null)
            {
                _turnstile.Release();
            }

            throw;
        }
    }

    public bool TryAcquire(out PoolPermit permit, CancellationToken cancellationToken = default)
    {
        if (_disabled)
        {
            permit = default;
            return true;
        }

        if (_semaphore == null)
        {
            throw new InvalidOperationException(NotInitializedMessage);
        }

        var turnstileAcquired = false;
        if (_turnstile != null)
        {
            if (!_turnstile.Wait(0, cancellationToken))
            {
                permit = default;
                return false;
            }

            turnstileAcquired = true;

            if (!_holdTurnstile)
            {
                _turnstile.Release();
                turnstileAcquired = false;
            }
        }

        if (_semaphore.Wait(0, cancellationToken))
        {
            permit = OnAcquired();
            return true;
        }

        if (turnstileAcquired && _turnstile != null)
        {
            _turnstile.Release();
        }

        permit = default;
        return false;
    }

    public async Task<PoolPermit> AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (_disabled)
        {
            return default;
        }

        if (_semaphore == null)
        {
            throw new InvalidOperationException(NotInitializedMessage);
        }

        // Turnstile fairness: acquire turnstile first to prevent starvation
        var turnstileAcquired = false;
        if (_turnstile != null)
        {
            if (!await _turnstile.WaitAsync(_acquireTimeout, cancellationToken).ConfigureAwait(false))
            {
                Interlocked.Increment(ref _totalTimeouts);
                throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
            }

            turnstileAcquired = true;

            // Readers touch-and-release; writers hold until permit released
            if (!_holdTurnstile)
            {
                _turnstile.Release();
                turnstileAcquired = false;
            }
        }

        try
        {
            // Use WaitAsync even for zero-timeout to maintain consistent async behavior
            // (Wait(0, ct) can throw OperationCanceledException synchronously)
            if (await _semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
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
        catch
        {
            // On failure, release turnstile if we're still holding it (writers only)
            if (turnstileAcquired && _turnstile != null)
            {
                _turnstile.Release();
            }

            throw;
        }
    }

    public async Task<(bool Success, PoolPermit Permit)> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        if (_disabled)
        {
            return (true, default);
        }

        if (_semaphore == null)
        {
            throw new InvalidOperationException(NotInitializedMessage);
        }

        var turnstileAcquired = false;
        if (_turnstile != null)
        {
            if (!await _turnstile.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                return (false, default);
            }

            turnstileAcquired = true;

            if (!_holdTurnstile)
            {
                _turnstile.Release();
                turnstileAcquired = false;
            }
        }

        if (await _semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return (true, OnAcquired());
        }

        if (turnstileAcquired && _turnstile != null)
        {
            _turnstile.Release();
        }

        return (false, default);
    }

    public Task WaitForDrainAsync(CancellationToken cancellationToken = default)
    {
        return WaitForDrainAsync(null, cancellationToken);
    }

    public async Task WaitForDrainAsync(TimeSpan? timeout, CancellationToken cancellationToken = default)
    {
        if (_disabled)
        {
            return;
        }

        if (Interlocked.Read(ref _inUse) == 0)
        {
            return;
        }

        var deadline = timeout.HasValue ? DateTime.UtcNow + timeout.Value : DateTime.MaxValue;
        var signal = GetCurrentDrainSignal();

        for (;;)
        {
            if (signal.Task.IsCompleted)
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Drain timeout");
            }

            try
            {
                await signal.Task.WaitAsync(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (TimeoutException)
            {
                if (Interlocked.Read(ref _inUse) == 0)
                {
                    return;
                }

                signal = GetCurrentDrainSignal();
            }
        }
    }

    private PoolPermit OnAcquired()
    {
        var inUse = Interlocked.Increment(ref _inUse);
        ResetDrainSignalIfNeeded();
        UpdatePeak(ref _peakInUse, inUse);
        Interlocked.Increment(ref _totalAcquired);
        return new PoolPermit(new PoolPermit.PoolPermitToken(this));
    }

    internal void Release()
    {
        Interlocked.Decrement(ref _inUse);

        // Signal drain-waiters only if _inUse is still zero at the instant
        // the signal is set.  The read and the TrySetResult must happen under
        // the same lock that OnAcquired uses to reset the signal; otherwise a
        // concurrent Acquire can increment _inUse without seeing (and
        // resetting) the not-yet-completed signal, leaving it spuriously
        // completed.
        lock (_drainLock)
        {
            if (Interlocked.Read(ref _inUse) == 0 && !_drainSignal.Task.IsCompleted)
            {
                _drainSignal.TrySetResult(true);
            }
        }

        _semaphore?.Release();

        // Writers release turnstile when permit is released
        if (_holdTurnstile && _turnstile != null)
        {
            _turnstile.Release();
        }
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

    private static TaskCompletionSource<bool> CreateDrainSignal(bool completed)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (completed)
        {
            tcs.TrySetResult(true);
        }

        return tcs;
    }

    private void ResetDrainSignalIfNeeded()
    {
        lock (_drainLock)
        {
            if (_drainSignal.Task.IsCompleted)
            {
                _drainSignal = CreateDrainSignal(completed: false);
            }
        }
    }

    private TaskCompletionSource<bool> GetCurrentDrainSignal()
    {
        lock (_drainLock)
        {
            return _drainSignal;
        }
    }

    public void Dispose()
    {
        if (_ownsSemaphore)
        {
            _semaphore?.Dispose();
        }

        if (_ownsTurnstile)
        {
            _turnstile?.Dispose();
        }
    }
}
