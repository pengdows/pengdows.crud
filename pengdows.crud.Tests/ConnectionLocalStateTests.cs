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

        var shape = "SELECT|1:4";
        Assert.False(state.IsAlreadyPreparedForShape(shape));
        state.MarkShapePrepared(shape);
        Assert.True(state.IsAlreadyPreparedForShape(shape));

        state.Reset();

        Assert.False(state.IsAlreadyPreparedForShape(shape));
        Assert.True(state.PrepareDisabled); // flag persists across Reset()
    }
}

