using System.Collections.Generic;
using System.Reflection;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextNormalizationCacheTests
{
    [Fact]
    public void TryBuildNormalizedConnectionMap_CachesResult()
    {
        ConnectionStringNormalizationCache.ClearForTests();

        var method = typeof(DatabaseContext).GetMethod(
            "TryBuildNormalizedConnectionMap",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        const string connectionString = "Server=test;Database=foo;User Id=app;Password=secret";
        var args = new object?[]
        {
            connectionString,
            null,
            null,
            null,
            string.Empty,
            null
        };

        var first = (bool)method!.Invoke(null, args)!;
        Assert.True(first);
        var firstMap = (Dictionary<string, string>)args[^1]!;
        Assert.True(ConnectionStringNormalizationCache.TryGet(connectionString, out var cachedFirst));
        Assert.Same(firstMap, cachedFirst);

        var secondArgs = new object?[]
        {
            connectionString,
            null,
            null,
            null,
            string.Empty,
            null
        };

        var second = (bool)method.Invoke(null, secondArgs)!;
        Assert.True(second);
        var secondMap = (Dictionary<string, string>)secondArgs[^1]!;

        Assert.Same(firstMap, secondMap);
        Assert.Equal(firstMap, cachedFirst);
        Assert.Equal(1, ConnectionStringNormalizationCache.Count);
    }
}
