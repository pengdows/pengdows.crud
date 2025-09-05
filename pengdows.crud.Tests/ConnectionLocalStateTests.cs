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
        state.MarkShapePrepared(sql);
        Assert.True(state.IsAlreadyPreparedForShape(sql));

        state.Reset();

        Assert.False(state.IsAlreadyPreparedForShape(sql));
        Assert.True(state.PrepareDisabled); // flag persists across Reset()
    }
}

