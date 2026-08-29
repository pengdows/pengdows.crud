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
    // CORE-012: the cache previously stored the raw connection string verbatim as its
    // dictionary key. Most providers embed credentials directly in the connection string
    // (Password=..., Pwd=..., User Id=...), so every distinct connection string ever seen by
    // the process — including rotated-out or per-tenant credentials — lived in this static
    // key set for the process lifetime. This test reaches into the cache's backing store via
    // reflection and asserts no retained key equals or contains the raw secret-bearing string.
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

    // CORE-012: the cache previously had no size bound at all — an application constructing
    // many distinct connection strings at runtime (per-tenant credentials, rotation) would
    // grow it without limit for the process lifetime. This proves old entries are evicted once
    // the bound is exceeded, matching the BoundedCache<TKey,TValue> LRU pattern used elsewhere.
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

        // The earliest-inserted entry must have been evicted, not retained forever.
        Assert.False(ConnectionStringNormalizationCache.TryGet(
            "Server=test;Database=db0;Password=secret0", out _));
    }

    private static IReadOnlyList<string> GetBackingStoreKeys()
    {
        var cacheField = typeof(ConnectionStringNormalizationCache).GetField(
            "Cache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(cacheField);
        var cache = cacheField!.GetValue(null);
        Assert.NotNull(cache);

        var mapField = cache!.GetType().GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(mapField);
        var map = mapField!.GetValue(cache);
        Assert.NotNull(map);

        var keysProperty = map!.GetType().GetProperty("Keys");
        var keys = (System.Collections.IEnumerable)keysProperty!.GetValue(map)!;
        return keys.Cast<object>().Select(k => k.ToString() ?? string.Empty).ToList();
    }

    private static int GetCapacity()
    {
        var cacheField = typeof(ConnectionStringNormalizationCache).GetField(
            "Cache", BindingFlags.NonPublic | BindingFlags.Static);
        var cache = cacheField!.GetValue(null);
        var capacityProperty = cache!.GetType().GetProperty("Capacity");
        return (int)capacityProperty!.GetValue(cache)!;
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