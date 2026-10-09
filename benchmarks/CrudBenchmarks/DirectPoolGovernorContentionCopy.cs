using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

/// <summary>
/// Benchmark-only governor that calls PoolGovernorConcurrencyGate directly.
/// It intentionally contains only admission and exactly-once slot release so the
/// benchmark can isolate the adapter conversion cost from the gate itself.
/// </summary>
internal sealed class DirectPoolGovernorContentionCopy : IDisposable
{
    private readonly PoolGovernorConcurrencyGate _gate;

    internal DirectPoolGovernorContentionCopy(int capacity, int maxQueueDepth)
    {
        _gate = new PoolGovernorConcurrencyGate(capacity, maxQueueDepth);
    }

    internal async ValueTask<DirectPoolSlot> AcquireAsync(CancellationToken cancellationToken = default)
    {
        var permit = await _gate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        return new DirectPoolSlot(new DirectPoolSlotToken(this, permit));
    }

    private void Release(int permit) => _gate.Release(permit);

    public void Dispose() => _gate.Dispose();

    private sealed class DirectPoolSlotToken : IDisposable
    {
        private readonly DirectPoolGovernorContentionCopy _owner;
        private readonly int _permit;
        private int _released;

        internal DirectPoolSlotToken(DirectPoolGovernorContentionCopy owner, int permit)
        {
            _owner = owner;
            _permit = permit;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _owner.Release(_permit);
            }
        }
    }
}

internal readonly struct DirectPoolSlot : IDisposable, IAsyncDisposable
{
    private readonly IDisposable? _token;

    internal DirectPoolSlot(IDisposable token) => _token = token;

    public void Dispose() => _token?.Dispose();

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
