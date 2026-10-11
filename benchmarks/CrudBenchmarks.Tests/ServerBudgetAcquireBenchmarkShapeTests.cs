using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

// The server-budget acquire benchmark times what one connection checkout pays for the per-server budget (POOL-003):
// nothing when no budget is registered, one volatile read when it is registered but cannot bind, one more atomic when it
// is engaged. Each variant has to be the shape it claims to be, and none may leak a permit or ever queue, or the timing
// would stop being the uncontended hot path.
public class ServerBudgetAcquireBenchmarkShapeTests
{
    [Theory]
    [InlineData(BudgetVariant.None, false, false)]
    [InlineData(BudgetVariant.Disengaged, true, false)]
    [InlineData(BudgetVariant.Engaged, true, true)]
    public async Task EachVariant_IsTheShapeItClaims(BudgetVariant variant, bool hasBudget, bool engaged)
    {
        using var rig = ServerBudgetRig.Create(variant);

        Assert.Equal(hasBudget, rig.HasBudget);
        Assert.Equal(engaged, rig.BudgetEngaged);
        await rig.RunAsync(10);
    }

    [Theory]
    [InlineData(BudgetVariant.None, false)]
    [InlineData(BudgetVariant.None, true)]
    [InlineData(BudgetVariant.Disengaged, false)]
    [InlineData(BudgetVariant.Disengaged, true)]
    [InlineData(BudgetVariant.Engaged, false)]
    [InlineData(BudgetVariant.Engaged, true)]
    public async Task EveryAcquire_IsReleased_AndNothingEverQueues(BudgetVariant variant, bool useAsyncPath)
    {
        using var rig = ServerBudgetRig.Create(variant);

        var done = useAsyncPath ? await rig.RunAsync(500) : rig.RunBlocking(500);

        Assert.Equal(500, done);
        Assert.Equal(0, rig.InUse);
        Assert.Equal(0, rig.BudgetInUse); // a permit taken and not returned would show here
        Assert.Equal(0, rig.TotalWaits);  // uncontended: the fast path every time
    }

    [Fact]
    public void TheEngagedVariant_TakesAServerPermitPerLease()
    {
        using var rig = ServerBudgetRig.Create(BudgetVariant.Engaged);

        Assert.Equal(1, rig.BudgetInUseWhileOneLeaseIsHeld());
    }

    [Fact]
    public void TheDisengagedVariant_TakesNoServerPermit()
    {
        using var rig = ServerBudgetRig.Create(BudgetVariant.Disengaged);

        Assert.Equal(0, rig.BudgetInUseWhileOneLeaseIsHeld());
    }
}
