#region

using System.Text;
using pengdows.crud.infrastructure;
using Xunit;

#endregion

namespace pengdows.crud.Tests.infrastructure;

public class StringBuilderPoolTests
{
    [Fact]
    public void Get_WithSeed_AppendsSeed_And_SubsequentGetIsCleared()
    {
        var sb = StringBuilderPool.Get("hello");
        Assert.Equal("hello", sb.ToString());

        StringBuilderPool.Return(sb);

        var sb2 = StringBuilderPool.Get();
        Assert.Equal(string.Empty, sb2.ToString());

        StringBuilderPool.Return(sb2);
    }

    [Fact]
    public void Return_Null_IsNoOp()
    {
        StringBuilder? sb = null;
        StringBuilderPool.Return(sb);

        // If no exception is thrown, test passes
        Assert.True(true);
    }

    [Fact]
    public void Return_Then_Get_RoundTripsInstanceSafely()
    {
        // Rent, write, return, rent again — should be empty and usable
        var sb = StringBuilderPool.Get();
        sb.Append("data");
        StringBuilderPool.Return(sb);

        var sb2 = StringBuilderPool.Get();
        Assert.Equal(string.Empty, sb2.ToString());
        sb2.Append("more-data");
        Assert.Equal("more-data", sb2.ToString());
        StringBuilderPool.Return(sb2);
    }
}