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

internal sealed class PoolGovernor : SafeAsyncDisposableBase
{
    private const string NotInitializedMessage = "Pool governor is not initialized.";
    private const int QueueDepthMultiplier = 8;
    private const int MinQueueDepth = 32;
    private static readonly ConditionalWeakTable<SemaphoreSlim, TurnstileState> SharedTurnstileStates = new();

    private readonly PoolLabel _label;
    private readonly string _poolKeyHash;
    private readonly SemaphoreSlim? _semaphore;
    private readonly TimeSpan _acquireTimeout;
    private readonly long _acquireTimeoutStopwatchTicks;
    private readonly int _maxSlots;
    private readonly int _maxQueueDepth;
    private readonly bool _disabled;
    private readonly bool _forbidden;
    private readonly bool _trackMetrics;
    private readonly bool _ownsSemaphore;
    private readonly bool _ownsTurnstile;
    private readonly object _drainLock = new();
    private TaskCompletionSource<bool> _drainSignal;

    // CORE-027: admission-closed state. 0 = open, 1 = closed. Checked first in every acquire
    // entry point so a caller racing a concurrent Close()/Dispose() gets a clear
    // ObjectDisposedException instead of either silently succeeding past intended shutdown or
    // throwing from deep inside semaphore machinery that was torn down underneath it. Also lets
    // WaitForDrainAsync provide a real guarantee: once closed, _inUse can only decrease.
    // Deliberately independent of SafeAsyncDisposableBase.IsDisposed: Close() can be called
    // ahead of actual disposal (see DatabaseContext.DisposePoolGovernors) to stop admission
    // before draining, without yet tearing down the semaphore.
    private int _closed;

    // Turnstile fairness support: prevents writer starvation under reader pressure
    private readonly SemaphoreSlim? _turnstile;
    private readonly TurnstileState? _turnstileState;
    private readonly bool _holdTurnstile;

