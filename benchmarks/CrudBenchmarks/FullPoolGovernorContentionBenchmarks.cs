using BenchmarkDotNet.Attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

[MemoryDiagnoser]
public class FullPoolGovernorContentionBenchmarks
{
    private static readonly TimeSpan ContentionWatchdogTimeout = TimeSpan.FromSeconds(10);

    [Params(1, 4)]
    public int MaxSlots { get; set; }

    [Params(32, 128)]
    public int Workers { get; set; }

    [Params(100)]
    public int OperationsPerWorker { get; set; }

    private PoolGovernor _semaphoreGovernor = null!;
    private SemaphoreSlim _semaphoreAdmission = null!;
    private PoolGovernorAdaptiveCopy _adaptiveGovernor = null!;
    private DirectPoolGovernorContentionCopy _directGovernor = null!;

    [GlobalSetup]
    public void Setup()
    {
        var queueDepth = Workers * 2;
        _semaphoreAdmission = new SemaphoreSlim(MaxSlots, MaxSlots);
        _semaphoreGovernor = new PoolGovernor(PoolLabel.Writer, "contention", MaxSlots, TimeSpan.FromSeconds(30),
            maxQueueDepth: queueDepth, sharedSemaphore: _semaphoreAdmission);
        _adaptiveGovernor = new PoolGovernorAdaptiveCopy(PoolLabel.Writer, "contention", MaxSlots, TimeSpan.FromSeconds(30), maxQueueDepth: queueDepth);
        _directGovernor = new DirectPoolGovernorContentionCopy(MaxSlots, queueDepth);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _semaphoreGovernor.Dispose();
        _semaphoreAdmission.Dispose();
        _adaptiveGovernor.Dispose();
        _directGovernor.Dispose();
    }

    [Benchmark(Baseline = true)]
    public Task SemaphoreAsyncContention() => RunSemaphoreAsync();

    [Benchmark]
    public Task AdaptiveAsyncContention() => RunAdaptiveAsync();

    [Benchmark]
    public Task DirectGateAsyncContention() => RunDirectAsync();

    private async Task RunSemaphoreAsync()
    {
        var workers = new Task[Workers];
        for (var worker = 0; worker < workers.Length; worker++)
        {
            workers[worker] = RunSemaphoreWorkerAsync();
        }

        await Task.WhenAll(workers)
            .WaitAsync(ContentionWatchdogTimeout)
            .ConfigureAwait(false);
    }

    private async Task RunAdaptiveAsync()
    {
        var workers = new Task[Workers];
        for (var worker = 0; worker < workers.Length; worker++)
        {
            workers[worker] = RunAdaptiveWorkerAsync();
        }

        await Task.WhenAll(workers)
            .WaitAsync(ContentionWatchdogTimeout)
            .ConfigureAwait(false);
    }

    private async Task RunDirectAsync()
    {
        var workers = new Task[Workers];
        for (var worker = 0; worker < workers.Length; worker++)
        {
            workers[worker] = RunDirectWorkerAsync();
        }

        await Task.WhenAll(workers)
            .WaitAsync(ContentionWatchdogTimeout)
            .ConfigureAwait(false);
    }

    private async Task RunSemaphoreWorkerAsync()
    {
        for (var operation = 0; operation < OperationsPerWorker; operation++)
        {
            await using var slot = await _semaphoreGovernor.AcquireAsync().ConfigureAwait(false);
            await Task.Yield();
        }
    }

    private async Task RunAdaptiveWorkerAsync()
    {
        for (var operation = 0; operation < OperationsPerWorker; operation++)
        {
            await using var slot = await _adaptiveGovernor.AcquireAsync().ConfigureAwait(false);
            await Task.Yield();
        }
    }

    private async Task RunDirectWorkerAsync()
    {
        for (var operation = 0; operation < OperationsPerWorker; operation++)
        {
            await using var slot = await _directGovernor.AcquireAsync().ConfigureAwait(false);
            await Task.Yield();
        }
    }
}
