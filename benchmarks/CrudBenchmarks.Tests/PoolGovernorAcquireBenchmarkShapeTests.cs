using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

// The acquire benchmarks time the uncontended hot path of PoolGovernor, blocking and asynchronous, with and
// without the writer turnstile. A benchmark that leaked a slot would soon stop being uncontended and start
// measuring a timeout, so the harness is held to account.
public class PoolGovernorAcquireBenchmarkShapeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EveryAcquire_IsReleased_AndNothingEverQueues(bool withTurnstile, bool useAsyncPath)
    {
        using var rig = AcquireRig.Create(withTurnstile);

        var done = useAsyncPath ? await rig.RunAsync(500) : rig.RunBlocking(500);

        Assert.Equal(500, done);
        Assert.Equal(AcquireRig.Slots, rig.AvailablePermits);
        Assert.Equal(0, rig.InUse);
        Assert.Equal(0, rig.TotalWaits); // uncontended: the fast path every time
    }

    [Fact]
    public void TheTurnstileRig_IsTheSingleWriterShape()
    {
        using var rig = AcquireRig.Create(withTurnstile: true);

        Assert.True(rig.HasTurnstile);
        Assert.Equal(1, AcquireRig.WriterSlots);
    }
}
