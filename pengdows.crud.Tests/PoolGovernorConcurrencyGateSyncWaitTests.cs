using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A caller that blocks on the gate is the slow path, and under sustained contention it is the common one.
/// It must not allocate per wait: a database library's hot path cannot afford a heap object for every
/// queued call, and the gate was measured allocating several hundred bytes for each.
/// </summary>
[Collection("AllocationSerial")]
public sealed class PoolGovernorConcurrencyGateSyncWaitTests
{
    private static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ABlockedSyncWait_AllocatesAlmostNothing_OnceWarm()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        const int rounds = 260;
        const int warmRounds = 60;
        var bytes = new long[rounds];
        using var go = new SemaphoreSlim(0);
        var held = gate.Acquire();

        // The worker is the thread measured: each round it blocks in Acquire behind the main thread's permit.
        var worker = Task.Factory.StartNew(() =>
        {
            for (var i = 0; i < rounds; i++)
            {
                Assert.True(go.Wait(HardTimeout));
                var before = GC.GetAllocatedBytesForCurrentThread();
                var permit = gate.Acquire(HardTimeout);
                bytes[i] = GC.GetAllocatedBytesForCurrentThread() - before;
                gate.Release(permit);
            }
        }, TaskCreationOptions.LongRunning);

        for (var i = 0; i < rounds; i++)
        {
            go.Release();
            await WaitUntilAsync(() => gate.QueueCount == 1);   // the worker is parked behind our permit
            gate.Release(held);                                 // hand it over
            await WaitUntilAsync(() => gate.ActiveCount == 0);  // the worker has used it and given it back
            held = gate.Acquire();
        }

        await worker.WaitAsync(HardTimeout);
        gate.Release(held);

        var measured = bytes.Skip(warmRounds).OrderBy(b => b).ToArray();
        var median = measured[measured.Length / 2];
        var budget = OptimizedBuild.Budget(releaseBytes: 64, debugExtraBytes: 256);
        Assert.True(median <= budget, $"A blocked synchronous wait allocated a median of {median} B; the budget is {budget} B.");
    }

    [Fact]
    public async Task ReusedSyncWaiters_SurviveTimeoutsAndCancellationsRacingGrants_WithoutCorruptingTheGate()
    {
        // A waiter that is reused must never be completed by a late grant, cancel or timeout that belongs to
        // its previous wait. Many threads time out and cancel against a small gate that is also being
        // squeezed, then every permit must still be accounted for.
        const int slots = 2;
        using var gate = new PoolGovernorConcurrencyGate(slots);
        using var stop = new CancellationTokenSource();
        var unexpected = new List<Exception>();

        var callers = Enumerable.Range(0, 8).Select(n => Task.Factory.StartNew(() =>
        {
            var random = new Random(n);
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var cts = new CancellationTokenSource();
                    if (random.Next(3) == 0)
                    {
                        cts.CancelAfter(random.Next(0, 3));
                    }

                    var permit = gate.Acquire(TimeSpan.FromMilliseconds(random.Next(0, 3)), cts.Token);
                    gate.Release(permit);
                }
                catch (TimeoutException)
                {
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    lock (unexpected)
                    {
                        unexpected.Add(ex);
                    }
                }
            }
        }, TaskCreationOptions.LongRunning)).ToArray();

        var squeezer = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var a = gate.Acquire();
                var b = gate.Acquire();
                await Task.Delay(1);
                gate.Release(a);
                gate.Release(b);
                await Task.Delay(1);
            }
        });

        await Task.Delay(TimeSpan.FromSeconds(3));
        stop.Cancel();
        await Task.WhenAll(callers.Append(squeezer)).WaitAsync(HardTimeout);

        Assert.Empty(unexpected);
        Assert.Equal(0, gate.ActiveCount);
        Assert.Equal(0, gate.QueueCount);
        Assert.Equal(slots, gate.AvailableCount);
        for (var i = 0; i < slots; i++)
        {
            Assert.True(gate.TryAcquire(out _), "A permit was lost.");
        }

        Assert.False(gate.TryAcquire(out _), "More permits exist than the gate was built with.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = Stopwatch.GetTimestamp() + (HardTimeout.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond);
        while (!condition())
        {
            Assert.True(Stopwatch.GetTimestamp() < deadline, "The condition was never met.");
            await Task.Yield();
        }
    }
}
