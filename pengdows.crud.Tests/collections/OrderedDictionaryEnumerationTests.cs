#region

using System;
using System.Linq;
using pengdows.crud.collections;
using Xunit;

#endregion

namespace pengdows.crud.Tests.collections;

public class OrderedDictionaryEnumerationTests
{
    [Fact]
    public void Enumeration_AfterRemovalsAndAdditions_RespectsInsertionOrder()
    {
        var dict = new OrderedDictionary<int, string>(32);
        dict.Add(1, "one");
        dict.Add(2, "two");
        dict.Add(3, "three");
        dict.Remove(2);
        dict.Add(4, "four");
        dict.Remove(1);
        dict.Add(5, "five");

        var expectedKeys = new[] { 3, 4, 5 };
        var expectedValues = new[] { "three", "four", "five" };

        Assert.Equal(expectedKeys, dict.Keys.ToArray());
        Assert.Equal(expectedValues, dict.Values.ToArray());
    }

    [Fact]
    public void Enumerator_ModifiedDuringIteration_ThrowsInvalidOperationException()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("alpha", 1);
        dict.Add("beta", 2);

        var enumerator = dict.GetEnumerator();
        Assert.True(enumerator.MoveNext());

        dict.Add("gamma", 3);

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void TrimExcess_PreservesInsertionOrderAfterTombstones()
    {
        var dict = new OrderedDictionary<int, int>(32);
        for (var i = 0; i < 16; i++)
        {
            dict.Add(i, i);
        }

        for (var i = 0; i < 8; i++)
        {
            dict.Remove(i);
        }

        dict.TrimExcess();

        Assert.Equal(Enumerable.Range(8, 8), dict.Keys);
    }
}
