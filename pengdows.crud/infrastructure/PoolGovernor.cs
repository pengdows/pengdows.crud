using pengdows.crud.enums;
// =============================================================================
// FILE: PoolGovernor.cs
// PURPOSE: Semaphore-based pool governor limiting concurrent connection usage.
//
// AI SUMMARY:
// - Controls maximum concurrent connections via SemaphoreSlim.
// - Thread-safe: all counters use Interlocked operations.
// - Key methods:
//   * Acquire(ct): Sync slot acquisition with timeout
//   * AcquireAsync(ct): Async permit acquisition with timeout (uses WaitAsync throughout)
//   * Release(): Returns permit to pool (called by PoolSlot)
//   * GetSnapshot(): Returns current pool statistics
// - Throws PoolSaturatedException when timeout expires waiting for slot.
// - Tracks: inUse, peakInUse, queued, totalAcquired, totalSlotTimeouts, totalTurnstileTimeouts.
//   * TotalTimeouts in snapshot = semaphore (slot) acquisition timeouts.
//   * TotalTurnstileTimeouts in snapshot = turnstile acquisition timeouts.
// - Can be disabled (returns default slots without blocking).
// - Shared semaphore support:
//   * OwnsSemaphore: true if governor created its own semaphore
//   * When using shared semaphore, caller must ensure maxSlots matches actual capacity
//   * Telemetry uses maxSlots as reported capacity (not verified at runtime)
// - Turnstile fairness support (optional):
//   * Reduces writer starvation risk under sustained reader pressure.
//   * Writers (holdTurnstile=true): hold turnstile for the duration of their slot.
//     - While a writer holds its slot, new readers cannot pass the turnstile.
//     - IMPORTANT: This does NOT drain readers already queued on the semaphore before
//       the writer acquired the turnstile. Starvation is reduced, not eliminated.
//   * Readers (holdTurnstile=false): gate on turnstile only while writers are
//     active/waiting; otherwise bypass turnstile and go straight to slot acquire.
//   * Only effective when reader and writer governors share the same turnstile instance
//     and target the same connection pool (same pool key). Governors targeting separate
//     connection pools (e.g., primary + read replica) should use independent turnstiles.
// - PoolSlot: RAII struct ensuring slot release on dispose.
// =============================================================================

using System.Diagnostics;
using System.Runtime.CompilerServices;
using pengdows.crud.exceptions;
using pengdows.crud.metrics;

namespace pengdows.crud.infrastructure;

internal sealed class PoolGovernor : IDisposable
{
    private const string NotInitializedMessage = "Pool governor is not initialized.";
    private static readonly ConditionalWeakTable<SemaphoreSlim, TurnstileState> SharedTurnstileStates = new();

    private readonly PoolLabel _label;
    private readonly string _poolKeyHash;
    private readonly SemaphoreSlim? _semaphore;
    private readonly TimeSpan _acquireTimeout;
    private readonly long _acquireTimeoutStopwatchTicks;
    private readonly int _maxSlots;
    private readonly bool _disabled;
    private readonly bool _forbidden;
    private readonly bool _trackMetrics;
    private readonly bool _ownsSemaphore;
    private readonly bool _ownsTurnstile;
    private readonly object _drainLock = new();
    private TaskCompletionSource<bool> _drainSignal;

    // Turnstile fairness support: prevents writer starvation under reader pressure
    private readonly SemaphoreSlim? _turnstile;
    private readonly TurnstileState? _turnstileState;
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
    private long _totalSlotTimeouts; // timed out waiting for a connection slot
    private long _totalTurnstileTimeouts; // timed out waiting for the fairness turnstile
    private long _totalCanceledWaits;

