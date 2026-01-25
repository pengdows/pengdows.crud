#region

using System;
using System.Collections;
using System.Collections.Generic;
using pengdows.crud.collections;
using Xunit;

#endregion

namespace pengdows.crud.Tests.collections;

public class OrderedDictionaryCoverageTests
{
    [Fact]
    public void OrderedDictionary_IEnumerator_Current_ReturnsCorrectValue()
    {
        // Arrange
        var dict = new OrderedDictionary<string, int>
        {
            ["first"] = 1,
            ["second"] = 2,
            ["third"] = 3
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
    public void OrderedDictionary_IEnumerator_MoveNext_IteratesCorrectly()
    {
        // Arrange
        var dict = new OrderedDictionary<string, int>
        {
            ["a"] = 10,
            ["b"] = 20
        };

        // Act
        var enumerator = ((IEnumerable)dict).GetEnumerator();
        var items = new List<object>();

        while (enumerator.MoveNext())
        {
            items.Add(enumerator.Current);
        }

        // Assert
        Assert.Equal(2, items.Count);

        var first = (KeyValuePair<string, int>)items[0];
        var second = (KeyValuePair<string, int>)items[1];

        Assert.Equal("a", first.Key);
        Assert.Equal(10, first.Value);
        Assert.Equal("b", second.Key);
        Assert.Equal(20, second.Value);
    }

    [Fact]
    public void OrderedDictionary_IEnumerator_Reset_ResetsPosition()
    {
        // Arrange
        var dict = new OrderedDictionary<string, int>
        {
            ["x"] = 100,
            ["y"] = 200
        };

        var enumerator = ((IEnumerable)dict).GetEnumerator();

        // Act - Move to first element, then reset
        enumerator.MoveNext();
        var firstCurrent = enumerator.Current;

        enumerator.Reset();
        enumerator.MoveNext();
        var afterResetCurrent = enumerator.Current;

        // Assert - Should be the same after reset
        Assert.Equal(firstCurrent, afterResetCurrent);
    }

    [Fact]
    public void OrderedDictionary_KeyedEnumerator_Current_ReturnsCorrectValue()
    {
        // This tests the keyed enumerator's Current property
        // Arrange
        var dict = new OrderedDictionary<string, int>
        {
            ["key1"] = 42,
            ["key2"] = 84
        };

        // Act - Test through Keys enumeration
        IEnumerator keyEnumerator = dict.Keys.GetEnumerator();
        keyEnumerator.MoveNext();

        // Assert
        var current = keyEnumerator.Current;
        Assert.Equal("key1", current);
    }

    [Fact]
    public void OrderedDictionary_ValuedEnumerator_Current_ReturnsCorrectValue()
    {
        // This tests the valued enumerator's Current property
        // Arrange
        var dict = new OrderedDictionary<string, int>
        {
            ["alpha"] = 111,
            ["beta"] = 222
        };

        // Act - Test through Values enumeration
        IEnumerator valueEnumerator = dict.Values.GetEnumerator();
        valueEnumerator.MoveNext();

        // Assert
        var current = valueEnumerator.Current;
        Assert.Equal(111, current);
    }

    [Fact]
    public void OrderedDictionary_MultipleEnumerators_WorkIndependently()
    {
        // Arrange
        var dict = new OrderedDictionary<string, int>
        {
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3
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
        Assert.Equal("two", current1.Key);
        Assert.Equal("one", current2.Key);
    }

    [Fact]
    public void OrderedDictionary_EmptyDictionary_EnumeratorBehavesCorrectly()
    {
        // Arrange
        var dict = new OrderedDictionary<string, int>();

        // Act
        var enumerator = ((IEnumerable)dict).GetEnumerator();

        // Assert
        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void OrderedDictionary_DisposedEnumerator_HandlesProperly()
    {
        // Arrange
        var dict = new OrderedDictionary<string, int>
        {
            ["test"] = 123
        };

        // Act & Assert - Should not throw when disposed
        var enumerator = ((IEnumerable)dict).GetEnumerator();
        enumerator.MoveNext();

        if (enumerator is IDisposable disposable)
        {
            disposable.Dispose(); // Should not throw
        }
    }

    [Fact]
    public void OrderedDictionary_ConcurrentModification_EnumeratorHandlesGracefully()
    {
        // Arrange
        var dict = new OrderedDictionary<string, int>
        {
            ["original"] = 999
        };

        var enumerator = ((IEnumerable)dict).GetEnumerator();
        enumerator.MoveNext();

        // Act - Modify dictionary after creating enumerator
        dict["new"] = 111;

        // Assert - Should still work with current item
        var current = enumerator.Current;
        Assert.NotNull(current);
    }

    [Fact]
    public void OrderedDictionary_ExtensionMethods_ExerciseLogger()
    {
        // This tests the OrderedDictionaryExtensions.Logger property
        // Arrange
        var dict = new OrderedDictionary<string, string>();

        // Act - Access the logger (this exercises the getter)
        var logger = OrderedDictionaryExtensions.Logger;

        // Assert
        Assert.NotNull(logger);
    }
}