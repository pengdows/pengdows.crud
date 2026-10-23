using System;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// A governor counts leases whether or not it limits admission: disabled means unlimited, not untracked.
/// The context's connection counts come from these leases.
/// </summary>
public sealed class PoolGovernorLeaseTrackingTests
{
    [Fact]
    public void DisabledGovernor_Acquire_TracksTheLease()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "key", 0, TimeSpan.FromSeconds(1), disabled: true);

        var slot = governor.Acquire();
        var held = governor.GetSnapshot();
        slot.Dispose();
        var released = governor.GetSnapshot();

        Assert.True(held.Disabled);
        Assert.Equal(1, held.InUse);
        Assert.Equal(1, held.PeakInUse);
        Assert.Equal(1, held.TotalAcquired);
        Assert.Equal(0, released.InUse);
        Assert.Equal(1, released.PeakInUse);
        Assert.Equal(1, governor.TotalReleased);
    }

    [Fact]
    public async Task DisabledGovernor_AcquireAsync_TracksTheLease()
    {
        using var governor = new PoolGovernor(PoolLabel.Writer, "key", 0, TimeSpan.FromSeconds(1), disabled: true);

        var slot = await governor.AcquireAsync();
        Assert.Equal(1, governor.GetSnapshot().InUse);
        await slot.DisposeAsync();

        Assert.Equal(0, governor.GetSnapshot().InUse);
        Assert.Equal(1, governor.GetSnapshot().TotalAcquired);
        Assert.Equal(1, governor.TotalReleased);
    }

    [Fact]
    public async Task DisabledGovernor_TryAcquire_TracksTheLease()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "key", 0, TimeSpan.FromSeconds(1), disabled: true);

        Assert.True(governor.TryAcquire(out var slot));
        var (success, asyncSlot) = await governor.TryAcquireAsync();
        Assert.True(success);
        Assert.Equal(2, governor.GetSnapshot().InUse);

        slot.Dispose();
        await asyncSlot.DisposeAsync();

        Assert.Equal(0, governor.GetSnapshot().InUse);
        Assert.Equal(2, governor.GetSnapshot().PeakInUse);
        Assert.Equal(2, governor.TotalReleased);
    }

    [Fact]
    public void DisabledGovernor_DoesNotLimitAdmission()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "key", 0, TimeSpan.FromMilliseconds(10), disabled: true);

        var slots = new PoolSlot[50];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = governor.Acquire();
        }

        Assert.Equal(50, governor.GetSnapshot().InUse);
        foreach (var slot in slots)
        {
            slot.Dispose();
        }

        Assert.Equal(0, governor.GetSnapshot().InUse);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReleasingALeaseTwice_CountsItOnce(bool disabled)
    {
        using var governor = new PoolGovernor(PoolLabel.Writer, "key", 2, TimeSpan.FromSeconds(1), disabled: disabled);

        var slot = governor.Acquire();
        slot.Dispose();
        slot.Dispose();

        Assert.Equal(0, governor.GetSnapshot().InUse);
        Assert.Equal(1, governor.GetSnapshot().TotalAcquired);
        Assert.Equal(1, governor.TotalReleased);
    }

    [Fact]
    public void BoundedGovernor_CountsAcquiredAndReleased()
    {
        using var governor = new PoolGovernor(PoolLabel.Reader, "key", 3, TimeSpan.FromSeconds(1));

        var first = governor.Acquire();
        var second = governor.Acquire();
        first.Dispose();

        Assert.Equal(1, governor.GetSnapshot().InUse);
        Assert.Equal(2, governor.GetSnapshot().PeakInUse);
        Assert.Equal(2, governor.GetSnapshot().TotalAcquired);
        Assert.Equal(1, governor.TotalReleased);

        second.Dispose();
        Assert.Equal(2, governor.TotalReleased);
    }
}
