#region

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using pengdows.crud.collections;
using Xunit;

#endregion

namespace pengdows.crud.Tests.collections;

public class OrderedDictionaryTests
{
    [Fact]
    public void Constructor_WithDefaultCapacity_CreatesEmptyDictionary()
    {
        var dict = new OrderedDictionary<string, int>();

        Assert.Empty(dict);
        Assert.False(dict.IsReadOnly);
    }

    [Fact]
    public void Constructor_WithCapacity_CreatesEmptyDictionary()
    {
        var dict = new OrderedDictionary<string, int>(100);

        Assert.Empty(dict);
    }

    [Fact]
    public void Constructor_WithNegativeCapacity_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderedDictionary<string, int>(-1));
    }

    [Fact]
    public void Constructor_WithLargeCapacity_CreatesEmptyDictionary()
    {
        var dict = new OrderedDictionary<string, int>(100_000);
        Assert.Empty(dict);
    }

    [Fact]
    public void Add_SingleItem_SuccessfullyAdds()
    {
        var dict = new OrderedDictionary<string, int>();

        dict.Add("key1", 100);

        Assert.Single(dict);
        Assert.Equal(100, dict["key1"]);
    }

    [Fact]
    public void Add_DuplicateKey_ThrowsArgumentException()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);

        Assert.Throws<ArgumentException>(() => dict.Add("key1", 200));
    }

    [Fact]
    public void Add_NullKey_ThrowsArgumentNullException()
    {
        var dict = new OrderedDictionary<string, int>();

        Assert.Throws<ArgumentNullException>(() => dict.Add(null!, 100));
    }

    [Fact]
    public void Indexer_Get_ReturnsCorrectValue()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);

        Assert.Equal(100, dict["key1"]);
    }

    [Fact]
    public void Indexer_Get_NonExistentKey_ThrowsKeyNotFoundException()
    {
        var dict = new OrderedDictionary<string, int>();

        Assert.Throws<KeyNotFoundException>(() => dict["nonexistent"]);
    }

    [Fact]
    public void Indexer_Set_UpdatesExistingValue()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);

        dict["key1"] = 200;

        Assert.Equal(200, dict["key1"]);
    }

    [Fact]
    public void Indexer_Set_AddsNewKey()
    {
        var dict = new OrderedDictionary<string, int>();

        dict["key1"] = 100;

        Assert.Single(dict);
        Assert.Equal(100, dict["key1"]);
    }

    [Fact]
    public void ContainsKey_ExistingKey_ReturnsTrue()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);

        Assert.True(dict.ContainsKey("key1"));
    }

    [Fact]
    public void ContainsKey_NonExistentKey_ReturnsFalse()
    {
        var dict = new OrderedDictionary<string, int>();

        Assert.False(dict.ContainsKey("nonexistent"));
    }

    [Fact]
    public void ContainsKey_NullKey_ThrowsArgumentNullException()
    {
        var dict = new OrderedDictionary<string, int>();

        Assert.Throws<ArgumentNullException>(() => dict.ContainsKey(null!));
    }

    [Fact]
    public void TryGetValue_ExistingKey_ReturnsTrueWithValue()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);

        var result = dict.TryGetValue("key1", out var value);

        Assert.True(result);
        Assert.Equal(100, value);
    }

    [Fact]
    public void TryGetValue_NonExistentKey_ReturnsFalseWithDefault()
    {
        var dict = new OrderedDictionary<string, int>();

        var result = dict.TryGetValue("nonexistent", out var value);

        Assert.False(result);
        Assert.Equal(default, value);
    }

    [Fact]
    public void TryAdd_NewKey_ReturnsTrueAndAdds()
    {
        var dict = new OrderedDictionary<string, int>();

        var result = dict.TryAdd("key1", 100);

        Assert.True(result);
        Assert.Single(dict);
        Assert.Equal(100, dict["key1"]);
    }

    [Fact]
    public void TryAdd_ExistingKey_ReturnsFalseAndDoesNotModify()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);

        var result = dict.TryAdd("key1", 200);

        Assert.False(result);
        Assert.Equal(100, dict["key1"]);
    }

    [Fact]
    public void Remove_ExistingKey_ReturnsTrueAndRemoves()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);

        var result = dict.Remove("key1");

        Assert.True(result);
        Assert.Empty(dict);
        Assert.False(dict.ContainsKey("key1"));
    }

    [Fact]
    public void Remove_NonExistentKey_ReturnsFalse()
    {
        var dict = new OrderedDictionary<string, int>();

        var result = dict.Remove("nonexistent");

        Assert.False(result);
    }

    [Fact]
    public void Remove_WithOutParameter_ExistingKey_ReturnsTrueWithValue()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);

        var result = dict.Remove("key1", out var value);

        Assert.True(result);
        Assert.Equal(100, value);
        Assert.Empty(dict);
    }

    [Fact]
    public void Remove_WithOutParameter_NonExistentKey_ReturnsFalseWithDefault()
    {
        var dict = new OrderedDictionary<string, int>();

        var result = dict.Remove("nonexistent", out var value);

        Assert.False(result);
        Assert.Equal(default, value);
    }

    [Fact]
    public void Clear_RemovesAllItems()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);
        dict.Add("key2", 200);

        dict.Clear();

        Assert.Empty(dict);
        Assert.False(dict.ContainsKey("key1"));
        Assert.False(dict.ContainsKey("key2"));
    }

    [Fact]
    public void InsertionOrder_MaintainedDuringEnumeration()
    {
        var dict = new OrderedDictionary<string, int>();
        var keys = new[] { "third", "first", "second", "fourth" };
        var values = new[] { 3, 1, 2, 4 };

        for (var i = 0; i < keys.Length; i++)
        {
            dict.Add(keys[i], values[i]);
        }

        var actualKeys = dict.Keys.ToArray();
        var actualValues = dict.Values.ToArray();

        Assert.Equal(keys, actualKeys);
        Assert.Equal(values, actualValues);
    }

    [Fact]
    public void InsertionOrder_MaintainedAfterRemoval()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("first", 1);
        dict.Add("second", 2);
        dict.Add("third", 3);
        dict.Add("fourth", 4);

        dict.Remove("second");

        var actualKeys = dict.Keys.ToArray();
        var expectedKeys = new[] { "first", "third", "fourth" };

        Assert.Equal(expectedKeys, actualKeys);
    }

    [Fact]
    public void InsertionOrder_MaintainedAfterUpdate()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("first", 1);
        dict.Add("second", 2);
        dict.Add("third", 3);

        dict["second"] = 20;

        var actualPairs = dict.ToArray();
        var expectedPairs = new[]
        {
            new KeyValuePair<string, int>("first", 1),
            new KeyValuePair<string, int>("second", 20),
            new KeyValuePair<string, int>("third", 3)
        };

        Assert.Equal(expectedPairs, actualPairs);
    }

    [Fact]
    public void Enumeration_RespectsInsertionOrder()
    {
        var dict = new OrderedDictionary<string, int>();
        var expected = new[]
        {
            new KeyValuePair<string, int>("zebra", 26),
            new KeyValuePair<string, int>("alpha", 1),
            new KeyValuePair<string, int>("beta", 2)
        };

        foreach (var kvp in expected)
        {
            dict.Add(kvp.Key, kvp.Value);
        }

        var actual = dict.ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Contains_KeyValuePair_ExistingPair_ReturnsTrue()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);

        var result = dict.Contains(new KeyValuePair<string, int>("key1", 100));

        Assert.True(result);
    }

    [Fact]
    public void Contains_KeyValuePair_ExistingKeyDifferentValue_ReturnsFalse()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);

        var result = dict.Contains(new KeyValuePair<string, int>("key1", 200));

        Assert.False(result);
    }

    [Fact]
    public void Contains_KeyValuePair_NonExistentKey_ReturnsFalse()
    {
        var dict = new OrderedDictionary<string, int>();

        var result = dict.Contains(new KeyValuePair<string, int>("key1", 100));

        Assert.False(result);
    }

    [Fact]
    public void Remove_KeyValuePair_ExistingPair_ReturnsTrueAndRemoves()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);

        var result = dict.Remove(new KeyValuePair<string, int>("key1", 100));

        Assert.True(result);
        Assert.Empty(dict);
    }

    [Fact]
    public void Remove_KeyValuePair_ExistingKeyDifferentValue_ReturnsFalse()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);

        var result = dict.Remove(new KeyValuePair<string, int>("key1", 200));

        Assert.False(result);
        Assert.Single(dict);
        Assert.Equal(100, dict["key1"]);
    }

    [Fact]
    public void CopyTo_CopiesInInsertionOrder()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("third", 3);
        dict.Add("first", 1);
        dict.Add("second", 2);

        var array = new KeyValuePair<string, int>[3];
        dict.CopyTo(array, 0);

        var expected = new[]
        {
            new KeyValuePair<string, int>("third", 3),
            new KeyValuePair<string, int>("first", 1),
            new KeyValuePair<string, int>("second", 2)
        };

        Assert.Equal(expected, array);
    }

    [Fact]
    public void CopyTo_WithOffset_CopiesCorrectly()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 1);
        dict.Add("key2", 2);

        var array = new KeyValuePair<string, int>[4];
        dict.CopyTo(array, 1);

        Assert.Equal(default, array[0]);
        Assert.Equal(new KeyValuePair<string, int>("key1", 1), array[1]);
        Assert.Equal(new KeyValuePair<string, int>("key2", 2), array[2]);
        Assert.Equal(default, array[3]);
    }

    [Fact]
    public void CopyTo_NullArray_ThrowsArgumentNullException()
    {
        var dict = new OrderedDictionary<string, int>();

        Assert.Throws<ArgumentNullException>(() => dict.CopyTo(null!, 0));
    }

    [Fact]
    public void CopyTo_NegativeArrayIndex_ThrowsArgumentOutOfRangeException()
    {
        var dict = new OrderedDictionary<string, int>();
        var array = new KeyValuePair<string, int>[1];

        Assert.Throws<ArgumentOutOfRangeException>(() => dict.CopyTo(array, -1));
    }

    [Fact]
    public void CopyTo_InsufficientSpace_ThrowsArgumentOutOfRangeException()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 1);
        dict.Add("key2", 2);

        var array = new KeyValuePair<string, int>[2];

        Assert.Throws<ArgumentOutOfRangeException>(() => dict.CopyTo(array, 1));
    }

    [Fact]
    public void EnsureCapacity_IncreasesCapacity()
    {
        var dict = new OrderedDictionary<string, int>();

        dict.EnsureCapacity(1000);

        for (var i = 0; i < 500; i++)
        {
            dict.Add($"key{i}", i);
        }

        Assert.Equal(500, dict.Count);
    }

    [Fact]
    public void EnsureCapacity_NegativeCapacity_ThrowsArgumentOutOfRangeException()
    {
        var dict = new OrderedDictionary<string, int>();

        Assert.Throws<ArgumentOutOfRangeException>(() => dict.EnsureCapacity(-1));
    }

    [Fact]
    public void TrimExcess_ReducesCapacityWhenAppropriate()
    {
        var dict = new OrderedDictionary<string, int>(1000);
        dict.Add("key1", 1);

        dict.TrimExcess();

        Assert.Single(dict);
        Assert.Equal(1, dict["key1"]);
    }

    [Fact]
    public void TrimExcess_EmptyDictionary_ResetsToDefault()
    {
        var dict = new OrderedDictionary<string, int>(1000);

        dict.TrimExcess();

        Assert.Empty(dict);
    }

    [Fact]
    public void Keys_ReturnsInInsertionOrder()
    {
        var dict = new OrderedDictionary<string, int>();
        var expectedKeys = new[] { "zebra", "alpha", "beta" };

        foreach (var key in expectedKeys)
        {
            dict.Add(key, key.Length);
        }

        Assert.Equal(expectedKeys, dict.Keys.ToArray());
    }

    [Fact]
    public void Values_ReturnsInInsertionOrder()
    {
        var dict = new OrderedDictionary<string, int>();
        var keys = new[] { "zebra", "alpha", "beta" };
        var expectedValues = new[] { 5, 5, 4 };

        for (var i = 0; i < keys.Length; i++)
        {
            dict.Add(keys[i], expectedValues[i]);
        }

        Assert.Equal(expectedValues, dict.Values.ToArray());
    }

    [Fact]
    public void KeysCollection_Contains_WorksCorrectly()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 1);

        Assert.True(dict.Keys.Contains("key1"));
        Assert.False(dict.Keys.Contains("key2"));
    }

    [Fact]
    public void KeysCollection_IsReadOnly()
    {
        var dict = new OrderedDictionary<string, int>();

        Assert.True(dict.Keys.IsReadOnly);
    }

    [Fact]
    public void ValuesCollection_Contains_WorksCorrectly()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);

        Assert.True(dict.Values.Contains(100));
        Assert.False(dict.Values.Contains(200));
    }

    [Fact]
    public void ValuesCollection_IsReadOnly()
    {
        var dict = new OrderedDictionary<string, int>();

        Assert.True(dict.Values.IsReadOnly);
    }

    [Fact]
    public void LargeCapacity_WorksCorrectly()
    {
        var dict = new OrderedDictionary<string, int>();
        const int itemCount = 10000;

        for (var i = 0; i < itemCount; i++)
        {
            dict.Add($"key{i}", i);
        }

        Assert.Equal(itemCount, dict.Count);

        for (var i = 0; i < itemCount; i++)
        {
            Assert.Equal(i, dict[$"key{i}"]);
        }

        var actualKeys = dict.Keys.Take(5).ToArray();
        var expectedKeys = new[] { "key0", "key1", "key2", "key3", "key4" };
        Assert.Equal(expectedKeys, actualKeys);
    }

    [Fact]
    public void LargeInserts_DoNotThrow_AndMaintainOrder()
    {
        var dict = new OrderedDictionary<string, int>();
        for (var i = 0; i < 70_000; i++)
        {
            dict.Add($"key{i}", i);
        }

        Assert.Equal(70_000, dict.Count);
        Assert.Equal(0, dict["key0"]);
        Assert.Equal(69_999, dict["key69999"]);
        Assert.Equal(new[] { "key0", "key1", "key2", "key3", "key4" }, dict.Keys.Take(5).ToArray());
    }

    [Fact]
    public void CustomComparer_WorksCorrectly()
    {
        var dict = new OrderedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        dict.Add("KEY1", 100);

        Assert.True(dict.ContainsKey("key1"));
        Assert.Equal(100, dict["key1"]);
        Assert.Equal(100, dict["KEY1"]);
    }

    [Fact]
    public void ExplicitInterfaceImplementations_WorkCorrectly()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);

        var genericEnumerable = (IEnumerable<KeyValuePair<string, int>>)dict;
        var enumerator = genericEnumerable.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.Equal(new KeyValuePair<string, int>("key1", 100), enumerator.Current);
        enumerator.Dispose();

        var nonGenericEnumerable = (IEnumerable)dict;
        var nonGenericEnumerator = nonGenericEnumerable.GetEnumerator();
        Assert.True(nonGenericEnumerator.MoveNext());
        Assert.IsType<KeyValuePair<string, int>>(nonGenericEnumerator.Current);

        if (nonGenericEnumerator is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    [Fact]
    public void IsPrime_UtilityMethod_WorksCorrectly()
    {
        var type = typeof(OrderedDictionary<string, int>);
        var method = type.GetMethod("IsPrime", BindingFlags.NonPublic | BindingFlags.Static);

        if (method != null)
        {
            var result2 = (bool)method.Invoke(null, new object[] { 2 })!;
            var result3 = (bool)method.Invoke(null, new object[] { 3 })!;
            var result4 = (bool)method.Invoke(null, new object[] { 4 })!;
            var result17 = (bool)method.Invoke(null, new object[] { 17 })!;

            Assert.True(result2);
            Assert.True(result3);
            Assert.False(result4);
            Assert.True(result17);
        }
    }

    [Fact]
    public void EnumeratorModification_ThrowsInvalidOperationException()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 1);
        dict.Add("key2", 2);

        var enumerator = dict.GetEnumerator();
        enumerator.MoveNext();

        dict.Add("key3", 3);

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void KeyEnumeratorModification_ThrowsInvalidOperationException()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 1);

        var enumerator = dict.Keys.GetEnumerator();
        enumerator.MoveNext();

        dict.Add("key2", 2);

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void ValueEnumeratorModification_ThrowsInvalidOperationException()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 1);

        var enumerator = dict.Values.GetEnumerator();
        enumerator.MoveNext();

        dict.Add("key2", 2);

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void StressTest_AddRemoveMultipleItems()
    {
        var dict = new OrderedDictionary<string, int>();
        const int iterations = 1000;

        for (var i = 0; i < iterations; i++)
        {
            dict.Add($"key{i}", i);
        }

        for (var i = 0; i < iterations; i += 2)
        {
            dict.Remove($"key{i}");
        }

        Assert.Equal(iterations / 2, dict.Count);

        var expectedKeys = Enumerable.Range(0, iterations)
            .Where(i => i % 2 == 1)
            .Select(i => $"key{i}")
            .ToArray();

        Assert.Equal(expectedKeys, dict.Keys.ToArray());
    }

    [Fact]
    public void Resize_MaintainsInsertionOrder()
    {
        var dict = new OrderedDictionary<string, int>(4);
        var keys = new[] { "a", "b", "c", "d", "e", "f", "g", "h" };

        for (var i = 0; i < keys.Length; i++)
        {
            dict.Add(keys[i], i);
        }

        Assert.Equal(keys, dict.Keys.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void VariousCapacities_WorkCorrectly(int capacity)
    {
        var dict = new OrderedDictionary<string, int>(capacity);

        dict.Add("test", 42);

        Assert.Single(dict);
        Assert.Equal(42, dict["test"]);
    }

    [Fact]
    public void GenericInterface_IDictionary_WorksCorrectly()
    {
        IDictionary<string, int> dict = new OrderedDictionary<string, int>();

        dict.Add("key1", 100);
        dict["key2"] = 200;

        Assert.Equal(2, dict.Count);
        Assert.True(dict.ContainsKey("key1"));
        Assert.Equal(100, dict["key1"]);
    }

    [Fact]
    public void GenericInterface_IReadOnlyDictionary_WorksCorrectly()
    {
        IReadOnlyDictionary<string, int> dict = new OrderedDictionary<string, int>();
        ((IDictionary<string, int>)dict).Add("key1", 100);

        Assert.Single(dict);
        Assert.True(dict.ContainsKey("key1"));
        Assert.Equal(100, dict["key1"]);
        Assert.Single(dict.Keys);
        Assert.Single(dict.Values);
    }

    [Fact]
    public void NonGenericInterface_IEnumerable_WorksCorrectly()
    {
        IEnumerable dict = new OrderedDictionary<string, int>();
        ((IDictionary<string, int>)dict).Add("key1", 100);

        var items = new List<object>();
        foreach (var item in dict)
        {
            items.Add(item);
        }

        Assert.Single(items);
        Assert.IsType<KeyValuePair<string, int>>(items[0]);
    }

    [Fact]
    public void KeysCollection_CopyTo_WorksCorrectly()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("first", 1);
        dict.Add("second", 2);

        var array = new string[2];
        dict.Keys.CopyTo(array, 0);

        Assert.Equal("first", array[0]);
        Assert.Equal("second", array[1]);
    }

    [Fact]
    public void KeysCollection_CopyTo_NullArray_ThrowsArgumentNullException()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 1);

        Assert.Throws<ArgumentNullException>(() => dict.Keys.CopyTo(null!, 0));
    }

    [Fact]
    public void ValuesCollection_CopyTo_WorksCorrectly()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("first", 100);
        dict.Add("second", 200);

        var array = new int[2];
        dict.Values.CopyTo(array, 0);

        Assert.Equal(100, array[0]);
        Assert.Equal(200, array[1]);
    }

    [Fact]
    public void ValuesCollection_CopyTo_NullArray_ThrowsArgumentNullException()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 1);

        Assert.Throws<ArgumentNullException>(() => dict.Values.CopyTo(null!, 0));
    }

    [Fact]
    public void KeysCollection_Add_ThrowsNotSupportedException()
    {
        var dict = new OrderedDictionary<string, int>();

        Assert.Throws<NotSupportedException>(() => dict.Keys.Add("key"));
    }

    [Fact]
    public void KeysCollection_Remove_ThrowsNotSupportedException()
    {
        var dict = new OrderedDictionary<string, int>();

        Assert.Throws<NotSupportedException>(() => dict.Keys.Remove("key"));
    }

    [Fact]
    public void KeysCollection_Clear_ThrowsNotSupportedException()
    {
        var dict = new OrderedDictionary<string, int>();

        Assert.Throws<NotSupportedException>(() => dict.Keys.Clear());
    }

    [Fact]
    public void ValuesCollection_Add_ThrowsNotSupportedException()
    {
        var dict = new OrderedDictionary<string, int>();

        Assert.Throws<NotSupportedException>(() => dict.Values.Add(1));
    }

    [Fact]
    public void ValuesCollection_Remove_ThrowsNotSupportedException()
    {
        var dict = new OrderedDictionary<string, int>();

        Assert.Throws<NotSupportedException>(() => dict.Values.Remove(1));
    }

    [Fact]
    public void ValuesCollection_Clear_ThrowsNotSupportedException()
    {
        var dict = new OrderedDictionary<string, int>();

        Assert.Throws<NotSupportedException>(() => dict.Values.Clear());
    }

    [Fact]
    public void HashCollisions_HandledCorrectly()
    {
        var dict = new OrderedDictionary<int, string>();

        var keys = new[] { 1, 17, 33, 49 };

        foreach (var key in keys)
        {
            dict.Add(key, $"value{key}");
        }

        foreach (var key in keys)
        {
            Assert.True(dict.ContainsKey(key));
            Assert.Equal($"value{key}", dict[key]);
        }

        Assert.Equal(keys.Length, dict.Count);
    }

    [Fact]
    public void FrequentResizing_MaintainsIntegrity()
    {
        var dict = new OrderedDictionary<string, int>(2);
        var expectedPairs = new List<KeyValuePair<string, int>>();

        for (var i = 0; i < 100; i++)
        {
            var key = $"key{i}";
            dict.Add(key, i);
            expectedPairs.Add(new KeyValuePair<string, int>(key, i));
        }

        Assert.Equal(expectedPairs, dict.ToArray());
    }

    [Fact]
    public void FreeListReuse_WorksCorrectly()
    {
        var dict = new OrderedDictionary<string, int>();

        dict.Add("key1", 1);
        dict.Add("key2", 2);
        dict.Add("key3", 3);

        dict.Remove("key2");

        dict.Add("key4", 4);

        var expectedOrder = new[] { "key1", "key3", "key4" };
        Assert.Equal(expectedOrder, dict.Keys.ToArray());
    }

    [Fact]
    public void Enumerator_BasicIteration_WorksCorrectly()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 1);
        dict.Add("key2", 2);

        var enumerator = dict.GetEnumerator();

        Assert.True(enumerator.MoveNext());
        Assert.Equal(new KeyValuePair<string, int>("key1", 1), enumerator.Current);

        Assert.True(enumerator.MoveNext());
        Assert.Equal(new KeyValuePair<string, int>("key2", 2), enumerator.Current);

        Assert.False(enumerator.MoveNext());

        enumerator.Dispose();
    }

    [Fact]
    public void KeyEnumerator_BasicIteration_WorksCorrectly()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 1);
        dict.Add("key2", 2);

        var enumerator = dict.Keys.GetEnumerator();

        Assert.True(enumerator.MoveNext());
        Assert.Equal("key1", enumerator.Current);

        Assert.True(enumerator.MoveNext());
        Assert.Equal("key2", enumerator.Current);

        Assert.False(enumerator.MoveNext());

        enumerator.Dispose();
    }

    [Fact]
    public void ValueEnumerator_BasicIteration_WorksCorrectly()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);
        dict.Add("key2", 200);

        var enumerator = dict.Values.GetEnumerator();

        Assert.True(enumerator.MoveNext());
        Assert.Equal(100, enumerator.Current);

        Assert.True(enumerator.MoveNext());
        Assert.Equal(200, enumerator.Current);

        Assert.False(enumerator.MoveNext());

        enumerator.Dispose();
    }

    [Fact]
    public void LargeCapacity_WorksAtHighCounts()
    {
        var dict = new OrderedDictionary<string, int>();
        const int testCount = 5000;

        for (var i = 0; i < testCount; i++)
        {
            dict.Add($"key{i}", i);
        }

        Assert.Equal(testCount, dict.Count);
        Assert.Equal(0, dict["key0"]);
        Assert.Equal(testCount - 1, dict[$"key{testCount - 1}"]);
    }
}