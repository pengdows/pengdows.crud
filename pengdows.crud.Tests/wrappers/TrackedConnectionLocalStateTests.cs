using System;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.wrappers;

// TrackedConnection implements IConnectionLocalState directly (LocalState => this, inlined to
// avoid an allocation per connection checkout) rather than delegating to a separate class — these
// tests exercise that inline implementation through the public LocalState property. Moved here
// (and renamed) from ConnectionLocalStateTests.cs, which tested this exact behavior under a name
// implying a standalone ConnectionLocalState class was involved; that class had zero production
// call sites and was deleted as dead code.
[Collection("AllocationSerial")]
public class TrackedConnectionLocalStateTests
{
    [Fact]
    public void Reset_ClearsPreparedShape_AndKeepsDisableFlag()
    {
        using var stateOwner = new TrackedConnection(new fakeDbConnection());
        var state = stateOwner.LocalState;
        state.DisablePrepare();

        var sql = "SELECT 1";
        Assert.False(state.IsAlreadyPreparedForShape(sql));
        var (added, evicted) = state.MarkShapePrepared(sql);
        Assert.True(added);
        Assert.Equal(0, evicted);
        Assert.True(state.IsAlreadyPreparedForShape(sql));

        state.Reset();

        Assert.False(state.IsAlreadyPreparedForShape(sql));
        Assert.True(state.PrepareDisabled); // flag persists across Reset()
    }

    [Fact]
    public void MarkShapePrepared_ReturnsFalse_WhenShapeAlreadyTracked()
    {
        using var stateOwner = new TrackedConnection(new fakeDbConnection());
        var state = stateOwner.LocalState;
        var (addedFirst, evictedFirst) = state.MarkShapePrepared("SELECT 1");
        Assert.True(addedFirst);
        Assert.Equal(0, evictedFirst);

        var (addedSecond, evictedSecond) = state.MarkShapePrepared("SELECT 1");
        Assert.False(addedSecond);
        Assert.Equal(0, evictedSecond);
    }

    // REL-005: a connection checkout usually runs one statement, yet tracking its one prepared shape
    // built a ConcurrentDictionary and a ConcurrentQueue per checkout (about 1.5 KB, profiled on
    // SQLite ReadSingle). One shape is held in a field; the collections exist only from the second.
    [Fact]
    public void OneShape_AllocatesNothing()
    {
        using (var warm = new TrackedConnection(new fakeDbConnection()))
        {
            warm.LocalState.MarkShapePrepared("SELECT 0");
            warm.LocalState.IsAlreadyPreparedForShape("SELECT 0");
        }

        using var stateOwner = new TrackedConnection(new fakeDbConnection());
        var state = stateOwner.LocalState;
        var before = GC.GetAllocatedBytesForCurrentThread();

        var (added, evicted) = state.MarkShapePrepared("SELECT 1");
        var prepared = state.IsAlreadyPreparedForShape("SELECT 1");
        var again = state.MarkShapePrepared("SELECT 1");

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(added);
        Assert.Equal(0, evicted);
        Assert.True(prepared);
        Assert.False(again.Added);
    }

    [Fact]
    public void SecondShape_KeepsTheFirstAndTheirOrder()
    {
        using var stateOwner = new TrackedConnection(new fakeDbConnection());
        var state = stateOwner.LocalState;
        state.MarkShapePrepared("SELECT 1");
        state.MarkShapePrepared("SELECT 2");

        Assert.True(state.IsAlreadyPreparedForShape("SELECT 1"));
        Assert.True(state.IsAlreadyPreparedForShape("SELECT 2"));
        Assert.False(state.MarkShapePrepared("SELECT 1").Added);

        for (var i = 3; i <= 33; i++)
        {
            state.MarkShapePrepared($"SELECT {i}");
        }

        // 33 shapes with a cap of 32: the first one marked is the one evicted.
        Assert.False(state.IsAlreadyPreparedForShape("SELECT 1"));
        Assert.True(state.IsAlreadyPreparedForShape("SELECT 2"));

        state.Reset();
        Assert.False(state.IsAlreadyPreparedForShape("SELECT 2"));
        Assert.True(state.MarkShapePrepared("SELECT 2").Added);
        Assert.True(state.IsAlreadyPreparedForShape("SELECT 2"));
    }

    [Fact]
    public void PreparedShapeCache_IsBoundedAt32_OldestShapesAreEvicted()
    {
        // Add more than the _maxPrepared (32) distinct shapes.
        // The cache must cap itself so oldest entries are evicted to make room.
        using var stateOwner = new TrackedConnection(new fakeDbConnection());
        var state = stateOwner.LocalState;
        var total = 40;

        for (var i = 0; i < total; i++)
        {
            state.MarkShapePrepared($"SELECT {i} FROM t");
        }

        // After 40 additions with cap 32, the first 8 entries (0-7) must have been evicted.
        Assert.False(state.IsAlreadyPreparedForShape("SELECT 0 FROM t"), "Oldest shape should have been evicted");
        Assert.False(state.IsAlreadyPreparedForShape("SELECT 7 FROM t"), "Shape 7 should have been evicted");
        Assert.True(state.IsAlreadyPreparedForShape("SELECT 8 FROM t"), "Shape 8 should still be cached");
        Assert.True(state.IsAlreadyPreparedForShape("SELECT 39 FROM t"), "Newest shape should still be cached");
    }
}
