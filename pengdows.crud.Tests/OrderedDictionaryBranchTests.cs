using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using pengdows.crud.collections;
using Xunit;

namespace pengdows.crud.Tests;

public class OrderedDictionaryBranchTests
{
    private static T InvokePrivateStatic<T>(string name, params object?[] args)
    {
        var method = typeof(OrderedDictionary<string, int>)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (T)method!.Invoke(null, args)!;
    }

    [Fact]
    public void Clear_SmallMode_ClearsEntries()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("a", 1);
        dict.Add("b", 2);

        dict.Clear();

        Assert.Empty(dict);
        Assert.False(dict.ContainsKey("a"));
    }

    [Fact]
    public void Clear_BucketMode_ClearsEntries()
    {
        var dict = new OrderedDictionary<string, int>();
        for (var i = 0; i < 9; i++)
        {
            dict.Add($"k{i}", i);
        }

        dict.Clear();

        Assert.Empty(dict);
        Assert.False(dict.ContainsKey("k0"));
    }

    [Fact]
    public void TryInsert_Behaviors_WorkInSmallMode()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("a", 1);

        Assert.Throws<ArgumentException>(() => dict.Add("a", 2));
        Assert.False(dict.TryAdd("a", 3));

        dict["a"] = 4;
        Assert.Equal(4, dict["a"]);
    }

    [Fact]
    public void Remove_SmallMode_RemovesAndReturnsFalseForMissing()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("a", 1);
        dict.Add("b", 2);

        Assert.True(dict.Remove("a"));
        Assert.False(dict.Remove("missing"));
    }

    [Fact]
    public void Remove_BucketMode_UpdatesCollections()
    {
        var dict = new OrderedDictionary<string, int>();
        for (var i = 0; i < 10; i++)
        {
            dict.Add($"k{i}", i);
        }

        Assert.True(dict.Remove("k0"));
        Assert.False(dict.ContainsKey("k0"));
    }

    [Fact]
    public void Remove_KeyValuePair_RequiresMatchingValue()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("a", 1);

        Assert.False(dict.Remove(new KeyValuePair<string, int>("a", 2)));
        Assert.True(dict.Remove(new KeyValuePair<string, int>("a", 1)));
    }

    [Fact]
    public void Remove_WithValue_ReturnsValueWhenPresent()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("a", 1);

        Assert.True(dict.Remove("a", out var value));
        Assert.Equal(1, value);
        Assert.False(dict.Remove("missing", out _));
    }

    [Fact]
    public void EnsureCapacity_ResizesStorage()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.EnsureCapacity(32);
        dict.Add("a", 1);

        Assert.True(dict.ContainsKey("a"));
    }

    [Fact]
    public void TrimExcess_HandlesEmptyAndCompacts()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.TrimExcess();

        dict.Add("a", 1);
        dict.Clear();
        dict.TrimExcess();
        Assert.Empty(dict);

        for (var i = 0; i < 20; i++)
        {
            dict.Add($"k{i}", i);
        }

        for (var i = 0; i < 15; i++)
        {
            dict.Remove($"k{i}");
        }

        dict.TrimExcess();
        Assert.True(dict.ContainsKey("k19"));
    }

    [Fact]
    public void Resize_UsesInsertionOrderWhenBucketsAllocated()
    {
        var dict = new OrderedDictionary<string, int>();
        for (var i = 0; i < 10; i++)
        {
            dict.Add($"k{i}", i);
        }

        dict.EnsureCapacity(64);

        Assert.Equal(10, dict.Count);
        Assert.Equal(0, dict["k0"]);
    }

    [Fact]
    public void PrimeHelpers_HandleEdgeCases()
    {
        Assert.False(InvokePrivateStatic<bool>("IsPrime", 1));
        Assert.True(InvokePrivateStatic<bool>("IsPrime", 2));
        Assert.False(InvokePrivateStatic<bool>("IsPrime", 4));
        Assert.True(InvokePrivateStatic<bool>("IsPrime", 17));
        Assert.False(InvokePrivateStatic<bool>("IsPrime", 21));

        var fromTable = InvokePrivateStatic<int>("GetPrime", 10);
        Assert.True(fromTable >= 10);

        var fallback = InvokePrivateStatic<int>("GetPrime", 2_359_299);
        Assert.True(fallback >= 2_359_299);
        Assert.True(fallback % 2 == 1);
    }

    [Fact]
    public void Enumerators_ThrowWhenModified()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("a", 1);

        var keyEnum = dict.Keys.GetEnumerator();
        dict.Add("b", 2);
        Assert.Throws<InvalidOperationException>(() => ((IEnumerator)keyEnum).Reset());

        var valueEnum = dict.Values.GetEnumerator();
        dict.Add("c", 3);
        Assert.Throws<InvalidOperationException>(() => ((IEnumerator)valueEnum).Reset());
    }
}