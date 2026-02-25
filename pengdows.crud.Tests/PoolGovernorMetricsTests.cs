using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PoolGovernorMetricsTests
{
    [Fact]
    public async Task GetSnapshot_WhenMetricsEnabled_TracksPeakQueued()
    {
        // Arrange
        var governor = new PoolGovernor(
            PoolLabel.Reader, 
            "test-pool", 
            1, 
            TimeSpan.FromSeconds(1), 
            trackMetrics: true);

        // Act
        await using var first = await governor.AcquireAsync();
        
        // Start 5 more acquiring tasks that will queue
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(async () => await governor.AcquireAsync()))
            .ToList();

        // Robust wait: poll until the queue depth reaches 5 or we timeout
        // (Wait up to 5s, checking every 10ms)
        for (int i = 0; i < 500 && governor.GetSnapshot().Queued < 5; i++)
        {
            await Task.Delay(10);
        }
        
        var snapshot = governor.GetSnapshot();
        
        // Cleanup: Release the first one so the others can eventually finish (or fail silently)
        await first.DisposeAsync();
        
        // Assert
        Assert.Equal(5, snapshot.PeakQueued);
    }

    [Fact]
    public async Task GetSnapshot_WhenSingleWriterMode_TracksPeakTurnstileQueued()
    {
        // Arrange: SingleWriter mode (maxSlots=1, holdTurnstile=true)
        var turnstile = new SemaphoreSlim(1, 1);
        var governor = new PoolGovernor(
            PoolLabel.Writer,
            "test-pool",
            1,
            TimeSpan.FromSeconds(1),
            trackMetrics: true,
            turnstile: turnstile,
            holdTurnstile: true,
            ownsTurnstile: true);

        // Act
        await using var first = await governor.AcquireAsync();
        
        // Start 5 more acquiring tasks that will queue on the turnstile
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(async () => await governor.AcquireAsync()))
            .ToList();

        // Robust wait: poll until turnstile queue reaches 5 or timeout
        for (int i = 0; i < 500 && governor.GetSnapshot().TurnstileQueued < 5; i++)
        {
            await Task.Delay(10);
        }
        
        var snapshot = governor.GetSnapshot();
        
        // Cleanup
        await first.DisposeAsync();
        
        // Assert
        Assert.Equal(5, snapshot.PeakTurnstileQueued);
        // Semaphore queue should be 0 because turnstile gates them first
        Assert.Equal(0, snapshot.PeakQueued);
    }

    [Fact]
    public async Task GetSnapshot_WhenMetricsDisabled_DoesNotTrackPeaks()
    {
        // Arrange
        var governor = new PoolGovernor(
            PoolLabel.Reader, 
            "test-pool", 
            1, 
            TimeSpan.FromSeconds(5), 
            trackMetrics: false);

        // Act
        await using var first = await governor.AcquireAsync();
        var waiter = Task.Run(async () => await governor.AcquireAsync());
        await Task.Delay(50);
        
        var snapshot = governor.GetSnapshot();
        
        await first.DisposeAsync();
        await (await waiter).DisposeAsync();

        // Assert
        Assert.Equal(0, snapshot.PeakQueued);
    }

    [Fact]
    public async Task GetSnapshot_WhenMetricsEnabled_TracksWaitAndHoldTimes()
    {
        // Arrange
        var governor = new PoolGovernor(
            PoolLabel.Reader, 
            "test-pool", 
            1, 
            TimeSpan.FromSeconds(5), 
            trackMetrics: true);

        // Act
        await using (var p = await governor.AcquireAsync())
        {
            await Task.Delay(100); // Hold time
        }

        var snapshot = governor.GetSnapshot();

        // Assert
        Assert.True(snapshot.TotalHoldTicks > 0);
        Assert.Equal(1, snapshot.TotalAcquired);
    }
}
