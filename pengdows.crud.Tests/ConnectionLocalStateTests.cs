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
}