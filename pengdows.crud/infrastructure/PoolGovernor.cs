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
// - Tracks: inUse, peakInUse, queued, totalAcquired, totalPermitTimeouts, totalTurnstileTimeouts.
//   * TotalTimeouts in snapshot = semaphore (permit) acquisition timeouts.
//   * TotalTurnstileTimeouts in snapshot = turnstile acquisition timeouts.
// - Can be disabled (returns default permits without blocking).
// - Shared semaphore support:
//   * OwnsSemaphore: true if governor created its own semaphore
//   * When using shared semaphore, caller must ensure maxPermits matches actual capacity
//   * Telemetry uses maxPermits as reported capacity (not verified at runtime)
// - Turnstile fairness support (optional):
//   * Reduces writer starvation risk under sustained reader pressure.
//   * Writers (holdTurnstile=true): hold turnstile for the duration of their permit.
//     - While a writer holds its slot, new readers cannot pass the turnstile.
//     - IMPORTANT: This does NOT drain readers already queued on the semaphore before
//       the writer acquired the turnstile. Starvation is reduced, not eliminated.
//   * Readers (holdTurnstile=false): touch-and-release turnstile, then acquire slot.
//   * Only effective when reader and writer governors share the same turnstile instance
//     and target the same connection pool (same pool key). Governors targeting separate
//     connection pools (e.g., primary + read replica) should use independent turnstiles.
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
    private readonly bool _trackMetrics;
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
    private long _peakQueued;
    private long _turnstileQueued;
    private long _peakTurnstileQueued;
    private long _totalAcquired;
    private long _totalWaitTicks;
    private long _totalHoldTicks;
    private long _totalPermitTimeouts; // timed out waiting for a connection slot
    private long _totalTurnstileTimeouts; // timed out waiting for the fairness turnstile
    private long _totalCanceledWaits;

    public PoolGovernor(
        PoolLabel label,
        string poolKeyHash,
        int maxPermits,
        TimeSpan acquireTimeout,
        bool disabled = false,
        bool trackMetrics = false,
        SemaphoreSlim? sharedSemaphore = null,
        SemaphoreSlim? turnstile = null,
        bool holdTurnstile = false,
        bool ownsTurnstile = false)
    {
        _label = label;
        _poolKeyHash = poolKeyHash;
        _acquireTimeout = acquireTimeout;
        _disabled = disabled;
        _trackMetrics = trackMetrics;
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

        var waitStart = _trackMetrics ? Stopwatch.GetTimestamp() : 0;

        // Turnstile fairness: acquire turnstile first to reduce writer starvation risk.
        // Writers hold the turnstile for their entire permit lifetime; readers touch-and-release.
        // NOTE: readers already queued on the semaphore when the writer grabs the turnstile
        // are not displaced — only NEW reader attempts are gated.
        var turnstileAcquired = false;
        long turnstileAcquiredAt = 0;
        if (_turnstile != null)
        {
            var tQueued = _trackMetrics ? Interlocked.Increment(ref _turnstileQueued) : 0;
            if (_trackMetrics) UpdatePeak(ref _peakTurnstileQueued, tQueued);

            try
            {
                if (!_turnstile.Wait(_acquireTimeout, cancellationToken))
                {
                    Interlocked.Increment(ref _totalTurnstileTimeouts);
                    throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
                }
            }
            finally
            {
                if (_trackMetrics) Interlocked.Decrement(ref _turnstileQueued);
            }

            turnstileAcquired = true;
            if (_trackMetrics) turnstileAcquiredAt = Stopwatch.GetTimestamp();

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
                return OnAcquired(waitStart);
            }

            var queued = _trackMetrics ? Interlocked.Increment(ref _queued) : 0;
            if (_trackMetrics) UpdatePeak(ref _peakQueued, queued);

            try
            {
                try
                {
                    var acquired = _semaphore.Wait(_acquireTimeout, cancellationToken);
                    if (!acquired)
                    {
                        Interlocked.Increment(ref _totalPermitTimeouts);
                        throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
                    }

                    return OnAcquired(waitStart);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref _totalCanceledWaits);
                    throw;
                }
            }
            finally
            {
                if (_trackMetrics) Interlocked.Decrement(ref _queued);
            }
        }
        catch {
            // On failure, release turnstile if we're still holding it (writers only)
            if (turnstileAcquired && _turnstile != null)
            {
                if (_trackMetrics) RecordWaitAndHold(waitStart, turnstileAcquiredAt, Stopwatch.GetTimestamp());
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

        var waitStart = _trackMetrics ? Stopwatch.GetTimestamp() : 0;
        var turnstileAcquired = false;
        long turnstileAcquiredAt = 0;
        if (_turnstile != null)
        {
            if (!_turnstile.Wait(0, cancellationToken))
            {
                permit = default;
                return false;
            }

            turnstileAcquired = true;
            if (_trackMetrics) turnstileAcquiredAt = Stopwatch.GetTimestamp();

            if (!_holdTurnstile)
            {
                _turnstile.Release();
                turnstileAcquired = false;
            }
        }

        if (_semaphore.Wait(0, cancellationToken))
        {
            permit = OnAcquired(waitStart);
            return true;
        }

        if (turnstileAcquired && _turnstile != null)
        {
            if (_trackMetrics) RecordWaitAndHold(waitStart, turnstileAcquiredAt, Stopwatch.GetTimestamp());
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

        var waitStart = _trackMetrics ? Stopwatch.GetTimestamp() : 0;

        // Turnstile fairness: acquire turnstile first to reduce writer starvation risk.
        // Writers hold the turnstile for their entire permit lifetime; readers touch-and-release.
        // NOTE: readers already queued on the semaphore when the writer grabs the turnstile
        // are not displaced — only NEW reader attempts are gated.
        var turnstileAcquired = false;
        long turnstileAcquiredAt = 0;
        if (_turnstile != null)
        {
            var tQueued = _trackMetrics ? Interlocked.Increment(ref _turnstileQueued) : 0;
            if (_trackMetrics) UpdatePeak(ref _peakTurnstileQueued, tQueued);

            try
            {
                if (!await _turnstile.WaitAsync(_acquireTimeout, cancellationToken).ConfigureAwait(false))
                {
                    Interlocked.Increment(ref _totalTurnstileTimeouts);
                    throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
                }
            }
            finally
            {
                if (_trackMetrics) Interlocked.Decrement(ref _turnstileQueued);
            }

            turnstileAcquired = true;
            if (_trackMetrics) turnstileAcquiredAt = Stopwatch.GetTimestamp();

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
                return OnAcquired(waitStart);
            }

            var queued = _trackMetrics ? Interlocked.Increment(ref _queued) : 0;
            if (_trackMetrics) UpdatePeak(ref _peakQueued, queued);

            try
            {
                try
                {
                    var acquired = await _semaphore.WaitAsync(_acquireTimeout, cancellationToken).ConfigureAwait(false);
                    if (!acquired)
                    {
                        Interlocked.Increment(ref _totalPermitTimeouts);
                        throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
                    }

                    return OnAcquired(waitStart);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref _totalCanceledWaits);
                    throw;
                }
            }
            finally
            {
                if (_trackMetrics) Interlocked.Decrement(ref _queued);
            }
        }
        catch {
            // On failure, release turnstile if we're still holding it (writers only)
            if (turnstileAcquired && _turnstile != null)
            {
                if (_trackMetrics) RecordWaitAndHold(waitStart, turnstileAcquiredAt, Stopwatch.GetTimestamp());
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

        var waitStart = _trackMetrics ? Stopwatch.GetTimestamp() : 0;
        var turnstileAcquired = false;
        long turnstileAcquiredAt = 0;
        if (_turnstile != null)
        {
            if (!await _turnstile.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                return (false, default);
            }

            turnstileAcquired = true;
            if (_trackMetrics) turnstileAcquiredAt = Stopwatch.GetTimestamp();

            if (!_holdTurnstile)
            {
                _turnstile.Release();
                turnstileAcquired = false;
            }
        }

        if (await _semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return (true, OnAcquired(waitStart));
        }

        if (turnstileAcquired && _turnstile != null)
        {
            if (_trackMetrics) RecordWaitAndHold(waitStart, turnstileAcquiredAt, Stopwatch.GetTimestamp());
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

        // Use a linked CancellationTokenSource so we can apply a deadline without
        // polling.  When the drain signal fires we return immediately; when the
        // timeout (or caller token) fires we surface the appropriate exception.
        using var timeoutCts = timeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        timeoutCts?.CancelAfter(timeout!.Value);
        var effectiveToken = timeoutCts?.Token ?? cancellationToken;

        while (Interlocked.Read(ref _inUse) > 0)
        {
            var signal = GetCurrentDrainSignal();

            if (signal.Task.IsCompleted)
            {
                // Signal already set — re-check _inUse before returning.
                // A concurrent Acquire() may have bumped it back up.
                break;
            }

            try
            {
                await signal.Task.WaitAsync(effectiveToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts != null
                                                     && timeoutCts.IsCancellationRequested
                                                     && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Drain timeout");
            }
            catch (OperationCanceledException)
            {
                // Task.WaitAsync throws TaskCanceledException (a subclass of
                // OperationCanceledException).  Normalize to the base type so
                // callers see a consistent exception regardless of runtime version.
                throw new OperationCanceledException(cancellationToken);
            }
            // Signal fired (or was reset and re-signalled) — loop to re-check _inUse.
        }
    }

    private void RecordWaitAndHold(long waitStart, long acquiredAt, long releasedAt)
    {
        var waitTicks = acquiredAt - waitStart;
        var holdTicks = releasedAt - acquiredAt;

        if (waitTicks > 0) Interlocked.Add(ref _totalWaitTicks, waitTicks);
        if (holdTicks > 0) Interlocked.Add(ref _totalHoldTicks, holdTicks);
    }

    private PoolPermit OnAcquired(long waitStart)
    {
        var inUse = Interlocked.Increment(ref _inUse);
        ResetDrainSignalIfNeeded();
        UpdatePeak(ref _peakInUse, inUse);
        Interlocked.Increment(ref _totalAcquired);
        return new PoolPermit(new PoolPermit.PoolPermitToken(this, waitStart));
    }

    internal void ReleaseToken(long waitStart, long acquiredAt, long releasedAt)
    {
        RecordWaitAndHold(waitStart, acquiredAt, releasedAt);
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
            (int)Math.Clamp(Interlocked.Read(ref _peakQueued), 0L, int.MaxValue),
            (int)Math.Clamp(Interlocked.Read(ref _turnstileQueued), 0L, int.MaxValue),
            (int)Math.Clamp(Interlocked.Read(ref _peakTurnstileQueued), 0L, int.MaxValue),
            Interlocked.Read(ref _totalAcquired),
            Interlocked.Read(ref _totalWaitTicks),
            Interlocked.Read(ref _totalHoldTicks),
            Interlocked.Read(ref _totalPermitTimeouts),
            Interlocked.Read(ref _totalTurnstileTimeouts),
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