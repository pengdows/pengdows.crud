using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public class BoundedCacheTests
{
    [Fact]
    public void GetOrAdd_ReturnsSameInstance_ForSameKey()
    {
        var cache = new BoundedCache<int, string>(2);
        var first = cache.GetOrAdd(1, _ => "a");
        var second = cache.GetOrAdd(1, _ => "b");
        Assert.Same(first, second);
    }

    [Fact]
    public void TryGet_ReturnsFalse_ForMissingKey()
    {
        var cache = new BoundedCache<int, string>(2);
        Assert.False(cache.TryGet(1, out _));
    }

    [Fact]
    public void EvictsOldest_WhenCapacityExceeded()
    {
        var cache = new BoundedCache<int, string>(2);
        cache.GetOrAdd(1, _ => "a");
        cache.GetOrAdd(2, _ => "b");
        cache.GetOrAdd(3, _ => "c");
        Assert.False(cache.TryGet(1, out _));
        Assert.True(cache.TryGet(2, out _));
        Assert.True(cache.TryGet(3, out _));
    }
}
