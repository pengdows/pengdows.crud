using System.Collections.Generic;
using System.Linq;
using pengdows.crud.collections;
using Xunit;

namespace pengdows.crud.Tests.collections;

/// <summary>
/// Tests specifically targeting small-mode / hash-mode transitions and behaviors.
/// SmallCapacity = 8, DefaultCapacity = 16.
/// </summary>
public class OrderedDictionarySmallModeTests
{
    [Fact]
    public void SmallMode_StaysInSmallModeUpTo8Items()
    {
        var dict = new OrderedDictionary<string, int>();

        // Add exactly 8 items - should stay in small mode
        for (var i = 0; i < 8; i++)
        {
            dict.Add($"key{i}", i);
        }

        Assert.Equal(8, dict.Count);

        // Verify all items accessible and in order
        var keys = dict.Keys.ToArray();
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal($"key{i}", keys[i]);
            Assert.Equal(i, dict[$"key{i}"]);
        }
    }

    [Fact]
    public void SmallMode_TransitionsToHashModeOn9thItem()
    {
        var dict = new OrderedDictionary<string, int>();

        // Add 8 items (small mode)
        for (var i = 0; i < 8; i++)
        {
            dict.Add($"key{i}", i);
        }

        // Add 9th item - should transition to hash mode
        dict.Add("key8", 8);

        Assert.Equal(9, dict.Count);

        // Verify order is preserved after transition
        var keys = dict.Keys.ToArray();
        for (var i = 0; i < 9; i++)
        {
            Assert.Equal($"key{i}", keys[i]);
            Assert.Equal(i, dict[$"key{i}"]);
        }
    }

    [Fact]
    public void SmallMode_RemoveMaintainsOrderByShifting()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("a", 1);
        dict.Add("b", 2);
        dict.Add("c", 3);
        dict.Add("d", 4);

        // Remove from middle
        dict.Remove("b");

        Assert.Equal(3, dict.Count);
        var keys = dict.Keys.ToArray();
        Assert.Equal(new[] { "a", "c", "d" }, keys);
    }

    [Fact]
    public void SmallMode_RemoveFirst_ShiftsRemaining()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("a", 1);
        dict.Add("b", 2);
        dict.Add("c", 3);

        dict.Remove("a");

        Assert.Equal(2, dict.Count);
        var keys = dict.Keys.ToArray();
        Assert.Equal(new[] { "b", "c" }, keys);
    }

    [Fact]
    public void SmallMode_RemoveLast_NoShiftNeeded()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("a", 1);
        dict.Add("b", 2);
        dict.Add("c", 3);

        dict.Remove("c");

        Assert.Equal(2, dict.Count);
        var keys = dict.Keys.ToArray();
        Assert.Equal(new[] { "a", "b" }, keys);
    }

    [Fact]
    public void SmallMode_UpdateExistingKey_DoesNotChangeOrder()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("a", 1);
        dict.Add("b", 2);
        dict.Add("c", 3);

        dict["b"] = 20;

        var pairs = dict.ToArray();
        Assert.Equal("a", pairs[0].Key);
        Assert.Equal("b", pairs[1].Key);
        Assert.Equal(20, pairs[1].Value);
        Assert.Equal("c", pairs[2].Key);
    }

    [Fact]
    public void SmallMode_TryAddDuplicate_ReturnsFalse()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("a", 1);

        var result = dict.TryAdd("a", 2);

        Assert.False(result);
        Assert.Equal(1, dict["a"]);
    }

    [Fact]
    public void SmallMode_FindValue_UsesHashPrefilter()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("key1", 100);
        dict.Add("key2", 200);

        Assert.True(dict.ContainsKey("key1"));
        Assert.True(dict.ContainsKey("key2"));
        Assert.False(dict.ContainsKey("key3"));
    }

    [Fact]
    public void SmallMode_ContainsKey_ReturnsFalseForMissing()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("a", 1);

        Assert.False(dict.ContainsKey("missing"));
    }

    [Fact]
    public void SmallMode_RemoveNonExistent_ReturnsFalse()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("a", 1);

        Assert.False(dict.Remove("missing"));
        Assert.Single(dict);
    }

    [Fact]
    public void HashMode_FreeListReuse_AfterRemove()
    {
        var dict = new OrderedDictionary<string, int>();

        // Fill past small mode threshold
        for (var i = 0; i < 10; i++)
        {
            dict.Add($"key{i}", i);
        }

        // Remove some items to create free slots
        dict.Remove("key5");
        dict.Remove("key3");

        Assert.Equal(8, dict.Count);

        // Add new items - should reuse free slots
        dict.Add("new1", 100);
        dict.Add("new2", 200);

        Assert.Equal(10, dict.Count);

        // Order should have original items (minus removed) followed by new items
        var keys = dict.Keys.ToArray();
        Assert.Equal("key0", keys[0]);
        Assert.Equal("key1", keys[1]);
        Assert.Equal("key2", keys[2]);
        Assert.Equal("key4", keys[3]);
        Assert.Equal("key6", keys[4]);
        Assert.Equal("key7", keys[5]);
        Assert.Equal("key8", keys[6]);
        Assert.Equal("key9", keys[7]);
        Assert.Equal("new1", keys[8]);
        Assert.Equal("new2", keys[9]);
    }

    [Fact]
    public void HashMode_RemoveMiddleOfChain_UnlinksCorrectly()
    {
        var dict = new OrderedDictionary<string, int>();

        // Add enough items to likely create hash collisions
        for (var i = 0; i < 20; i++)
        {
            dict.Add($"key{i}", i);
        }

        // Remove items from the middle
        dict.Remove("key10");
        dict.Remove("key5");

        Assert.Equal(18, dict.Count);
        Assert.False(dict.ContainsKey("key10"));
        Assert.False(dict.ContainsKey("key5"));

        // Remaining items should still be accessible
        Assert.True(dict.ContainsKey("key0"));
        Assert.True(dict.ContainsKey("key19"));
    }

    [Fact]
    public void TrimExcess_ShrinksBackToSmallMode()
    {
        var dict = new OrderedDictionary<string, int>();

        // Add many items to trigger hash mode
        for (var i = 0; i < 100; i++)
        {
            dict.Add($"key{i}", i);
        }

        // Remove most items
        for (var i = 8; i < 100; i++)
        {
            dict.Remove($"key{i}");
        }

        Assert.Equal(8, dict.Count);

        // Trim should shrink back to small mode
        dict.TrimExcess();

        Assert.Equal(8, dict.Count);

        // Verify items are still accessible and in order
        var keys = dict.Keys.ToArray();
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal($"key{i}", keys[i]);
        }
    }

    [Fact]
    public void TrimExcess_KeepsHashModeWhenAboveThreshold()
    {
        var dict = new OrderedDictionary<string, int>();

        // Add items to trigger hash mode
        for (var i = 0; i < 20; i++)
        {
            dict.Add($"key{i}", i);
        }

        // Remove only a few items (less than 10% threshold)
        dict.Remove("key19");

        Assert.Equal(19, dict.Count);

        // Trim should not shrink because we're above 90% threshold
        dict.TrimExcess();

        Assert.Equal(19, dict.Count);

        // All remaining items should be accessible
        for (var i = 0; i < 19; i++)
        {
            Assert.True(dict.ContainsKey($"key{i}"));
        }
    }

    [Fact]
    public void TrimExcess_EmptyAfterClear_ReleasesMemory()
    {
        var dict = new OrderedDictionary<string, int>();

        for (var i = 0; i < 100; i++)
        {
            dict.Add($"key{i}", i);
        }

        dict.Clear();
        Assert.Empty(dict);

        // TrimExcess on empty should still work
        dict.TrimExcess();
        Assert.Empty(dict);
    }

    [Fact]
    public void Clear_ReleasesArrays()
    {
        var dict = new OrderedDictionary<string, int>();

        for (var i = 0; i < 100; i++)
        {
            dict.Add($"key{i}", i);
        }

        dict.Clear();

        Assert.Empty(dict);

        // Adding after clear should work
        dict.Add("new", 1);
        Assert.Single(dict);
        Assert.Equal(1, dict["new"]);
    }

    [Fact]
    public void Constructor_WithSmallCapacity_StaysInSmallMode()
    {
        var dict = new OrderedDictionary<string, int>(4);

        dict.Add("a", 1);
        dict.Add("b", 2);
        dict.Add("c", 3);
        dict.Add("d", 4);

        Assert.Equal(4, dict.Count);

        var keys = dict.Keys.ToArray();
        Assert.Equal(new[] { "a", "b", "c", "d" }, keys);
    }

    [Fact]
    public void Constructor_WithLargeCapacity_StartsInHashMode()
    {
        var dict = new OrderedDictionary<string, int>(100);

        dict.Add("a", 1);

        Assert.Single(dict);
        Assert.Equal(1, dict["a"]);
    }

    [Fact]
    public void EnsureCapacity_SmallToSmall_GrowsEntriesOnly()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("a", 1);

        // This should just grow entries array, not switch to hash mode
        dict.EnsureCapacity(5);

        dict.Add("b", 2);
        dict.Add("c", 3);

        Assert.Equal(3, dict.Count);
        Assert.Equal(new[] { "a", "b", "c" }, dict.Keys.ToArray());
    }

    [Fact]
    public void EnsureCapacity_SmallToHash_TransitionsCorrectly()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("a", 1);
        dict.Add("b", 2);

        // This should transition to hash mode
        dict.EnsureCapacity(20);

        // Existing items should still work
        Assert.Equal(2, dict.Count);
        Assert.Equal(1, dict["a"]);
        Assert.Equal(2, dict["b"]);

        // Can add more items
        dict.Add("c", 3);
        Assert.Equal(3, dict.Count);
    }

    [Fact]
    public void Enumeration_SmallMode_RespectsInsertionOrder()
    {
        var dict = new OrderedDictionary<string, int>();
        dict.Add("z", 1);
        dict.Add("a", 2);
        dict.Add("m", 3);

        var keys = dict.Keys.ToArray();
        Assert.Equal(new[] { "z", "a", "m" }, keys);
    }

    [Fact]
    public void Enumeration_AfterTransition_RespectsInsertionOrder()
    {
        var dict = new OrderedDictionary<string, int>();

        // Add in specific order
        dict.Add("z", 1);
        dict.Add("a", 2);
        dict.Add("m", 3);
        dict.Add("b", 4);
        dict.Add("y", 5);
        dict.Add("c", 6);
        dict.Add("x", 7);
        dict.Add("d", 8);

        // This should trigger transition to hash mode
        dict.Add("e", 9);

        var keys = dict.Keys.ToArray();
        Assert.Equal(new[] { "z", "a", "m", "b", "y", "c", "x", "d", "e" }, keys);
    }

    [Fact]
    public void SmallMode_AllOperations_WorkCorrectly()
    {
        var dict = new OrderedDictionary<string, int>();

        // Add
        dict.Add("a", 1);
        dict.Add("b", 2);
        Assert.Equal(2, dict.Count);

        // ContainsKey
        Assert.True(dict.ContainsKey("a"));
        Assert.False(dict.ContainsKey("c"));

        // TryGetValue
        Assert.True(dict.TryGetValue("a", out var val));
        Assert.Equal(1, val);
        Assert.False(dict.TryGetValue("c", out _));

        // Indexer get/set
        Assert.Equal(1, dict["a"]);
        dict["a"] = 10;
        Assert.Equal(10, dict["a"]);

        // TryAdd
        Assert.False(dict.TryAdd("a", 20));
        Assert.True(dict.TryAdd("c", 3));

        // Remove
        Assert.True(dict.Remove("b"));
        Assert.Equal(2, dict.Count);

        // CopyTo
        var arr = new KeyValuePair<string, int>[2];
        dict.CopyTo(arr, 0);
        Assert.Equal("a", arr[0].Key);
        Assert.Equal("c", arr[1].Key);

        // Clear
        dict.Clear();
        Assert.Empty(dict);
    }

    [Fact]
    public void HashMode_RemoveFromBucketHead_UpdatesBucketPointer()
    {
        var dict = new OrderedDictionary<string, int>();

        // Add enough items to be in hash mode
        for (var i = 0; i < 10; i++)
        {
            dict.Add($"key{i}", i);
        }

        // Remove first added item
        dict.Remove("key0");

        Assert.Equal(9, dict.Count);
        Assert.False(dict.ContainsKey("key0"));

        // Other items should still be accessible
        for (var i = 1; i < 10; i++)
        {
            Assert.True(dict.ContainsKey($"key{i}"));
        }
    }

    [Fact]
    public void HashMode_RemoveNotInChain_ReturnsFalse()
    {
        var dict = new OrderedDictionary<string, int>();

        for (var i = 0; i < 10; i++)
        {
            dict.Add($"key{i}", i);
        }

        Assert.False(dict.Remove("notfound"));
        Assert.Equal(10, dict.Count);
    }
}
