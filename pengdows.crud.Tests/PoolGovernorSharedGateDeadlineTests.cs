using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

[Collection("PoolGovernorDeadlineSerial")]
public sealed class PoolGovernorSharedGateDeadlineTests
{
    private static readonly TimeSpan Timeout2000 = TimeSpan.FromMilliseconds(2000);

    [Fact]
    public async Task AsyncAcquire_WhenTheSharedGateTimesOutAfterTheRoleGateWait_ThrowsPoolSaturatedWithinOneTimeout()
    {
        var elapsed = await RunScenarioAsync(async governor => await governor.AcquireAsync());

        Assert.True(elapsed < TimeSpan.FromMilliseconds(2500),
            $"The caller asked for a {Timeout2000.TotalMilliseconds} ms timeout but waited {elapsed.TotalMilliseconds} ms.");
    }

    [Fact]
    public async Task SyncAcquire_WhenTheSharedGateTimesOutAfterTheRoleGateWait_ThrowsPoolSaturatedWithinOneTimeout()
    {
        var elapsed = await RunScenarioAsync(governor => Task.Run(() => governor.Acquire()));

        Assert.True(elapsed < TimeSpan.FromMilliseconds(2500),
            $"The caller asked for a {Timeout2000.TotalMilliseconds} ms timeout but waited {elapsed.TotalMilliseconds} ms.");
    }

    // The role gate holds the caller for ~1000 ms; a competing client then takes the shared permit
    // that the holder's release freed, so the shared gate is what times out. The spinning barger
    // normally wins that race; when the governor wins it instead the scenario is rerun.
    private static async Task<TimeSpan> RunScenarioAsync(Func<PoolGovernor, Task> acquire)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var shared = new PoolGovernorConcurrencyGate(2, int.MaxValue);
            using var governor = new PoolGovernor(
                PoolLabel.Writer,
                "deadline-test",
                1,
                Timeout2000,
                sharedConcurrencyGate: shared);

            var holder = governor.Acquire();
            shared.Acquire();
            using var stop = new CancellationTokenSource();
            var barger = Task.Factory.StartNew(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    if (shared.TryAcquire(out _))
                    {
                        return true;
                    }
                }

                return false;
            }, TaskCreationOptions.LongRunning);

            var stopwatch = Stopwatch.StartNew();
            var waiting = acquire(governor);
            await Task.Delay(1000);
            holder.Dispose();

            try
            {
                await waiting;
            }
            catch (PoolSaturatedException)
            {
                stop.Cancel();
                await barger;
                return stopwatch.Elapsed;
            }

            stop.Cancel();
            await barger;
        }

        throw new Xunit.Sdk.XunitException("The shared gate was never the one that timed out.");
    }
}
