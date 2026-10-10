using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Characterizes the narrow claim behind PoolGovernor: a correctly sized provider pool is a
/// reasonable concurrency gate, but it does not provide bounded admission or single-writer
/// serialization by itself.
///
/// These tests intentionally compare the same semaphore-shaped provider-pool model with the real
/// PoolGovernor. The live provider comparisons remain in the opt-in PostgreSQL benchmarks; these
/// tests keep the causal claims fast, deterministic, and runnable without Docker.
/// </summary>
public sealed class ConnectionGovernanceThesisTests
{
    [Fact]
    public async Task UnboundedGovernorAndCorrectlySizedProviderPool_HaveTheSameAdmissionShape()
    {
        using var providerPool = new SemaphoreSlim(1, 1);
        using var governor = new PoolGovernor(
            PoolLabel.Reader,
            "thesis-provider-equivalent",
            maxSlots: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(100));

        await using var providerLease = await AcquireProviderAsync(providerPool);
        await using var governorLease = await governor.AcquireAsync();

        var providerWaiter = AcquireProviderAsync(providerPool);
        var governorWaiter = governor.AcquireAsync().AsTask();

        await Assert.ThrowsAsync<TimeoutException>(async () => await providerWaiter);
        await Assert.ThrowsAsync<PoolSaturatedException>(async () => await governorWaiter);
    }

    [Fact]
    public async Task BoundedGovernor_RejectsExcessBacklogImmediately_WhileProviderPoolKeepsWaiting()
    {
        using var providerPool = new SemaphoreSlim(1, 1);
        using var governor = new PoolGovernor(
            PoolLabel.Reader,
            "thesis-bounded-admission",
            maxSlots: 1,
            acquireTimeout: TimeSpan.FromSeconds(5),
            maxQueueDepth: 1);

        await using var providerLease = await AcquireProviderAsync(providerPool);
        await using var governorLease = await governor.AcquireAsync();

        var providerWaiter = AcquireProviderAsync(providerPool, PatientProviderTimeout);
        var governorWaiter = governor.AcquireAsync().AsTask();
        await WaitUntilAsync(() => governor.QueueDepth == 1);

        var providerExcessWaiter = AcquireProviderAsync(providerPool, PatientProviderTimeout);
        var governorExcessWaiter = governor.AcquireAsync().AsTask();

        var saturated = await Assert.ThrowsAsync<PoolSaturatedException>(async () => await governorExcessWaiter);

        Assert.Equal(1, governor.MaxQueueDepth);
        Assert.Equal(PoolLabel.Reader, saturated.PoolLabel);
        Assert.False(providerExcessWaiter.IsCompleted);

        await providerLease.DisposeAsync();
        await governorLease.DisposeAsync();
        await (await providerWaiter).DisposeAsync();
        await (await governorWaiter).DisposeAsync();
        await (await providerExcessWaiter).DisposeAsync();
    }

    [Fact]
    public async Task PoolSizeAlone_DoesNotSerializeWrites_WhenTheProviderPoolHasMultipleSlots()
    {
        using var providerPool = new SemaphoreSlim(2, 2);
        var activeWrites = 0;
        var peakWrites = 0;
        var releaseWork = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task RunWriteAsync()
        {
            await using var lease = await AcquireProviderAsync(providerPool);
            var active = Interlocked.Increment(ref activeWrites);
            UpdatePeak(ref peakWrites, active);
            await releaseWork.Task;
            Interlocked.Decrement(ref activeWrites);
        }

        var first = RunWriteAsync();
        var second = RunWriteAsync();

        await WaitUntilAsync(() => Volatile.Read(ref peakWrites) == 2);
        Assert.Equal(2, peakWrites);

        releaseWork.TrySetResult(true);
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task SingleWriterGovernor_SerializesWrites_EvenWhenTheWorkloadArrivesConcurrently()
    {
        using var governor = new PoolGovernor(
            PoolLabel.Writer,
            "thesis-single-writer",
            maxSlots: 1,
            acquireTimeout: TimeSpan.FromSeconds(5));
        var activeWrites = 0;
        var peakWrites = 0;
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task RunWriteAsync(bool first)
        {
            await using var lease = await governor.AcquireAsync();
            var active = Interlocked.Increment(ref activeWrites);
            UpdatePeak(ref peakWrites, active);
            if (first)
            {
                firstStarted.TrySetResult(true);
                await releaseFirst.Task;
            }
            Interlocked.Decrement(ref activeWrites);
        }

        var first = RunWriteAsync(first: true);
        await firstStarted.Task;
        var second = RunWriteAsync(first: false);

        await Task.Delay(25);
        Assert.False(second.IsCompleted);
        Assert.Equal(1, peakWrites);

        releaseFirst.TrySetResult(true);
        await Task.WhenAll(first, second);
        Assert.Equal(1, peakWrites);
    }

    // The provider's own wait budget. The default is short so a test of the timeout itself is quick; a waiter
    // that has to outlast other awaits in its test is given a long budget, because a loaded host can take
    // longer than 100 ms to get between two of them.
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PatientProviderTimeout = TimeSpan.FromSeconds(10);

    private static async Task<ProviderLease> AcquireProviderAsync(SemaphoreSlim providerPool, TimeSpan? timeout = null)
    {
        if (!await providerPool.WaitAsync(timeout ?? ProviderTimeout))
        {
            throw new TimeoutException("The provider pool wait timed out.");
        }

        return new ProviderLease(providerPool);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The expected contention state was not reached before the deadline.");
    }

    private static void UpdatePeak(ref int peak, int current)
    {
        while (true)
        {
            var observed = Volatile.Read(ref peak);
            if (current <= observed)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref peak, current, observed) == observed)
            {
                return;
            }
        }
    }

    private sealed class ProviderLease : IAsyncDisposable
    {
        private readonly SemaphoreSlim _pool;
        private int _released;

        public ProviderLease(SemaphoreSlim pool)
        {
            _pool = pool;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _pool.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