    public PoolGovernor(
        PoolLabel label,
        string poolKeyHash,
        int maxSlots,
        TimeSpan acquireTimeout,
        bool disabled = false,
        bool forbidden = false,
        bool trackMetrics = false,
        SemaphoreSlim? sharedSemaphore = null,
        SemaphoreSlim? turnstile = null,
        bool holdTurnstile = false,
        bool ownsTurnstile = false)
    {
        _label = label;
        _poolKeyHash = poolKeyHash;
        _acquireTimeout = acquireTimeout;
        _acquireTimeoutStopwatchTicks = ConvertTimeoutToStopwatchTicks(acquireTimeout);
        _trackMetrics = trackMetrics;
        _turnstile = turnstile;
        _turnstileState = turnstile == null
            ? null
            : SharedTurnstileStates.GetValue(turnstile, static _ => new TurnstileState());
        _holdTurnstile = holdTurnstile;
        _ownsTurnstile = ownsTurnstile;
        _drainSignal = CreateDrainSignal(completed: true);

        if (disabled)
        {
            _disabled = true;
            _maxSlots = 0;
            _semaphore = null;
            _ownsSemaphore = false;
            return;
        }

        if (forbidden)
        {
            _forbidden = true;
            _maxSlots = 0;
            _semaphore = null;
            _ownsSemaphore = false;
            return;
        }

        if (maxSlots <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSlots), "Pool governor requires at least one slot.");
        }

        _maxSlots = maxSlots;

        if (sharedSemaphore != null)
        {
            // Shared semaphore: caller is responsible for ensuring maxSlots matches
            // the semaphore's actual capacity. We cannot verify this at runtime since
            // SemaphoreSlim does not expose its max count. Telemetry will use maxSlots
            // as the reported capacity - caller must ensure consistency.
            _semaphore = sharedSemaphore;
            _ownsSemaphore = false;
        }
        else
        {
            _semaphore = new SemaphoreSlim(maxSlots, maxSlots);
            _ownsSemaphore = true;
        }
    }

    /// <summary>
    /// Whether this governor owns its semaphore (vs using a shared one).
    /// When false, telemetry maxSlots may not reflect actual semaphore capacity.
    /// </summary>
    internal bool OwnsSemaphore => _ownsSemaphore;

    /// <summary>
    /// Whether this governor is forbidden (MaxPoolSize=0).
    /// Forbidden governors throw <see cref="PoolForbiddenException"/> on every acquire attempt.
    /// </summary>
    internal bool Forbidden => _forbidden;

    public PoolLabel Label => _label;
    public string PoolKeyHash => _poolKeyHash;

    public PoolSlot Acquire(CancellationToken cancellationToken = default)
    {
        if (_forbidden)
        {
            throw new PoolForbiddenException(_label, _poolKeyHash);
        }

        if (_disabled)
        {
            return default;
        }

        if (_semaphore == null)
        {
            throw new InvalidOperationException(NotInitializedMessage);
        }

        var turnstileAcquired = false;
        var writerTurnstileInterestRegistered = false;
        try
        {
            RegisterWriterTurnstileInterest(ref writerTurnstileInterestRegistered);
            var useTurnstileGate = ShouldUseTurnstileGate(writerTurnstileInterestRegistered);

            // Fast path: no deadline arithmetic for immediate gate/slot success.
            if (useTurnstileGate && _turnstile != null && _turnstile.Wait(0, cancellationToken))
            {
                turnstileAcquired = true;
                if (!_holdTurnstile)
                {
                    _turnstile.Release();
                    turnstileAcquired = false;
                }

                if (_semaphore.Wait(0, cancellationToken))
                {
                    var immediateWaitStart = _trackMetrics ? Stopwatch.GetTimestamp() : 0;
                    var releaseWriterInterestOnRelease = _holdTurnstile && writerTurnstileInterestRegistered;
                    writerTurnstileInterestRegistered = false;
                    return OnAcquired(immediateWaitStart, releaseWriterInterestOnRelease);
                }
            }
            else if (!useTurnstileGate && _semaphore.Wait(0, cancellationToken))
            {
                var immediateWaitStart = _trackMetrics ? Stopwatch.GetTimestamp() : 0;
                var releaseWriterInterestOnRelease = _holdTurnstile && writerTurnstileInterestRegistered;
                writerTurnstileInterestRegistered = false;
                return OnAcquired(immediateWaitStart, releaseWriterInterestOnRelease);
            }

            // Slow path: timed waits with a single deadline budget across gates.
            var waitStart = Stopwatch.GetTimestamp();
            var deadlineTicks = waitStart + _acquireTimeoutStopwatchTicks;

            // Turnstile fairness: acquire turnstile first to reduce writer starvation risk.
            // Writers hold the turnstile for their entire slot lifetime.
            // Readers only gate here when writers are active/waiting; otherwise they bypass.
            // NOTE: readers already queued on the semaphore when the writer grabs the turnstile
            // are not displaced — only NEW reader attempts are gated.
            if (useTurnstileGate && _turnstile != null && !turnstileAcquired)
            {
                var tQueued = _trackMetrics ? Interlocked.Increment(ref _turnstileQueued) : 0;
                if (_trackMetrics)
                {
                    UpdatePeak(ref _peakTurnstileQueued, tQueued);
                }

                try
                {
                    var turnstileRemaining = GetRemainingTimeout(deadlineTicks);
                    if (turnstileRemaining == TimeSpan.Zero
                        || !_turnstile.Wait(turnstileRemaining, cancellationToken))
                    {
                        Interlocked.Increment(ref _totalTurnstileTimeouts);
                        throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
                    }
                }
                finally
                {
                    if (_trackMetrics)
                    {
                        Interlocked.Decrement(ref _turnstileQueued);
                    }
                }

                turnstileAcquired = true;

                // Readers touch-and-release; writers hold until slot released
                if (!_holdTurnstile)
                {
                    _turnstile.Release();
                    turnstileAcquired = false;
                }
            }

            if (_semaphore.Wait(0, cancellationToken))
            {
                var releaseWriterInterestOnRelease = _holdTurnstile && writerTurnstileInterestRegistered;
                writerTurnstileInterestRegistered = false;
                return OnAcquired(waitStart, releaseWriterInterestOnRelease);
            }

            var queued = _trackMetrics ? Interlocked.Increment(ref _queued) : 0;
            if (_trackMetrics)
            {
                UpdatePeak(ref _peakQueued, queued);
            }

            try
            {
                try
                {
                    var semRemaining = GetRemainingTimeout(deadlineTicks);
                    if (semRemaining == TimeSpan.Zero)
                    {
                        Interlocked.Increment(ref _totalSlotTimeouts);
                        throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
                    }

                    var acquired = _semaphore.Wait(semRemaining, cancellationToken);
                    if (!acquired)
                    {
                        Interlocked.Increment(ref _totalSlotTimeouts);
                        throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
                    }

                    var releaseWriterInterestOnRelease = _holdTurnstile && writerTurnstileInterestRegistered;
                    writerTurnstileInterestRegistered = false;
                    return OnAcquired(waitStart, releaseWriterInterestOnRelease);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref _totalCanceledWaits);
                    throw;
                }
            }
            finally
            {
                if (_trackMetrics)
                {
                    Interlocked.Decrement(ref _queued);
                }
            }
        }
        catch
        {
            // On failure, release turnstile if we're still holding it (writers only).
            // Do NOT record wait/hold metrics here — failure duration is not slot hold time.
            if (turnstileAcquired && _turnstile != null)
            {
                _turnstile.Release();
            }

            UnregisterWriterTurnstileInterest(ref writerTurnstileInterestRegistered);
            throw;
        }
    }

    public bool TryAcquire(out PoolSlot slot, CancellationToken cancellationToken = default)
    {
        if (_forbidden)
        {
            throw new PoolForbiddenException(_label, _poolKeyHash);
        }

        if (_disabled)
        {
            slot = default;
            return true;
        }

        if (_semaphore == null)
        {
            throw new InvalidOperationException(NotInitializedMessage);
        }

        var waitStart = _trackMetrics ? Stopwatch.GetTimestamp() : 0;
        var turnstileAcquired = false;
        var writerTurnstileInterestRegistered = false;
        RegisterWriterTurnstileInterest(ref writerTurnstileInterestRegistered);
        var useTurnstileGate = ShouldUseTurnstileGate(writerTurnstileInterestRegistered);
        if (useTurnstileGate && _turnstile != null)
        {
            if (!_turnstile.Wait(0, cancellationToken))
            {
                UnregisterWriterTurnstileInterest(ref writerTurnstileInterestRegistered);
                slot = default;
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
            var releaseWriterInterestOnRelease = _holdTurnstile && writerTurnstileInterestRegistered;
            writerTurnstileInterestRegistered = false;
            slot = OnAcquired(waitStart, releaseWriterInterestOnRelease);
            return true;
        }

        // Slot miss — release turnstile without recording hold metrics.
        // Failure duration is not slot hold time.
        if (turnstileAcquired && _turnstile != null)
        {
            _turnstile.Release();
        }

        UnregisterWriterTurnstileInterest(ref writerTurnstileInterestRegistered);
        slot = default;
        return false;
    }

    public async ValueTask<PoolSlot> AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (_forbidden)
        {
            throw new PoolForbiddenException(_label, _poolKeyHash);
        }

        if (_disabled)
        {
            return default;
        }

        if (_semaphore == null)
        {
            throw new InvalidOperationException(NotInitializedMessage);
        }

        var turnstileAcquired = false;
        var writerTurnstileInterestRegistered = false;
        try
        {
            RegisterWriterTurnstileInterest(ref writerTurnstileInterestRegistered);
            var useTurnstileGate = ShouldUseTurnstileGate(writerTurnstileInterestRegistered);

            // Fast path: no deadline arithmetic for immediate gate/slot success.
            if (useTurnstileGate && _turnstile != null
                && await _turnstile.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                turnstileAcquired = true;
                if (!_holdTurnstile)
                {
                    _turnstile.Release();
                    turnstileAcquired = false;
                }

                if (await _semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                {
                    var immediateWaitStart = _trackMetrics ? Stopwatch.GetTimestamp() : 0;
                    var releaseWriterInterestOnRelease = _holdTurnstile && writerTurnstileInterestRegistered;
                    writerTurnstileInterestRegistered = false;
                    return OnAcquired(immediateWaitStart, releaseWriterInterestOnRelease);
                }
            }
            else if (!useTurnstileGate && await _semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                var immediateWaitStart = _trackMetrics ? Stopwatch.GetTimestamp() : 0;
                var releaseWriterInterestOnRelease = _holdTurnstile && writerTurnstileInterestRegistered;
                writerTurnstileInterestRegistered = false;
                return OnAcquired(immediateWaitStart, releaseWriterInterestOnRelease);
            }

            // Slow path: timed waits with a single deadline budget across gates.
            var waitStart = Stopwatch.GetTimestamp();
            var deadlineTicks = waitStart + _acquireTimeoutStopwatchTicks;

            // Turnstile fairness: acquire turnstile first to reduce writer starvation risk.
            // Writers hold the turnstile for their entire slot lifetime.
            // Readers only gate here when writers are active/waiting; otherwise they bypass.
            // NOTE: readers already queued on the semaphore when the writer grabs the turnstile
            // are not displaced — only NEW reader attempts are gated.
            if (useTurnstileGate && _turnstile != null && !turnstileAcquired)
            {
                var tQueued = _trackMetrics ? Interlocked.Increment(ref _turnstileQueued) : 0;
                if (_trackMetrics)
                {
                    UpdatePeak(ref _peakTurnstileQueued, tQueued);
                }

                try
                {
                    var turnstileRemaining = GetRemainingTimeout(deadlineTicks);
                    if (turnstileRemaining == TimeSpan.Zero
                        || !await _turnstile.WaitAsync(turnstileRemaining, cancellationToken).ConfigureAwait(false))
                    {
                        Interlocked.Increment(ref _totalTurnstileTimeouts);
                        throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
                    }
                }
                finally
                {
                    if (_trackMetrics)
                    {
                        Interlocked.Decrement(ref _turnstileQueued);
                    }
                }

                turnstileAcquired = true;

                // Readers touch-and-release; writers hold until slot released
                if (!_holdTurnstile)
                {
                    _turnstile.Release();
                    turnstileAcquired = false;
                }
            }

            // Use WaitAsync even for zero-timeout to maintain consistent async behavior
            // (Wait(0, ct) can throw OperationCanceledException synchronously)
            if (await _semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                var releaseWriterInterestOnRelease = _holdTurnstile && writerTurnstileInterestRegistered;
                writerTurnstileInterestRegistered = false;
                return OnAcquired(waitStart, releaseWriterInterestOnRelease);
            }

            var queued = _trackMetrics ? Interlocked.Increment(ref _queued) : 0;
            if (_trackMetrics)
            {
                UpdatePeak(ref _peakQueued, queued);
            }

            try
            {
                try
                {
                    var semRemaining = GetRemainingTimeout(deadlineTicks);
                    if (semRemaining == TimeSpan.Zero)
                    {
                        Interlocked.Increment(ref _totalSlotTimeouts);
                        throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
                    }

                    var acquired = await _semaphore.WaitAsync(semRemaining, cancellationToken).ConfigureAwait(false);
                    if (!acquired)
                    {
                        Interlocked.Increment(ref _totalSlotTimeouts);
                        throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
                    }

                    var releaseWriterInterestOnRelease = _holdTurnstile && writerTurnstileInterestRegistered;
                    writerTurnstileInterestRegistered = false;
                    return OnAcquired(waitStart, releaseWriterInterestOnRelease);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref _totalCanceledWaits);
                    throw;
                }
            }
            finally
            {
                if (_trackMetrics)
                {
                    Interlocked.Decrement(ref _queued);
                }
            }
        }
        catch
        {
            // On failure, release turnstile if we're still holding it (writers only).
            // Do NOT record wait/hold metrics here — failure duration is not slot hold time.
            if (turnstileAcquired && _turnstile != null)
            {
                _turnstile.Release();
            }

            UnregisterWriterTurnstileInterest(ref writerTurnstileInterestRegistered);
            throw;
        }
    }

    public async ValueTask<(bool Success, PoolSlot Permit)> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        if (_forbidden)
        {
            throw new PoolForbiddenException(_label, _poolKeyHash);
        }

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
        var writerTurnstileInterestRegistered = false;
        RegisterWriterTurnstileInterest(ref writerTurnstileInterestRegistered);
        var useTurnstileGate = ShouldUseTurnstileGate(writerTurnstileInterestRegistered);
        if (useTurnstileGate && _turnstile != null)
        {
            if (!await _turnstile.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                UnregisterWriterTurnstileInterest(ref writerTurnstileInterestRegistered);
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
            var releaseWriterInterestOnRelease = _holdTurnstile && writerTurnstileInterestRegistered;
            writerTurnstileInterestRegistered = false;
            return (true, OnAcquired(waitStart, releaseWriterInterestOnRelease));
        }

        // Slot miss — release turnstile without recording hold metrics.
        // Do NOT record wait/hold metrics here — failure duration is not slot hold time.
        if (turnstileAcquired && _turnstile != null)
        {
            _turnstile.Release();
        }

        UnregisterWriterTurnstileInterest(ref writerTurnstileInterestRegistered);
        return (false, default);
    }

    public Task WaitForDrainAsync(CancellationToken cancellationToken = default)
    {
        return WaitForDrainAsync(null, cancellationToken);
    }

    public async Task WaitForDrainAsync(TimeSpan? timeout, CancellationToken cancellationToken = default)
    {
        if (_disabled || _forbidden)
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
        if (!_trackMetrics)
        {
            return;
        }
        var waitTicks = acquiredAt - waitStart;
        var holdTicks = releasedAt - acquiredAt;

        if (waitTicks > 0)
        {
            Interlocked.Add(ref _totalWaitTicks, waitTicks);
        }

        if (holdTicks > 0)
        {
            Interlocked.Add(ref _totalHoldTicks, holdTicks);
        }
    }

    /// <summary>
    /// Returns the time remaining before the acquisition deadline expires.
    /// Never returns negative — returns <see cref="TimeSpan.Zero"/> once the deadline has passed.
    /// </summary>
    private static TimeSpan GetRemainingTimeout(long deadlineTicks)
    {
        var remainingTicks = deadlineTicks - Stopwatch.GetTimestamp();
        if (remainingTicks <= 0)
        {
            return TimeSpan.Zero;
        }
        return TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
    }

    private static long ConvertTimeoutToStopwatchTicks(TimeSpan timeout)
    {
        var converted = timeout.Ticks / TimeSpan.TicksPerSecond * Stopwatch.Frequency
                        + timeout.Ticks % TimeSpan.TicksPerSecond * Stopwatch.Frequency / TimeSpan.TicksPerSecond;

        return Math.Max(1, converted);
    }

    private bool ShouldUseTurnstileGate(bool writerTurnstileInterestRegistered)
    {
        if (_turnstile == null)
        {
            return false;
        }

        if (_holdTurnstile)
        {
            return true;
        }

        if (writerTurnstileInterestRegistered)
        {
            return true;
        }

        var state = _turnstileState;
        return state != null && Volatile.Read(ref state.WritersActiveOrWaiting) > 0;
    }

    private void RegisterWriterTurnstileInterest(ref bool writerTurnstileInterestRegistered)
    {
        if (!_holdTurnstile || _turnstileState == null || writerTurnstileInterestRegistered)
        {
            return;
        }

        Interlocked.Increment(ref _turnstileState.WritersActiveOrWaiting);
        writerTurnstileInterestRegistered = true;
    }

    private void UnregisterWriterTurnstileInterest(ref bool writerTurnstileInterestRegistered)
    {
        if (!writerTurnstileInterestRegistered || _turnstileState == null)
        {
            return;
        }

        Interlocked.Decrement(ref _turnstileState.WritersActiveOrWaiting);
        writerTurnstileInterestRegistered = false;
    }

    private PoolSlot OnAcquired(long waitStart, bool releaseWriterTurnstileInterestOnRelease)
    {
        var inUse = Interlocked.Increment(ref _inUse);
        ResetDrainSignalIfNeeded();
        UpdatePeak(ref _peakInUse, inUse);
        Interlocked.Increment(ref _totalAcquired);
        return new PoolSlot(new PoolSlot.PoolSlotToken(this, waitStart, releaseWriterTurnstileInterestOnRelease));
    }

    internal void ReleaseToken(long waitStart, long acquiredAt, long releasedAt, bool releaseWriterTurnstileInterest)
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

        // Writers release turnstile when slot is released
        if (_holdTurnstile && _turnstile != null)
        {
            _turnstile.Release();
        }

        if (releaseWriterTurnstileInterest && _turnstileState != null)
        {
            Interlocked.Decrement(ref _turnstileState.WritersActiveOrWaiting);
        }
    }

    public PoolStatisticsSnapshot GetSnapshot()
    {
        return new PoolStatisticsSnapshot(
            _label,
            _poolKeyHash,
            _maxSlots,
            (int)Math.Clamp(Interlocked.Read(ref _inUse), 0L, int.MaxValue),
            (int)Math.Clamp(Interlocked.Read(ref _peakInUse), 0L, int.MaxValue),
            (int)Math.Clamp(Interlocked.Read(ref _queued), 0L, int.MaxValue),
            (int)Math.Clamp(Interlocked.Read(ref _peakQueued), 0L, int.MaxValue),
            (int)Math.Clamp(Interlocked.Read(ref _turnstileQueued), 0L, int.MaxValue),
            (int)Math.Clamp(Interlocked.Read(ref _peakTurnstileQueued), 0L, int.MaxValue),
            Interlocked.Read(ref _totalAcquired),
            Interlocked.Read(ref _totalWaitTicks),
            Interlocked.Read(ref _totalHoldTicks),
            Interlocked.Read(ref _totalSlotTimeouts),
            Interlocked.Read(ref _totalTurnstileTimeouts),
            Interlocked.Read(ref _totalCanceledWaits),
            _disabled,
            _forbidden);
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

    private sealed class TurnstileState
    {
        internal long WritersActiveOrWaiting;
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
