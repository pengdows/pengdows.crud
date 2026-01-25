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
        Assert.True(state.MarkShapePrepared(sql, out var evicted));
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
        Assert.True(state.MarkShapePrepared("SELECT 1", out var evictedFirst));
        Assert.Equal(0, evictedFirst);

        Assert.False(state.MarkShapePrepared("SELECT 1", out var evictedSecond));
        Assert.Equal(0, evictedSecond);
    }
}