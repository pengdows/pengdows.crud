using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PoolGovernorConcurrencyGateRaceTests
{
    private static void UpdateHighWater(ref int location, int value)
    {
        while (true)
        {
            var observed = Volatile.Read(ref location);
            if (observed >= value || Interlocked.CompareExchange(ref location, value, observed) == observed)
            {
                return;
            }
        }
    }

    [Fact]
    public async Task CancellationReleaseAndResizeRaces_DoNotLosePermitsOrWaiters()
    {
        using var gate = new PoolGovernorConcurrencyGate(2, maxQueueDepth: 128);
        var inUse = 0;
        var highWater = 0;
        var workerCancellations = 0;

        var held = gate.Acquire();
        var secondHeld = gate.Acquire();
        using var forcedCancellation = new CancellationTokenSource();
        var forcedWaiter = gate.AcquireAsync(forcedCancellation.Token).AsTask();
        while (gate.QueueCount == 0)
        {
            await Task.Yield();
        }

        forcedCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await forcedWaiter);
        gate.Release(held);
        gate.Release(secondHeld);

        var workers = Enumerable.Range(0, 16).Select(async worker =>
        {
            for (var iteration = 0; iteration < 200; iteration++)
            {
                using var attempt = new CancellationTokenSource();
                if ((worker + iteration) % 5 == 0)
                {
                    attempt.CancelAfter(1);
                }

                try
                {
                    var permit = await gate.AcquireAsync(attempt.Token);
                    var current = Interlocked.Increment(ref inUse);
                    UpdateHighWater(ref highWater, current);
                    Assert.InRange(current, 1, 2);
                    try
                    {
                        await Task.Delay(1);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref inUse);
                        gate.Release(permit);
                    }
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref workerCancellations);
                }
            }
        }).ToArray();

        var resizing = Task.Run(async () =>
        {
            var limits = new[] { 1, 2, 1, 2 };
            while (!Task.WhenAll(workers).IsCompleted)
            {
                gate.SetLimit(limits[Environment.TickCount & 3]);
                await Task.Yield();
            }
        });

        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(10));
        await resizing.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(workerCancellations > 0);
        Assert.Equal(0, inUse);
        Assert.Equal(0, gate.ActiveCount);
        Assert.Equal(0, gate.QueueCount);
        Assert.InRange(highWater, 1, 2);
    }

    [Fact]
    public async Task QueuedWaiters_AreNeverLostWhenFastPathCallersBargeDuringPromotion()
    {
        using var gate = new PoolGovernorConcurrencyGate(2);
        using var stop = new CancellationTokenSource();
        var barging = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                if (gate.TryAcquire(out var permit))
                {
                    gate.Release(permit);
                }
            }
        })).ToArray();

        var waiters = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 20_000; i++)
            {
                var permit = await gate.AcquireAsync();
                gate.Release(permit);
            }
        })).ToArray();

        var completed = Task.WhenAll(waiters);
        var finished = await Task.WhenAny(completed, Task.Delay(TimeSpan.FromSeconds(60)));
        stop.Cancel();
        await Task.WhenAll(barging);

        Assert.True(ReferenceEquals(finished, completed), "A queued waiter was granted a permit it never received.");
        await completed;
        Assert.Equal(0, gate.ActiveCount);
        Assert.Equal(0, gate.QueueCount);
    }

    [Fact]
    public async Task SyncQueuedWaiters_AreNeverLostWhenFastPathCallersBargeDuringPromotion()
    {
        using var gate = new PoolGovernorConcurrencyGate(2);
        using var stop = new CancellationTokenSource();
        var barging = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                if (gate.TryAcquire(out var permit))
                {
                    gate.Release(permit);
                }
            }
        })).ToArray();

        var waiters = Enumerable.Range(0, 4).Select(_ => Task.Factory.StartNew(() =>
        {
            for (var i = 0; i < 5_000; i++)
            {
                var permit = gate.Acquire();
                gate.Release(permit);
            }
        }, TaskCreationOptions.LongRunning)).ToArray();

        var completed = Task.WhenAll(waiters);
        var finished = await Task.WhenAny(completed, Task.Delay(TimeSpan.FromSeconds(60)));
        stop.Cancel();
        await Task.WhenAll(barging);

        Assert.True(ReferenceEquals(finished, completed), "A queued sync waiter was granted a permit it never received.");
        await completed;
    }

    [Fact]
    public async Task Dispose_RacingWithCancellationOfQueuedAsyncWaiters_CompletesEachWaiterExactlyOnce()
    {
        for (var round = 0; round < 200; round++)
        {
            var gate = new PoolGovernorConcurrencyGate(1);
            var held = gate.Acquire();
            var sources = Enumerable.Range(0, 16).Select(_ => new CancellationTokenSource()).ToArray();
            var waiters = sources.Select(s => gate.AcquireAsync(s.Token).AsTask()).ToArray();

            var canceller = Task.Run(() =>
            {
                foreach (var source in sources)
                {
                    source.Cancel();
                }
            });
            var disposer = Task.Run(gate.Dispose);

            await Task.WhenAll(canceller, disposer);
            foreach (var waiter in waiters)
            {
                var ex = await Record.ExceptionAsync(async () => await waiter);
                Assert.True(
                    ex is OperationCanceledException or ObjectDisposedException,
                    $"Unexpected completion: {ex?.GetType().Name ?? "success"}");
            }

            gate.Release(held);
            foreach (var source in sources)
            {
                source.Dispose();
            }
        }
    }

    [Fact]
    public async Task SyncAcquire_AfterDispose_ThrowsObjectDisposedNotAggregate()
    {
        var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();
        var blocked = Task.Run(() => gate.Acquire(TimeSpan.FromSeconds(30)));
        while (gate.QueueCount == 0)
        {
            await Task.Delay(1);
        }

        gate.Dispose();

        var ex = await Record.ExceptionAsync(async () => await blocked);
        Assert.IsType<ObjectDisposedException>(ex);
        gate.Release(held);
    }

    [Fact]
    public async Task SyncAcquire_WhenCanceled_RethrowsOriginalCancellationWithoutAggregate()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();
        using var cts = new CancellationTokenSource();
        var blocked = Task.Run(() => gate.Acquire(TimeSpan.FromSeconds(30), cts.Token));
        while (gate.QueueCount == 0)
        {
            await Task.Delay(1);
        }

        cts.Cancel();

        var ex = await Record.ExceptionAsync(async () => await blocked);
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        gate.Release(held);
        Assert.Equal(0, gate.QueueCount);
    }
}
