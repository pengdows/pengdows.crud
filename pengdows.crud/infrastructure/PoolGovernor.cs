using pengdows.crud.enums;
// =============================================================================
// FILE: PoolGovernor.cs
// PURPOSE: Pool governor limiting concurrent connection usage.
//
// AI SUMMARY:
// - Controls maximum concurrent connections via PoolGovernorConcurrencyGate, retaining the
//   SemaphoreSlim path only for explicitly shared admission controls used by legacy callers.
// - Thread-safe: all counters use Interlocked operations.
// - Key methods:
//   * Acquire(ct): Sync slot acquisition with timeout
//   * AcquireAsync(ct): Async permit acquisition with timeout (uses WaitAsync throughout)
//   * Release(): Returns permit to pool (called by PoolSlot)
//   * GetSnapshot(): Returns current pool statistics
// - Throws PoolSaturatedException when timeout expires waiting for slot, or immediately when
//   the wait queue (slot gate or turnstile) is already at MaxQueueDepth.
// - Tracks: inUse, peakInUse, queued, totalAcquired, totalSlotTimeouts, totalTurnstileTimeouts.
//   * TotalTimeouts in snapshot = slot acquisition timeouts.
//   * TotalTurnstileTimeouts in snapshot = turnstile acquisition timeouts.
// - Can be disabled (returns default slots without blocking).
// - Slot admission goes through PoolGovernorConcurrencyGate, the only admission primitive.
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
    private const int QueueDepthMultiplier = 8;
    private const int MinQueueDepth = 32;
    private static readonly ConditionalWeakTable<SemaphoreSlim, TurnstileState> SharedTurnstileStates = new();

    private readonly PoolLabel _label;
    private readonly string _poolKeyHash;
    private readonly PoolGovernorConcurrencyGate? _concurrencyGate;
    private readonly TimeSpan _acquireTimeout;
    private readonly long _acquireTimeoutStopwatchTicks;
    private readonly int _maxSlots;
    private readonly int _maxQueueDepth;
    private readonly bool _disabled;
    private readonly bool _forbidden;
    private readonly bool _trackMetrics;
    private readonly TimeProvider _timeProvider;
    private readonly bool _ownsTurnstile;
    // Made by WaitForDrainAsync only while it waits, and completed by the release that drops _inUse
    // to zero. Acquire and release touch it with no lock and no allocation (PERF-024): a waiter
    // publishes it and then re-reads _inUse, a release decrements _inUse and then reads it, both
    // through full fences, so a release can't miss a waiter that saw a slot in use.
    private TaskCompletionSource<bool>? _drainSignal;

    // CORE-027: admission-closed state. 0 = open, 1 = closed. Checked first in every acquire
    // entry point so a caller racing a concurrent Close()/Dispose() gets a clear
    // ObjectDisposedException instead of either silently succeeding past intended shutdown or
    // throwing from deep inside semaphore machinery that was torn down underneath it. Also lets
    // WaitForDrainAsync provide a real guarantee: once closed, _inUse can only decrease.
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
        SemaphoreSlim? turnstile = null,
        bool holdTurnstile = false,
        bool ownsTurnstile = false,
        int? maxQueueDepth = null,
        TimeProvider? timeProvider = null)
    {
        _label = label;
        _timeProvider = MetricsClock.Resolve(timeProvider);
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

        if (disabled)
        {
            _disabled = true;
            _maxSlots = 0;
            _concurrencyGate = null;
            return;
        }

        if (forbidden)
        {
            _forbidden = true;
            _maxSlots = 0;
            _concurrencyGate = null;
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

        // PoolGovernor owns queue-depth admission and metrics. The gate's queue is therefore
        // deliberately unbounded here so callers receive PoolSaturatedException with the
        // governor snapshot rather than the gate's internal exception.
        _concurrencyGate = new PoolGovernorConcurrencyGate(maxSlots, int.MaxValue);
    }

    /// <summary>
    /// Whether this governor is forbidden (MaxPoolSize=0).
    /// Forbidden governors throw <see cref="PoolForbiddenException"/> on every acquire attempt.
    /// </summary>
    internal bool Forbidden => _forbidden;

    public PoolLabel Label => _label;

    /// <summary>Callers currently waiting for a slot (tracked whether or not metrics are on).</summary>
    internal long QueueDepth => Interlocked.Read(ref _queueDepth);
    public string PoolKeyHash => _poolKeyHash;

    /// <summary>
    /// Maximum number of callers allowed to queue for a slot before further callers are
    /// rejected immediately with <see cref="PoolSaturatedException"/>, rather than waiting
    /// out the full acquire timeout. Exposed internally for test verification.
    /// </summary>
    internal int MaxQueueDepth => _maxQueueDepth;

    /// <summary>
    /// Current number of immediately-acquirable semaphore permits. Test-only diagnostic used to
    /// verify <see cref="ReleaseToken"/>'s release ordering (permits must become available before
    /// <c>_inUse</c> reflects the release, not after).
    /// </summary>
    internal int AvailablePermits => _concurrencyGate?.AvailableCount ?? 0;

    /// <summary>
    /// Test-only hook invoked immediately before <see cref="ReleaseToken"/> decrements
    /// <c>_inUse</c> — lets a test observe/assert state at that exact instant. Never set outside
    /// tests.
    /// </summary>
    internal Action? TestOnlyBeforeInUseDecrement { get; set; }

    /// <summary>
    /// True once <see cref="Close"/> (or <see cref="Dispose"/>) has been called. Every acquire
    /// entry point checks this first and throws <see cref="ObjectDisposedException"/> if set.
    /// </summary>
    internal bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <summary>
    /// Closes admission: every subsequent Acquire/TryAcquire/AcquireAsync/TryAcquireAsync call
    /// throws <see cref="ObjectDisposedException"/> immediately, before touching the semaphore.
    /// Idempotent.
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

    // Every acquire's preconditions (DRY-017: four copies): throws when the governor is closed,
    // forbidden or not initialized; false when it is disabled, where every acquire succeeds with no slot.
    private bool Gates()
    {
        ThrowIfClosed();
        if (_forbidden)
        {
            throw new PoolForbiddenException(_label, _poolKeyHash);
        }

        if (_disabled)
        {
            return false;
        }

        if (_concurrencyGate == null)
        {
            throw new InvalidOperationException(NotInitializedMessage);
        }

        return true;
    }

    private bool TryAcquireAdmission(CancellationToken cancellationToken)
        => _concurrencyGate!.TryAcquire(out _, cancellationToken);

    private bool AcquireAdmission(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            _concurrencyGate!.Acquire(timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private ValueTask<bool> AcquireAdmissionAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        // PoolGovernor uses a zero-time probe before entering its own queued slow path.
        // Keep that probe entirely synchronous: routing it through the timed gate overload
        // creates a linked timeout CTS even when the caller is not waiting.
        if (timeout == TimeSpan.Zero)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<bool>(Task.FromCanceled<bool>(cancellationToken));
            }

            return ValueTask.FromResult(TryAcquireAdmission(cancellationToken));
        }

        return AcquireGateAdmissionAsync(timeout, cancellationToken);
    }

    private async ValueTask<bool> AcquireGateAdmissionAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await _concurrencyGate!.AcquireAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private void ReleaseAdmission() => _concurrencyGate!.Release(0);

    // What one acquire attempt has taken so far, so a failure anywhere can give it back: the turnstile, and the writer
    // interest registered on the shared turnstile state.
    private struct AcquireProgress
    {
        internal bool TurnstileAcquired;
        internal bool WriterInterestRegistered;
        internal bool UseTurnstileGate;
    }

    // The blocking and asynchronous acquires differ only in how they wait. The uncontended fast path is the same
    // synchronous code in both, because a zero-timeout probe never blocks; the slow path is one method that either
    // blocks or awaits according to isAsync. In blocking mode it never yields, so it has finished by the time it
    // returns and nothing waits on a continuation.
    public PoolSlot Acquire(CancellationToken cancellationToken = default)
    {
        if (!Gates())
        {
            return default;
        }

        var progress = new AcquireProgress();
        try
        {
            if (TryAcquireImmediate(cancellationToken, ref progress, out var immediate))
            {
                return immediate;
            }
        }
        catch
        {
            AbandonAcquire(progress.TurnstileAcquired, ref progress.WriterInterestRegistered);
            throw;
        }

        var pending = AcquireSlowAsync(false, progress, cancellationToken);
        Debug.Assert(pending.IsCompleted, "A blocking acquire must finish without yielding.");
        return pending.GetAwaiter().GetResult();
    }

    // Takes a free slot (and, when one is in play, the turnstile) without waiting. A cancelled token makes the
    // zero-timeout waits throw OperationCanceledException synchronously, so the caller cleans up on failure.
    private bool TryAcquireImmediate(CancellationToken cancellationToken, ref AcquireProgress progress,
        out PoolSlot slot)
    {
        RegisterWriterTurnstileInterest(ref progress.WriterInterestRegistered);
        progress.UseTurnstileGate = ShouldUseTurnstileGate(progress.WriterInterestRegistered);

        // Fast path: no deadline arithmetic for immediate gate/slot success.
        var admitted = false;
        if (progress.UseTurnstileGate && _turnstile != null && _turnstile.Wait(0, cancellationToken))
        {
            progress.TurnstileAcquired = true;
            if (!_holdTurnstile)
            {
                _turnstile.Release();
                progress.TurnstileAcquired = false;
            }

            admitted = TryAcquireAdmission(cancellationToken);
        }
        else if (!progress.UseTurnstileGate)
        {
            admitted = TryAcquireAdmission(cancellationToken);
        }

        if (!admitted)
        {
            slot = default;
            return false;
        }

        var immediateWaitStart = _trackMetrics ? _timeProvider.GetTimestamp() : 0;
        var releaseWriterInterestOnRelease = _holdTurnstile && progress.WriterInterestRegistered;
        progress.WriterInterestRegistered = false;
        slot = OnAcquired(immediateWaitStart, immediateWaitStart, releaseWriterInterestOnRelease);
        return true;
    }

    // On failure, release the turnstile if we're still holding it (writers only) and give back the writer
    // interest. Do NOT record wait/hold metrics here — failure duration is not slot hold time.
    private void AbandonAcquire(bool turnstileAcquired, ref bool writerTurnstileInterestRegistered)
    {
        if (turnstileAcquired && _turnstile != null)
        {
            _turnstile.Release();
        }

        UnregisterWriterTurnstileInterest(ref writerTurnstileInterestRegistered);
    }

    // The queued, deadline-bounded path shared by Acquire (isAsync: false, blocks the calling thread and so
    // completes before it returns) and AcquireAsync (isAsync: true, awaits).
    private async ValueTask<PoolSlot> AcquireSlowAsync(bool isAsync, AcquireProgress progress,
        CancellationToken cancellationToken)
    {
        var useTurnstileGate = progress.UseTurnstileGate;
        var turnstileAcquired = progress.TurnstileAcquired;
        var writerTurnstileInterestRegistered = progress.WriterInterestRegistered;
        try
        {
            // Slow path: timed waits with a single deadline budget across gates.
            Interlocked.Increment(ref _totalWaits);
            var waitStart = _timeProvider.GetTimestamp();
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
                        || !(isAsync
                            ? await _turnstile.WaitAsync(turnstileRemaining, cancellationToken).ConfigureAwait(false)
                            : _turnstile.Wait(turnstileRemaining, cancellationToken)))
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

            // A cancelled token is reported as a cancelled task when awaiting and as a plain
            // OperationCanceledException when blocking, as each acquire always has.
            var admitted = isAsync
                ? await AcquireAdmissionAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false)
                : TryAcquireAdmission(cancellationToken);
            if (admitted)
            {
                var releaseWriterInterestOnRelease = _holdTurnstile && writerTurnstileInterestRegistered;
                writerTurnstileInterestRegistered = false;
                return OnAcquired(waitStart, _trackMetrics ? _timeProvider.GetTimestamp() : 0, releaseWriterInterestOnRelease);
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

                    var acquired = isAsync
                        ? await AcquireAdmissionAsync(semRemaining, cancellationToken).ConfigureAwait(false)
                        : AcquireAdmission(semRemaining, cancellationToken);
                    if (!acquired)
                    {
                        Interlocked.Increment(ref _totalSlotTimeouts);
                        throw new PoolSaturatedException(_label, _poolKeyHash, GetSnapshot(), _acquireTimeout);
                    }

                    var releaseWriterInterestOnRelease = _holdTurnstile && writerTurnstileInterestRegistered;
                    writerTurnstileInterestRegistered = false;
                    return OnAcquired(waitStart, _trackMetrics ? _timeProvider.GetTimestamp() : 0, releaseWriterInterestOnRelease);
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
            AbandonAcquire(turnstileAcquired, ref writerTurnstileInterestRegistered);
            throw;
        }
    }

    public bool TryAcquire(out PoolSlot slot, CancellationToken cancellationToken = default)
    {
        if (!Gates())
        {
            slot = default;
            return true;
        }

        var waitStart = _trackMetrics ? _timeProvider.GetTimestamp() : 0;
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

            if (TryAcquireAdmission(cancellationToken))
            {
                var releaseWriterInterestOnRelease = _holdTurnstile && writerTurnstileInterestRegistered;
                writerTurnstileInterestRegistered = false;
                slot = OnAcquired(waitStart, waitStart, releaseWriterInterestOnRelease);
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
        if (!Gates())
        {
            return default;
        }

        var progress = new AcquireProgress();
        try
        {
            if (TryAcquireImmediate(cancellationToken, ref progress, out var immediate))
            {
                return immediate;
            }
        }
        catch (Exception ex)
        {
            AbandonAcquire(progress.TurnstileAcquired, ref progress.WriterInterestRegistered);

            // The shared synchronous fast path throws a plain OperationCanceledException for a cancelled token;
            // the asynchronous acquire has always reported it as a cancelled task (TaskCanceledException).
            if (ex is OperationCanceledException and not TaskCanceledException
                && cancellationToken.IsCancellationRequested)
            {
                return await ValueTask.FromCanceled<PoolSlot>(cancellationToken).ConfigureAwait(false);
            }

            throw;
        }

        return await AcquireSlowAsync(true, progress, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<(bool Success, PoolSlot Permit)> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        if (!Gates())
        {
            return (true, default);
        }

        var waitStart = _trackMetrics ? _timeProvider.GetTimestamp() : 0;
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

            if (await AcquireAdmissionAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            {
                var releaseWriterInterestOnRelease = _holdTurnstile && writerTurnstileInterestRegistered;
                writerTurnstileInterestRegistered = false;
                return (true, OnAcquired(waitStart, waitStart, releaseWriterInterestOnRelease));
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
            var signal = Volatile.Read(ref _drainSignal);
            if (signal == null || signal.Task.IsCompleted)
            {
                // Publish a pending signal (a release unpublishes a signal before completing it, so
                // a completed one here is stale: replace it). Waiters share one signal.
                var fresh = CreateDrainSignal(completed: false);
                var seen = Interlocked.CompareExchange(ref _drainSignal, fresh, signal);
                signal = ReferenceEquals(seen, signal) ? fresh : seen;
                if (signal == null || signal.Task.IsCompleted)
                {
                    continue;
                }
            }

            // A release that dropped _inUse to zero before the signal was published saw no signal
            // to complete; re-read _inUse after publishing.
            if (Interlocked.Read(ref _inUse) == 0)
            {
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

    private void RecordWaitAndHold(long waitStart, long acquiredAt)
    {
        if (!_trackMetrics)
        {
            return;
        }

        var releasedAt = _timeProvider.GetTimestamp();
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
    private TimeSpan GetRemainingTimeout(long deadlineTicks)
    {
        var remainingTicks = deadlineTicks - _timeProvider.GetTimestamp();
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

    // waitStart and acquiredAt only feed the wait and hold metrics, so they are 0 when metrics are off. When nothing
    // waited they are the same reading: the wait started at the moment of acquisition.
    private PoolSlot OnAcquired(long waitStart, long acquiredAt, bool releaseWriterTurnstileInterestOnRelease)
    {
        if (IsClosed)
        {
            // CORE-027 TOCTOU: ThrowIfClosed() is only checked once, at the very top of
            // Acquire/TryAcquire/AcquireAsync/TryAcquireAsync, before any semaphore/turnstile
            // work. A concurrent Close() landing after that check but before a slow-path Wait()
            // completes lets this straggler still reach here with a real permit in hand — give it
            // back and reject exactly as ThrowIfClosed() would have a few instructions earlier,
            // so "once closed, _inUse can only decrease" (WaitForDrainAsync's contract) actually
            // holds.
            //
            // Only the semaphore permit is released here — the turnstile permit (if any writer is
            // currently holding one) is released by the caller's own catch block via its
            // `turnstileAcquired` local, exactly like every other failure path in
            // Acquire/TryAcquire/AcquireAsync/TryAcquireAsync. Only the WritersActiveOrWaiting
            // bookkeeping needs handling directly here, since every call site already flips its
            // local writerTurnstileInterestRegistered to false immediately before calling
            // OnAcquired, making the caller's own UnregisterWriterTurnstileInterest a no-op by the
            // time this throws.
            ReleaseAdmission();
            if (releaseWriterTurnstileInterestOnRelease && _turnstileState != null)
            {
                Interlocked.Decrement(ref _turnstileState.WritersActiveOrWaiting);
            }

            throw new ObjectDisposedException(nameof(PoolGovernor));
        }

        var inUse = Interlocked.Increment(ref _inUse);
        UpdatePeak(ref _peakInUse, inUse);
        Interlocked.Increment(ref _totalAcquired);
        return new PoolSlot(new PoolSlot.PoolSlotToken(this, waitStart, acquiredAt, releaseWriterTurnstileInterestOnRelease));
    }

    internal void ReleaseToken(long waitStart, long acquiredAt, bool releaseWriterTurnstileInterest)
    {
        RecordWaitAndHold(waitStart, acquiredAt);

        // Release semaphore and turnstile BEFORE decrementing _inUse (and before signaling
        // drain-waiters below). _inUse dropping to zero is the signal callers rely on — both
        // WaitForDrainAsync's direct _inUse==0 fast path and the drain-signal machinery further
        // down — to mean "safe to dispose the governor". If _inUse were decremented first, a
        // concurrent WaitForDrainAsync could observe zero and let a caller dispose the semaphore/
        // turnstile while this method is still about to call Release() on them, throwing
        // ObjectDisposedException on this thread.
        ReleaseAdmission();

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
        if (Interlocked.Decrement(ref _inUse) == 0)
        {
            // Wake drain waiters. The signal is unpublished before it completes, so a waiter never
            // finds a completed signal installed; an acquire racing in after the decrement only
            // makes the wake spurious, and the waiter re-reads _inUse.
            var waiting = Volatile.Read(ref _drainSignal);
            if (waiting != null &&
                ReferenceEquals(Interlocked.CompareExchange(ref _drainSignal, null, waiting), waiting))
            {
                waiting.TrySetResult(true);
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

    private sealed class TurnstileState
    {
        internal long WritersActiveOrWaiting;
    }

    public void Dispose()
    {
        Close();

        _concurrencyGate?.Dispose();


        if (_ownsTurnstile)
        {
            _turnstile?.Dispose();
        }
    }
}
