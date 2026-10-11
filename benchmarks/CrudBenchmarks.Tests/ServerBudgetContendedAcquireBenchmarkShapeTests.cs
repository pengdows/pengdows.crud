using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

// The contended server-budget benchmark has every thread acquire from its own governor while all of them draw on one
// shared budget, so the only thing they contend for is the budget's counter. It must really be contended (the budget is
// engaged), must never queue (a wait would time a timeout, not the atomic), and must return every permit.
public class ServerBudgetContendedAcquireBenchmarkShapeTests
{
    [Fact]
    public void TheEngagedRig_SharesOneEngagedBudgetAcrossEveryGovernor()
    {
        using var rig = ServerBudgetContendedRig.Create(BudgetVariant.Engaged, threads: 4);

        Assert.True(rig.BudgetEngaged);
        Assert.Equal(4, rig.GovernorCount);
        Assert.Equal(4, rig.BudgetRegistrants);
    }

    [Fact]
    public void TheNoneRig_HasNoBudget()
    {
        using var rig = ServerBudgetContendedRig.Create(BudgetVariant.None, threads: 4);

        Assert.False(rig.BudgetEngaged);
        Assert.Equal(0, rig.BudgetRegistrants);
    }

    [Theory]
    [InlineData(BudgetVariant.None)]
    [InlineData(BudgetVariant.Engaged)]
    public void EveryAcquire_IsReleased_AndNothingEverQueues(BudgetVariant variant)
    {
        using var rig = ServerBudgetContendedRig.Create(variant, threads: 4);

        var done = rig.RunBlocking(operationsPerThread: 2_000);

        Assert.Equal(8_000, done);
        Assert.Equal(0, rig.BudgetInUse);
        Assert.Equal(0, rig.InUse);
        Assert.Equal(0, rig.TotalWaits);
    }
}
