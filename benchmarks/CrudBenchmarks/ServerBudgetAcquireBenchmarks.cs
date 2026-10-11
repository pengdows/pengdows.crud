using BenchmarkDotNet.Attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace CrudBenchmarks;

/// <summary>Which per-server budget a governor is in front of (POOL-003).</summary>
public enum BudgetVariant
{
    /// <summary>No budget registered: the governor exactly as it was before POOL-003.</summary>
    None,

    /// <summary>Registered, but the registered sizes fit the limit, so the budget never binds: the cost is one volatile read.</summary>
    Disengaged,

    /// <summary>Registered and engaged: every lease also takes and returns a server permit.</summary>
    Engaged
}

/// <summary>
/// What the per-server budget adds to the uncontended acquire/release of <see cref="PoolGovernor"/>, blocking and
/// asynchronous. Three variants against the same 8-slot governor: no budget, a budget that is registered but can never
/// bind, and an engaged budget. The acceptance bar (docs/FUTURE_WORK.md, POOL-003) is a few nanoseconds and 0 B when
/// engaged, and no change within noise when disengaged.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 12)]
public class ServerBudgetAcquireBenchmarks
{
    private const int Operations = 1_000;

    [Params(BudgetVariant.None, BudgetVariant.Disengaged, BudgetVariant.Engaged)]
    public BudgetVariant Variant { get; set; }

    private ServerBudgetRig _rig = null!;

    [GlobalSetup]
    public void Setup() => _rig = ServerBudgetRig.Create(Variant);

    [GlobalCleanup]
    public void Cleanup() => _rig.Dispose();

    [Benchmark(OperationsPerInvoke = Operations)]
    public int Blocking() => _rig.RunBlocking(Operations);

    [Benchmark(OperationsPerInvoke = Operations)]
    public async Task<int> Async() => await _rig.RunAsync(Operations);
}

/// <summary>One 8-slot governor, one caller, nothing contending, in front of the chosen budget variant.</summary>
internal sealed class ServerBudgetRig : IDisposable
{
    private const int Slots = 8;
    private readonly PoolGovernor _governor;
    private readonly ServerBudgetRegistration? _registration;

    private ServerBudgetRig(PoolGovernor governor, ServerBudgetRegistration? registration)
    {
        _governor = governor;
        _registration = registration;
    }

    internal static ServerBudgetRig Create(BudgetVariant variant)
    {
        var key = "bench-" + Guid.NewGuid().ToString("N") + ":5432";
        ServerBudgetRegistration? registration = variant switch
        {
            BudgetVariant.None => null,
            // 8 against a limit of 1,000: the sizes fit, so the budget never binds.
            BudgetVariant.Disengaged => ServerBudgetRegistry.Register(key, usableLimit: 1_000, configuredMaximum: Slots),
            // 8 against a limit of 4: the sizes exceed it, so the budget engages (one caller never waits at 4).
            _ => ServerBudgetRegistry.Register(key, usableLimit: 4, configuredMaximum: Slots)
        };

        var governor = new PoolGovernor(PoolLabel.Reader, "budget-bench", Slots, TimeSpan.FromSeconds(30),
            serverBudget: registration);
        return new ServerBudgetRig(governor, registration);
    }

    internal bool HasBudget => _registration != null;
    internal bool BudgetEngaged => _registration?.Budget.Engaged ?? false;
    internal int InUse => _governor.GetSnapshot().InUse;
    internal int BudgetInUse => _registration?.Budget.InUse ?? 0;
    internal long TotalWaits => _governor.GetSnapshot().TotalWaits;

    internal int BudgetInUseWhileOneLeaseIsHeld()
    {
        using var lease = _governor.Acquire();
        return BudgetInUse;
    }

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
        _registration?.Dispose();
    }
}
