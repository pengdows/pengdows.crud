#region

using pengdows.crud.@internal;
using Xunit;

#endregion

namespace pengdows.crud.Tests.@internal;

public class ClauseCountersTests
{
    [Fact]
    public void NextSet_IncrementsWithPrefix()
    {
        var c = new ClauseCounters();
        Assert.Equal("s0", c.NextSet());
        Assert.Equal("s1", c.NextSet());
        Assert.Equal("s2", c.NextSet());
    }

    [Fact]
    public void NextWhere_IncrementsWithPrefix()
    {
        var c = new ClauseCounters();
        Assert.Equal("w0", c.NextWhere());
        Assert.Equal("w1", c.NextWhere());
    }

    [Fact]
    public void NextJoin_IncrementsWithPrefix()
    {
        var c = new ClauseCounters();
        Assert.Equal("j0", c.NextJoin());
        Assert.Equal("j1", c.NextJoin());
    }

    [Fact]
    public void NextKey_IncrementsWithPrefix()
    {
        var c = new ClauseCounters();
        Assert.Equal("k0", c.NextKey());
        Assert.Equal("k1", c.NextKey());
    }

    [Fact]
    public void NextVer_IncrementsWithPrefix()
    {
        var c = new ClauseCounters();
        Assert.Equal("v0", c.NextVer());
        Assert.Equal("v1", c.NextVer());
    }

    [Fact]
    public void NextIns_IncrementsWithPrefix()
    {
        var c = new ClauseCounters();
        Assert.Equal("i0", c.NextIns());
        Assert.Equal("i1", c.NextIns());
    }
}