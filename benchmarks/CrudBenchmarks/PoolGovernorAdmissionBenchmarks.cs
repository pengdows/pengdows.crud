using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

/// <summary>
/// The real <see cref="PoolGovernor"/>, built two ways that differ only in the admission primitive: with its
/// default ConcurrencyGate, or handed a SemaphoreSlim of the same size. Everything else (queue depth,
/// timeout, slot accounting, metrics off) is identical, so a difference between the arms is the primitive.
/// </summary>
internal sealed class AdmissionArm : IDisposable
{
    private readonly SemaphoreSlim? _semaphore;

    private AdmissionArm(PoolGovernor governor, SemaphoreSlim? semaphore)
    {
        Governor = governor;
        _semaphore = semaphore;
    }

    internal PoolGovernor Governor { get; }

    internal bool UsesItsOwnGate => Governor.OwnsSemaphore;
    internal int AvailablePermits => Governor.AvailablePermits;
    internal int InUse => Governor.GetSnapshot().InUse;
    internal long TotalWaits => Governor.GetSnapshot().TotalWaits;
    internal int MaxQueueDepth => Governor.MaxQueueDepth;

    internal static AdmissionArm Gate(int slots) => new(NewGovernor(slots, null), null);

    internal static AdmissionArm Semaphore(int slots)
    {
        var semaphore = new SemaphoreSlim(slots, slots);
        return new AdmissionArm(NewGovernor(slots, semaphore), semaphore);
    }

    // An unbounded queue, as a 2.0.x context configures it: the default cap would reject callers beyond it, and
    // a benchmark of a primitive must not be measuring rejections.
    private static PoolGovernor NewGovernor(int slots, SemaphoreSlim? semaphore) =>
        new(PoolLabel.Reader, "admission-benchmark", slots, TimeSpan.FromSeconds(60),
            sharedSemaphore: semaphore, maxQueueDepth: int.MaxValue);

    public void Dispose()
    {
        Governor.Dispose();
        _semaphore?.Dispose();
    }
}

internal readonly record struct WorkloadResult(long Operations, int PeakConcurrent);

/// <summary>Releases every slot it holds when disposed.</summary>
internal sealed class HeldSlots : IDisposable
{
    private readonly PoolSlot[] _slots;

    internal HeldSlots(PoolSlot[] slots) => _slots = slots;

    public void Dispose()
    {
        foreach (var slot in _slots)
        {
            slot.Dispose();
        }
    }
}

internal static class AdmissionWorkload
{
    // Contended acquire/release from many async callers, each holding briefly. Peak tracking costs an extra
    // atomic per operation, so the benchmarks switch it off and only the harness tests leave it on.
    internal static async Task<WorkloadResult> RunContendedAsync(
        AdmissionArm arm, int tasks, int opsPerTask, int holdMicroseconds, bool trackPeak = true)
    {
        var governor = arm.Governor;
        long operations = 0;
        var current = 0;
        var peak = 0;

        var workers = new Task[tasks];
        for (var t = 0; t < tasks; t++)
        {
            workers[t] = Task.Run(async () =>
            {
                for (var i = 0; i < opsPerTask; i++)
                {
                    using var slot = await governor.AcquireAsync().ConfigureAwait(false);
                    if (trackPeak)
                    {
                        RecordPeak(ref peak, Interlocked.Increment(ref current));
                    }

                    await HoldAsync(holdMicroseconds).ConfigureAwait(false);

                    if (trackPeak)
                    {
                        Interlocked.Decrement(ref current);
                    }

                    Interlocked.Increment(ref operations);
                }
            });
        }

        await Task.WhenAll(workers).ConfigureAwait(false);
        return new WorkloadResult(operations, peak);
    }

