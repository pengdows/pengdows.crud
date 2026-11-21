using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Drain safety: <see cref="PoolGovernor.WaitForDrainAsync(TimeSpan?, System.Threading.CancellationToken)"/> must not
/// report the governor drained while a release is still touching the semaphore or turnstile, even when every counted
/// holder has already been removed from <c>InUse</c>.
/// </summary>
public class PoolGovernorDrainInFlightTests
{
    [Fact]
    public async Task WaitForDrain_DoesNotComplete_WhileAReleaseIsStillInFlight()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "drain-in-flight", 1, TimeSpan.FromSeconds(5));
        var slot = governor.Acquire();

        var reachedInFlightWindow = new ManualResetEventSlim(false);
        var letReleaseFinish = new ManualResetEventSlim(false);

        // Runs inside the release: the exact count is already zero, the release has not finished.
        governor.TestOnlyBeforeInUseDecrement = () =>
        {
            reachedInFlightWindow.Set();
            letReleaseFinish.Wait(TimeSpan.FromSeconds(10));
        };

        var release = Task.Run(() => slot.Dispose());
        Assert.True(reachedInFlightWindow.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, governor.GetSnapshot().InUse);

        var drain = governor.WaitForDrainAsync(TimeSpan.FromSeconds(10));
        var completedEarly = await Task.WhenAny(drain, Task.Delay(TimeSpan.FromMilliseconds(300))) == drain;

        Assert.False(completedEarly, "drain completed while a release was still in flight");

        letReleaseFinish.Set();
        await release;
        await drain;
    }
}
