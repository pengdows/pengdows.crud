using System;
using System.Diagnostics;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// G2 (CONCURRENCY.md): with a configured queue-depth cap, a caller arriving once the cap is
/// already full is rejected immediately, before it ever waits. With no cap (DatabaseContext's
/// documented default, passed here as the literal <see cref="int.MaxValue"/> it uses -- not
/// PoolGovernor's own different null-default, which is an 8x/32 floor, not unbounded), a caller
/// instead waits out the full acquire timeout and then fails. CONCURRENCY.md's existing coverage
/// (DatabaseContextQueueCapDefaultTests, PoolGovernorQueueCapConfigurationTests) only checks that
/// the configured value lands on <c>MaxQueueDepth</c> -- a static property read, not a behavioral
/// test of which of the two failure modes a caller actually experiences. These are that test.
/// </summary>
public sealed class PoolGovernorQueueCapBehaviorTests
{
    [Fact(Timeout = 10000)]
    public async Task ConfiguredQueueCap_RejectsTheNextCallerSynchronously_BeforeItEverWaits()
    {
        using var governor = new PoolGovernor(
            PoolLabel.Reader, "g2-configured-cap", maxSlots: 1, acquireTimeout: TimeSpan.FromSeconds(5),
            maxQueueDepth: 1);

        var held = await governor.AcquireAsync(); // the only slot
        var queuedTask = governor.AcquireAsync().AsTask(); // the one queue slot
        await WaitUntilAsync(() => governor.QueueDepth == 1);

        var stopwatch = Stopwatch.StartNew();
        var saturated = await Assert.ThrowsAsync<PoolSaturatedException>(() => governor.AcquireAsync().AsTask());
        stopwatch.Stop();

        Assert.Equal(PoolLabel.Reader, saturated.PoolLabel);
        // Generous relative to the 5s acquire timeout: a synchronous rejection should be close to
        // instant, not anywhere near a real wait.
        Assert.True(stopwatch.ElapsedMilliseconds < 200,
            $"Rejection took {stopwatch.ElapsedMilliseconds} ms; a queue-cap rejection must happen " +
            "before the caller ever waits, not after a timeout.");

        await held.DisposeAsync();
        await using var queued = await queuedTask;
    }

    [Fact(Timeout = 10000)]
    public async Task DefaultUnboundedQueue_LetsTheCallerWaitOutTheFullTimeout_InsteadOfRejectingSynchronously()
    {
        using var governor = new PoolGovernor(
            PoolLabel.Reader, "g2-unbounded", maxSlots: 1, acquireTimeout: TimeSpan.FromMilliseconds(300),
            maxQueueDepth: int.MaxValue); // DatabaseContext's actual default (UnboundedQueueDepth), not PoolGovernor's own 8x/32 null-default.

        var held = await governor.AcquireAsync();

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<PoolSaturatedException>(() => governor.AcquireAsync().AsTask());
        stopwatch.Stop();

        // The other side of the same claim: with no cap, this caller must have genuinely waited
        // out (most of) the acquire timeout rather than being rejected at enqueue.
        Assert.True(stopwatch.ElapsedMilliseconds >= 250,
            $"Rejection took only {stopwatch.ElapsedMilliseconds} ms against a 300ms timeout; an " +
            "unbounded queue should let the caller wait, not reject it synchronously.");

        await held.DisposeAsync();
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
}
