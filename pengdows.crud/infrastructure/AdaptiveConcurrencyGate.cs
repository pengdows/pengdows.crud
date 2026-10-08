namespace pengdows.crud.infrastructure;

/// <summary>
/// A single logical concurrency gate whose effective limit may be lowered or raised while
/// leases are outstanding. Lowering the limit never revokes existing leases.
/// </summary>
internal sealed class AdaptiveConcurrencyGate : IDisposable
{
    private readonly object _sync = new();
    private readonly int _configuredMaximum;
    private readonly Queue<Waiter> _waiters = new();
    private int _effectiveLimit;
    private int _active;
    private bool _disposed;

    internal AdaptiveConcurrencyGate(int configuredMaximum)
    {
        if (configuredMaximum <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(configuredMaximum));
        }

        _configuredMaximum = configuredMaximum;
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
                return _waiters.Count(waiter => waiter.IsQueued);
            }
        }
    }

    internal ValueTask<Lease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            ThrowIfDisposed();

            if (_active < _effectiveLimit && _waiters.Count == 0)
            {
                _active++;
                return ValueTask.FromResult(new Lease(this));
            }

            var waiter = new Waiter(this, cancellationToken);
            _waiters.Enqueue(waiter);
            waiter.RegisterCancellation();
            return new ValueTask<Lease>(waiter.Completion.Task);
        }
    }

    internal bool LowerLimit(int limit)
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (limit < 1 || limit >= _effectiveLimit || limit > _configuredMaximum)
            {
                return false;
            }

            _effectiveLimit = limit;
            return true;
        }
    }

    internal bool RaiseLimit(int limit)
    {
        List<Waiter>? released = null;

        lock (_sync)
        {
            ThrowIfDisposed();

            if (limit <= _effectiveLimit || limit > _configuredMaximum)
            {
                return false;
            }

            _effectiveLimit = limit;
            released = PromoteWaitersUnderLock();
        }

        CompleteWaiters(released);
        return true;
    }

    private List<Waiter>? PromoteWaitersUnderLock()
    {
        List<Waiter>? released = null;

        while (_active < _effectiveLimit && _waiters.Count > 0)
        {
            var waiter = _waiters.Dequeue();
            if (!waiter.TryMarkGranted())
            {
                waiter.UnregisterCancellation();
                continue;
            }

            _active++;
            (released ??= new List<Waiter>()).Add(waiter);
        }

        return released;
    }

    private void Release()
    {
        List<Waiter>? released;

        lock (_sync)
        {
            if (_active <= 0)
            {
                throw new InvalidOperationException("The adaptive concurrency gate was released without an active lease.");
            }

            _active--;
            released = PromoteWaitersUnderLock();
        }

        CompleteWaiters(released);
    }

    private void Cancel(Waiter waiter)
    {
        lock (_sync)
        {
            if (!waiter.TryMarkCanceled())
            {
                return;
            }

            // Queue removal is intentionally deferred. The queue is normally short and lazy
            // removal keeps cancellation from changing FIFO ordering or requiring a linked list.
        }

        waiter.Completion.TrySetCanceled(waiter.CancellationToken);
        waiter.UnregisterCancellation();
    }

    private static void CompleteWaiters(List<Waiter>? waiters)
    {
        if (waiters == null)
        {
            return;
        }

        foreach (var waiter in waiters)
        {
            waiter.UnregisterCancellation();
            waiter.Completion.TrySetResult(new Lease(waiter.Gate));
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AdaptiveConcurrencyGate));
        }
    }

    public void Dispose()
    {
        List<Waiter>? canceled = null;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            while (_waiters.Count > 0)
            {
                var waiter = _waiters.Dequeue();
                waiter.TryMarkCanceled();
                (canceled ??= new List<Waiter>()).Add(waiter);
            }
        }

        if (canceled == null)
        {
            return;
        }

        foreach (var waiter in canceled)
        {
            waiter.UnregisterCancellation();
            waiter.Completion.TrySetException(new ObjectDisposedException(nameof(AdaptiveConcurrencyGate)));
        }
    }

    internal sealed class Lease : IDisposable, IAsyncDisposable
    {
        private readonly AdaptiveConcurrencyGate _gate;
        private int _released;

        internal Lease(AdaptiveConcurrencyGate gate)
        {
            _gate = gate;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _gate.Release();
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Waiter
    {
        private readonly AdaptiveConcurrencyGate _gate;
        private CancellationTokenRegistration _registration;
        private int _state;

        internal Waiter(AdaptiveConcurrencyGate gate, CancellationToken cancellationToken)
        {
            _gate = gate;
            CancellationToken = cancellationToken;
            Completion = new TaskCompletionSource<Lease>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        internal AdaptiveConcurrencyGate Gate => _gate;
        internal CancellationToken CancellationToken { get; }
        internal TaskCompletionSource<Lease> Completion { get; }
        internal bool IsQueued => Volatile.Read(ref _state) == 0;
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

        internal bool TryMarkCanceled() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

        internal bool TryMarkGranted() => Interlocked.CompareExchange(ref _state, 2, 0) == 0;

        internal void UnregisterCancellation() => _registration.Unregister();
    }
}
