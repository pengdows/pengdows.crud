using System;
using System.Threading.Tasks;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PoolGovernorTests
{
    [Fact]
    public async Task AcquireAsync_WhenCapacityAvailable_TracksStats()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 2, TimeSpan.FromMilliseconds(100));

        await using var permit = await governor.AcquireAsync();

        var snapshot = governor.GetSnapshot();
        Assert.Equal(2, snapshot.MaxPermits);
        Assert.Equal(1, snapshot.InUse);
        Assert.Equal(1, snapshot.PeakInUse);
        Assert.Equal(0, snapshot.Queued);
        Assert.Equal(1, snapshot.TotalAcquired);
    }

    [Fact]
    public async Task AcquireAsync_WhenContended_QueuesAndCompletes()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 1, TimeSpan.FromMilliseconds(200));
        await using var first = await governor.AcquireAsync();

        var waiter = governor.AcquireAsync();
        await Task.Delay(25);

        var queuedSnapshot = governor.GetSnapshot();
        Assert.True(queuedSnapshot.Queued >= 1);

        await first.DisposeAsync();
        await using var second = await waiter;

        var finalSnapshot = governor.GetSnapshot();
        Assert.Equal(0, finalSnapshot.Queued);
        Assert.Equal(2, finalSnapshot.TotalAcquired);
    }

    [Fact]
    public async Task AcquireAsync_WhenTimeout_ThrowsPoolSaturatedException()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 1, TimeSpan.FromMilliseconds(25));
        await using var permit = await governor.AcquireAsync();

        var ex = await Assert.ThrowsAsync<PoolSaturatedException>(() => governor.AcquireAsync());
        Assert.Equal(PoolLabel.Reader, ex.PoolLabel);
        Assert.Equal("reader-key", ex.PoolKeyHash);
        Assert.True(ex.Snapshot.TotalTimeouts >= 1);
    }

    [Fact]
    public async Task PermitDispose_ReleasesCapacity()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "reader-key", 1, TimeSpan.FromMilliseconds(100));
        await using (await governor.AcquireAsync())
        {
        }

        var snapshot = governor.GetSnapshot();
        Assert.Equal(0, snapshot.InUse);

        await using var second = await governor.AcquireAsync();
        var after = governor.GetSnapshot();
        Assert.Equal(1, after.InUse);
    }
}