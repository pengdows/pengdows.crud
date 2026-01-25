using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PoolGovernorCancellationTests
{
    [Fact]
    public async Task AcquireAsync_CanceledWhileQueued_IncrementsTotalCanceledWaits()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "cancel-test", 1, TimeSpan.FromSeconds(5));

        using var permit = governor.Acquire();

        using var cts = new CancellationTokenSource();
        var waitingTask = governor.AcquireAsync(cts.Token);

        var queued = false;
        for (var i = 0; i < 50; i++)
        {
            if (governor.GetSnapshot().Queued > 0)
            {
                queued = true;
                break;
            }

            await Task.Delay(10);
        }

        Assert.True(queued, "Expected the waiter to be queued before cancellation.");

        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await waitingTask);

        var snapshot = governor.GetSnapshot();
        Assert.Equal(1, snapshot.TotalCanceledWaits);
        Assert.Equal(0, snapshot.Queued);
    }
}