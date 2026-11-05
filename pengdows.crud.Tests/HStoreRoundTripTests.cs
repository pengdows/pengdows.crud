using System.Collections.Generic;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class HStoreRoundTripTests
{
    [Theory]
    [InlineData("a=>b", "c")]
    [InlineData("k=v", "x")]
    [InlineData("k>v", "x")]
    [InlineData("plain", "a=>b")]
    [InlineData("has space", "comma, and=>arrow")]
    [InlineData("quote\"key", "back\\slash")]
    public void ToString_ThenParse_RoundTripsKeysAndValuesContainingSeparators(string key, string value)
    {
        var original = new HStore(new Dictionary<string, string?> { [key] = value });

        var parsed = HStore.Parse(original.ToString());

        Assert.Equal(1, parsed.Count);
        Assert.Equal(value, parsed[key]);
    }

    [Fact]
    public void Parse_KeyContainingArrow_IsNotSplitAtTheArrowInsideQuotes()
    {
        var parsed = HStore.Parse("\"a=>b\"=>c");

        Assert.Equal(1, parsed.Count);
        Assert.True(parsed.ContainsKey("a=>b"));
        Assert.Equal("c", parsed["a=>b"]);
    }

    [Fact]
    public void Parse_NullValueAfterQuotedArrowKey_StaysNull()
    {
        var parsed = HStore.Parse("\"x=>y\"=>NULL");

        Assert.True(parsed.ContainsKey("x=>y"));
        Assert.Null(parsed["x=>y"]);
    }
}
