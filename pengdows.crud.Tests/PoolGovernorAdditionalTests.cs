using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.exceptions;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class PoolGovernorAdditionalTests
{
    [Fact]
    public void Acquire_DisabledGovernor_ReturnsDefaultSlot()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "hash", 1, TimeSpan.FromMilliseconds(1), true);
        var slot = governor.Acquire();
        Assert.Equal(default, slot);

        var snapshot = governor.GetSnapshot();
        Assert.True(snapshot.Disabled);
        Assert.Equal(0, snapshot.InUse);
    }

    [Fact]
    public void Acquire_TracksUsageAndReleasesSlot()
    {
        var governor = new PoolGovernor(PoolLabel.Writer, "writer-hash", 1, TimeSpan.FromSeconds(1));
        var slot = governor.Acquire();
        var active = governor.GetSnapshot();
        Assert.Equal(1, active.InUse);
        Assert.Equal(1, active.TotalAcquired);

        slot.Dispose();
        var drained = governor.GetSnapshot();
        Assert.Equal(0, drained.InUse);
        Assert.Equal(1, drained.TotalAcquired);
    }

    [Fact]
    public void Acquire_WhenPoolSaturated_ThrowsPoolSaturatedException()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "hash", 1, TimeSpan.FromMilliseconds(50));
        var slot = governor.Acquire();

        var ex = Assert.Throws<PoolSaturatedException>(() => governor.Acquire());
        Assert.Equal("hash", ex.PoolKeyHash);

        slot.Dispose();
        var snapshot = governor.GetSnapshot();
        Assert.Equal(1, snapshot.TotalSlotTimeouts);
    }

    [Fact]
    public async Task AcquireAsync_WhenPoolSaturated_ThrowsPoolSaturatedException()
    {
        var governor = new PoolGovernor(PoolLabel.Writer, "writer-hash", 1, TimeSpan.FromMilliseconds(50));
        var slot = await governor.AcquireAsync();

        var ex = await Assert.ThrowsAsync<PoolSaturatedException>(() => governor.AcquireAsync().AsTask());
        Assert.Equal("writer-hash", ex.PoolKeyHash);

        slot.Dispose();
        var snapshot = governor.GetSnapshot();
        Assert.Equal(1, snapshot.TotalSlotTimeouts);
    }

    [Fact]
    public void Acquire_WithSharedSemaphore_UsesSharedCapacity()
    {
        using var shared = new SemaphoreSlim(1, 1);
        var governor = new PoolGovernor(PoolLabel.Reader, "shared", 1, TimeSpan.FromMilliseconds(20),
            sharedSemaphore: shared);

        Assert.False(governor.OwnsSemaphore);

        using var slot = governor.Acquire();
        var snapshot = governor.GetSnapshot();
        Assert.Equal(1, snapshot.MaxSlots);
        Assert.Equal(1, snapshot.InUse);

        Assert.Throws<PoolSaturatedException>(() => governor.Acquire());
    }

    [Fact]
    public void Slot_DisposeTwice_DoesNotOverRelease()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "double-release", 1, TimeSpan.FromMilliseconds(20));

        var slot = governor.Acquire();
        slot.Dispose();
        slot.Dispose();

        using var second = governor.Acquire();

        Assert.Throws<PoolSaturatedException>(() => governor.Acquire());
    }

    [Fact]
    public void TryAcquire_WhenSlotAvailable_ReturnsTrueAndSlot()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "try-reader", 1, TimeSpan.FromMilliseconds(50));

        var acquired = governor.TryAcquire(out var slot);

        Assert.True(acquired);
        Assert.NotEqual(default, slot);
        Assert.Equal(1, governor.GetSnapshot().InUse);

        slot.Dispose();
    }

    [Fact]
    public void TryAcquire_WhenSaturated_ReturnsFalse()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "try-reader", 1, TimeSpan.FromMilliseconds(10));
        using var slot = governor.Acquire();

        var acquired = governor.TryAcquire(out var second);

        Assert.False(acquired);
        Assert.Equal(default, second);
        Assert.Equal(0, governor.GetSnapshot().TotalSlotTimeouts);
    }

    [Fact]
    public async Task TryAcquireAsync_WhenSlotAvailable_ReturnsTrueAndSlot()
    {
        var governor = new PoolGovernor(PoolLabel.Writer, "try-writer", 1, TimeSpan.FromMilliseconds(50));

        var acquired = await governor.TryAcquireAsync();

        Assert.True(acquired.Success);
        Assert.NotEqual(default, acquired.Permit);
        Assert.Equal(1, governor.GetSnapshot().InUse);

        await acquired.Permit.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenSaturated_ReturnsFalse()
    {
        var governor = new PoolGovernor(PoolLabel.Writer, "try-writer", 1, TimeSpan.FromMilliseconds(10));
        await using var slot = await governor.AcquireAsync();

        var acquired = await governor.TryAcquireAsync();

        Assert.False(acquired.Success);
        Assert.Equal(default, acquired.Permit);
        Assert.Equal(0, governor.GetSnapshot().TotalSlotTimeouts);
    }

    [Fact]
    public async Task WaitForDrainAsync_CompletesAfterRelease()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "drain", 1, TimeSpan.FromMilliseconds(50));
        var slot = governor.Acquire();

        var waitTask = governor.WaitForDrainAsync();

        Assert.False(waitTask.IsCompleted);
        slot.Dispose();

        await waitTask;
        Assert.Equal(0, governor.GetSnapshot().InUse);
    }

    [Fact]
    public async Task WaitForDrainAsync_WhenCanceled_Throws()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "drain-cancel", 1, TimeSpan.FromMilliseconds(50));
        using var slot = governor.Acquire();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await governor.WaitForDrainAsync(null, cts.Token));
    }

    [Fact]
    public async Task WaitForDrainAsync_WhenTimedOut_Throws()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "drain-timeout", 1, TimeSpan.FromMilliseconds(50));
        using var slot = governor.Acquire();

        await Assert.ThrowsAsync<TimeoutException>(async () => await governor.WaitForDrainAsync(TimeSpan.FromMilliseconds(25)));
    }

    // ── drain-signal race regression ──────────────────────────────────────
    // Release() decrements _inUse and calls TrySetResult on the drain signal
    // non-atomically.  A concurrent Acquire that increments _inUse between
    // those two steps can leave the signal completed while a slot is live.
    //
    // Setup (capacity 2):
    //   1. Acquire A and B          (inUse=2, sem=0)
    //   2. Dispose A                (inUse=1, sem=1  — frees one slot)
    //   3. Concurrently: Dispose B  AND  TryAcquire C
    //        B.Release: Decrement → inUse=0, enters signal block
    //        C.Acquire: grabs A's freed slot, OnAcquired → inUse=1,
    //                   ResetDrainSignalIfNeeded sees signal NOT yet completed
    //                   → does nothing
    //        B.Release: TrySetResult → signal completed, but inUse=1  ← bug
    //
    // Detection: a pre-cancelled CancellationToken lets us probe
    // WaitForDrainAsync with zero polling delay.
    //   • spuriously completed signal → method returns  (bug)
    //   • signal correctly pending   → OperationCanceledException  (correct)
    [Fact]
    public async Task WaitForDrainAsync_NoSpuriousCompletion_ConcurrentReleaseAndAcquire()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "drain-race", 2, TimeSpan.FromSeconds(5));
        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();

        for (var i = 0; i < 50_000; i++)
        {
            var a = governor.Acquire();
            var b = governor.Acquire();

            // Release A first so its semaphore slot is available for C to
            // grab during B's release window.
            a.Dispose();

            // Race B's release against C's acquire.
            PoolSlot c = default;
            await Task.WhenAll(
                Task.Run(() => b.Dispose()),
                Task.Run(() =>
                {
                    while (true)
                    {
                        if (governor.TryAcquire(out var local))
                        {
                            c = local;
                            break;
                        }

                        Thread.Yield();
                    }
                })
            );

            // c is held (InUse == 1).  A spuriously completed drain signal
            // causes WaitForDrainAsync to return instead of throwing OCE.
            try
            {
                await governor.WaitForDrainAsync(null, cancelledCts.Token);

                // If we reach here the signal completed.  Confirm it is
                // spurious (InUse > 0) rather than a legitimate momentary
                // drain that was already reset by C's OnAcquired.
                var inUse = governor.GetSnapshot().InUse;
                Assert.True(inUse == 0,
                    $"WaitForDrainAsync returned while InUse={inUse} " +
                    "(drain-signal race)");
            }
            catch (OperationCanceledException)
            {
                // Correct: signal pending, pre-cancelled token fired first.
            }

            c.Dispose();
        }
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "dispose", 1, TimeSpan.FromMilliseconds(50));

        governor.Dispose();
        governor.Dispose();
    }
}
