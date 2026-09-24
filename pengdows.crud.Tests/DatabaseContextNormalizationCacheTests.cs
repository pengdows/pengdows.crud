using System.Collections.Generic;
using System.Reflection;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

[Collection("NormalizationCacheSerial")]
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

    // The cached map depends on the read-only key/value, application name and suffix, so the cache
    // key must too; otherwise the same connection string built with different parameters gets the
    // first caller's map back.
    [Fact]
    public void TryBuildNormalizedConnectionMap_DifferentParameters_DoNotShareCachedMap()
    {
        ConnectionStringNormalizationCache.ClearForTests();
        var method = typeof(DatabaseContext).GetMethod("TryBuildNormalizedConnectionMap",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        const string connectionString = "Server=test;ApplicationIntent=ReadOnly";

        var plainArgs = new object?[] { connectionString, null, null, null, string.Empty, null };
        Assert.True((bool)method.Invoke(null, plainArgs)!);
        var plainMap = (Dictionary<string, string>)plainArgs[^1]!;

        var readOnlyArgs = new object?[] { connectionString, "ApplicationIntent", "ReadOnly", null, string.Empty, null };
        Assert.True((bool)method.Invoke(null, readOnlyArgs)!);
        var readOnlyMap = (Dictionary<string, string>)readOnlyArgs[^1]!;

        Assert.True(plainMap.ContainsKey("ApplicationIntent"));
        Assert.False(readOnlyMap.ContainsKey("ApplicationIntent"));
    }
}
