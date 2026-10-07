using System;
using System.Linq;
using pengdows.crud.collections;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PERF-031: every container's parameter dictionary allocated room for 8 entries (216 bytes) on its first
/// add, though most gateway statements carry one to four parameters, and a clone, which knows its count,
/// couldn't ask for less. Small mode now starts at 4 entries and doubles to 8 before switching to
/// hashing; a requested capacity of 8 or fewer is allocated exactly.
/// </summary>
[Collection("AllocationSerial")]
public sealed class OrderedDictionarySmallAllocationTests
{
    // An entry is a hash code, a chain link, a key reference and a value reference.
    private static readonly int EntryBytes = 8 + 2 * IntPtr.Size;
    private static readonly int ArrayHeaderBytes = 3 * IntPtr.Size;

    private static readonly string[] Keys = Enumerable.Range(0, 40).Select(i => "p" + i).ToArray();
    private static readonly object Value = new();

    private static long Measure(Action<OrderedDictionary<string, object>> fill, Func<OrderedDictionary<string, object>> create) =>
        AllocationMeasurement.Lowest(() =>
        {
            var dictionary = create();
            var before = GC.GetAllocatedBytesForCurrentThread();
            fill(dictionary);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });

    [Fact]
    public void FirstAdd_AllocatesRoomForFourEntries()
    {
        var bytes = Measure(d => d.Add(Keys[0], Value), () => new OrderedDictionary<string, object>());

        Assert.Equal(ArrayHeaderBytes + 4 * EntryBytes, bytes);
    }

    [Fact]
    public void EnsureCapacity_OfAFewEntries_AllocatesExactlyThatMany()
    {
        var bytes = Measure(d =>
        {
            d.EnsureCapacity(3);
            d.Add(Keys[0], Value);
            d.Add(Keys[1], Value);
            d.Add(Keys[2], Value);
        }, () => new OrderedDictionary<string, object>());

        Assert.Equal(ArrayHeaderBytes + 3 * EntryBytes, bytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(40)]
    public void GrowingThroughSmallModeIntoHashing_KeepsOrderAndLookups(int count)
    {
        var dictionary = new OrderedDictionary<string, object>();
        for (var i = 0; i < count; i++)
        {
            dictionary.Add(Keys[i], i);
        }

        Assert.Equal(Keys.Take(count), dictionary.Keys);
        for (var i = 0; i < count; i++)
        {
            Assert.Equal(i, dictionary[Keys[i]]);
        }

        Assert.Throws<ArgumentException>(() => dictionary.Add(Keys[0], -1));
        Assert.True(dictionary.Remove(Keys[count - 1]));
        Assert.Equal(Keys.Take(count - 1), dictionary.Keys);
    }

    [Fact]
    public void EnsureCapacity_ThenGrowingPastIt_KeepsOrder()
    {
        var dictionary = new OrderedDictionary<string, object>();
        dictionary.EnsureCapacity(2);
        for (var i = 0; i < 12; i++)
        {
            dictionary.Add(Keys[i], i);
        }

        Assert.Equal(Keys.Take(12), dictionary.Keys);
        Assert.Equal(11, dictionary[Keys[11]]);
    }
}