    private long _inUse;
    private long _peakInUse;
    private long _queued;
    private long _peakQueued;
    private long _queueDepth; // unconditional — decoupled from _trackMetrics, used only for admission control.
    private long _turnstileQueueDepth; // unconditional — decoupled from _trackMetrics, used only for admission control.
    private long _turnstileQueued;
    private long _peakTurnstileQueued;
    private long _totalAcquired;
    private long _totalWaits;
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
        bool ownsTurnstile = false,
        int? maxQueueDepth = null)
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

        if (maxQueueDepth is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxQueueDepth), "Queue depth cap must be >= 0.");
        }

        _maxSlots = maxSlots;
        _maxQueueDepth = maxQueueDepth ?? Math.Max(maxSlots * QueueDepthMultiplier, MinQueueDepth);

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
    /// Current number of immediately-acquirable semaphore permits. Test-only diagnostic used to
    /// verify <see cref="ReleaseToken"/>'s release ordering (permits must become available before
    /// <c>_inUse</c> reflects the release, not after).
    /// </summary>
    internal int AvailablePermits => _semaphore?.CurrentCount ?? 0;

    /// <summary>
    /// Test-only hook invoked immediately before <see cref="ReleaseToken"/> decrements
    /// <c>_inUse</c> — lets a test observe/assert state at that exact instant. Never set outside
    /// tests.
    /// </summary>
    internal Action? TestOnlyBeforeInUseDecrement { get; set; }

    /// <summary>
    /// Whether this governor is forbidden (MaxPoolSize=0).
    /// Forbidden governors throw <see cref="PoolForbiddenException"/> on every acquire attempt.
    /// </summary>
    internal bool Forbidden => _forbidden;

    public PoolLabel Label => _label;
    public string PoolKeyHash => _poolKeyHash;

    /// <summary>
    /// Maximum number of callers allowed to queue for a slot before further callers are
    /// rejected immediately with <see cref="PoolSaturatedException"/>, rather than waiting
    /// out the full acquire timeout. Exposed internally for test verification.
    /// </summary>
    internal int MaxQueueDepth => _maxQueueDepth;

    /// <summary>
    /// True once <see cref="Close"/> (or <see cref="Dispose"/>) has been called. Every acquire
    /// entry point checks this first and throws <see cref="ObjectDisposedException"/> if set.
    /// </summary>
    internal bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <summary>
    /// Closes admission: every subsequent Acquire/TryAcquire/AcquireAsync/TryAcquireAsync call
    /// throws <see cref="ObjectDisposedException"/> immediately, before touching the semaphore.
    /// Idempotent. Does not itself wait for existing holders to release — call
    /// <see cref="WaitForDrainAsync(CancellationToken)"/> afterward for that; closing first is
    /// what makes that wait a real guarantee (in-flight permits can only decrease, never
    /// increase, once closed).
    /// </summary>
    public void Close()
    {
        Interlocked.Exchange(ref _closed, 1);
    }

    private void ThrowIfClosed()
    {
        if (IsClosed)
        {
            throw new ObjectDisposedException(nameof(PoolGovernor));
        }
    }

    public PoolSlot Acquire(CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
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
            Interlocked.Increment(ref _totalWaits);
            var waitStart = Stopwatch.GetTimestamp();
            var deadlineTicks = waitStart + _acquireTimeoutStopwatchTicks;

            // Turnstile fairness: acquire turnstile first to reduce writer starvation risk.
            // Writers hold the turnstile for their entire slot lifetime.
            // Readers only gate here when writers are active/waiting; otherwise they bypass.
            // NOTE: readers already queued on the semaphore when the writer grabs the turnstile
            // are not displaced — only NEW reader attempts are gated.
            if (useTurnstileGate && _turnstile != null && !turnstileAcquired)
            {
                // Queue-depth admission control: unconditional (decoupled from _trackMetrics)
                // so a caller storm on the turnstile is bounded even when metrics tracking is
                // disabled. Mirrors the semaphore-side check below — without this, a stalled
                // writer holding/awaiting the turnstile let every new reader queue up and wait
                // out the full acquire timeout with no fast-fail circuit breaker at all.
                var turnstileQueueDepth = Interlocked.Increment(ref _turnstileQueueDepth);
                if (turnstileQueueDepth > _maxQueueDepth)
                {
                    Interlocked.Decrement(ref _turnstileQueueDepth);
                    throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
                }

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
                    Interlocked.Decrement(ref _turnstileQueueDepth);
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

            // Queue-depth admission control: unconditional (decoupled from _trackMetrics)
            // so a caller storm is bounded even when metrics tracking is disabled.
            var queueDepth = Interlocked.Increment(ref _queueDepth);
            if (queueDepth > _maxQueueDepth)
            {
                Interlocked.Decrement(ref _queueDepth);
                throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
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
                Interlocked.Decrement(ref _queueDepth);
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
        ThrowIfClosed();
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
        try
        {
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
        catch
        {
            // A cancelled token makes Wait(0, cancellationToken) throw OperationCanceledException
            // synchronously, bypassing every manual cleanup branch above — clean up here instead.
            if (turnstileAcquired && _turnstile != null)
            {
                _turnstile.Release();
            }

            UnregisterWriterTurnstileInterest(ref writerTurnstileInterestRegistered);
            throw;
        }
    }

    public async ValueTask<PoolSlot> AcquireAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();
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
            Interlocked.Increment(ref _totalWaits);
            var waitStart = Stopwatch.GetTimestamp();
            var deadlineTicks = waitStart + _acquireTimeoutStopwatchTicks;

            // Turnstile fairness: acquire turnstile first to reduce writer starvation risk.
            // Writers hold the turnstile for their entire slot lifetime.
            // Readers only gate here when writers are active/waiting; otherwise they bypass.
            // NOTE: readers already queued on the semaphore when the writer grabs the turnstile
            // are not displaced — only NEW reader attempts are gated.
            if (useTurnstileGate && _turnstile != null && !turnstileAcquired)
            {
                // Queue-depth admission control: unconditional (decoupled from _trackMetrics)
                // so a caller storm on the turnstile is bounded even when metrics tracking is
                // disabled. Mirrors the semaphore-side check below — without this, a stalled
                // writer holding/awaiting the turnstile let every new reader queue up and wait
                // out the full acquire timeout with no fast-fail circuit breaker at all.
                var turnstileQueueDepth = Interlocked.Increment(ref _turnstileQueueDepth);
                if (turnstileQueueDepth > _maxQueueDepth)
                {
                    Interlocked.Decrement(ref _turnstileQueueDepth);
                    throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
                }

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
                    Interlocked.Decrement(ref _turnstileQueueDepth);
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

            // Queue-depth admission control: unconditional (decoupled from _trackMetrics)
            // so a caller storm is bounded even when metrics tracking is disabled.
            var queueDepth = Interlocked.Increment(ref _queueDepth);
            if (queueDepth > _maxQueueDepth)
            {
                Interlocked.Decrement(ref _queueDepth);
                throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
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
                Interlocked.Decrement(ref _queueDepth);
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
        ThrowIfClosed();
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
        try
        {
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
        catch
        {
            // A cancelled token makes WaitAsync(0, cancellationToken) throw
            // OperationCanceledException synchronously, bypassing every manual cleanup
            // branch above — clean up here instead.
            if (turnstileAcquired && _turnstile != null)
            {
                _turnstile.Release();
            }

            UnregisterWriterTurnstileInterest(ref writerTurnstileInterestRegistered);
            throw;
        }
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
                // Signal already set — but it may be a STALE completed signal: a concurrent
                // Acquire()'s Interlocked.Increment(ref _inUse) can land before its own
                // ResetDrainSignalIfNeeded() takes _drainLock, so GetCurrentDrainSignal() above
                // can still observe the old (already-completed) instance even though _inUse is
                // genuinely back above zero. Actually re-check _inUse — the comment here
                // previously promised this but `break` skipped straight past it — rather than
                // trusting a signal snapshot that may already be behind current reality.
                if (Interlocked.Read(ref _inUse) == 0)
                {
                    break;
                }

                // Stale signal, _inUse still genuinely positive: a concurrent ResetDrainSignalIfNeeded
                // hasn't installed a fresh signal yet. Respect cancellation/timeout instead of
                // spinning past it, and yield instead of hot-looping a CPU core waiting for the
                // other thread's ResetDrainSignalIfNeeded to run.
                if (effectiveToken.IsCancellationRequested)
                {
                    if (timeoutCts != null && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException("Drain timeout");
                    }

                    throw new OperationCanceledException(cancellationToken);
                }

                await Task.Yield();
                continue;
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

    private static long ConvertTimeoutToStopwatchTicks(TimeSpan timeout) =>
        Math.Max(1L, (long)(timeout.TotalSeconds * Stopwatch.Frequency));

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
        if (IsClosed)
        {
            // CORE-027 TOCTOU: ThrowIfClosed() is only checked once, at the very top of
            // Acquire/TryAcquire/AcquireAsync/TryAcquireAsync, before any semaphore/turnstile
            // work. A concurrent Close() landing after that check but before a slow-path Wait()
            // completes lets this straggler still reach here with a real permit in hand — give it
            // back and reject exactly as ThrowIfClosed() would have a few instructions earlier,
            // so "once closed, _inUse can only decrease" (WaitForDrainAsync's contract) actually
            // holds and a concurrent drain-then-dispose can't tear down the semaphore/turnstile
            // out from under this still-in-flight acquire.
            //
            // Only the semaphore permit is released here — the turnstile permit (if any writer is
            // currently holding one) is released by the caller's own catch block via its
            // `turnstileAcquired` local, exactly like every other failure path in
            // Acquire/TryAcquire/AcquireAsync/TryAcquireAsync. Only the WritersActiveOrWaiting
            // bookkeeping needs handling directly here, since every call site already flips its
            // local writerTurnstileInterestRegistered to false immediately before calling
            // OnAcquired, making the caller's own UnregisterWriterTurnstileInterest a no-op by the
            // time this throws.
            _semaphore?.Release();
            if (releaseWriterTurnstileInterestOnRelease && _turnstileState != null)
            {
                Interlocked.Decrement(ref _turnstileState.WritersActiveOrWaiting);
            }

            throw new ObjectDisposedException(nameof(PoolGovernor));
        }

        var inUse = Interlocked.Increment(ref _inUse);
        ResetDrainSignalIfNeeded();
        UpdatePeak(ref _peakInUse, inUse);
        Interlocked.Increment(ref _totalAcquired);
        return new PoolSlot(new PoolSlot.PoolSlotToken(this, waitStart, releaseWriterTurnstileInterestOnRelease));
    }

    internal void ReleaseToken(long waitStart, long acquiredAt, long releasedAt, bool releaseWriterTurnstileInterest)
    {
        RecordWaitAndHold(waitStart, acquiredAt, releasedAt);

        // Release semaphore and turnstile BEFORE decrementing _inUse (and before signaling
        // drain-waiters below). _inUse dropping to zero is the signal callers rely on — both
        // WaitForDrainAsync's direct _inUse==0 fast path and the drain-signal machinery further
        // down — to mean "safe to dispose the governor". If _inUse were decremented first, a
        // concurrent WaitForDrainAsync could observe zero and let a caller dispose the semaphore/
        // turnstile while this method is still about to call Release() on them, throwing
        // ObjectDisposedException on this thread.
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

        TestOnlyBeforeInUseDecrement?.Invoke();
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
            _forbidden)
        {
            TotalWaits = Interlocked.Read(ref _totalWaits)
        };
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

    protected override void DisposeManaged()
    {
        Close();

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
