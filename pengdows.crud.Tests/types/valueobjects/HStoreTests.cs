using System.Collections.Generic;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.types.valueobjects;

public static class HStoreTests
{
    [Fact]
    public static void Parse_PopulatesDictionaryWithNullHandling()
    {
        var text = "\"key 1\"=>\"value\", bare=>NULL";
        var hstore = HStore.Parse(text);

        Assert.Equal("value", hstore["key 1"]);
        Assert.Null(hstore["bare"]);
        Assert.True(hstore.ContainsKey("key 1"));
        Assert.Equal(2, hstore.Count);
    }

    [Fact]
    public static void ToString_EscapesQuotesAndSeparators()
    {
        var data = new Dictionary<string, string?>
        {
            ["needs,escaping"] = "value",
            ["quoted"] = "he\"llo"
        };

        var hstore = new HStore(data);
        var text = hstore.ToString();

        Assert.Contains("\"needs,escaping\"=>value", text);
        Assert.Contains("quoted=>\"he\\\"llo\"", text);
    }

    [Fact]
    public static void Enumerator_YieldsAllPairs()
    {
        var hstore = new HStore(new Dictionary<string, string?> { ["a"] = "1", ["b"] = "2" });

        var pairs = new List<KeyValuePair<string, string?>>();
        foreach (var pair in hstore)
        {
            pairs.Add(pair);
        }

        Assert.Equal(2, pairs.Count);
        Assert.Contains(pairs, kvp => kvp.Key == "a" && kvp.Value == "1");
        Assert.Contains(pairs, kvp => kvp.Key == "b" && kvp.Value == "2");
    }
}
