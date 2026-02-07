using System.Collections.Generic;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public class ConnectionStringNormalizationCacheTests
{
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
