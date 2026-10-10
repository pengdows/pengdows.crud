using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

// The gate-versus-SemaphoreSlim comparison measures the real PoolGovernor twice: once with its default
// ConcurrencyGate and once handed a SemaphoreSlim. A benchmark that silently measured the wrong thing, or
// that leaked permits, would make the comparison meaningless, so the harness itself is held to account.
public class PoolGovernorAdmissionBenchmarkShapeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaturatedPool_AtTheMaximumSlotCount_MakesCallersActuallyWait(bool useGate)
    {
        // At 512 slots a caller count at or below 512 never queues, so the saturated benchmark must run more
        // callers than slots, or it would measure the uncontended fast path and call it contention.
        Assert.True(PoolGovernorSaturatedBenchmarks.Callers > PoolGovernorSaturatedBenchmarks.MaxSlots);

        using var syncArm = useGate ? AdmissionArm.Gate(PoolGovernorSaturatedBenchmarks.MaxSlots) : AdmissionArm.Semaphore(PoolGovernorSaturatedBenchmarks.MaxSlots);
        var sync = await AdmissionWorkload.RunContendedSyncAsync(syncArm, PoolGovernorSaturatedBenchmarks.Callers, 2, 5_000);
        Assert.Equal(PoolGovernorSaturatedBenchmarks.Callers * 2, sync.Operations);
        // A hold that spins burns a core per held slot, so on a small machine only a few slots are ever in use at once.
        Assert.Equal(PoolGovernorSaturatedBenchmarks.MaxSlots, sync.PeakConcurrent);
        Assert.True(syncArm.TotalWaits > 0, $"No synchronous caller ever had to wait (peak {sync.PeakConcurrent}, waits {syncArm.TotalWaits}).");
        Assert.Equal(PoolGovernorSaturatedBenchmarks.MaxSlots, syncArm.AvailablePermits);

        using var asyncArm = useGate ? AdmissionArm.Gate(PoolGovernorSaturatedBenchmarks.MaxSlots) : AdmissionArm.Semaphore(PoolGovernorSaturatedBenchmarks.MaxSlots);
        var asyncResult = await AdmissionWorkload.RunContendedAsync(asyncArm, PoolGovernorSaturatedBenchmarks.Callers, 2, 5_000);
        Assert.Equal(PoolGovernorSaturatedBenchmarks.Callers * 2, asyncResult.Operations);
        Assert.Equal(PoolGovernorSaturatedBenchmarks.MaxSlots, asyncResult.PeakConcurrent);
        Assert.True(asyncArm.TotalWaits > 0, "No asynchronous caller ever had to wait.");
        Assert.Equal(PoolGovernorSaturatedBenchmarks.MaxSlots, asyncArm.AvailablePermits);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(64)]
    public void TheTwoArms_AreTheRealGovernor_DifferingOnlyInTheAdmissionPrimitive(int slots)
    {
        using var gate = AdmissionArm.Gate(slots);
        using var semaphore = AdmissionArm.Semaphore(slots);

        Assert.True(gate.UsesItsOwnGate);          // built its own ConcurrencyGate
        Assert.False(semaphore.UsesItsOwnGate);    // was handed a SemaphoreSlim
        Assert.Equal(slots, gate.AvailablePermits);
        Assert.Equal(slots, semaphore.AvailablePermits);
        Assert.Equal(gate.MaxQueueDepth, semaphore.MaxQueueDepth);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 8)]
    [InlineData(true, 1)]
    [InlineData(true, 8)]
    public async Task AsyncContention_CountsEveryOperation_NeverOverAdmits_AndLeaksNoPermit(bool useGate, int slots)
    {
        using var arm = useGate ? AdmissionArm.Gate(slots) : AdmissionArm.Semaphore(slots);

        var result = await AdmissionWorkload.RunContendedAsync(arm, tasks: 16, opsPerTask: 50, holdMicroseconds: 1);

        Assert.Equal(16 * 50, result.Operations);
        Assert.InRange(result.PeakConcurrent, 1, slots);
        Assert.Equal(slots, arm.AvailablePermits);
        Assert.Equal(0, arm.InUse);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 8)]
    [InlineData(true, 1)]
    [InlineData(true, 8)]
    public async Task SyncContention_CountsEveryOperation_NeverOverAdmits_AndLeaksNoPermit(bool useGate, int slots)
    {
        using var arm = useGate ? AdmissionArm.Gate(slots) : AdmissionArm.Semaphore(slots);

        var result = await AdmissionWorkload.RunContendedSyncAsync(arm, tasks: 16, opsPerTask: 50, holdMicroseconds: 1);

        Assert.Equal(16 * 50, result.Operations);
        Assert.InRange(result.PeakConcurrent, 1, slots);
        Assert.Equal(slots, arm.AvailablePermits);
        Assert.Equal(0, arm.InUse);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManyMoreAsyncCallersThanSlots_AllQueue_NoneIsRejected(bool useGate)
    {
        // Production leaves the governor's queue unbounded on 2.0.x, so a benchmark must too: a default cap
        // would reject the surplus callers and the run would measure the rejections.
        using var arm = useGate ? AdmissionArm.Gate(1) : AdmissionArm.Semaphore(1);

        var result = await AdmissionWorkload.RunContendedAsync(arm, tasks: 200, opsPerTask: 3, holdMicroseconds: 0);

        Assert.Equal(200 * 3, result.Operations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SyncContention_WithOneSlot_MakesMostOperationsActuallyWait(bool useGate)
    {
        // Contention only exists if callers overlap. Threads that start one after another let the early ones
        // finish before the later ones begin, so the callers are released together, and the governor's own
        // count of acquisitions that had to wait proves they really collided.
        using var arm = useGate ? AdmissionArm.Gate(1) : AdmissionArm.Semaphore(1);

        await AdmissionWorkload.RunContendedSyncAsync(arm, tasks: 16, opsPerTask: 200, holdMicroseconds: 0);

        // Every caller has to have waited at least once. Measured: released together, the gate makes 65-80% of
        // the operations wait; with staggered starts it makes none. SemaphoreSlim lets the thread that just
        // released take the permit straight back (barging), so only a few percent of its operations wait.
        Assert.True(arm.TotalWaits >= 16, $"Only {arm.TotalWaits} of {16 * 200} operations ever had to wait.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncContention_WithOneSlot_MakesMostOperationsActuallyWait(bool useGate)
    {
        using var arm = useGate ? AdmissionArm.Gate(1) : AdmissionArm.Semaphore(1);

        await AdmissionWorkload.RunContendedAsync(arm, tasks: 32, opsPerTask: 50, holdMicroseconds: 20);

        Assert.True(arm.TotalWaits >= 32 * 50 / 3, $"Only {arm.TotalWaits} of {32 * 50} operations ever had to wait.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingQueuedWaiters_CancelsEachOne_AndLeaksNoPermit(bool useGate)
    {
        using var arm = useGate ? AdmissionArm.Gate(4) : AdmissionArm.Semaphore(4);

        var cancelled = await AdmissionWorkload.CancelWhileQueuedAsync(arm, slots: 4, count: 100);

        Assert.Equal(100, cancelled);
        Assert.Equal(4, arm.AvailablePermits);
        Assert.Equal(0, arm.InUse);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheFastFailProbe_OnAFullGovernor_ReportsFullAndTakesNoPermit(bool useGate)
    {
        using var arm = useGate ? AdmissionArm.Gate(2) : AdmissionArm.Semaphore(2);
        using var held = AdmissionWorkload.HoldAll(arm, 2);

        Assert.False(AdmissionWorkload.TryAcquireOnce(arm));
        Assert.Equal(0, arm.AvailablePermits);
    }
}
