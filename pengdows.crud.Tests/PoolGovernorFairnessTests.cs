using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for PoolGovernor turnstile fairness mechanism.
/// Writers hold the turnstile for the duration of their slot.  New readers
/// are gated at the turnstile while a writer slot is occupied, reducing — but
/// not eliminating — writer starvation under sustained reader pressure.
/// </summary>
public sealed class PoolGovernorFairnessTests
{
    [Fact]
    public async Task WriterWithTurnstile_BlocksNewReaders()
    {
        // Arrange: Create coordinated governors with turnstile
        var turnstile = new SemaphoreSlim(1, 1);
        var writerGovernor = new PoolGovernor(
            PoolLabel.Writer,
            "test-key",
            maxSlots: 1,
            acquireTimeout: TimeSpan.FromSeconds(5),
            disabled: false,
            sharedSemaphore: null,
            turnstile: turnstile,
            holdTurnstile: true); // Writer holds turnstile

        var readerGovernor = new PoolGovernor(
            PoolLabel.Reader,
            "test-key",
            maxSlots: 10,
            acquireTimeout: TimeSpan.FromSeconds(5),
            disabled: false,
            sharedSemaphore: null,
            turnstile: turnstile,
            holdTurnstile: false); // Readers touch-and-release

        // Act: Writer acquires (holds turnstile)
        var writerSlot = await writerGovernor.AcquireAsync();

        // Reader tries to acquire - should remain blocked until cancellation
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(() => readerGovernor.AcquireAsync(cts.Token).AsTask());

        // Release writer - now reader should complete
        await writerSlot.DisposeAsync();
        var readerSlot = await readerGovernor.AcquireAsync();
        await readerSlot.DisposeAsync();
    }

    [Fact]
    public async Task MultipleReaders_CanAcquireConcurrently_WhenNoWriterWaiting()
    {
        // Arrange: Turnstile shared between reader and writer governors
        var turnstile = new SemaphoreSlim(1, 1);
        var readerGovernor = new PoolGovernor(
            PoolLabel.Reader,
            "test-key",
            maxSlots: 5,
            acquireTimeout: TimeSpan.FromSeconds(5),
            disabled: false,
            sharedSemaphore: null,
            turnstile: turnstile,
            holdTurnstile: false);

        // Act: Multiple readers acquire concurrently
        var slots = new List<PoolSlot>();
        for (var i = 0; i < 5; i++)
        {
            slots.Add(await readerGovernor.AcquireAsync());
        }

        // Assert: All readers acquired successfully
        var snapshot = readerGovernor.GetSnapshot();
        Assert.Equal(5, snapshot.InUse);

        // Cleanup
        foreach (var slot in slots)
        {
            await slot.DisposeAsync();
        }
    }