    // The same, from blocking callers. These block, so they run on dedicated threads: on the thread pool a
    // blocked caller starves it, and the benchmark would measure the pool's slow thread injection.
    internal static async Task<WorkloadResult> RunContendedSyncAsync(
        AdmissionArm arm, int tasks, int opsPerTask, int holdMicroseconds, bool trackPeak = true)
    {
        var governor = arm.Governor;
        long operations = 0;
        var current = 0;
        var peak = 0;

        // Every caller is created first and released together: threads that start one after another let
        // the early ones finish before the later ones begin, and then nothing contended at all.
        using var ready = new CountdownEvent(tasks);
        using var start = new ManualResetEventSlim(false);
        var workers = new Task[tasks];
        for (var t = 0; t < tasks; t++)
        {
            workers[t] = Task.Factory.StartNew(() =>
            {
                ready.Signal();
                start.Wait();
                for (var i = 0; i < opsPerTask; i++)
                {
                    using var slot = governor.Acquire();
                    if (trackPeak)
                    {
                        RecordPeak(ref peak, Interlocked.Increment(ref current));
                    }

                    Hold(holdMicroseconds);

                    if (trackPeak)
                    {
                        Interlocked.Decrement(ref current);
                    }

                    Interlocked.Increment(ref operations);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        ready.Wait();
        start.Set();
        await Task.WhenAll(workers).ConfigureAwait(false);
        return new WorkloadResult(operations, peak);
    }

    // Fills the governor, then queues a waiter and cancels it, <paramref name="count"/> times: the cost of
    // the cancellation path, and proof that cancelled waiters give nothing back or take nothing.
    internal static async Task<int> CancelWhileQueuedAsync(AdmissionArm arm, int slots, int count)
    {
        var governor = arm.Governor;
        var held = new PoolSlot[slots];
        for (var i = 0; i < slots; i++)
        {
            held[i] = await governor.AcquireAsync().ConfigureAwait(false);
        }

        var cancelled = 0;
        for (var i = 0; i < count; i++)
        {
            using var cts = new CancellationTokenSource();
            var waiting = governor.AcquireAsync(cts.Token).AsTask();
            cts.Cancel();
            try
            {
                (await waiting.ConfigureAwait(false)).Dispose();
            }
            catch (OperationCanceledException)
            {
                cancelled++;
            }
        }

        foreach (var slot in held)
        {
            slot.Dispose();
        }

        return cancelled;
    }

    internal static HeldSlots HoldAll(AdmissionArm arm, int slots)
    {
        var held = new PoolSlot[slots];
        for (var i = 0; i < slots; i++)
        {
            held[i] = arm.Governor.AcquireAsync().AsTask().GetAwaiter().GetResult();
        }

        return new HeldSlots(held);
    }

    // The fast-fail probe on a governor with no free slot.
    internal static bool TryAcquireOnce(AdmissionArm arm)
    {
        if (!arm.Governor.TryAcquire(out var slot))
        {
            return false;
        }

        slot.Dispose();
        return true;
    }

    // A hold this long is a database call waiting on I/O, not a CPU burning: it yields the thread, so hundreds of
    // slots can be held at once on a machine with a handful of cores. Shorter holds are work done under the
    // permit and spin.
    private const int YieldingHoldMicroseconds = 1_000;

    private static ValueTask HoldAsync(int microseconds) => microseconds >= YieldingHoldMicroseconds
        ? new ValueTask(Task.Delay(microseconds / 1_000))
        : HoldSpin(microseconds);

    private static ValueTask HoldSpin(int microseconds)
    {
        Hold(microseconds);
        return default;
    }

    // Holds the slot for the given time: a stand-in for work done under a permit.
    private static void Hold(int microseconds)
    {
        if (microseconds <= 0)
        {
            return;
        }

        if (microseconds >= YieldingHoldMicroseconds)
        {
            Thread.Sleep(microseconds / 1_000);
            return;
        }

        var end = Stopwatch.GetTimestamp() + (microseconds * Stopwatch.Frequency / 1_000_000);
        while (Stopwatch.GetTimestamp() < end)
        {
            Thread.SpinWait(1);
        }
    }

    private static void RecordPeak(ref int peak, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref peak)))
        {
            if (Interlocked.CompareExchange(ref peak, value, seen) == seen)
            {
                return;
            }
        }
    }
}

/// <summary>
/// Uncontended cost of the admission primitive inside the real governor: one caller, a free slot.
/// The SemaphoreSlim arm is the baseline of each pair, so the Ratio column reads "gate / semaphore".
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 12)]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PoolGovernorAdmissionBenchmarks
{
    [Params(1, 100)]
    public int Slots { get; set; }

    private AdmissionArm _gate = null!;
    private AdmissionArm _semaphore = null!;
    private AdmissionArm _gateFull = null!;
    private AdmissionArm _semaphoreFull = null!;
    private HeldSlots _gateHeld = null!;
    private HeldSlots _semaphoreHeld = null!;

    [GlobalSetup]
    public void Setup()
    {
        _gate = AdmissionArm.Gate(Slots);
        _semaphore = AdmissionArm.Semaphore(Slots);
        _gateFull = AdmissionArm.Gate(Slots);
        _semaphoreFull = AdmissionArm.Semaphore(Slots);
        _gateHeld = AdmissionWorkload.HoldAll(_gateFull, Slots);
        _semaphoreHeld = AdmissionWorkload.HoldAll(_semaphoreFull, Slots);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _gateHeld.Dispose();
        _semaphoreHeld.Dispose();
        _gate.Dispose();
        _semaphore.Dispose();
        _gateFull.Dispose();
        _semaphoreFull.Dispose();
    }

    [BenchmarkCategory("Uncontended-Sync"), Benchmark(Baseline = true)]
    public void SyncAcquireRelease_Semaphore()
    {
        using var slot = _semaphore.Governor.AcquireAsync().AsTask().GetAwaiter().GetResult();
    }

    [BenchmarkCategory("Uncontended-Sync"), Benchmark]
    public void SyncAcquireRelease_Gate()
    {
        using var slot = _gate.Governor.AcquireAsync().AsTask().GetAwaiter().GetResult();
    }

    [BenchmarkCategory("Uncontended-Async"), Benchmark(Baseline = true)]
    public async ValueTask AsyncAcquireRelease_Semaphore()
    {
        using var slot = await _semaphore.Governor.AcquireAsync().ConfigureAwait(false);
    }

    [BenchmarkCategory("Uncontended-Async"), Benchmark]
    public async ValueTask AsyncAcquireRelease_Gate()
    {
        using var slot = await _gate.Governor.AcquireAsync().ConfigureAwait(false);
    }

    [BenchmarkCategory("FastFail-WhenFull"), Benchmark(Baseline = true)]
    public bool TryAcquireWhenFull_Semaphore() => AdmissionWorkload.TryAcquireOnce(_semaphoreFull);

    [BenchmarkCategory("FastFail-WhenFull"), Benchmark]
    public bool TryAcquireWhenFull_Gate() => AdmissionWorkload.TryAcquireOnce(_gateFull);
}

