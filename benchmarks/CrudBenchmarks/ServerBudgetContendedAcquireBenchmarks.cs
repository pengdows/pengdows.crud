using BenchmarkDotNet.Attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

/// <summary>
/// The per-server budget under contention: every thread acquires and releases on its own governor while all the
/// governors draw on one engaged budget, so the only thing the threads fight over is the budget's counter (one cache
/// line). Compared with the same threads and no budget. Nothing ever queues; a wait would time a timeout, not the atomic.
/// Reported per operation as wall-clock time over all threads' operations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 12)]
public class ServerBudgetContendedAcquireBenchmarks
{
    private const int Threads = 8;
    private const int OperationsPerThread = 50_000;

    [Params(BudgetVariant.None, BudgetVariant.Engaged)]
    public BudgetVariant Variant { get; set; }

    private ServerBudgetContendedRig _rig = null!;

    [GlobalSetup]
    public void Setup() => _rig = ServerBudgetContendedRig.Create(Variant, Threads);

    [GlobalCleanup]
    public void Cleanup() => _rig.Dispose();

    [Benchmark(OperationsPerInvoke = Threads * OperationsPerThread)]
    public int Blocking() => _rig.RunBlocking(OperationsPerThread);
}

internal sealed class ServerBudgetContendedRig : IDisposable
{
    private const int SlotsPerGovernor = 64;
    private readonly PoolGovernor[] _governors;
    private readonly ServerBudgetRegistration[] _registrations;

    private ServerBudgetContendedRig(PoolGovernor[] governors, ServerBudgetRegistration[] registrations)
    {
        _governors = governors;
        _registrations = registrations;
    }

    internal static ServerBudgetContendedRig Create(BudgetVariant variant, int threads)
    {
        var key = "bench-contended-" + Guid.NewGuid().ToString("N") + ":5432";
        var governors = new PoolGovernor[threads];
        var registrations = new List<ServerBudgetRegistration>();
        for (var i = 0; i < threads; i++)
        {
            ServerBudgetRegistration? registration = null;
            if (variant == BudgetVariant.Engaged)
            {
                // threads x 64 against a limit of twice the threads: the sizes exceed it, so the budget engages, but
                // only `threads` leases are ever out at once, so nobody waits.
                registration = ServerBudgetRegistry.Register(key, usableLimit: threads * 2, SlotsPerGovernor);
                registrations.Add(registration);
            }

            governors[i] = new PoolGovernor(PoolLabel.Reader, "contended-" + i, SlotsPerGovernor,
                TimeSpan.FromSeconds(30), serverBudget: registration);
        }

        return new ServerBudgetContendedRig(governors, registrations.ToArray());
    }

    internal int GovernorCount => _governors.Length;
    internal bool BudgetEngaged => _registrations.Length > 0 && _registrations[0].Budget.Engaged;
    internal int BudgetRegistrants => _registrations.Length > 0 ? _registrations[0].Budget.Registrants : 0;
    internal int BudgetInUse => _registrations.Length > 0 ? _registrations[0].Budget.InUse : 0;
    internal int InUse => _governors.Sum(g => g.GetSnapshot().InUse);
    internal long TotalWaits => _governors.Sum(g => g.GetSnapshot().TotalWaits);

    internal int RunBlocking(int operationsPerThread)
    {
        var threads = new Thread[_governors.Length];
        for (var t = 0; t < threads.Length; t++)
        {
            var governor = _governors[t];
            threads[t] = new Thread(() =>
            {
                for (var i = 0; i < operationsPerThread; i++)
                {
                    governor.Acquire().Dispose();
                }
            }) { IsBackground = true };
        }

        foreach (var thread in threads) { thread.Start(); }
        foreach (var thread in threads) { thread.Join(); }
        return operationsPerThread * _governors.Length;
    }

    public void Dispose()
    {
        foreach (var governor in _governors) { governor.Dispose(); }
        foreach (var registration in _registrations) { registration.Dispose(); }
    }
}
