using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PoolGovernorConcurrencyGateBehaviorTests
{
    private static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(10);

    // ── construction and configuration ──

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsANonPositiveMaximum(int maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PoolGovernorConcurrencyGate(maximum));
    }

    [Fact]
    public void Constructor_RejectsANegativeQueueDepth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PoolGovernorConcurrencyGate(2, -1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(5)]
    public void SetLimit_RejectsAValueOutsideOneToTheConfiguredMaximum(int limit)
    {
        using var gate = new PoolGovernorConcurrencyGate(4);

        Assert.Throws<ArgumentOutOfRangeException>(() => gate.SetLimit(limit));
    }

    [Fact]
    public void SetLimit_AfterDispose_ReturnsTheLastEffectiveLimitInsteadOfThrowing()
    {
        var gate = new PoolGovernorConcurrencyGate(4);
        gate.Dispose();

        Assert.Equal(4, gate.SetLimit(2));
    }

    [Fact]
    public async Task SetLimit_RaisingTheLimit_AdmitsQueuedWaiters()
    {
        using var gate = new PoolGovernorConcurrencyGate(3);
        gate.SetLimit(1);
        var first = gate.Acquire();
        var waiting = gate.AcquireAsync().AsTask();
        Assert.False(waiting.IsCompleted);

        gate.SetLimit(2);

        await waiting.WaitAsync(HardTimeout);
        Assert.Equal(2, gate.ActiveCount);
        gate.Release(first);
    }

    // ── queue cap ──

    [Fact]
    public async Task QueueCap_RejectsAnAsyncWaiterBeyondTheCap()
    {
        using var gate = new PoolGovernorConcurrencyGate(1, maxQueueDepth: 1);
        var held = gate.Acquire();
        var queued = gate.AcquireAsync().AsTask();

        Assert.Throws<ConcurrencyGateSaturatedException>(() => gate.AcquireAsync());
        Assert.Equal(1, gate.QueueCount);

        gate.Release(held);
        await queued.WaitAsync(HardTimeout);
    }

    [Fact]
    public void QueueCap_RejectsASyncWaiterBeyondTheCap()
    {
        using var gate = new PoolGovernorConcurrencyGate(1, maxQueueDepth: 0);
        gate.Acquire();

        Assert.Throws<ConcurrencyGateSaturatedException>(() => gate.Acquire(TimeSpan.FromSeconds(1)));
    }

    // ── disposal ──

    [Fact]
    public async Task Dispose_FaultsEveryQueuedAsyncWaiterWithObjectDisposed()
    {
        var gate = new PoolGovernorConcurrencyGate(1);
        gate.Acquire();
        var waiters = Enumerable.Range(0, 5).Select(_ => gate.AcquireAsync().AsTask()).ToArray();

        gate.Dispose();

        foreach (var waiter in waiters)
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => waiter.WaitAsync(HardTimeout));
        }

        Assert.Equal(0, gate.QueueCount);
    }

    [Fact]
    public async Task Dispose_FaultsAQueuedSyncWaiterWithObjectDisposed()
    {
        var gate = new PoolGovernorConcurrencyGate(1);
        gate.Acquire();
        var waiter = Task.Run(() => gate.Acquire(HardTimeout));
        await WaitUntilAsync(() => gate.QueueCount == 1);

        gate.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => waiter.WaitAsync(HardTimeout));
    }

    [Fact]
    public void AfterDispose_EveryEntryPointThrowsObjectDisposed()
    {
        var gate = new PoolGovernorConcurrencyGate(2);
        gate.Dispose();

        Assert.Throws<ObjectDisposedException>(() => gate.Acquire());
        Assert.Throws<ObjectDisposedException>(() => gate.TryAcquire(out _));
        Assert.Throws<ObjectDisposedException>(() => gate.AcquireAsync());
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var gate = new PoolGovernorConcurrencyGate(2);

        gate.Dispose();
        gate.Dispose();
    }

    // ── timeouts and cancellation ──

    [Fact]
    public void SyncAcquire_TimesOutWithTimeoutException_AndLeavesNoQueuedWaiter()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();

        Assert.Throws<TimeoutException>(() => gate.Acquire(TimeSpan.FromMilliseconds(50)));
        Assert.Equal(0, gate.QueueCount);

        gate.Release(held);
        Assert.True(gate.TryAcquire(out _));
    }

    [Fact]
    public async Task AsyncAcquire_TimesOutWithTimeoutException_AndLeavesNoQueuedWaiter()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await gate.AcquireAsync(TimeSpan.FromMilliseconds(50)));
        Assert.Equal(0, gate.QueueCount);

        gate.Release(held);
        Assert.True(gate.TryAcquire(out _));
    }

    [Fact]
    public void ANegativeTimeoutThatIsNotInfinite_IsRejected()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => gate.Acquire(TimeSpan.FromMilliseconds(-5)));
    }

    [Fact]
    public async Task AnAlreadyCanceledToken_NeverTakesAPermit()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        Assert.Throws<OperationCanceledException>(() => gate.Acquire(canceled.Token));
        Assert.Throws<OperationCanceledException>(() => gate.TryAcquire(out _, canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await gate.AcquireAsync(canceled.Token));

        Assert.Equal(0, gate.ActiveCount);
    }

    [Fact]
    public async Task ACanceledQueuedWaiter_IsRemoved_AndItsWaiterObjectIsReusable()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();
        using var cts = new CancellationTokenSource();
        var canceled = gate.AcquireAsync(cts.Token).AsTask();
        Assert.Equal(1, gate.QueueCount);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.WaitAsync(HardTimeout));
        Assert.Equal(0, gate.QueueCount);

        var next = gate.AcquireAsync().AsTask();
        gate.Release(held);
        await next.WaitAsync(HardTimeout);
        Assert.Equal(1, gate.ActiveCount);
    }

    // ── ordering ──

    [Fact]
    public async Task QueuedWaiters_AreAdmittedInArrivalOrder()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);
        var held = gate.Acquire();
        var waiters = Enumerable.Range(0, 6).Select(_ => gate.AcquireAsync().AsTask()).ToArray();

        var order = new List<int>();
        gate.Release(held);
        for (var i = 0; i < waiters.Length; i++)
        {
            await waiters[i].WaitAsync(HardTimeout);
            Assert.All(waiters.Skip(i + 1), w => Assert.False(w.IsCompleted, $"a waiter behind {i} was admitted first"));
            order.Add(i);
            gate.Release(0);
        }

        Assert.Equal(Enumerable.Range(0, 6), order);
    }

    // ── exactly-once semantics ──

    [Fact]
    public async Task CancelRacingARelease_EndsWithEitherOnePermitOrNone_AndAHealthyGate()
    {
        using var gate = new PoolGovernorConcurrencyGate(1);

        for (var i = 0; i < 2_000; i++)
        {
            var held = gate.Acquire();
            using var cts = new CancellationTokenSource();
            var waiter = gate.AcquireAsync(cts.Token).AsTask();

            var cancel = Task.Run(() => cts.Cancel());
            var release = Task.Run(() => gate.Release(held));
            await Task.WhenAll(cancel, release).WaitAsync(HardTimeout);

            try
            {
                var permit = await waiter.WaitAsync(HardTimeout);
                gate.Release(permit);
            }
            catch (OperationCanceledException)
            {
                // The cancel won: the permit went back to the gate rather than to the waiter.
            }

            Assert.Equal(0, gate.ActiveCount);
            Assert.Equal(0, gate.QueueCount);
            Assert.Equal(1, gate.AvailableCount);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ReleasingMoreThanWasAcquired_DoesNotCreateExtraCapacity(int capacity)
    {
        using var gate = new PoolGovernorConcurrencyGate(capacity);
        var permit = gate.Acquire();

        gate.Release(permit);
        gate.Release(permit);
        gate.Release(permit);

        Assert.Equal(0, gate.ActiveCount);
        Assert.Equal(capacity, gate.AvailableCount);
    }

    // ── a one-slot gate behaves like a multi-slot gate ──

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task AtCapacity_AReleaseHandsThePermitToTheNextWaiter_WithoutExposingAFreeSlot(int capacity)
    {
        using var gate = new PoolGovernorConcurrencyGate(capacity);
        var permits = Enumerable.Range(0, capacity).Select(_ => gate.Acquire()).ToArray();
        var waiting = gate.AcquireAsync().AsTask();
        Assert.False(waiting.IsCompleted);
        Assert.False(gate.TryAcquire(out _));

        gate.Release(permits[0]);

        await waiting.WaitAsync(HardTimeout);
        Assert.Equal(capacity, gate.ActiveCount);
        Assert.Equal(0, gate.AvailableCount);
        Assert.False(gate.TryAcquire(out _));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + HardTimeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was never met.");
            await Task.Delay(5);
        }
    }
}
