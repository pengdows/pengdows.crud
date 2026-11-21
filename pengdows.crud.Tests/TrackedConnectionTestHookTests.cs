using System;
using System.Reflection;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The G4 race hook on <see cref="TrackedConnection"/>. It exists only in builds that define
/// <c>PENGDOWS_CRUD_TEST_HOOKS</c> (set for this test project by its reference to the library), so these tests also
/// pin the contract the race test relies on: when it runs, and what state the slot is in.
/// </summary>
public class TrackedConnectionTestHookTests
{
    private const string HookName = "TestOnlyAfterSlotFieldSetBeforeAttachedCas";

    private static PoolGovernor NewGovernor(int slots) =>
        new(PoolLabel.Writer, "test-hook", slots, TimeSpan.FromMilliseconds(100));

    [Fact]
    public void TestHook_IsCompiledIntoTheTestBuild()
    {
        // If the library's PENGDOWS_CRUD_TEST_HOOKS define is lost, the race test cannot compile; this names the gate.
        var property = typeof(TrackedConnection).GetProperty(HookName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(property);
        Assert.Equal(typeof(Action), property!.PropertyType);
    }

    [Fact]
    public void TestHook_DefaultsToNull()
    {
        var tracked = new TrackedConnection(new fakeDbConnection());

        Assert.Null(Hook(tracked));
    }

    [Fact]
    public void TestHook_RunsExactlyOnce_ForASuccessfulAttach()
    {
        using var governor = NewGovernor(1);
        var tracked = new TrackedConnection(new fakeDbConnection());
        var invocations = 0;
        SetHook(tracked, () => invocations++);

        tracked.AttachSlot(governor.Acquire());

        Assert.Equal(1, invocations);
    }

    [Fact]
    public void TestHook_DoesNotRun_WhenADuplicateAttachIsRejected()
    {
        // The first CAS rejects the second attach before the hook point, so the hook must not run for it.
        using var governor = NewGovernor(2);
        var tracked = new TrackedConnection(new fakeDbConnection());
        tracked.AttachSlot(governor.Acquire());
        var invocations = 0;
        SetHook(tracked, () => invocations++);

        Assert.Throws<InvalidOperationException>(() => tracked.AttachSlot(governor.Acquire()));

        Assert.Equal(0, invocations);
    }

    [Fact]
    public void TestHook_RunsWhileTheAttachIsInProgress_SoAReentrantAttachIsRejectedAndReturnsItsSlot()
    {
        using var governor = NewGovernor(2);
        var tracked = new TrackedConnection(new fakeDbConnection());
        InvalidOperationException? nested = null;
        SetHook(tracked, () => nested = Assert.Throws<InvalidOperationException>(() => tracked.AttachSlot(governor.Acquire())));

        tracked.AttachSlot(governor.Acquire());

        Assert.NotNull(nested);
        Assert.Contains("already attached", nested!.Message, StringComparison.Ordinal);

        // Only the outer attach's permit is still held: the rejected nested slot went back to the governor.
        Assert.Equal(1, governor.AvailablePermits);
    }

    private static Action? Hook(TrackedConnection tracked) =>
        (Action?)typeof(TrackedConnection).GetProperty(HookName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(tracked);

    private static void SetHook(TrackedConnection tracked, Action hook) =>
        typeof(TrackedConnection).GetProperty(HookName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(tracked, hook);
}
