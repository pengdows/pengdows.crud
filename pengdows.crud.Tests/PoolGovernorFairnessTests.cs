using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for PoolGovernor turnstile fairness mechanism.
/// The turnstile ensures writer preference - when a writer is waiting,
/// new readers cannot start until the writer completes.
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
            maxPermits: 1,
            acquireTimeout: TimeSpan.FromSeconds(5),
            disabled: false,
            sharedSemaphore: null,
            turnstile: turnstile,
            holdTurnstile: true); // Writer holds turnstile

        var readerGovernor = new PoolGovernor(
            PoolLabel.Reader,
            "test-key",
            maxPermits: 10,
            acquireTimeout: TimeSpan.FromSeconds(5),
            disabled: false,
            sharedSemaphore: null,
            turnstile: turnstile,
            holdTurnstile: false); // Readers touch-and-release

        // Act: Writer acquires (holds turnstile)
        var writerPermit = await writerGovernor.AcquireAsync();

        // Reader tries to acquire - should block because writer holds turnstile
        var readerTask = Task.Run(async () => await readerGovernor.AcquireAsync());
        await Task.Delay(50); // Give reader time to attempt

        // Assert: Reader should be blocked (task not completed)
        Assert.False(readerTask.IsCompleted, "Reader should be blocked while writer holds turnstile");

        // Release writer - now reader should complete
        await writerPermit.DisposeAsync();
        var readerPermit = await readerTask;

        // PoolPermit is a struct, so we just verify the task completed successfully
        await readerPermit.DisposeAsync();
    }

    [Fact]
    public async Task MultipleReaders_CanAcquireConcurrently_WhenNoWriterWaiting()
    {
        // Arrange: Turnstile shared between reader and writer governors
        var turnstile = new SemaphoreSlim(1, 1);
        var readerGovernor = new PoolGovernor(
            PoolLabel.Reader,
            "test-key",
            maxPermits: 5,
            acquireTimeout: TimeSpan.FromSeconds(5),
            disabled: false,
            sharedSemaphore: null,
            turnstile: turnstile,
            holdTurnstile: false);

        // Act: Multiple readers acquire concurrently
        var permits = new List<PoolPermit>();
        for (int i = 0; i < 5; i++)
        {
            permits.Add(await readerGovernor.AcquireAsync());
        }

        // Assert: All readers acquired successfully
        var snapshot = readerGovernor.GetSnapshot();
        Assert.Equal(5, snapshot.InUse);

        // Cleanup
        foreach (var permit in permits)
        {
            await permit.DisposeAsync();
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
            maxPermits: 1,
            acquireTimeout: TimeSpan.FromSeconds(10),
            disabled: false,
            sharedSemaphore: null,
            turnstile: turnstile,
            holdTurnstile: true);

        var readerGovernor = new PoolGovernor(
            PoolLabel.Reader,
            "test-key",
            maxPermits: 5,
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
        for (int i = 0; i < 3; i++)
        {
            readerTasks.Add(Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await using var permit = await readerGovernor.AcquireAsync(cts.Token);
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
            await using var permit = await writerGovernor.AcquireAsync();
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
            maxPermits: 1,
            acquireTimeout: TimeSpan.FromSeconds(5),
            disabled: false,
            sharedSemaphore: null,
            turnstile: turnstile,
            holdTurnstile: true);

        var readerGovernor = new PoolGovernor(
            PoolLabel.Reader,
            "test-key",
            maxPermits: 5,
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
        var permits = new List<PoolPermit>();
        for (int i = 0; i < 3; i++)
        {
            permits.Add(await readerGovernor.AcquireAsync());
        }

        // Assert
        var snapshot = readerGovernor.GetSnapshot();
        Assert.Equal(3, snapshot.InUse);

        // Cleanup
        foreach (var permit in permits)
        {
            await permit.DisposeAsync();
        }
    }

    [Fact]
    public async Task GovernorWithoutTurnstile_BehavesNormally()
    {
        // Arrange: Governor without turnstile (null)
        var governor = new PoolGovernor(
            PoolLabel.Writer,
            "test-key",
            maxPermits: 2,
            acquireTimeout: TimeSpan.FromSeconds(5),
            disabled: false,
            sharedSemaphore: null,
            turnstile: null,
            holdTurnstile: true);

        // Act: Acquire normally
        var permit1 = await governor.AcquireAsync();
        var permit2 = await governor.AcquireAsync();

        // Assert
        var snapshot = governor.GetSnapshot();
        Assert.Equal(2, snapshot.InUse);

        await permit1.DisposeAsync();
        await permit2.DisposeAsync();
    }

    [Fact]
    public void SyncAcquire_WithTurnstile_Works()
    {
        // Arrange
        var turnstile = new SemaphoreSlim(1, 1);
        var governor = new PoolGovernor(
            PoolLabel.Writer,
            "test-key",
            maxPermits: 1,
            acquireTimeout: TimeSpan.FromSeconds(5),
            disabled: false,
            sharedSemaphore: null,
            turnstile: turnstile,
            holdTurnstile: true);

        // Act
        using var permit = governor.Acquire();

        // Assert
        var snapshot = governor.GetSnapshot();
        Assert.Equal(1, snapshot.InUse);
    }

    [Fact]
    public async Task ExceptionDuringAcquire_ReleasesTurnstile()
    {
        // Arrange: Writer governor with 2 permits but we'll exhaust the semaphore
        // separately to test turnstile cleanup on semaphore timeout
        var turnstile = new SemaphoreSlim(1, 1);
        var sharedSemaphore = new SemaphoreSlim(1, 1);

        var governor = new PoolGovernor(
            PoolLabel.Writer,
            "test-key",
            maxPermits: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(50),
            disabled: false,
            sharedSemaphore: sharedSemaphore,
            turnstile: turnstile,
            holdTurnstile: true);

        // Exhaust the semaphore externally (simulate another consumer)
        sharedSemaphore.Wait();

        // Act: Acquire should get turnstile but timeout on semaphore
        await Assert.ThrowsAsync<pengdows.crud.exceptions.PoolSaturatedException>(
            () => governor.AcquireAsync());

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
            maxPermits: 1,
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

        await Assert.ThrowsAsync<OperationCanceledException>(() => acquireTask);

        // Verify turnstile was released
        Assert.True(turnstile.Wait(0), "Turnstile should have been released after cancellation");
        turnstile.Release();

        // Cleanup
        sharedSemaphore.Release();
    }
}
