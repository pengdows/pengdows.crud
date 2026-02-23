#region

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class QueryCacheTests : SqlLiteContextTestBase
{
    [Fact]
    public void BuildDelete_UsesCachedSql()
    {
        TypeMap.Register<CacheEntity>();
        var helper = new TableGateway<CacheEntity, int>(Context);

        helper.BuildDelete(1);
        var cache = GetQueryCache(helper);
        var first = cache["DeleteById"];

        helper.BuildDelete(2);
        var second = cache["DeleteById"];

        Assert.Same(first, second);
    }

    [Fact]
    public void BuildDelete_CacheScopedPerInstance()
    {
        TypeMap.Register<CacheEntity>();
        var helper1 = new TableGateway<CacheEntity, int>(Context);
        helper1.BuildDelete(1);
        var cache1 = GetQueryCache(helper1);
        Assert.True(cache1.ContainsKey("DeleteById"));

        var helper2 = new TableGateway<CacheEntity, int>(Context);
        var cache2 = GetQueryCache(helper2);
        Assert.False(cache2.ContainsKey("DeleteById"));
    }

    [Fact]
    public void BuildBaseRetrieve_CachesPerAlias()
    {
        TypeMap.Register<CacheEntity>();
        var helper = new TableGateway<CacheEntity, int>(Context);

        helper.BuildBaseRetrieve("a");
        var cache = GetQueryCache(helper);
        var first = cache["BaseRetrieve:a"];

        helper.BuildBaseRetrieve("a");
        var second = cache["BaseRetrieve:a"];

        Assert.Same(first, second);
    }

    [Fact]
    public void BuildBaseRetrieve_DifferentAlias_SeparatesCache()
    {
        TypeMap.Register<CacheEntity>();
        var helper = new TableGateway<CacheEntity, int>(Context);

        helper.BuildBaseRetrieve("a");
        helper.BuildBaseRetrieve("b");
        var cache = GetQueryCache(helper);

        Assert.NotSame(cache["BaseRetrieve:a"], cache["BaseRetrieve:b"]);
    }

    [Fact]
    public void BuildWhere_CachesByCount()
    {
        TypeMap.Register<CacheEntity>();
        var helper = new TableGateway<CacheEntity, int>(Context);
        var wrapped = Context.WrapObjectName("Id");

        var sc1 = Context.CreateSqlContainer();
        helper.BuildWhere(wrapped, new[] { 1, 2 }, sc1);
        var cache = GetQueryCache(helper);
        var key = $"WhereQuery:{wrapped}:2";
        var first = cache[key];

        var sc2 = Context.CreateSqlContainer();
        helper.BuildWhere(wrapped, new[] { 3, 4 }, sc2);
        var second = cache[key];

        Assert.Same(first, second);
    }

    [Fact]
    public void BuildWhere_DifferentCount_SeparatesCache()
    {
        TypeMap.Register<CacheEntity>();
        var helper = new TableGateway<CacheEntity, int>(Context);
        var wrapped = Context.WrapObjectName("Id");

        var sc1 = Context.CreateSqlContainer();
        helper.BuildWhere(wrapped, new[] { 1 }, sc1);

        var sc2 = Context.CreateSqlContainer();
        helper.BuildWhere(wrapped, new[] { 1, 2 }, sc2);

        var cache = GetQueryCache(helper);
        // Single-ID clause is stored in CachedSqlTemplates (dialect-keyed), not in _queryCache,
        // to prevent cross-dialect cache pollution (e.g. SQLite "@" vs PostgreSQL ":" markers).
        Assert.False(cache.ContainsKey(wrapped), "Single-ID clause is in CachedSqlTemplates, not _queryCache");
        // Multi-ID uses "WhereQuery:{col}:{bucket}" in _queryCache.
        Assert.True(cache.ContainsKey($"WhereQuery:{wrapped}:2"),
            "Two-ID clause should be cached under WhereQuery key");
        // SQL output is structurally different (equality vs IN-list).
        Assert.NotEqual(sc1.Query.ToString(), sc2.Query.ToString());
    }

    [Fact]
    public void BuildWhere_ReusesParameters_WhenContainerReused()
    {
        TypeMap.Register<CacheEntity>();
        var helper = new TableGateway<CacheEntity, int>(Context);
        var wrapped = Context.WrapObjectName("Id");

        var sc = Context.CreateSqlContainer();
        helper.BuildWhere(wrapped, new[] { 1, 2 }, sc);
        var countBefore = sc.ParameterCount;
        var p0 = Context.MakeParameterName("p0");
        var p1 = Context.MakeParameterName("p1");

        helper.BuildWhere(wrapped, new[] { 3, 4 }, sc);

        Assert.Equal(countBefore, sc.ParameterCount);
        Assert.Equal(3, sc.GetParameterValue<int>(p0));
        Assert.Equal(4, sc.GetParameterValue<int>(p1));
    }

    [Fact]
    public void BuildWhere_AddsParameters_WhenCountIncreases()
    {
        TypeMap.Register<CacheEntity>();
        var helper = new TableGateway<CacheEntity, int>(Context);
        var wrapped = Context.WrapObjectName("Id");

        var sc = Context.CreateSqlContainer();
        helper.BuildWhere(wrapped, new[] { 1 }, sc);
        var countBefore = sc.ParameterCount;

        helper.BuildWhere(wrapped, new[] { 2, 3 }, sc);

        var p0 = Context.MakeParameterName("p0");
        var p1 = Context.MakeParameterName("p1");

        Assert.True(sc.ParameterCount > countBefore);
        Assert.Equal(2, sc.GetParameterValue<int>(p0));
        Assert.Equal(3, sc.GetParameterValue<int>(p1));
    }

    [Fact]
    public async Task BuildDelete_ConcurrentCalls_ShareCache()
    {
        TypeMap.Register<CacheEntity>();
        var helper = new TableGateway<CacheEntity, int>(Context);

        await Task.WhenAll(
            Task.Run(() => helper.BuildDelete(1)),
            Task.Run(() => helper.BuildDelete(2))
        );

        var cache = GetQueryCache(helper);
        Assert.True(cache.ContainsKey("DeleteById"));
    }

    [Fact]
    public async Task BuildWhere_ConcurrentDifferentCounts_SeparateCache()
    {
        TypeMap.Register<CacheEntity>();
        var helper = new TableGateway<CacheEntity, int>(Context);
        var wrapped = Context.WrapObjectName("Id");

        await Task.WhenAll(
            Task.Run(() =>
            {
                var sc = Context.CreateSqlContainer();
                helper.BuildWhere(wrapped, new[] { 1 }, sc);
            }),
            Task.Run(() =>
            {
                var sc = Context.CreateSqlContainer();
                helper.BuildWhere(wrapped, new[] { 1, 2 }, sc);
            })
        );

        var cache = GetQueryCache(helper);
        // Single-ID clause is stored in CachedSqlTemplates (dialect-keyed), not in _queryCache,
        // to prevent cross-dialect cache pollution (e.g. SQLite "@" vs PostgreSQL ":" markers).
        Assert.False(cache.ContainsKey(wrapped), $"Single-ID clause should be in CachedSqlTemplates, not _queryCache");
        // Multi-ID (count=2) caches the IN-list clause under "WhereQuery:{col}:2".
        Assert.True(cache.ContainsKey($"WhereQuery:{wrapped}:2"),
            $"Two-ID clause should be cached under WhereQuery key");
    }

    [Fact]
    public void BuildBaseRetrieve_WhenLimitExceeded_DropsOldEntries()
    {
        TypeMap.Register<CacheEntity>();
        var helper = new TableGateway<CacheEntity, int>(Context);

        helper.BuildBaseRetrieve("a0");
        var cache = GetQueryCache(helper);

        var limit = (int)typeof(TableGateway<CacheEntity, int>)
            .GetField("MaxCacheSize", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        for (var i = 1; i <= limit; i++)
        {
            helper.BuildBaseRetrieve($"a{i}");
        }

        cache = GetQueryCache(helper);
        Assert.False(cache.ContainsKey("BaseRetrieve:a0"));
        Assert.True(cache.Count <= limit);
    }

    [Fact]
    public void ClearCaches_RemovesAllEntries()
    {
        TypeMap.Register<CacheEntity>();
        var helper = new TableGateway<CacheEntity, int>(Context);

        // Use BuildBaseRetrieve with a non-"a" alias since it still populates the query cache.
        // (BuildDelete now uses cloned container templates instead of the query cache.)
        helper.BuildBaseRetrieve("b");
        var cache = GetQueryCache(helper);
        Assert.True(cache.ContainsKey("BaseRetrieve:b"));

        helper.ClearCaches();
        cache = GetQueryCache(helper);
        Assert.Empty(cache);

        helper.BuildBaseRetrieve("b");
        cache = GetQueryCache(helper);
        Assert.True(cache.ContainsKey("BaseRetrieve:b"));
    }

    private static Dictionary<string, string> GetQueryCache<TEntity, TId>(TableGateway<TEntity, TId> helper)
        where TEntity : class, new()
    {
        // _queryCache is ConcurrentDictionary<SupportedDatabase, BoundedCache<string, string>>
        // Aggregate all dialect caches into one dictionary for test inspection.
        var field = typeof(TableGateway<TEntity, TId>)
            .GetField("_queryCache", BindingFlags.NonPublic | BindingFlags.Instance);
        var outerDict = field!.GetValue(helper)!;

        var result = new Dictionary<string, string>();
        foreach (var dialectKvp in (System.Collections.IEnumerable)outerDict)
        {
            var kvpType = dialectKvp.GetType();
            var boundedCache = kvpType.GetProperty("Value")!.GetValue(dialectKvp)!;

            var mapField = boundedCache.GetType().GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);
            var rawMap = mapField!.GetValue(boundedCache)!;

            // _map is ConcurrentDictionary<TKey, CacheEntry> — unwrap each CacheEntry.Value
            var entryType = rawMap.GetType().GetGenericArguments()[1];
            var valueProp = entryType.GetProperty("Value")!;

            foreach (var item in (System.Collections.IEnumerable)rawMap)
            {
                var itemType = item.GetType();
                var key = (string)itemType.GetProperty("Key")!.GetValue(item)!;
                var entry = itemType.GetProperty("Value")!.GetValue(item)!;
                result[key] = (string)valueProp.GetValue(entry)!;
            }
        }

        return result;
    }

    [Table("CacheEntity")]
    private class CacheEntity
    {
        [Id] [Column("Id", DbType.Int32)] public int Id { get; set; }

        [Column("Name", DbType.String)] public string? Name { get; set; }
    }
}