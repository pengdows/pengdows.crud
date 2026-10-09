using System.Threading;
using BenchmarkDotNet.Attributes;
using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

/// <summary>
/// Benchmark-only copy of PoolGovernor's slot-admission hot path. The two copies deliberately
/// retain the same PoolSlot-shaped token allocation and accounting; only the admission primitive
/// differs. This is not production code and must not be used as a substitute for the governor.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5, invocationCount: 1_000_000)]
public class PoolGovernorAdmissionCopyBenchmarks
{
    [Params(100)]
    public int MaxSlots { get; set; }

    private SemaphorePoolGovernorCopy _semaphoreGovernor = null!;
    private AdaptivePoolGovernorCopy _adaptiveGovernor = null!;

    [GlobalSetup]
    public void Setup()
    {
        _semaphoreGovernor = new SemaphorePoolGovernorCopy(MaxSlots);
        _adaptiveGovernor = new AdaptivePoolGovernorCopy(MaxSlots);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _semaphoreGovernor.Dispose();
        _adaptiveGovernor.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void SemaphoreSync()
    {
        using var slot = _semaphoreGovernor.Acquire();
    }

    [Benchmark]
    public void AdaptiveSync()
    {
        using var slot = _adaptiveGovernor.Acquire();
    }

    [Benchmark]
    public async ValueTask SemaphoreAsync()
    {
        await using var slot = await _semaphoreGovernor.AcquireAsync().ConfigureAwait(false);
    }

    [Benchmark]
    public async ValueTask AdaptiveAsync()
    {
        await using var slot = await _adaptiveGovernor.AcquireAsync().ConfigureAwait(false);
    }
}

internal sealed class SemaphorePoolGovernorCopy : IDisposable
{
    private readonly SemaphoreSlim _admission;
    private long _inUse;
    private long _totalAcquired;

    internal SemaphorePoolGovernorCopy(int maxSlots)
    {
        _admission = new SemaphoreSlim(maxSlots, maxSlots);
    }

    internal PoolGovernorCopySlot Acquire()
    {
        _admission.Wait();
        OnAcquired();
        return new PoolGovernorCopySlot(new SemaphoreToken(this));
    }

    internal async ValueTask<PoolGovernorCopySlot> AcquireAsync()
    {
        await _admission.WaitAsync().ConfigureAwait(false);
        OnAcquired();
        return new PoolGovernorCopySlot(new SemaphoreToken(this));
    }

    private void OnAcquired()
    {
        Interlocked.Increment(ref _inUse);
        Interlocked.Increment(ref _totalAcquired);
    }

    private void Release()
    {
        _admission.Release();
        Interlocked.Decrement(ref _inUse);
    }

    public void Dispose() => _admission.Dispose();

    private sealed class SemaphoreToken : IDisposable
    {
        private readonly SemaphorePoolGovernorCopy _owner;
        private int _released;

        internal SemaphoreToken(SemaphorePoolGovernorCopy owner) => _owner = owner;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _owner.Release();
            }
        }
    }
}

internal sealed class AdaptivePoolGovernorCopy : IDisposable
{
    private readonly PoolGovernorConcurrencyGate _admission;
    private long _inUse;
    private long _totalAcquired;

    internal AdaptivePoolGovernorCopy(int maxSlots)
    {
        _admission = new PoolGovernorConcurrencyGate(maxSlots);
    }

    internal PoolGovernorCopySlot Acquire()
    {
        var slot = _admission.Acquire();
        OnAcquired();
        return new PoolGovernorCopySlot(new AdaptiveToken(this, slot));
    }

    internal async ValueTask<PoolGovernorCopySlot> AcquireAsync()
    {
        var slot = await _admission.AcquireAsync().ConfigureAwait(false);
        OnAcquired();
        return new PoolGovernorCopySlot(new AdaptiveToken(this, slot));
    }

    private void OnAcquired()
    {
        Interlocked.Increment(ref _inUse);
        Interlocked.Increment(ref _totalAcquired);
    }

    private void Release(int slot)
    {
        _admission.Release(slot);
        Interlocked.Decrement(ref _inUse);
    }

    public void Dispose() => _admission.Dispose();

    private sealed class AdaptiveToken : IDisposable
    {
        private readonly AdaptivePoolGovernorCopy _owner;
        private readonly int _slot;
        private int _released;

        internal AdaptiveToken(AdaptivePoolGovernorCopy owner, int slot)
        {
            _owner = owner;
            _slot = slot;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _owner.Release(_slot);
            }
        }
    }
}

internal readonly struct PoolGovernorCopySlot : IDisposable, IAsyncDisposable
{
    private readonly IDisposable? _token;

    internal PoolGovernorCopySlot(IDisposable token) => _token = token;

    public void Dispose() => _token?.Dispose();

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
