using System;
using System.Collections.Generic;
using System.Linq;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class HStoreBranchTests
{
    [Fact]
    public void Constructor_WithNullDictionary_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new HStore((Dictionary<string, string?>)null!));
    }

    [Fact]
    public void DefaultStore_ReportsEmptyState()
    {
        var store = default(HStore);

        Assert.True(store.IsEmpty);
        Assert.Equal(0, store.Count);
        Assert.False(store.ContainsKey("missing"));
        Assert.Null(store["missing"]);
        Assert.Empty(store.Keys);
        Assert.Empty(store.Values);
        Assert.Equal(string.Empty, store.ToString());
        Assert.Equal(0, store.GetHashCode());
        Assert.True(store.Equals(default));

        var seen = 0;
        foreach (var _ in store)
        {
            seen++;
        }

        Assert.Equal(0, seen);
    }

    [Fact]
    public void Parse_Whitespace_ReturnsEmptyStore()
    {
        var store = HStore.Parse("   ");
        Assert.True(store.IsEmpty);
    }

    [Fact]
    public void Parse_InvalidPair_Throws()
    {
        Assert.Throws<FormatException>(() => HStore.Parse("badpair"));
    }

    [Fact]
    public void RoundTrip_HandlesNullsAndEscapes()
    {
        var data = new Dictionary<string, string?>
        {
            ["key"] = "value",
            ["key 2"] = "value,2",
            ["quote"] = "va\"lue",
            ["slash"] = "back\\slash",
            ["null"] = null
        };
        var store = new HStore(data);

        var text = store.ToString();
        var roundtrip = HStore.Parse(text);

        Assert.Equal(store, roundtrip);
        Assert.True(store.ContainsKey("key 2"));
        Assert.Equal("value,2", store["key 2"]);
        Assert.Null(store["null"]);
    }

    [Fact]
    public void Escape_Unescape_EmptyValues_RoundTrip()
    {
        var store = new HStore(new Dictionary<string, string?>
        {
            ["empty"] = ""
        });

        var text = store.ToString();
        var parsed = HStore.Parse(text);

        Assert.Equal(string.Empty, parsed["empty"]);
    }

    [Fact]
    public void Equals_HandlesDifferentStores()
    {
        var left = new HStore(new Dictionary<string, string?>
        {
            ["a"] = "1",
            ["b"] = "2"
        });
        var right = new HStore(new Dictionary<string, string?>
        {
            ["a"] = "1",
            ["b"] = "3"
        });

        Assert.False(left.Equals(right));
        Assert.True(left != right);
    }

    [Fact]
    public void KeysAndValues_ReturnInOrder()
    {
        var store = new HStore(new[]
        {
            new KeyValuePair<string, string?>("a", "1"),
            new KeyValuePair<string, string?>("b", null)
        });

        Assert.Equal(new[] { "a", "b" }, store.Keys.ToArray());
        Assert.Equal(new string?[] { "1", null }, store.Values.ToArray());
    }
}