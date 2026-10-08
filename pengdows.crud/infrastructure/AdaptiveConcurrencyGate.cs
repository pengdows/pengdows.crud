using System.Diagnostics;

namespace pengdows.crud.infrastructure;

internal readonly record struct AdaptiveConcurrencyGateSnapshot(
    int ConfiguredMaximum,
    int EffectiveLimit,
    int ActiveCount,
    int QueueCount,
    int MaxQueueDepth);

internal sealed class ConcurrencyGateSaturatedException : Exception
{
    internal ConcurrencyGateSaturatedException(int maxQueueDepth)
        : base($"The concurrency gate queue is full at {maxQueueDepth} waiters.")
    {
    }
}

/// <summary>
/// A single logical concurrency gate whose effective limit may be lowered or raised while
/// leases are outstanding. Lowering the limit never revokes existing leases.
/// </summary>
internal sealed class AdaptiveConcurrencyGate : IDisposable
{
    private readonly object _sync = new();
    private readonly int _configuredMaximum;
    private readonly int _maxQueueDepth;
    private readonly bool _singleLeaseSlot;
    private int[] _freeLeaseSlots = Array.Empty<int>();
    private int[] _leaseStates = Array.Empty<int>();
    private int _freeLeaseSlotCount;
    private int _leaseSlotCount;
    private int _singleLeaseState;
    private Waiter? _waiterHead;
    private Waiter? _waiterTail;
    private int _queueCount;
    private int _effectiveLimit;
    private int _active;
    private bool _disposed;

    internal AdaptiveConcurrencyGate(int configuredMaximum, int? maxQueueDepth = null)
    {
        if (configuredMaximum <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(configuredMaximum));
        }

