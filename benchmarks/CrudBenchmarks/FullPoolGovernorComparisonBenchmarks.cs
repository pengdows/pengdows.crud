using BenchmarkDotNet.Attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5, invocationCount: 1_000_000)]
public class FullPoolGovernorComparisonBenchmarks
{
    [Params(100)]
    public int MaxSlots { get; set; }

    private PoolGovernor _semaphoreGovernor = null!;
    private SemaphoreSlim _semaphoreAdmission = null!;
    private PoolGovernorAdaptiveCopy _adaptiveGovernor = null!;

    [GlobalSetup]
    public void Setup()
    {
        _semaphoreAdmission = new SemaphoreSlim(MaxSlots, MaxSlots);
        _semaphoreGovernor = new PoolGovernor(PoolLabel.Writer, "benchmark", MaxSlots, TimeSpan.FromSeconds(5),
            sharedSemaphore: _semaphoreAdmission);
        _adaptiveGovernor = new PoolGovernorAdaptiveCopy(PoolLabel.Writer, "benchmark", MaxSlots, TimeSpan.FromSeconds(5));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _semaphoreGovernor.Dispose();
        _semaphoreAdmission.Dispose();
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
