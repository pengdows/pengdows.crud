#pragma warning disable CS0618 // ConnectionLocalState is [Obsolete] (kept for 2.x compatibility); these tests pin its shipped behavior.

using pengdows.crud.connection;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.connection;

/// <summary>
/// Pins the shipped behavior of the public <see cref="ConnectionLocalState"/> type. Production code uses the
/// inlined state on <c>TrackedConnection</c>, so these tests are the only coverage of the class itself.
/// </summary>
public class ConnectionLocalStateContractTests
{
    [Fact]
    public void ComputeShapeHash_ReturnsCommandText()
    {
        using var cmd = new fakeDbCommand { CommandText = "SELECT 1" };

        Assert.Equal("SELECT 1", ConnectionLocalState.ComputeShapeHash(cmd));
    }

    [Fact]
    public void NewState_HasNothingPreparedAndNothingApplied()
    {
        var state = new ConnectionLocalState();

        Assert.False(state.PrepareDisabled);
        Assert.False(state.SessionSettingsApplied);
        Assert.False(state.IsAlreadyPreparedForShape("SELECT 1"));
    }

    [Fact]
    public void DisablePrepare_SetsFlag()
    {
        var state = new ConnectionLocalState();

        state.DisablePrepare();

        Assert.True(state.PrepareDisabled);
    }

    [Fact]
    public void MarkSessionSettingsApplied_SetsFlag()
    {
        var state = new ConnectionLocalState();

        state.MarkSessionSettingsApplied();

        Assert.True(state.SessionSettingsApplied);
    }

    [Fact]
    public void MarkShapePrepared_FirstMark_AddsWithoutEviction()
    {
        var state = new ConnectionLocalState();

        var (added, evicted) = state.MarkShapePrepared("SELECT 1");

        Assert.True(added);
        Assert.Equal(0, evicted);
        Assert.True(state.IsAlreadyPreparedForShape("SELECT 1"));
    }

    [Fact]
    public void MarkShapePrepared_SecondMarkOfSameShape_ReturnsNotAdded()
    {
        var state = new ConnectionLocalState();
        state.MarkShapePrepared("SELECT 1");

        var (added, evicted) = state.MarkShapePrepared("SELECT 1");

        Assert.False(added);
        Assert.Equal(0, evicted);
    }

    [Fact]
    public void MarkShapePrepared_BeyondCapacity_EvictsOldestFirst()
    {
        var state = new ConnectionLocalState();
        for (var i = 0; i < 32; i++)
        {
            state.MarkShapePrepared($"SELECT {i}");
        }

        var (added, evicted) = state.MarkShapePrepared("SELECT 32");

        Assert.True(added);
        Assert.Equal(1, evicted);
        Assert.False(state.IsAlreadyPreparedForShape("SELECT 0"));
        Assert.True(state.IsAlreadyPreparedForShape("SELECT 1"));
        Assert.True(state.IsAlreadyPreparedForShape("SELECT 32"));
    }

    [Fact]
    public void Reset_ClearsPreparedShapes_KeepsPrepareDisabled_ClearsSessionSettings()
    {
        var state = new ConnectionLocalState();
        state.DisablePrepare();
        state.MarkSessionSettingsApplied();
        state.MarkShapePrepared("SELECT 1");

        state.Reset();

        Assert.False(state.IsAlreadyPreparedForShape("SELECT 1"));
        Assert.True(state.PrepareDisabled);
        Assert.False(state.SessionSettingsApplied);
    }

    [Fact]
    public void Reset_BeforeAnyMark_IsANoOp()
    {
        var state = new ConnectionLocalState();

        state.Reset();

        Assert.False(state.IsAlreadyPreparedForShape("SELECT 1"));
    }

    [Fact]
    public void Reset_ThenMark_StartsOverWithFreshCapacity()
    {
        var state = new ConnectionLocalState();
        for (var i = 0; i < 32; i++)
        {
            state.MarkShapePrepared($"SELECT {i}");
        }

        state.Reset();
        var (added, evicted) = state.MarkShapePrepared("SELECT 99");

        Assert.True(added);
        Assert.Equal(0, evicted);
        Assert.True(state.IsAlreadyPreparedForShape("SELECT 99"));
    }
}
