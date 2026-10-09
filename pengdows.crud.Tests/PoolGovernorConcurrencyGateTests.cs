using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PoolGovernorConcurrencyGateTests
{
    [Fact]
    public async Task CompactGate_AdmitsAndReleasesSyncAndAsync()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);

        var first = gate.Acquire();
        var blocked = gate.AcquireAsync().AsTask();
        Assert.False(blocked.IsCompleted);

        gate.Release(first);
        var second = await blocked;
        gate.Release(second);

        Assert.Equal(0, gate.ActiveCount);
        Assert.Equal(0, gate.QueueCount);
    }

    [Fact]
    public void CompactGate_TryAcquireDoesNotQueueWhenNoCapacityExists()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();

        Assert.False(gate.TryAcquire(out _));
        Assert.Equal(0, gate.QueueCount);

        gate.Release(held);
    }

    [Fact]
    public async Task CompactGate_LoweringLimitBlocksUntilCapacityFallsBelowLimit()
    {
        using var gate = new PoolGovernorConcurrencyGate(3);
        var first = gate.Acquire();
        var second = gate.Acquire();
        Assert.Equal(1, gate.SetLimit(1));

        var blocked = gate.AcquireAsync().AsTask();
        Assert.False(blocked.IsCompleted);
        gate.Release(first);
        Assert.False(blocked.IsCompleted);
        gate.Release(second);

        var third = await blocked;
        gate.Release(third);
    }

    [Fact]
    public async Task CompactGate_CancellationRemovesQueuedWaiter()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();
        using var cancellation = new CancellationTokenSource();
        var blocked = gate.AcquireAsync(cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await blocked);
        gate.Release(held);
        Assert.Equal(0, gate.QueueCount);
    }

    [Fact]
    public async Task CompactGate_ContentionResizeAndCancellationNeverExceedConfiguredMaximum()
    {
        using var gate = new PoolGovernorConcurrencyGate(8, maxQueueDepth: 256);
        using var stop = new CancellationTokenSource();
        var inUse = 0;
        var highWater = 0;
        var canceled = 0;

        var workers = Enumerable.Range(0, 32).Select(async worker =>
        {
            for (var iteration = 0; iteration < 60; iteration++)
            {
                using var attempt = new CancellationTokenSource();
                if ((worker + iteration) % 7 == 0)
                {
                    attempt.CancelAfter(1);
                }

                try
                {
                    var permit = await gate.AcquireAsync(attempt.Token);
                    var current = Interlocked.Increment(ref inUse);
                    UpdateHighWater(ref highWater, current);
                    Assert.True(current <= 8);
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
                    Interlocked.Increment(ref canceled);
                }
            }
        }).ToArray();

        var resizer = Task.Run(async () =>
        {
            var limits = new[] { 1, 8, 3, 7, 2, 8 };
            for (var i = 0; i < 200 && !Task.WhenAll(workers).IsCompleted; i++)
            {
                gate.SetLimit(limits[i % limits.Length]);
                await Task.Yield();
            }
        });

        await Task.WhenAll(workers);
        stop.Cancel();
        await resizer;

        Assert.True(canceled > 0);
        Assert.Equal(0, inUse);
        Assert.Equal(0, gate.ActiveCount);
        Assert.Equal(0, gate.QueueCount);
        Assert.True(highWater <= 8);
    }

    [Fact]
    public async Task CompactGate_HeavyContentionDoesNotLoseWakeups()
    {
        using var gate = new PoolGovernorConcurrencyGate(4, maxQueueDepth: 256);
        var workers = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            for (var iteration = 0; iteration < 100; iteration++)
            {
                var permit = await gate.AcquireAsync();
                await Task.Yield();
                gate.Release(permit);
            }
        })).ToArray();

        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, gate.ActiveCount);
        Assert.Equal(0, gate.QueueCount);
    }

    [Fact]
    public async Task CompactGate_SingleSlotHandsOffDirectlyToNextWaiter()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();
        var waiting = gate.AcquireAsync().AsTask();

        Assert.False(waiting.IsCompleted);
        gate.Release(held);

        var transferred = await waiting;
        Assert.Equal(1, gate.ActiveCount);
        gate.Release(transferred);
        Assert.Equal(0, gate.ActiveCount);
    }

    [Fact]
    public async Task CompactGate_SingleSlotHandoff_DoesNotAllocateCompletionBatch()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();

        // Warm the waiter pool and the async completion path before measuring the release path.
        var warmup = gate.AcquireAsync().AsTask();
        gate.Release(held);
        held = await warmup;
        gate.Release(held);
        held = gate.Acquire();

        const int handoffs = 256;
        var allocated = 0L;
        for (var i = 0; i < handoffs; i++)
        {
            var waiting = gate.AcquireAsync().AsTask();
            var before = GC.GetAllocatedBytesForCurrentThread();
            gate.Release(held);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            held = await waiting;
        }

        gate.Release(held);
        Assert.True(allocated < handoffs * 64, $"Single-slot handoffs allocated {allocated} bytes.");
    }

    [Fact]
    public async Task CompactGate_MultiSlotRelease_DoesNotAllocateOneItemCompletionBatch()
    {
        using var gate = new PoolGovernorConcurrencyGate(2);
        var first = gate.Acquire();
        var second = gate.Acquire();

        // Warm the pooled waiter before measuring the release path.
        var warmup = gate.AcquireAsync();
        gate.Release(first);
        var warmupPermit = await warmup;
        gate.Release(warmupPermit);
        first = gate.Acquire();

        var waiting = gate.AcquireAsync();
        var before = GC.GetAllocatedBytesForCurrentThread();
        gate.Release(first);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var transferred = await waiting;
        gate.Release(second);
        gate.Release(transferred);

        Assert.True(allocated < 32, $"Single waiter release allocated {allocated} bytes.");
    }

    [Fact]
    public async Task CompactGate_RecyclesCompletedAsyncWaiters()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();
        var waiting = gate.AcquireAsync().AsTask();

        gate.Release(held);
        var transferred = await waiting;
        gate.Release(transferred);

        Assert.True(gate.RecycledWaiterCount > 0);
    }

    [Fact]
    public async Task CompactGate_QueuedAsyncAcquisition_DoesNotAllocateLinkedListNodes()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();

        var warmup = gate.AcquireAsync();
        gate.Release(held);
        held = await warmup;
        gate.Release(held);
        held = gate.Acquire();

        const int attempts = 256;
        var allocated = 0L;
        for (var i = 0; i < attempts; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var waiting = gate.AcquireAsync();
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            gate.Release(held);
            held = await waiting;
        }

        gate.Release(held);
        Assert.True(allocated < attempts * 16, $"Queued acquisitions allocated {allocated} bytes.");
    }

    private static void UpdateHighWater(ref int location, int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref location);
            if (value <= current || Interlocked.CompareExchange(ref location, value, current) == current)
            {
                return;
            }
        }
    }
}
