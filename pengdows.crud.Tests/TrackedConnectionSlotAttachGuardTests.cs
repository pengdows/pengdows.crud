using System;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// G4: a <see cref="TrackedConnection"/> owns at most one governor slot for its whole life and returns it exactly
/// once. Invalid transitions (a second <c>AttachSlot</c>, or <c>AttachSlot</c> after the slot was released) must
/// fail with <see cref="InvalidOperationException"/> and must not leak the slot they were handed.
/// </summary>
/// <remarks>
/// <para>
/// Without the guard, 2.0.6's <c>AttachSlot</c> overwrote an attached slot (leaking the first permit) and silently
/// kept a slot attached after release (stranding that permit). The invalid-transition tests below fail on that code.
/// </para>
/// <para>
/// No production call site is known to reach either invalid transition (M0 addendum): every governed checkout
/// attaches in the constructor, and the late-attach sites run during single-threaded initialization. These tests
/// make that invariant enforced instead of conventional.
/// </para>
/// <para>
/// Assertions read gate state (<c>AvailablePermits</c>) and <c>TotalReleased</c>, not the snapshot's <c>InUse</c>:
/// <c>InUse</c> is a diagnostic counter decremented after the permit is returned.
/// </para>
/// <para>
/// Not covered: a release racing an in-progress attach (the Attaching -> Released branch). Exercising it
/// deterministically needs a test hook inside <c>AttachSlot</c>, which this change deliberately does not add.
/// </para>
/// </remarks>
public class TrackedConnectionSlotAttachGuardTests
{
    private static PoolGovernor NewGovernor(int slots) =>
        new(PoolLabel.Writer, "slot-attach-guard", slots, TimeSpan.FromMilliseconds(100));

    // ---- Valid paths: behaviour must be unchanged -------------------------------------------------------------

    // Every governed checkout attaches its slot in the constructor.
    [Fact]
    public void ConstructorSlot_DisposedTwice_ReleasesExactlyOnce()
    {
        using var governor = NewGovernor(1);
        var tracked = new TrackedConnection(new fakeDbConnection(), slot: governor.Acquire());
        Assert.Equal(0, governor.AvailablePermits);

        tracked.Dispose();
        tracked.Dispose();

        Assert.Equal(1L, governor.TotalReleased);
        Assert.Equal(1, governor.AvailablePermits);
    }

    [Fact]
    public async Task ConstructorSlot_DisposeAsyncThenDispose_ReleasesExactlyOnce()
    {
        using var governor = NewGovernor(1);
        var tracked = new TrackedConnection(new fakeDbConnection(), slot: governor.Acquire());

        await tracked.DisposeAsync();
        tracked.Dispose();

        Assert.Equal(1L, governor.TotalReleased);
        Assert.Equal(1, governor.AvailablePermits);
    }

    // The initialization connection is created without a slot and attached later when it becomes the
    // SingleConnection persistent connection or a PreventDatabaseUnload sentinel.
    [Fact]
    public void LateAttach_OnSlotlessConnection_ReleasesExactlyOnceOnDispose()
    {
        using var governor = NewGovernor(1);
        var tracked = new TrackedConnection(new fakeDbConnection());

        tracked.AttachSlot(governor.Acquire());
        Assert.Equal(0, governor.AvailablePermits);

        tracked.Dispose();
        tracked.Dispose();

        Assert.Equal(1L, governor.TotalReleased);
        Assert.Equal(1, governor.AvailablePermits);
    }

    // ---- Invalid transitions: explicit failure, no leaked permit ------------------------------------------------

    [Fact]
    public void DuplicateAttach_AfterConstructorSlot_Throws_ReturnsRejectedSlot_KeepsFirst()
    {
        using var governor = NewGovernor(2);
        var tracked = new TrackedConnection(new fakeDbConnection(), slot: governor.Acquire());
        var second = governor.Acquire();
        Assert.Equal(0, governor.AvailablePermits);

        var ex = Assert.Throws<InvalidOperationException>(() => tracked.AttachSlot(second));
        Assert.Contains("already attached", ex.Message);

        // The rejected slot went back to the governor; the first is still held by the connection.
        Assert.Equal(1L, governor.TotalReleased);
        Assert.Equal(1, governor.AvailablePermits);

        tracked.Dispose();

        Assert.Equal(2L, governor.TotalReleased);
        Assert.Equal(2, governor.AvailablePermits);
    }

