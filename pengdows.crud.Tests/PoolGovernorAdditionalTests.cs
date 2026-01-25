using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class PoolGovernorAdditionalTests
{
    [Fact]
    public void Acquire_DisabledGovernor_ReturnsDefaultPermit()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "hash", 1, TimeSpan.FromMilliseconds(1), true);
        var permit = governor.Acquire();
        Assert.Equal(default, permit);

        var snapshot = governor.GetSnapshot();
        Assert.True(snapshot.Disabled);
        Assert.Equal(0, snapshot.InUse);
    }

    [Fact]
    public void Acquire_TracksUsageAndReleasesPermit()
    {
        var governor = new PoolGovernor(PoolLabel.Writer, "writer-hash", 1, TimeSpan.FromSeconds(1));
        var permit = governor.Acquire();
        var active = governor.GetSnapshot();
        Assert.Equal(1, active.InUse);
        Assert.Equal(1, active.TotalAcquired);

        permit.Dispose();
        var drained = governor.GetSnapshot();
        Assert.Equal(0, drained.InUse);
        Assert.Equal(1, drained.TotalAcquired);
    }

    [Fact]
    public void Acquire_WhenPoolSaturated_ThrowsPoolSaturatedException()
    {
        var governor = new PoolGovernor(PoolLabel.Reader, "hash", 1, TimeSpan.FromMilliseconds(50));
        var permit = governor.Acquire();

        var ex = Assert.Throws<PoolSaturatedException>(() => governor.Acquire());
        Assert.Equal("hash", ex.PoolKeyHash);

        permit.Dispose();
        var snapshot = governor.GetSnapshot();
        Assert.Equal(1, snapshot.TotalTimeouts);
    }

    [Fact]
    public async Task AcquireAsync_WhenPoolSaturated_ThrowsPoolSaturatedException()
    {
        var governor = new PoolGovernor(PoolLabel.Writer, "writer-hash", 1, TimeSpan.FromMilliseconds(50));
        var permit = await governor.AcquireAsync();

        var ex = await Assert.ThrowsAsync<PoolSaturatedException>(() => governor.AcquireAsync());
        Assert.Equal("writer-hash", ex.PoolKeyHash);

        permit.Dispose();
        var snapshot = governor.GetSnapshot();
        Assert.Equal(1, snapshot.TotalTimeouts);
    }
}