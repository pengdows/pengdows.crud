using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Governor permits must be returned whenever a TrackedConnection is disposed, and the library's
/// behaviour for a connection that is NOT disposed must be pinned so a change is deliberate.
/// </summary>
public class TrackedConnectionSlotLifetimeTests
{
    private static PoolGovernor NewGovernor() =>
        new(PoolLabel.Writer, "slot-lifetime", 1, TimeSpan.FromMilliseconds(100));

    // TrackedConnection.Dispose / DisposeAsync called _onDispose outside any try/finally, and ReleaseSlot() ran after
    // it, so a throwing callback skipped ReleaseSlot() and leaked the permit (PoolSaturatedException for every later
    // caller; audit G-04, failed on 2.0.6). The callback is now isolated and the slot is always released.
    [Fact]
    public void Dispose_WhenOnDisposeCallbackThrows_StillReleasesSlot()
    {
        using var governor = NewGovernor();
        var tracked = new TrackedConnection(new fakeDbConnection(),
            onDispose: _ => throw new InvalidOperationException("callback failed"),
            slot: governor.Acquire());
        Assert.Equal(1, governor.GetSnapshot().InUse);

        _ = Record.Exception(() => tracked.Dispose());

        Assert.Equal(0, governor.GetSnapshot().InUse);
    }

    [Fact]
    public async Task DisposeAsync_WhenOnDisposeCallbackThrows_StillReleasesSlot()
    {
        using var governor = NewGovernor();
        var tracked = new TrackedConnection(new fakeDbConnection(),
            onDispose: _ => throw new InvalidOperationException("callback failed"),
            slot: governor.Acquire());
        Assert.Equal(1, governor.GetSnapshot().InUse);

        _ = await Record.ExceptionAsync(async () => await tracked.DisposeAsync());

        Assert.Equal(0, governor.GetSnapshot().InUse);
    }

    // Characterisation, passes on 2.0.6: there is no finalizer or GC backstop, so an abandoned
    // TrackedConnection keeps its permit until Dispose. If a backstop is added this test must change
    // on purpose; docs should state that leak-freedom depends on callers disposing leases.
    [Fact]
    public void AbandonedConnection_KeepsItsPermit_UntilDisposed()
    {
        using var governor = NewGovernor();
        var weak = Abandon(governor);

        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.Equal(1, governor.GetSnapshot().InUse);
        Assert.False(governor.TryAcquire(out _));
    }

    private static WeakReference<TrackedConnection> Abandon(PoolGovernor governor)
    {
        var tracked = new TrackedConnection(new fakeDbConnection(), slot: governor.Acquire());
        return new WeakReference<TrackedConnection>(tracked);
    }
}
