using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

[Collection("NormalizationCacheSerial")]
public class ConnectionStringNormalizationCacheTests
{
    // Keys used to be the raw connection string, so every credential ever seen stayed in memory
    // for the process lifetime. Reflect into the backing store and check no key contains it.
    [Fact]
    public void TryAdd_DoesNotRetainRawConnectionStringOrCredentialsAsCacheKey()
    {
        ConnectionStringNormalizationCache.ClearForTests();

        const string connectionString = "Server=test;Database=foo;User Id=app;Password=super-secret-value";
        ConnectionStringNormalizationCache.TryAdd(connectionString, new Dictionary<string, string>());

        var backingKeys = GetBackingStoreKeys();

        Assert.DoesNotContain(connectionString, backingKeys);
        Assert.DoesNotContain(backingKeys, k => k.Contains("super-secret-value", StringComparison.Ordinal));
        Assert.DoesNotContain(backingKeys, k => k.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    // The cache used to be unbounded; per-tenant or rotated credentials grew it forever.
    [Fact]
    public void Cache_IsBounded_EvictsOldEntriesBeyondCapacity()
    {
        ConnectionStringNormalizationCache.ClearForTests();
        var capacity = GetCapacity();

        for (var i = 0; i < capacity + 50; i++)
        {
            ConnectionStringNormalizationCache.TryAdd(
                $"Server=test;Database=db{i};Password=secret{i}",
                new Dictionary<string, string>());
        }

        Assert.True(ConnectionStringNormalizationCache.Count <= capacity);
        Assert.False(ConnectionStringNormalizationCache.TryGet(
            "Server=test;Database=db0;Password=secret0", out _));
    }

    private static IReadOnlyList<string> GetBackingStoreKeys()
    {
        var cache = typeof(ConnectionStringNormalizationCache)
            .GetField("Cache", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        Assert.NotNull(cache);
        var map = cache!.GetType().GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(cache);
        Assert.NotNull(map);
        var keys = (System.Collections.IEnumerable)map!.GetType().GetProperty("Keys")!.GetValue(map)!;
        return keys.Cast<object>().Select(k => k.ToString() ?? string.Empty).ToList();
    }

    private static int GetCapacity()
    {
        var cache = typeof(ConnectionStringNormalizationCache)
            .GetField("Cache", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        Assert.NotNull(cache);
        return (int)cache!.GetType().GetProperty("Capacity")!.GetValue(cache)!;
    }

    [Fact]
    public void TryAdd_ReturnsCachedDictionary()
    {
        ConnectionStringNormalizationCache.ClearForTests();
        var normalized = new Dictionary<string, string> { ["Server"] = "test" };

        Assert.True(ConnectionStringNormalizationCache.TryAdd("Server=test", normalized));
        Assert.True(ConnectionStringNormalizationCache.TryGet("Server=test", out var cached));
        Assert.Same(normalized, cached);
        Assert.False(ConnectionStringNormalizationCache.TryAdd("Server=test", new Dictionary<string, string>()));
        Assert.Equal(1, ConnectionStringNormalizationCache.Count);
    }

    [Fact]
    public void ClearForTests_ResetsCache()
    {
        ConnectionStringNormalizationCache.ClearForTests();
        ConnectionStringNormalizationCache.TryAdd("Server=temp", new Dictionary<string, string>());
        ConnectionStringNormalizationCache.ClearForTests();

        Assert.False(ConnectionStringNormalizationCache.TryGet("Server=temp", out _));
        Assert.Equal(0, ConnectionStringNormalizationCache.Count);
    }
}