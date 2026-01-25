#region

using System.Collections;
using System.Collections.Generic;
using pengdows.crud.collections;
using Xunit;

#endregion

namespace pengdows.crud.Tests.collections;

public class OrderedDictionaryEnumeratorCoverageTests
{
    [Fact]
    public void OrderedDictionary_NonGenericEnumerator_Current_ReturnsKeyValuePair()
    {
        // This specifically tests the IEnumerator.Current property that was uncovered
        // Arrange
        var dict = new OrderedDictionary<string, int>
        {
            ["first"] = 1,
            ["second"] = 2
        };

        // Act
        var enumerator = ((IEnumerable)dict).GetEnumerator();
        enumerator.MoveNext();

        // Assert - Test the non-generic IEnumerator.Current property
        var current = enumerator.Current;
        Assert.NotNull(current);
        Assert.IsType<KeyValuePair<string, int>>(current);

        var kvp = (KeyValuePair<string, int>)current;
        Assert.Equal("first", kvp.Key);
        Assert.Equal(1, kvp.Value);
    }

    [Fact]
    public void OrderedDictionary_KeyEnumerator_Current_ReturnsKey()
    {
        // This tests the keyed enumerator's Current property (another uncovered item)
        // Arrange
        var dict = new OrderedDictionary<string, int>
        {
            ["alpha"] = 100,
            ["beta"] = 200
        };

        // Act
        IEnumerator keyEnumerator = dict.Keys.GetEnumerator();
        keyEnumerator.MoveNext();

        // Assert
        var current = keyEnumerator.Current;
        Assert.Equal("alpha", current);
    }

    [Fact]
    public void OrderedDictionary_ValueEnumerator_Current_ReturnsValue()
    {
        // This tests the value enumerator's Current property
        // Arrange
        var dict = new OrderedDictionary<string, int>
        {
            ["gamma"] = 300,
            ["delta"] = 400
        };

        // Act
        IEnumerator valueEnumerator = dict.Values.GetEnumerator();
        valueEnumerator.MoveNext();

        // Assert
        var current = valueEnumerator.Current;
        Assert.Equal(300, current);
    }

    [Fact]
    public void OrderedDictionary_NonGenericEnumerator_MoveNext_IteratesCorrectly()
    {
        // Test the full enumeration cycle
        // Arrange
        var dict = new OrderedDictionary<string, string>
        {
            ["a"] = "apple",
            ["b"] = "banana"
        };

        // Act
        var enumerator = ((IEnumerable)dict).GetEnumerator();
        var items = new List<KeyValuePair<string, string>>();

        while (enumerator.MoveNext())
        {
            var kvp = (KeyValuePair<string, string>)enumerator.Current;
            items.Add(kvp);
        }

        // Assert
        Assert.Equal(2, items.Count);
        Assert.Equal("a", items[0].Key);
        Assert.Equal("apple", items[0].Value);
        Assert.Equal("b", items[1].Key);
        Assert.Equal("banana", items[1].Value);
    }

    [Fact]
    public void OrderedDictionary_NonGenericEnumerator_Reset_ResetsPosition()
    {
        // Test enumerator reset functionality
        // Arrange
        var dict = new OrderedDictionary<int, string>
        {
            [1] = "one",
            [2] = "two"
        };

        var enumerator = ((IEnumerable)dict).GetEnumerator();

        // Act - Move to first element, then reset
        enumerator.MoveNext();
        var firstCurrent = (KeyValuePair<int, string>)enumerator.Current;

        enumerator.Reset();
        enumerator.MoveNext();
        var afterResetCurrent = (KeyValuePair<int, string>)enumerator.Current;

        // Assert - Should be the same after reset
        Assert.Equal(firstCurrent.Key, afterResetCurrent.Key);
        Assert.Equal(firstCurrent.Value, afterResetCurrent.Value);
    }

    [Fact]
    public void OrderedDictionary_EmptyDictionary_EnumeratorBehavesCorrectly()
    {
        // Test enumeration on empty dictionary
        // Arrange
        var dict = new OrderedDictionary<string, int>();

        // Act
        var enumerator = ((IEnumerable)dict).GetEnumerator();

        // Assert
        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void OrderedDictionary_MultipleEnumerators_WorkIndependently()
    {
        // Test that multiple enumerators don't interfere with each other
        // Arrange
        var dict = new OrderedDictionary<string, int>
        {
            ["x"] = 10,
            ["y"] = 20,
            ["z"] = 30
        };

        // Act - Create multiple enumerators
        var enum1 = ((IEnumerable)dict).GetEnumerator();
        var enum2 = ((IEnumerable)dict).GetEnumerator();

        // Move first enumerator to second position
        enum1.MoveNext(); // first
        enum1.MoveNext(); // second

        // Move second enumerator to first position
        enum2.MoveNext(); // first

        // Assert - They should be at different positions
        var current1 = (KeyValuePair<string, int>)enum1.Current;
        var current2 = (KeyValuePair<string, int>)enum2.Current;

        Assert.NotEqual(current1.Key, current2.Key);
        Assert.Equal("y", current1.Key);
        Assert.Equal("x", current2.Key);
    }
}