        _configuredMaximum = configuredMaximum;
        _singleLeaseSlot = configuredMaximum == 1;
        _maxQueueDepth = maxQueueDepth ?? Math.Max(configuredMaximum * 8, 32);
        if (_maxQueueDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxQueueDepth));
        }

        _effectiveLimit = configuredMaximum;
    }

    internal int ConfiguredMaximum => _configuredMaximum;

    internal int EffectiveLimit
    {
        get
        {
            lock (_sync)
            {
                return _effectiveLimit;
            }
        }
    }

    internal int ActiveCount
    {
        get
        {
            lock (_sync)
            {
                return _active;
            }
        }
    }

    internal int QueueCount
    {
        get
        {
            lock (_sync)
            {
                return _queueCount;
            }
        }
    }

    internal int MaxQueueDepth => _maxQueueDepth;

    internal AdaptiveConcurrencyGateSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new AdaptiveConcurrencyGateSnapshot(
                _configuredMaximum,
                _effectiveLimit,
                _active,
                _queueCount,
                _maxQueueDepth);
        }
    }

    internal ValueTask<Lease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new ValueTask<Lease>(Task.FromCanceled<Lease>(cancellationToken));
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return new ValueTask<Lease>(Task.FromException<Lease>(
                    new ObjectDisposedException(nameof(AdaptiveConcurrencyGate))));
            }

            if (_active < _effectiveLimit && _queueCount == 0)
            {
                var lease = CreateLeaseUnderLock();
                AssertInvariantUnderLock();
                return ValueTask.FromResult(lease);
            }

            if (_queueCount >= _maxQueueDepth)
            {
                return new ValueTask<Lease>(Task.FromException<Lease>(
                    new ConcurrencyGateSaturatedException(_maxQueueDepth)));
            }

            var waiter = new Waiter(this, cancellationToken, synchronous: false);
            EnqueueUnderLock(waiter);
            waiter.RegisterCancellation();
            AssertInvariantUnderLock();
            return new ValueTask<Lease>(waiter.Completion!.Task);
        }
    }

    internal Lease Acquire(CancellationToken cancellationToken = default)
    {
        return Acquire(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    internal Lease Acquire(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ValidateTimeout(timeout);
        cancellationToken.ThrowIfCancellationRequested();

        Waiter? waiter = null;
        lock (_sync)
        {
            ThrowIfDisposedUnderLock();
            if (_active < _effectiveLimit && _queueCount == 0)
            {
                var lease = CreateLeaseUnderLock();
                AssertInvariantUnderLock();
                return lease;
            }

            if (_queueCount >= _maxQueueDepth)
            {
                throw new ConcurrencyGateSaturatedException(_maxQueueDepth);
            }

            waiter = new Waiter(this, cancellationToken, synchronous: true);
            EnqueueUnderLock(waiter);
            waiter.RegisterCancellation();
            AssertInvariantUnderLock();
        }

        if (!waiter.Signal!.Wait(timeout))
        {
            if (Cancel(waiter, WaiterFailure.Timeout))
            {
                return waiter.TakeSynchronousResult();
            }

            // Grant won the cancellation race. The grant path owns the lease now;
            // wait for its signal and return it rather than leaking it as a timeout.
            waiter.Signal.Wait();
        }

        return waiter.TakeSynchronousResult();
    }

    internal ValueTask<Lease> AcquireAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ValidateTimeout(timeout);
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return AcquireAsync(cancellationToken);
        }

        var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        return new ValueTask<Lease>(AwaitTimedAcquireAsync(timeoutSource, cancellationToken));
    }

    private async Task<Lease> AwaitTimedAcquireAsync(
        CancellationTokenSource timeoutSource,
        CancellationToken callerCancellationToken)
    {
        try
        {
            return await AcquireAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested && !callerCancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The concurrency gate acquisition timed out.");
        }
        finally
        {
            timeoutSource.Dispose();
        }
    }

    internal int SetLimit(int limit)
    {
        if (limit < 1 || limit > _configuredMaximum)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit,
                $"The effective limit must be between 1 and {_configuredMaximum}.");
        }

        Waiter? released = null;
        lock (_sync)
        {
            if (_disposed)
            {
                return _effectiveLimit;
            }

            if (limit == _effectiveLimit)
            {
                AssertInvariantUnderLock();
                return limit;
            }

            var wasRaised = limit > _effectiveLimit;
            _effectiveLimit = limit;
            if (wasRaised)
            {
                released = PromoteWaitersUnderLock();
            }

            AssertInvariantUnderLock();
        }

        CompleteWaiters(released);
        return limit;
    }

    private Waiter? PromoteWaitersUnderLock()
    {
        Waiter? released = null;
        Waiter? releasedTail = null;

        while (_active < _effectiveLimit && _queueCount > 0)
        {
            var waiter = _waiterHead!;
            RemoveHeadUnderLock(waiter);
            // Defensive only: cancellation unlinks waiters while holding the same lock, so a
            // queued waiter should normally always transition from state 0 to state 2 here.
            if (!waiter.TryMarkGranted())
            {
                waiter.UnregisterCancellation();
                continue;
            }

            waiter.GrantedLease = CreateLeaseUnderLock();
            waiter.GrantNext = null;
            if (releasedTail == null)
            {
                released = waiter;
            }
            else
            {
                releasedTail.GrantNext = waiter;
            }

            releasedTail = waiter;
        }

        return released;
    }

    private void Release(int leaseSlot, int leaseGeneration)
    {
        Waiter? released;

        lock (_sync)
        {
            if (leaseSlot == -1)
            {
                if (!_singleLeaseSlot || _singleLeaseState != leaseGeneration)
                {
                    return;
                }

                _singleLeaseState = -leaseGeneration;
            }
            else if (leaseSlot >= _leaseSlotCount || _leaseStates[leaseSlot] != leaseGeneration)
            {
                return;
            }
            else
            {
                _leaseStates[leaseSlot] = -leaseGeneration;
                _freeLeaseSlots[_freeLeaseSlotCount++] = leaseSlot;
            }

            _active--;
            released = PromoteWaitersUnderLock();
            AssertInvariantUnderLock();
        }

        CompleteWaiters(released);
    }

    private Lease CreateLeaseUnderLock()
    {
        if (_singleLeaseSlot)
        {
            var singleGeneration = _singleLeaseState < 0
                ? -_singleLeaseState + 1
                : _singleLeaseState + 1;
            if (singleGeneration <= 0)
            {
                singleGeneration = 1;
            }

            _singleLeaseState = singleGeneration;
            _active++;
            return new Lease(this, -1, singleGeneration);
        }

        if (_freeLeaseSlotCount == 0)
        {
            var newSlotCount = _leaseSlotCount == 0
                ? Math.Min(_configuredMaximum, 4)
                : _leaseSlotCount > _configuredMaximum / 2
                    ? _configuredMaximum
                    : _leaseSlotCount * 2;
            var oldSlotCount = _leaseSlotCount;
            Array.Resize(ref _freeLeaseSlots, newSlotCount);
            Array.Resize(ref _leaseStates, newSlotCount);
            for (var slot = oldSlotCount; slot < newSlotCount; slot++)
            {
                _freeLeaseSlots[_freeLeaseSlotCount++] = slot;
            }

            _leaseSlotCount = newSlotCount;
        }

        var leaseSlot = _freeLeaseSlots[--_freeLeaseSlotCount];
        var previousState = _leaseStates[leaseSlot];
        var leaseGeneration = previousState < 0 ? -previousState + 1 : previousState + 1;
        if (leaseGeneration <= 0)
        {
            leaseGeneration = 1;
        }

        _leaseStates[leaseSlot] = leaseGeneration;
        _active++;
        return new Lease(this, leaseSlot, leaseGeneration);
    }

    private bool Cancel(Waiter waiter)
    {
        return Cancel(waiter, WaiterFailure.Canceled);
    }

    private bool Cancel(Waiter waiter, WaiterFailure failure)
    {
        lock (_sync)
        {
            if (!waiter.TryMarkCanceled(failure))
            {
                return false;
            }

            if (waiter.IsQueued)
            {
                RemoveUnderLock(waiter);
            }

            AssertInvariantUnderLock();
        }

        waiter.Signal?.Set();
        waiter.Completion?.TrySetCanceled(waiter.CancellationToken);
        waiter.UnregisterCancellation();
        return true;
    }

    private static void CompleteWaiters(Waiter? waiters)
    {
        while (waiters != null)
        {
            var waiter = waiters;
            waiter.UnregisterCancellation();
            var next = waiter.GrantNext;
            waiter.GrantNext = null;
            waiter.CompleteWithLease();
            waiters = next;
        }
    }

    public void Dispose()
    {
        Waiter? canceled;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            canceled = null;
            Waiter? canceledTail = null;
            while (_queueCount > 0)
            {
                var waiter = _waiterHead!;
                RemoveHeadUnderLock(waiter);
                if (waiter.TryMarkCanceled(WaiterFailure.Disposed))
                {
                    waiter.GrantNext = null;
                    if (canceledTail == null)
                    {
                        canceled = waiter;
                    }
                    else
                    {
                        canceledTail.GrantNext = waiter;
                    }

                    canceledTail = waiter;
                }
            }

            AssertInvariantUnderLock();
        }

        while (canceled != null)
        {
            var waiter = canceled;
            canceled = waiter.GrantNext;
            waiter.GrantNext = null;
            waiter.UnregisterCancellation();
            waiter.Signal?.Set();
            waiter.Completion?.TrySetException(new ObjectDisposedException(nameof(AdaptiveConcurrencyGate)));
        }
    }

    private void EnqueueUnderLock(Waiter waiter)
    {
        waiter.Previous = _waiterTail;
        waiter.Next = null;
        waiter.IsQueued = true;
        if (_waiterTail == null)
        {
            _waiterHead = waiter;
        }
        else
        {
            _waiterTail.Next = waiter;
        }

        _waiterTail = waiter;
        _queueCount++;
    }

    private void RemoveHeadUnderLock(Waiter waiter)
    {
        if (waiter != _waiterHead)
        {
            throw new InvalidOperationException("The concurrency gate queue head was corrupted.");
        }

        RemoveUnderLock(waiter);
    }

    private void RemoveUnderLock(Waiter waiter)
    {
        if (waiter.Previous == null)
        {
            _waiterHead = waiter.Next;
        }
        else
        {
            waiter.Previous.Next = waiter.Next;
        }

        if (waiter.Next == null)
        {
            _waiterTail = waiter.Previous;
        }
        else
        {
            waiter.Next.Previous = waiter.Previous;
        }

        waiter.Previous = null;
        waiter.Next = null;
        waiter.IsQueued = false;
        _queueCount--;
    }

    private void ThrowIfDisposedUnderLock()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AdaptiveConcurrencyGate));
        }
    }

    private static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout < Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    private void AssertInvariantUnderLock()
    {
        var valid = _queueCount == 0 || _active >= _effectiveLimit;
        Debug.Assert(valid, "A live waiter must not remain while capacity is available.");
        if (!valid)
        {
            Environment.FailFast(
                "Adaptive concurrency gate invariant violated: a live waiter remains while capacity is available.");
        }
    }

    internal readonly struct Lease : IDisposable, IAsyncDisposable
    {
        private readonly AdaptiveConcurrencyGate _gate;
        private readonly int _leaseSlot;
        private readonly int _leaseGeneration;

        internal Lease(AdaptiveConcurrencyGate gate, int leaseSlot, int leaseGeneration)
        {
            _gate = gate;
            _leaseSlot = leaseSlot;
            _leaseGeneration = leaseGeneration;
        }

        public void Dispose()
        {
            _gate.Release(_leaseSlot, _leaseGeneration);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private enum WaiterFailure
    {
        Canceled,
        Timeout,
        Disposed
    }

    private sealed class Waiter
    {
        private readonly AdaptiveConcurrencyGate _gate;
        private CancellationTokenRegistration _registration;
        private readonly bool _synchronous;
        private int _state;
        private WaiterFailure _failure;

        internal Waiter(AdaptiveConcurrencyGate gate, CancellationToken cancellationToken, bool synchronous)
        {
            _gate = gate;
            CancellationToken = cancellationToken;
            _synchronous = synchronous;
            if (synchronous)
            {
                Signal = new ManualResetEventSlim(false);
            }
            else
            {
                Completion = new TaskCompletionSource<Lease>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        internal CancellationToken CancellationToken { get; }
        internal TaskCompletionSource<Lease>? Completion { get; }
        internal ManualResetEventSlim? Signal { get; }
        internal Lease GrantedLease { get; set; }
        internal Waiter? Previous { get; set; }
        internal Waiter? Next { get; set; }
        internal Waiter? GrantNext { get; set; }
        internal bool IsQueued { get; set; }

        internal void RegisterCancellation()
        {
            if (CancellationToken.CanBeCanceled)
            {
                _registration = CancellationToken.Register(static state =>
                {
                    var waiter = (Waiter)state!;
                    waiter._gate.Cancel(waiter);
                }, this);
            }
        }

        internal bool TryMarkCanceled(WaiterFailure failure)
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            {
                return false;
            }

            _failure = failure;
            return true;
        }

        internal bool TryMarkGranted() => Interlocked.CompareExchange(ref _state, 2, 0) == 0;

        internal void CompleteWithLease()
        {
            if (_synchronous)
            {
                Signal!.Set();
                return;
            }

            Completion!.TrySetResult(GrantedLease);
        }

        internal Lease TakeSynchronousResult()
        {
            try
            {
                if (Volatile.Read(ref _state) == 2)
                {
                    return GrantedLease;
                }

                if (_failure == WaiterFailure.Timeout)
                {
                    throw new TimeoutException("The concurrency gate acquisition timed out.");
                }

                if (_failure == WaiterFailure.Disposed)
                {
                    throw new ObjectDisposedException(nameof(AdaptiveConcurrencyGate));
                }

                throw new OperationCanceledException(CancellationToken);
            }
            finally
            {
                Signal!.Dispose();
            }
        }

        internal void UnregisterCancellation() => _registration.Unregister();
    }
}
