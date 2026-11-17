using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Footnote Assumption (CONCURRENCY.md): "a timeout set to infinity removes the time bound"
/// (PoolGovernor.cs, ConvertTimeoutToStopwatchTicks / MaxTimeoutStopwatchTicks, L822-828).
///
/// ConvertTimeoutToStopwatchTicks is `Math.Max(1L, (long)Math.Min(timeout.TotalSeconds *
/// Stopwatch.Frequency, MaxTimeoutStopwatchTicks))`. There are two distinct .NET idioms for
/// "wait forever," and the comment at L825-826 ("capped so that TimeSpan.MaxValue... is a long
/// wait and not a cast that overflows, which on some runtimes is a one-tick budget that expires
/// at once") only names one of them:
///
///  - TimeSpan.MaxValue: a huge POSITIVE TotalSeconds. Multiplied by Stopwatch.Frequency this can
///    overflow a double toward +Infinity or just be astronomically large; Math.Min against
///    MaxTimeoutStopwatchTicks clamps it down to that constant (long.MaxValue / 4 stopwatch
///    ticks) -- a deadline so far in the future it is functionally unbounded for any real test or
///    production run, but not literally infinite.
///  - Timeout.InfiniteTimeSpan (TimeSpan.FromMilliseconds(-1)): a NEGATIVE TotalSeconds. Math.Min
///    of a negative number against a large positive constant returns the negative number
///    unchanged, and Math.Max(1L, negative) then clamps it to exactly 1 stopwatch tick -- the
///    near-immediate-expiry failure mode the L825-826 comment warns TimeSpan.MaxValue avoids,
///    but landed on by the other idiom instead.
///
/// Both forms are exercised directly: PoolGovernor's constructor performs no range validation
/// on acquireTimeout, so this tests ConvertTimeoutToStopwatchTicks's actual behavior for each,
/// independent of any validation DatabaseContextConfiguration layers on top.
/// </summary>
public sealed class PoolGovernorInfiniteTimeoutTests
{
    [Fact(Timeout = 10000)]
    public async Task TimeSpanMaxValue_ActsAsEffectivelyUnbounded_WaiterDoesNotTimeOutWithinTestWindow()
    {
        using var governor = new PoolGovernor(PoolLabel.Writer, "infinite-timeout-maxvalue", maxSlots: 1,
            acquireTimeout: TimeSpan.MaxValue);
        var held = governor.Acquire();

        var waiterTask = governor.AcquireAsync().AsTask();

        // A genuinely unbounded wait can't be proven to never finish; the bar this test can
        // actually clear is "does not throw PoolSaturatedException within a window many orders of
        // magnitude larger than any real acquire timeout would ever be."
        var finishedEarly = await Task.WhenAny(waiterTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.NotSame(waiterTask, finishedEarly);
        Assert.False(waiterTask.IsCompleted, "A TimeSpan.MaxValue timeout must not expire within a 2-second window.");

        held.Dispose();
        var slot = await waiterTask;
        await slot.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task TimeoutInfiniteTimeSpan_DoesNotCollapseToAOneTickBudget_WaiterDoesNotTimeOutWithinTestWindow()
    {
        using var governor = new PoolGovernor(PoolLabel.Writer, "infinite-timeout-sentinel", maxSlots: 1,
            acquireTimeout: Timeout.InfiniteTimeSpan);
        var held = governor.Acquire();

        var waiterTask = governor.AcquireAsync().AsTask();

        var finishedEarly = await Task.WhenAny(waiterTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.NotSame(waiterTask, finishedEarly);
        Assert.False(waiterTask.IsCompleted,
            "Timeout.InfiniteTimeSpan must not collapse to a near-zero budget and expire almost immediately.");

        held.Dispose();
        var slot = await waiterTask;
        await slot.DisposeAsync();
    }

    [Fact(Timeout = 10000)]
    public async Task TimeoutInfiniteTimeSpan_SynchronousAcquire_DoesNotThrowPoolSaturatedImmediately()
    {
        // The synchronous path (Acquire) hits the same ConvertTimeoutToStopwatchTicks conversion;
        // if InfiniteTimeSpan collapsed to one tick, a saturated governor would throw
        // PoolSaturatedException essentially instantly instead of genuinely blocking the caller.
        using var governor = new PoolGovernor(PoolLabel.Writer, "infinite-timeout-sync", maxSlots: 1,
            acquireTimeout: Timeout.InfiniteTimeSpan);
        var held = governor.Acquire();

        var releaseAfterDelay = Task.Run(async () =>
        {
            await Task.Delay(300);
            held.Dispose();
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var slot = await Task.Run(() => governor.Acquire());
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 250,
            $"Acquire() returned after only {sw.ElapsedMilliseconds}ms; a one-tick budget would " +
            "have thrown PoolSaturatedException almost instantly instead of genuinely waiting for " +
            "the slot to free up.");

        slot.Dispose();
        await releaseAfterDelay;
    }
}
