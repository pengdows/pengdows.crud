using pengdows.crud.connection;
using Xunit;

namespace pengdows.crud.Tests;

public class ConnectionLocalStateTests
{
    [Fact]
    public void Reset_ClearsPreparedShape_AndKeepsDisableFlag()
    {
        var state = new ConnectionLocalState
        {
            PrepareDisabled = true
        };

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
        var state = new ConnectionLocalState();
        var (addedFirst, evictedFirst) = state.MarkShapePrepared("SELECT 1");
        Assert.True(addedFirst);
        Assert.Equal(0, evictedFirst);

        var (addedSecond, evictedSecond) = state.MarkShapePrepared("SELECT 1");
        Assert.False(addedSecond);
        Assert.Equal(0, evictedSecond);
    }

    [Fact]
    public void PreparedShapeCache_IsBoundedAt32_OldestShapesAreEvicted()
    {
        // Add more than the _maxPrepared (32) distinct shapes.
        // The cache must cap itself so oldest entries are evicted to make room.
        var state = new ConnectionLocalState();
        var total = 40;

        for (var i = 0; i < total; i++)
        {
            state.MarkShapePrepared($"SELECT {i} FROM t");
        }

        // After 40 additions with cap 32, the first 8 entries (0–7) must have been evicted.
        Assert.False(state.IsAlreadyPreparedForShape("SELECT 0 FROM t"), "Oldest shape should have been evicted");
        Assert.False(state.IsAlreadyPreparedForShape("SELECT 7 FROM t"), "Shape 7 should have been evicted");
        Assert.True(state.IsAlreadyPreparedForShape("SELECT 8 FROM t"), "Shape 8 should still be cached");
        Assert.True(state.IsAlreadyPreparedForShape("SELECT 39 FROM t"), "Newest shape should still be cached");
    }
}