/// <summary>
/// Contention: many callers, few slots, with and without a short hold; plus the cancellation path.
/// Each invocation is a whole batch, so the numbers are milliseconds and the allocation column is per batch.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 10)]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PoolGovernorContentionBenchmarks
{
    private const int AsyncCallers = 64;
    private const int SyncCallers = 16;
    private const int CancelCount = 500;

    // Scaled to the hold so that no cell is too short to contend or too long to measure.
    private int SyncOpsPerCaller => HoldMicroseconds switch { 0 => 2_000, 5 or 10 => 1_000, 50 or 100 => 200, _ => 10 };
    private int AsyncOpsPerCaller => HoldMicroseconds >= 10_000 ? 5 : 100;

    // 1 = fully serial, 8 = heavily contended, 64 = as many slots as async callers (no waiting).
    [Params(1, 8, 64)]
    public int Slots { get; set; }

    // Microseconds each caller holds its slot: 0 is the primitive alone; 5, 10 and 50 are very short pieces of
    // work under the permit; 100 is about a local database round trip; 10,000 (10 ms) a slow query.
    [Params(0, 5, 10, 50, 100, 10_000)]
    public int HoldMicroseconds { get; set; }

    private AdmissionArm _gate = null!;
    private AdmissionArm _semaphore = null!;

    [GlobalSetup]
    public void Setup()
    {
        _gate = AdmissionArm.Gate(Slots);
        _semaphore = AdmissionArm.Semaphore(Slots);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _gate.Dispose();
        _semaphore.Dispose();
    }

    [BenchmarkCategory("Contended-Async"), Benchmark(Baseline = true)]
    public async Task<long> Async_Semaphore() =>
        (await AdmissionWorkload.RunContendedAsync(_semaphore, AsyncCallers, AsyncOpsPerCaller, HoldMicroseconds, trackPeak: false)).Operations;

    [BenchmarkCategory("Contended-Async"), Benchmark]
    public async Task<long> Async_Gate() =>
        (await AdmissionWorkload.RunContendedAsync(_gate, AsyncCallers, AsyncOpsPerCaller, HoldMicroseconds, trackPeak: false)).Operations;

    [BenchmarkCategory("Contended-Sync"), Benchmark(Baseline = true)]
    public async Task<long> Sync_Semaphore() =>
        (await AdmissionWorkload.RunContendedSyncAsync(_semaphore, SyncCallers, SyncOpsPerCaller, HoldMicroseconds, trackPeak: false)).Operations;

    [BenchmarkCategory("Contended-Sync"), Benchmark]
    public async Task<long> Sync_Gate() =>
        (await AdmissionWorkload.RunContendedSyncAsync(_gate, SyncCallers, SyncOpsPerCaller, HoldMicroseconds, trackPeak: false)).Operations;

    // The cancellation path does not depend on the hold time, so it is measured once per slot count.
    [BenchmarkCategory("Cancel-Queued"), Benchmark(Baseline = true)]
    public Task<int> CancelQueued_Semaphore() => HoldMicroseconds == 0
        ? AdmissionWorkload.CancelWhileQueuedAsync(_semaphore, Slots, CancelCount)
        : Task.FromResult(0);

    [BenchmarkCategory("Cancel-Queued"), Benchmark]
    public Task<int> CancelQueued_Gate() => HoldMicroseconds == 0
        ? AdmissionWorkload.CancelWhileQueuedAsync(_gate, Slots, CancelCount)
        : Task.FromResult(0);
}