    [Fact]
    public void DuplicateAttach_AfterLateAttach_Throws_ReturnsRejectedSlot_KeepsFirst()
    {
        using var governor = NewGovernor(2);
        var tracked = new TrackedConnection(new fakeDbConnection());
        tracked.AttachSlot(governor.Acquire());
        var second = governor.Acquire();

        var ex = Assert.Throws<InvalidOperationException>(() => tracked.AttachSlot(second));
        Assert.Contains("already attached", ex.Message);
        Assert.Equal(1L, governor.TotalReleased);
        Assert.Equal(1, governor.AvailablePermits);

        tracked.Dispose();

        Assert.Equal(2L, governor.TotalReleased);
        Assert.Equal(2, governor.AvailablePermits);
    }

    // The release-before-attach ordering M0 examined: ReleaseSlot runs with nothing attached, then a slot arrives.
    [Fact]
    public void AttachAfterRelease_OnSlotlessConnection_Throws_AndReturnsTheSlot()
    {
        using var governor = NewGovernor(1);
        var tracked = new TrackedConnection(new fakeDbConnection());
        tracked.Dispose();

        var late = governor.Acquire();
        Assert.Equal(0, governor.AvailablePermits);

        var ex = Assert.Throws<InvalidOperationException>(() => tracked.AttachSlot(late));
        Assert.Contains("after this connection has released", ex.Message);

        Assert.Equal(1L, governor.TotalReleased);
        Assert.Equal(1, governor.AvailablePermits);
    }

    [Fact]
    public async Task AttachAfterRelease_OfAnAttachedSlot_Throws_AndReturnsBothSlots()
    {
        using var governor = NewGovernor(2);
        var tracked = new TrackedConnection(new fakeDbConnection(), slot: governor.Acquire());
        await tracked.DisposeAsync();
        Assert.Equal(1L, governor.TotalReleased);

        var late = governor.Acquire();
        var ex = Assert.Throws<InvalidOperationException>(() => tracked.AttachSlot(late));
        Assert.Contains("after this connection has released", ex.Message);

        Assert.Equal(2L, governor.TotalReleased);
        Assert.Equal(2, governor.AvailablePermits);
    }

    // G4 (CONCURRENCY.md): "a release racing an in-progress attach... needs a hook the guard does
    // not add." TestOnlyAfterSlotFieldSetBeforeAttachedCas (TrackedConnection.cs, AttachSlot) is
    // that hook -- it forces the exact interleaving a real race would only produce by luck,
    // deterministically, in one thread: a release lands after the slot field is set but before
    // the second CAS that would mark it Attached.
    [Fact]
    public void AttachSlot_ReleaseRacingInProgressAttach_Throws_AndReturnsTheSlot_WithoutLeakingOrDoubleReleasing()
    {
        using var governor = NewGovernor(1);
        var tracked = new TrackedConnection(new fakeDbConnection());
        var slot = governor.Acquire();

        // Runs from inside AttachSlot's own call stack, between "_slot = slot" and the CAS that
        // would mark the state Attached -- the exact window the race is about.
        tracked.TestOnlyAfterSlotFieldSetBeforeAttachedCas = () => tracked.Dispose();

        var ex = Assert.Throws<InvalidOperationException>(() => tracked.AttachSlot(slot));
        Assert.Contains("after this connection has released", ex.Message);

        // Exactly once: not leaked (stranded, counted as still outstanding) and not
        // double-released (which would corrupt the governor's accounting the opposite way).
        Assert.Equal(1L, governor.TotalReleased);
        Assert.Equal(1, governor.AvailablePermits);

        // The racing Dispose() call itself must behave normally even though it ran from inside
        // AttachSlot's own call stack, not as an ordinary top-level call -- including being
        // idempotent on a second, ordinary call afterward.
        tracked.Dispose();
        Assert.Equal(1L, governor.TotalReleased);
    }
}