    [Fact]
    public async Task WriterStarvationPrevention_WriterCompletesWithinBoundedTime()
    {
        // Arrange: Simulate reader pressure with a writer waiting
        var turnstile = new SemaphoreSlim(1, 1);
        var writerGovernor = new PoolGovernor(
            PoolLabel.Writer,
            "test-key",
            maxSlots: 1,
            acquireTimeout: TimeSpan.FromSeconds(10),
            disabled: false,
            sharedSemaphore: null,
            turnstile: turnstile,
            holdTurnstile: true);

        var readerGovernor = new PoolGovernor(
            PoolLabel.Reader,
            "test-key",
            maxSlots: 5,
            acquireTimeout: TimeSpan.FromSeconds(10),
            disabled: false,
            sharedSemaphore: null,
            turnstile: turnstile,
            holdTurnstile: false);

        var cts = new CancellationTokenSource();
        var readerCount = 0;
        var writerCompleted = false;

        // Start continuous reader pressure
        var readerTasks = new List<Task>();
        for (var i = 0; i < 3; i++)
        {
            readerTasks.Add(Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await using var slot = await readerGovernor.AcquireAsync(cts.Token);
                        Interlocked.Increment(ref readerCount);
                        await Task.Delay(10, cts.Token); // Hold briefly
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }));
        }

        // Let readers run for a bit
        await Task.Delay(100);

        // Act: Writer tries to acquire
        var writerTask = Task.Run(async () =>
        {
            await using var slot = await writerGovernor.AcquireAsync();
            writerCompleted = true;
        });

        // Assert: Writer should complete within bounded time (not starved)
        var completedInTime = await Task.WhenAny(writerTask, Task.Delay(TimeSpan.FromSeconds(5))) == writerTask;

        cts.Cancel();
        await Task.WhenAll(readerTasks.ToArray());

        Assert.True(completedInTime, "Writer should complete within bounded time - was starved by readers");
        Assert.True(writerCompleted, "Writer task should have completed");
        Assert.True(readerCount > 0, "Some readers should have completed");
    }

    [Fact]
    public async Task TurnstileRelease_OnWriterDispose_AllowsNewReaders()
    {
        // Arrange
        var turnstile = new SemaphoreSlim(1, 1);
        var writerGovernor = new PoolGovernor(
            PoolLabel.Writer,
            "test-key",
            maxSlots: 1,
            acquireTimeout: TimeSpan.FromSeconds(5),
            disabled: false,
            sharedSemaphore: null,
            turnstile: turnstile,
            holdTurnstile: true);

        var readerGovernor = new PoolGovernor(
            PoolLabel.Reader,
            "test-key",
            maxSlots: 5,
            acquireTimeout: TimeSpan.FromSeconds(5),
            disabled: false,
            sharedSemaphore: null,
            turnstile: turnstile,
            holdTurnstile: false);

        // Act: Writer acquires and releases
        await using (await writerGovernor.AcquireAsync())
        {
            // Writer holds turnstile
        }

        // After writer releases, readers should be able to acquire
        var slots = new List<PoolSlot>();
        for (var i = 0; i < 3; i++)
        {
            slots.Add(await readerGovernor.AcquireAsync());
        }

        // Assert
        var snapshot = readerGovernor.GetSnapshot();
        Assert.Equal(3, snapshot.InUse);

        // Cleanup
        foreach (var slot in slots)
        {
            await slot.DisposeAsync();
        }
    }

    [Fact]
    public async Task GovernorWithoutTurnstile_BehavesNormally()
    {
        // Arrange: Governor without turnstile (null)
        var governor = new PoolGovernor(
            PoolLabel.Writer,
            "test-key",
            maxSlots: 2,
            acquireTimeout: TimeSpan.FromSeconds(5),
            disabled: false,
            sharedSemaphore: null,
            turnstile: null,
            holdTurnstile: true);

        // Act: Acquire normally
        var slot1 = await governor.AcquireAsync();
        var slot2 = await governor.AcquireAsync();

        // Assert
        var snapshot = governor.GetSnapshot();
        Assert.Equal(2, snapshot.InUse);

        await slot1.DisposeAsync();
        await slot2.DisposeAsync();
    }

    [Fact]
    public void SyncAcquire_WithTurnstile_Works()
    {
        // Arrange
        var turnstile = new SemaphoreSlim(1, 1);
        var governor = new PoolGovernor(
            PoolLabel.Writer,
            "test-key",
            maxSlots: 1,
            acquireTimeout: TimeSpan.FromSeconds(5),
            disabled: false,
            sharedSemaphore: null,
            turnstile: turnstile,
            holdTurnstile: true);

        // Act
        using var slot = governor.Acquire();

        // Assert
        var snapshot = governor.GetSnapshot();
        Assert.Equal(1, snapshot.InUse);
    }

    [Fact]
    public async Task ExceptionDuringAcquire_ReleasesTurnstile()
    {
        // Arrange: Writer governor with 2 slots but we'll exhaust the semaphore
        // separately to test turnstile cleanup on semaphore timeout
        var turnstile = new SemaphoreSlim(1, 1);
        var sharedSemaphore = new SemaphoreSlim(1, 1);

        var governor = new PoolGovernor(
            PoolLabel.Writer,
            "test-key",
            maxSlots: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(50),
            disabled: false,
            sharedSemaphore: sharedSemaphore,
            turnstile: turnstile,
            holdTurnstile: true);

        // Exhaust the semaphore externally (simulate another consumer)
        sharedSemaphore.Wait();

        // Act: Acquire should get turnstile but timeout on semaphore
        await Assert.ThrowsAsync<pengdows.crud.exceptions.PoolSaturatedException>(() => governor.AcquireAsync().AsTask());

        // Verify turnstile was released by checking we can acquire it
        Assert.True(turnstile.Wait(0), "Turnstile should have been released after semaphore timeout");
        turnstile.Release();

        // Cleanup
        sharedSemaphore.Release();
    }

    [Fact]
    public async Task Cancellation_ReleasesTurnstile()
    {
        // Arrange: Governor where we'll cancel during semaphore wait
        var turnstile = new SemaphoreSlim(1, 1);
        var sharedSemaphore = new SemaphoreSlim(1, 1);

        var governor = new PoolGovernor(
            PoolLabel.Writer,
            "test-key",
            maxSlots: 1,
            acquireTimeout: TimeSpan.FromSeconds(30),
            disabled: false,
            sharedSemaphore: sharedSemaphore,
            turnstile: turnstile,
            holdTurnstile: true);

        // Exhaust the semaphore externally
        sharedSemaphore.Wait();

        // Act: Start acquire and cancel it
        using var cts = new CancellationTokenSource();
        var acquireTask = governor.AcquireAsync(cts.Token);

        // Give it time to acquire turnstile and start waiting on semaphore
        await Task.Delay(20);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await acquireTask);

        // Verify turnstile was released
        Assert.True(turnstile.Wait(0), "Turnstile should have been released after cancellation");
        turnstile.Release();

        // Cleanup
        sharedSemaphore.Release();
    }
}
