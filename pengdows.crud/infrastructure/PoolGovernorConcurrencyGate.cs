using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Sources;

namespace pengdows.crud.infrastructure;

/// <summary>
/// Count-only adaptive gate for PoolGovernor. A permit has no identity: PoolSlotToken already
/// guarantees exactly-once release, so retaining lease slots and generations is unnecessary.
/// </summary>
internal sealed class PoolGovernorConcurrencyGate : IDisposable
{
    private readonly object _sync = new();
    private readonly int _configuredMaximum;
    private readonly int _maxQueueDepth;
    private readonly bool _singleSlot;
    private readonly ConcurrentBag<Waiter> _asyncWaiterPool = new();
    private Waiter? _waiterHead;
    private Waiter? _waiterTail;
    private int _effectiveLimit;
    private int _active;
    private int _available;
    private int _queueCount;
    private int _disposedFlag;

    internal PoolGovernorConcurrencyGate(int configuredMaximum, int? maxQueueDepth = null)
    {
        if (configuredMaximum <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(configuredMaximum));
        }

        _configuredMaximum = configuredMaximum;
        _singleSlot = configuredMaximum == 1;
        _effectiveLimit = configuredMaximum;
        _available = configuredMaximum;
        _maxQueueDepth = maxQueueDepth ?? Math.Max(configuredMaximum * 8, 32);
        if (_maxQueueDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxQueueDepth));
        }
    }

    internal int ActiveCount
    {
        get => Volatile.Read(ref _active);
    }

    internal int QueueCount
    {
        get => Volatile.Read(ref _queueCount);
    }

    internal int AvailableCount
    {
        get => _singleSlot
            ? (Volatile.Read(ref _active) == 0 ? 1 : 0)
            : Math.Max(Volatile.Read(ref _available), 0);
    }

    internal int RecycledWaiterCount => Volatile.Read(ref _recycledWaiters);
    private int _recycledWaiters;

    internal int Acquire(CancellationToken cancellationToken = default)
        => Acquire(Timeout.InfiniteTimeSpan, cancellationToken);

    internal int Acquire(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ValidateTimeout(timeout);
        cancellationToken.ThrowIfCancellationRequested();
        if (TryAcquireFast(out var fastPermit))
        {
            return fastPermit;
        }
        var waiter = EnqueueOrAdmit(cancellationToken, asyncWaiter: false);
        if (waiter == null)
        {
            return 0;
        }

        if (!waiter.WaitForCompletion(timeout))
        {
            // Timed out. If the cancellation wins it completes the waiter with the TimeoutException itself; if a
            // grant, a dispose or a cancel got there first, that completion is already on its way.
            Cancel(waiter, WaiterFailure.Timeout);
            waiter.WaitForCompletion(Timeout.InfiniteTimeSpan);
        }

        // Disposing the registration waits out a cancellation callback that is still running, so none can fire at
        // this waiter once it is reused for the next wait.
        waiter.TryCancelRegistration();
        try
        {
            return waiter.TakeSyncResult();
        }
        finally
        {
            ReturnSyncWaiter(waiter);
        }
    }

    internal bool TryAcquire(out int permit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return TryAcquireFast(out permit);
    }

    internal ValueTask<int> AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new ValueTask<int>(Task.FromCanceled<int>(cancellationToken));
        }

        if (TryAcquireFast(out var fastPermit))
        {
            return ValueTask.FromResult(fastPermit);
        }
        var waiter = EnqueueOrAdmit(cancellationToken, asyncWaiter: true);
        return waiter == null
            ? ValueTask.FromResult(0)
            : waiter.AsValueTask();
    }

    internal async ValueTask<int> AcquireAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ValidateTimeout(timeout);
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return await AcquireAsync(cancellationToken).ConfigureAwait(false);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            return await AcquireAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The pool governor acquisition timed out.");
        }
    }

    // The hook for automatic pool resizing: the effective limit can move between 1 and the configured
    // maximum while leases are outstanding. Lowering never revokes a lease; raising admits queued waiters.
    internal int SetLimit(int limit)
    {
        if (limit < 1 || limit > _configuredMaximum)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        List<Waiter>? released = null;
        lock (_sync)
        {
            if (Volatile.Read(ref _disposedFlag) != 0)
            {
                return _effectiveLimit;
            }

            var wasRaised = limit > _effectiveLimit;
            var delta = limit - _effectiveLimit;
            _effectiveLimit = limit;
            Interlocked.Add(ref _available, delta);
            if (wasRaised)
            {
                released = PromoteUnderLock();
            }
        }

        Complete(null, released);
        return limit;
    }

    internal void Release(int permit)
    {
        Waiter? handedOff = null;
        Waiter? promoted = null;
        lock (_sync)
        {
            if (Volatile.Read(ref _active) == 0)
            {
                return;
            }

            if (_singleSlot)
            {
                handedOff = HandoffSingleSlotUnderLock();
            }
            else
            {
                Interlocked.Decrement(ref _active);
                Interlocked.Increment(ref _available);
                promoted = _queueCount == 0 ? null : PromoteOneUnderLock();
            }
        }

        Complete(handedOff ?? promoted, null);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposedFlag, 1) != 0)
        {
            return;
        }

        List<Waiter> pending;
        lock (_sync)
        {
            pending = new List<Waiter>(_queueCount);
            while (_waiterHead != null)
            {
                var waiter = _waiterHead;
                RemoveWaiterUnderLock(waiter);
                if (waiter.TryCancel())
                {
                    pending.Add(waiter);
                }
            }
            Interlocked.Exchange(ref _queueCount, 0);
        }

        foreach (var waiter in pending)
        {
            waiter.TryCancelRegistration();
            waiter.SetException(new ObjectDisposedException(nameof(PoolGovernorConcurrencyGate)));
        }
    }

    private Waiter? EnqueueOrAdmit(CancellationToken cancellationToken, bool asyncWaiter)
    {
        lock (_sync)
        {
            ThrowIfDisposedUnderLock();
            if (_queueCount == 0
                && (_singleSlot
                    ? Interlocked.CompareExchange(ref _active, 1, 0) == 0
                    : TryTakeAvailable()))
            {
                if (!_singleSlot)
                {
                    Interlocked.Increment(ref _active);
                }

                return null;
            }

            if (_queueCount >= _maxQueueDepth)
            {
                throw new ConcurrencyGateSaturatedException(_maxQueueDepth);
            }

            var waiter = asyncWaiter
                ? RentAsyncWaiter(cancellationToken)
                : RentSyncWaiter(cancellationToken);
            EnqueueWaiterUnderLock(waiter);
            Interlocked.Increment(ref _queueCount);
            waiter.RegisterCancellation();
            return waiter;
        }
    }

    private List<Waiter>? PromoteUnderLock()
    {
        List<Waiter>? released = null;
        while (_waiterHead != null && TryTakeAvailable())
        {
            var waiter = _waiterHead;
            RemoveWaiterUnderLock(waiter);
            if (!waiter.TryGrant())
            {
                Interlocked.Increment(ref _available);
                continue;
            }

            Interlocked.Increment(ref _active);
            (released ??= new List<Waiter>()).Add(waiter);
        }

        return released;
    }

    private Waiter? PromoteOneUnderLock()
    {
        while (_waiterHead != null && TryTakeAvailable())
        {
            var waiter = _waiterHead;
            RemoveWaiterUnderLock(waiter);
            if (!waiter.TryGrant())
            {
                Interlocked.Increment(ref _available);
                continue;
            }

            Interlocked.Increment(ref _active);
            return waiter;
        }

        return null;
    }

    private bool Cancel(Waiter waiter, WaiterFailure failure)
    {
        lock (_sync)
        {
            if (!waiter.TryCancel())
            {
                return false;
            }

            if (waiter.IsQueued)
            {
                RemoveWaiterUnderLock(waiter);
            }
        }

        waiter.TryCancelRegistration();
        waiter.SetException(failure == WaiterFailure.Timeout
            ? new TimeoutException("The pool governor acquisition timed out.")
            : new OperationCanceledException(waiter.CancellationToken));
        return true;
    }

    private static void Complete(Waiter? handedOff, List<Waiter>? released)
    {
        if (handedOff != null)
        {
            handedOff.TryCancelRegistration();
            handedOff.SetResult(0);
        }

        if (released == null)
        {
            return;
        }

        foreach (var waiter in released)
        {
            waiter.TryCancelRegistration();
            waiter.SetResult(0);
        }
    }

    // A thread blocks on one gate at a time, so one cached waiter per thread is all a blocking caller needs, and a
    // blocked wait allocates nothing.
    [ThreadStatic]
    private static Waiter? t_syncWaiter;

    private Waiter RentSyncWaiter(CancellationToken cancellationToken)
    {
        var waiter = t_syncWaiter;
        if (waiter == null)
        {
            return new Waiter(this, cancellationToken, asyncWaiter: false);
        }

        t_syncWaiter = null;
        waiter.Initialize(this, cancellationToken);
        return waiter;
    }

    private static void ReturnSyncWaiter(Waiter waiter)
    {
        waiter.ReleaseForCache();
        t_syncWaiter = waiter;
    }

    private Waiter RentAsyncWaiter(CancellationToken cancellationToken)
    {
        if (!_asyncWaiterPool.TryTake(out var waiter))
        {
            return new Waiter(this, cancellationToken, asyncWaiter: true);
        }

        waiter.Initialize(this, cancellationToken);
        Interlocked.Decrement(ref _recycledWaiters);
        return waiter;
    }

    private void ReturnAsyncWaiter(Waiter waiter)
    {
        waiter.ResetForReuse();
        _asyncWaiterPool.Add(waiter);
        Interlocked.Increment(ref _recycledWaiters);
    }

    private void ThrowIfDisposedUnderLock()
    {
        if (Volatile.Read(ref _disposedFlag) != 0)
        {
            throw new ObjectDisposedException(nameof(PoolGovernorConcurrencyGate));
        }
    }

    private bool TryAcquireFast(out int permit)
    {
        if (Volatile.Read(ref _disposedFlag) != 0)
        {
            throw new ObjectDisposedException(nameof(PoolGovernorConcurrencyGate));
        }

        if (Volatile.Read(ref _queueCount) != 0
            || (_singleSlot
                ? Interlocked.CompareExchange(ref _active, 1, 0) != 0
                : !TryTakeAvailable()))
        {
            permit = default;
            return false;
        }

        if (!_singleSlot)
        {
            Interlocked.Increment(ref _active);
        }
        if (Volatile.Read(ref _disposedFlag) != 0)
        {
            Release(0);
            throw new ObjectDisposedException(nameof(PoolGovernorConcurrencyGate));
        }

        permit = 0;
        return true;
    }

    private Waiter? HandoffSingleSlotUnderLock()
    {
        while (_waiterHead != null)
        {
            var waiter = _waiterHead;
            RemoveWaiterUnderLock(waiter);
            if (!waiter.TryGrant())
            {
                continue;
            }

            // Keep _active at one: ownership transfers directly from the releasing
            // caller to this waiter without exposing a free-slot window.
            return waiter;
        }

        Interlocked.Exchange(ref _active, 0);
        Interlocked.Exchange(ref _available, 1);
        return null;
    }

    private void EnqueueWaiterUnderLock(Waiter waiter)
    {
        waiter.Previous = _waiterTail;
        waiter.Next = null;
        if (_waiterTail == null)
        {
            _waiterHead = waiter;
        }
        else
        {
            _waiterTail.Next = waiter;
        }

        _waiterTail = waiter;
    }

    private void RemoveWaiterUnderLock(Waiter waiter)
    {
        var previous = waiter.Previous;
        var next = waiter.Next;
        if (previous == null)
        {
            _waiterHead = next;
        }
        else
        {
            previous.Next = next;
        }

        if (next == null)
        {
            _waiterTail = previous;
        }
        else
        {
            next.Previous = previous;
        }

        waiter.Previous = null;
        waiter.Next = null;
        Interlocked.Decrement(ref _queueCount);
    }

    private bool TryTakeAvailable()
    {
        var available = Volatile.Read(ref _available);
        while (available > 0)
        {
            var next = available - 1;
            if (Interlocked.CompareExchange(ref _available, next, available) == available)
            {
                return true;
            }

            available = Volatile.Read(ref _available);
        }

        return false;
    }

    private static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout < Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    private sealed class Waiter : IValueTaskSource<int>
    {
        private int _state;
        private CancellationTokenRegistration _registration;
        private ManualResetValueTaskSourceCore<int> _valueCompletion;

        private PoolGovernorConcurrencyGate _gate;
        private CancellationToken _cancellationToken;

        // A blocking waiter completes through these instead of a Task: a flag, a result and a lock to park on.
        // The completer sets them under the lock, so a parked thread cannot miss the wake-up.
        private const int SyncSpinIterations = 12;
        private readonly object? _syncLock;
        private int _syncDone;
        private int _syncResult;
        private Exception? _syncException;

        internal Waiter(PoolGovernorConcurrencyGate gate, CancellationToken cancellationToken, bool asyncWaiter)
        {
            _gate = gate;
            _cancellationToken = cancellationToken;
            if (asyncWaiter)
            {
                _valueCompletion.RunContinuationsAsynchronously = true;
            }
            else
            {
                _syncLock = new object();
            }
        }

        internal CancellationToken CancellationToken => _cancellationToken;
        internal Waiter? Previous;
        internal Waiter? Next;
        internal bool IsQueued => Previous != null || Next != null || _gate._waiterHead == this;

        internal ValueTask<int> AsValueTask() => new(this, _valueCompletion.Version);

        internal void SetResult(int result)
        {
            if (_syncLock != null)
            {
                CompleteSync(result, null);
            }
            else
            {
                _valueCompletion.SetResult(result);
            }
        }

        internal void SetException(Exception exception)
        {
            if (_syncLock != null)
            {
                CompleteSync(0, exception);
            }
            else
            {
                _valueCompletion.SetException(exception);
            }
        }

        internal void Initialize(PoolGovernorConcurrencyGate gate, CancellationToken cancellationToken)
        {
            _gate = gate;
            _cancellationToken = cancellationToken;
            _state = 0;
            _registration = default;
            Previous = null;
            Next = null;
            _syncDone = 0;
            _syncResult = 0;
            _syncException = null;
        }

        // A cached waiter holds nothing from its last wait: not the gate, not the token.
        internal void ReleaseForCache()
        {
            _gate = null!;
            _cancellationToken = default;
            _registration = default;
            Previous = null;
            Next = null;
        }

        private void CompleteSync(int result, Exception? exception)
        {
            lock (_syncLock!)
            {
                if (Volatile.Read(ref _syncDone) != 0)
                {
                    return;
                }

                _syncResult = result;
                _syncException = exception;
                Volatile.Write(ref _syncDone, 1);
                Monitor.Pulse(_syncLock);
            }
        }

        // Spins briefly, because a handoff is usually only microseconds away and a parked thread costs a kernel
        // wake-up to resume, then parks. Returns false only if the timeout elapsed first.
        internal bool WaitForCompletion(TimeSpan timeout)
        {
            if (Volatile.Read(ref _syncDone) != 0)
            {
                return true;
            }

            if (Environment.ProcessorCount > 1)
            {
                var spinner = new SpinWait();
                for (var i = 0; i < SyncSpinIterations; i++)
                {
                    spinner.SpinOnce(sleep1Threshold: -1);
                    if (Volatile.Read(ref _syncDone) != 0)
                    {
                        return true;
                    }
                }
            }

            lock (_syncLock!)
            {
                if (timeout == Timeout.InfiniteTimeSpan)
                {
                    while (Volatile.Read(ref _syncDone) == 0)
                    {
                        Monitor.Wait(_syncLock);
                    }

                    return true;
                }

                var end = Stopwatch.GetTimestamp() + (timeout.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond);
                while (Volatile.Read(ref _syncDone) == 0)
                {
                    var remaining = end - Stopwatch.GetTimestamp();
                    if (remaining <= 0)
                    {
                        return false;
                    }

                    var milliseconds = (remaining * 1000 + Stopwatch.Frequency - 1) / Stopwatch.Frequency;
                    Monitor.Wait(_syncLock, (int)Math.Min(milliseconds, int.MaxValue));
                }

                return true;
            }
        }

        // Takes the lock once so a completer still inside it has finished before this waiter can be reused, then
        // returns the result, or throws what the completer recorded with its original stack.
        internal int TakeSyncResult()
        {
            lock (_syncLock!)
            {
            }

            var exception = _syncException;
            var result = _syncResult;
            if (exception != null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }

            return result;
        }

        internal void ResetForReuse()
        {
            Previous = null;
            Next = null;
            _state = 0;
            _cancellationToken = default;
            _registration = default;
            _valueCompletion.Reset();
        }

        internal void RegisterCancellation()
        {
            if (CancellationToken.CanBeCanceled)
            {
                _registration = CancellationToken.Register(static state =>
                {
                    var waiter = (Waiter)state!;
                    waiter._gate.Cancel(waiter, WaiterFailure.Canceled);
                }, this);
            }
        }

        internal void TryCancelRegistration() => _registration.Dispose();
        internal bool TryGrant() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;
        internal bool TryCancel() => Interlocked.CompareExchange(ref _state, 2, 0) == 0;

        ValueTaskSourceStatus IValueTaskSource<int>.GetStatus(short token)
            => _valueCompletion.GetStatus(token);

        int IValueTaskSource<int>.GetResult(short token)
        {
            try
            {
                return _valueCompletion.GetResult(token);
            }
            finally
            {
                _gate.ReturnAsyncWaiter(this);
            }
        }

        void IValueTaskSource<int>.OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags)
            => _valueCompletion.OnCompleted(continuation, state, token, flags);
    }

    private enum WaiterFailure { Canceled, Timeout }
}