/// <summary>
/// A pool at its maximum size (512) with twice as many callers as slots, so half of them are always queued:
/// the exhaustion case a pool this large exists to survive. Callers must outnumber slots, otherwise nothing ever
/// waits and the benchmark would measure only the uncontended fast path.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 10)]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PoolGovernorSaturatedBenchmarks
{
    public const int MaxSlots = 512;
    public const int Callers = 1024;

    // Waking 1,024 threads takes milliseconds, so a hold much shorter than that lets early callers finish before late
    // ones arrive and the pool never saturates: those cells show how rarely 512 slots queue at all.
    private int OpsPerCaller => HoldMicroseconds switch { 0 => 40, 100 => 10, 1_000 => 5, _ => 3 };

    [Params(0, 100, 1_000, 10_000)]
    public int HoldMicroseconds { get; set; }

    private AdmissionArm _gate = null!;
    private AdmissionArm _semaphore = null!;

    [GlobalSetup]
    public void Setup()
    {
        _gate = AdmissionArm.Gate(MaxSlots);
        _semaphore = AdmissionArm.Semaphore(MaxSlots);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _gate.Dispose();
        _semaphore.Dispose();
    }

    [BenchmarkCategory("Saturated-Async"), Benchmark(Baseline = true)]
    public async Task<long> Async_Semaphore() =>
        (await AdmissionWorkload.RunContendedAsync(_semaphore, Callers, OpsPerCaller, HoldMicroseconds, trackPeak: false)).Operations;

    [BenchmarkCategory("Saturated-Async"), Benchmark]
    public async Task<long> Async_Gate() =>
        (await AdmissionWorkload.RunContendedAsync(_gate, Callers, OpsPerCaller, HoldMicroseconds, trackPeak: false)).Operations;

    [BenchmarkCategory("Saturated-Sync"), Benchmark(Baseline = true)]
    public async Task<long> Sync_Semaphore() =>
        (await AdmissionWorkload.RunContendedSyncAsync(_semaphore, Callers, OpsPerCaller, HoldMicroseconds, trackPeak: false)).Operations;

    [BenchmarkCategory("Saturated-Sync"), Benchmark]
    public async Task<long> Sync_Gate() =>
        (await AdmissionWorkload.RunContendedSyncAsync(_gate, Callers, OpsPerCaller, HoldMicroseconds, trackPeak: false)).Operations;
}
