using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class AdaptiveConcurrencyGateTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, int expected = 1)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), $"Condition was not met; expected {expected}.");
    }

    [Fact]
    public async Task LowerLimit_DoesNotRevokeExistingLeases_ButBlocksNewAdmission()
    {
        using var gate = new AdaptiveConcurrencyGate(3);
        await using var first = await gate.AcquireAsync();
        await using var second = await gate.AcquireAsync();

        Assert.Equal(1, gate.SetLimit(1));
        Assert.Equal(1, gate.EffectiveLimit);
        Assert.Equal(2, gate.ActiveCount);

        var blocked = gate.AcquireAsync().AsTask();
        await WaitUntilAsync(() => gate.QueueCount >= 1);
        Assert.False(blocked.IsCompleted);

        await first.DisposeAsync();
        Assert.False(blocked.IsCompleted);

        await second.DisposeAsync();
        await using var third = await blocked;
        Assert.Equal(1, gate.ActiveCount);
    }

    [Fact]
    public async Task RaiseLimit_WakesOnlyCapacityMadeAvailable()
    {
        using var gate = new AdaptiveConcurrencyGate(3);
        Assert.Equal(1, gate.SetLimit(1));
        await using var held = await gate.AcquireAsync();

        var first = gate.AcquireAsync().AsTask();
        var second = gate.AcquireAsync().AsTask();
        await WaitUntilAsync(() => gate.QueueCount >= 2, expected: 2);

        Assert.Equal(2, gate.SetLimit(2));
        await using var firstLease = await first;
        Assert.False(second.IsCompleted);

        await held.DisposeAsync();
        await using var secondLease = await second;
        Assert.Equal(2, gate.ActiveCount);
    }

    [Fact]
    public async Task LimitCannotExceedConfiguredMaximum_AndReleaseCannotOverRelease()
    {
        using var gate = new AdaptiveConcurrencyGate(2);
        await using var lease = await gate.AcquireAsync();

        Assert.Throws<ArgumentOutOfRangeException>(() => gate.SetLimit(3));
        Assert.Equal(2, gate.EffectiveLimit);

        await lease.DisposeAsync();
        await lease.DisposeAsync();

        Assert.Equal(0, gate.ActiveCount);
        await using var first = await gate.AcquireAsync();
        await using var second = await gate.AcquireAsync();
        Assert.Equal(2, gate.ActiveCount);
    }

    [Fact]
    public async Task AcquireAsync_CancellationRemovesWaiter()
    {
        using var gate = new AdaptiveConcurrencyGate(1);
        await using var held = await gate.AcquireAsync();
        using var cancellation = new CancellationTokenSource();

        var waiting = gate.AcquireAsync(cancellation.Token).AsTask();
        await WaitUntilAsync(() => gate.QueueCount >= 1);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
        Assert.Equal(0, gate.QueueCount);
    }

    [Fact]
    public async Task CanceledWaiter_DoesNotBlockTheNextWaiter()
    {
        using var gate = new AdaptiveConcurrencyGate(1);
        await using var held = await gate.AcquireAsync();
        using var cancellation = new CancellationTokenSource();

        var canceled = gate.AcquireAsync(cancellation.Token).AsTask();
        await WaitUntilAsync(() => gate.QueueCount >= 1);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceled);

        await held.DisposeAsync();
        await using var next = await gate.AcquireAsync();
        Assert.Equal(1, gate.ActiveCount);
    }

    [Fact]
    public async Task Dispose_WithPendingWaiter_CompletesWaiterWithObjectDisposedException()
    {
        var gate = new AdaptiveConcurrencyGate(1);
        await using var held = await gate.AcquireAsync();
        var waiting = gate.AcquireAsync().AsTask();
        await WaitUntilAsync(() => gate.QueueCount >= 1);

        gate.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await waiting);
    }

    [Fact]
    public async Task AcquireAsync_WithPreCanceledToken_ReturnsCanceledOperation()
    {
        using var gate = new AdaptiveConcurrencyGate(1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var operation = gate.AcquireAsync(cancellation.Token).AsTask();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await operation);
    }

    [Fact]
    public void SetLimit_RejectsValuesOutsideConfiguredRange()
    {
        using var gate = new AdaptiveConcurrencyGate(2);

        Assert.Throws<ArgumentOutOfRangeException>(() => gate.SetLimit(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => gate.SetLimit(3));
    }

    [Fact]
    public async Task AcquireAsync_DoesNotBargeAheadOfAnExistingWaiter()
    {
        using var gate = new AdaptiveConcurrencyGate(3);
        await using var held = await gate.AcquireAsync();
        Assert.Equal(1, gate.SetLimit(1));

        var first = gate.AcquireAsync().AsTask();
        var second = gate.AcquireAsync().AsTask();
        await WaitUntilAsync(() => gate.QueueCount == 2);

        Assert.Equal(2, gate.SetLimit(2));
        await using var firstLease = await first;

        // One waiter remains queued. A new caller must join behind it rather than use
        // any transient capacity created by a future change to the grant path.
        var third = gate.AcquireAsync().AsTask();
        await WaitUntilAsync(() => gate.QueueCount == 2);
        Assert.False(third.IsCompleted);

        await held.DisposeAsync();
        await using var secondLease = await second;
        Assert.False(third.IsCompleted);

        await firstLease.DisposeAsync();
        await using var thirdLease = await third;
        Assert.Equal(2, gate.ActiveCount);
    }

    [Fact]
    public async Task ConcurrentAcquireCancelAndResize_LeavesNoLeasesOrWaiters()
    {
        using var gate = new AdaptiveConcurrencyGate(8);
        Assert.Equal(1, gate.SetLimit(1));
        var acquired = 0;
        var workerCanceled = 0;
        var inUse = 0;
        var highWaterMark = 0;
        var workersDone = 0;
        using var startupCancellation = new CancellationTokenSource();
        await using var heldForCancellation = await gate.AcquireAsync();
        const int workerCount = 32;
        const int iterations = 100;

        var workerTasks = Enumerable.Range(0, workerCount).Select(worker => Task.Run(async () =>
        {
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                using var cancellation = new CancellationTokenSource();
                var token = iteration == 0 ? startupCancellation.Token : cancellation.Token;
                if (iteration != 0 && (worker + iteration) % 3 == 0)
                {
                    cancellation.CancelAfter((worker + iteration) % 3 + 1);
                }

                try
                {
                    await using var lease = await gate.AcquireAsync(token);
                    Interlocked.Increment(ref acquired);
                    var current = Interlocked.Increment(ref inUse);
                    UpdateHighWaterMark(ref highWaterMark, current);
                    try
                    {
                        await Task.Delay(2);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref inUse);
                    }
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref workerCanceled);
                }
            }
        })).ToArray();
        var workers = Task.WhenAll(workerTasks);

        await WaitUntilAsync(() => gate.QueueCount >= 1);
        startupCancellation.Cancel();
        await heldForCancellation.DisposeAsync();
        Assert.Equal(8, gate.SetLimit(8));

        var controller = Task.Run(async () =>
        {
            var iteration = 0;
            while (Volatile.Read(ref workersDone) == 0)
            {
                gate.SetLimit(iteration % 8 + 1);
                iteration++;
                await Task.Yield();
            }
        });

        try
        {
            await workers;
        }
        finally
        {
            Interlocked.Exchange(ref workersDone, 1);
        }
        await controller;

        Assert.Equal(workerCount * iterations, acquired + workerCanceled);
        Assert.True(workerCanceled > 0);
        Assert.Equal(0, inUse);
        Assert.InRange(highWaterMark, 1, gate.ConfiguredMaximum);
        Assert.Equal(0, gate.ActiveCount);
        Assert.Equal(0, gate.QueueCount);
    }

    private static void UpdateHighWaterMark(ref int highWaterMark, int current)
    {
        while (true)
        {
            var observed = Volatile.Read(ref highWaterMark);
            if (current <= observed || Interlocked.CompareExchange(ref highWaterMark, current, observed) == observed)
            {
                return;
            }
        }
    }
}
