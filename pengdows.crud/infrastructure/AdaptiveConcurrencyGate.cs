namespace pengdows.crud.infrastructure;

/// <summary>
/// A single logical concurrency gate whose effective limit may be lowered or raised while
/// leases are outstanding. Lowering the limit never revokes existing leases.
/// </summary>
internal sealed class AdaptiveConcurrencyGate : IDisposable
{
    private readonly object _sync = new();
    private readonly int _configuredMaximum;
    private readonly LinkedList<Waiter> _waiters = new();
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
                return _waiters.Count;
            }
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

            if (_active < _effectiveLimit && _waiters.Count == 0)
            {
                _active++;
                return ValueTask.FromResult(new Lease(this));
            }

            var waiter = new Waiter(this, cancellationToken);
            waiter.Node = _waiters.AddLast(waiter);
            waiter.RegisterCancellation();
            return new ValueTask<Lease>(waiter.Completion.Task);
        }
    }

    internal int SetLimit(int limit)
    {
        if (limit < 1 || limit > _configuredMaximum)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit,
                $"The effective limit must be between 1 and {_configuredMaximum}.");
        }

        List<Waiter>? released = null;
        lock (_sync)
        {
            if (_disposed)
            {
                return _effectiveLimit;
            }

            if (limit == _effectiveLimit)
            {
                return limit;
            }

            var wasRaised = limit > _effectiveLimit;
            _effectiveLimit = limit;
            if (wasRaised)
            {
                released = PromoteWaitersUnderLock();
            }
        }

        CompleteWaiters(released);
        return limit;
    }

    private List<Waiter>? PromoteWaitersUnderLock()
    {
        List<Waiter>? released = null;

        while (_active < _effectiveLimit && _waiters.Count > 0)
        {
            var node = _waiters.First!;
            var waiter = node.Value;
            _waiters.RemoveFirst();
            waiter.Node = null;
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

            if (waiter.Node != null)
            {
                _waiters.Remove(waiter.Node);
                waiter.Node = null;
            }
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
            while (_waiters.First != null)
            {
                var node = _waiters.First;
                var waiter = node!.Value;
                _waiters.RemoveFirst();
                waiter.Node = null;
                if (waiter.TryMarkCanceled())
                {
                    (canceled ??= new List<Waiter>()).Add(waiter);
                }
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
        internal LinkedListNode<Waiter>? Node;
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
