using BenchmarkDotNet.Attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

/// <summary>
/// The uncontended hot path of <see cref="PoolGovernor"/>: acquire a free slot and give it back, blocking and
/// asynchronous, with and without the writer turnstile (the shape SingleWriter uses). Nothing ever queues, so this
/// is the cost every connection checkout pays before it does any work, and it is what a change to the acquire code
/// must not slow down.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 12)]
public class PoolGovernorAcquireBenchmarks
{
    private const int Operations = 1_000;

    [Params(false, true)]
    public bool WithTurnstile { get; set; }

    private AcquireRig _rig = null!;

    [GlobalSetup]
    public void Setup() => _rig = AcquireRig.Create(WithTurnstile);

    [GlobalCleanup]
    public void Cleanup() => _rig.Dispose();

    [Benchmark(OperationsPerInvoke = Operations)]
    public int Blocking() => _rig.RunBlocking(Operations);

    [Benchmark(OperationsPerInvoke = Operations)]
    public async Task<int> Async() => await _rig.RunAsync(Operations);
}

/// <summary>One governor, one slot, nothing contending for it.</summary>
internal sealed class AcquireRig : IDisposable
{
    internal const int Slots = 1;
    internal const int WriterSlots = Slots;

    private readonly PoolGovernor _governor;
    private readonly SemaphoreSlim? _turnstile;

    private AcquireRig(PoolGovernor governor, SemaphoreSlim? turnstile)
    {
        _governor = governor;
        _turnstile = turnstile;
    }

    internal static AcquireRig Create(bool withTurnstile)
    {
        if (!withTurnstile)
        {
            return new AcquireRig(
                new PoolGovernor(PoolLabel.Reader, "acquire-bench", Slots, TimeSpan.FromSeconds(30)), null);
        }

        // A writer that holds the turnstile for the life of its slot: how SingleWriter serializes writes.
        var turnstile = new SemaphoreSlim(1, 1);
        return new AcquireRig(
            new PoolGovernor(PoolLabel.Writer, "acquire-bench", Slots, TimeSpan.FromSeconds(30),
                turnstile: turnstile, holdTurnstile: true), turnstile);
    }

    internal bool HasTurnstile => _turnstile != null;
    internal int AvailablePermits => _governor.AvailablePermits;
    internal int InUse => _governor.GetSnapshot().InUse;
    internal long TotalWaits => _governor.GetSnapshot().TotalWaits;

    internal int RunBlocking(int operations)
    {
        for (var i = 0; i < operations; i++)
        {
            _governor.Acquire().Dispose();
        }

        return operations;
    }

    internal async Task<int> RunAsync(int operations)
    {
        for (var i = 0; i < operations; i++)
        {
            (await _governor.AcquireAsync().ConfigureAwait(false)).Dispose();
        }

        return operations;
    }

    public void Dispose()
    {
        _governor.Dispose();
        _turnstile?.Dispose();
    }
}
