#region
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud;
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
        var helper = new EntityHelper<CacheEntity, int>(Context);

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
        var helper1 = new EntityHelper<CacheEntity, int>(Context);
        helper1.BuildDelete(1);
        var cache1 = GetQueryCache(helper1);
        Assert.True(cache1.ContainsKey("DeleteById"));

        var helper2 = new EntityHelper<CacheEntity, int>(Context);
        var cache2 = GetQueryCache(helper2);
        Assert.False(cache2.ContainsKey("DeleteById"));
    }

    [Fact]
    public void BuildBaseRetrieve_CachesPerAlias()
    {
        TypeMap.Register<CacheEntity>();
        var helper = new EntityHelper<CacheEntity, int>(Context);

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
        var helper = new EntityHelper<CacheEntity, int>(Context);

        helper.BuildBaseRetrieve("a");
        helper.BuildBaseRetrieve("b");
        var cache = GetQueryCache(helper);

        Assert.NotSame(cache["BaseRetrieve:a"], cache["BaseRetrieve:b"]);
    }

    [Fact]
    public void BuildWhere_CachesByCount()
    {
        TypeMap.Register<CacheEntity>();
        var helper = new EntityHelper<CacheEntity, int>(Context);
        var wrapped = Context.WrapObjectName("Id");

        var sc1 = Context.CreateSqlContainer();
        helper.BuildWhere(wrapped, new[] { 1, 2 }, sc1);
        var cache = GetQueryCache(helper);
        var key = $"Where:{wrapped}:2";
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
        var helper = new EntityHelper<CacheEntity, int>(Context);
        var wrapped = Context.WrapObjectName("Id");

        var sc1 = Context.CreateSqlContainer();
        helper.BuildWhere(wrapped, new[] { 1 }, sc1);

        var sc2 = Context.CreateSqlContainer();
        helper.BuildWhere(wrapped, new[] { 1, 2 }, sc2);

        var cache = GetQueryCache(helper);
        Assert.NotSame(cache[$"Where:{wrapped}:1"], cache[$"Where:{wrapped}:2"]);
    }

    [Fact]
    public void BuildWhere_ReusesParameters_WhenContainerReused()
    {
        TypeMap.Register<CacheEntity>();
        var helper = new EntityHelper<CacheEntity, int>(Context);
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
        var helper = new EntityHelper<CacheEntity, int>(Context);
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
        var helper = new EntityHelper<CacheEntity, int>(Context);

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
        var helper = new EntityHelper<CacheEntity, int>(Context);
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
        Assert.NotSame(cache[$"Where:{wrapped}:1"], cache[$"Where:{wrapped}:2"]);
    }

    [Fact]
    public void BuildBaseRetrieve_WhenLimitExceeded_DropsOldEntries()
    {
        TypeMap.Register<CacheEntity>();
        var helper = new EntityHelper<CacheEntity, int>(Context);

        helper.BuildBaseRetrieve("a0");
        var cache = GetQueryCache(helper);

        var limit = (int)typeof(EntityHelper<CacheEntity, int>)
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
        var helper = new EntityHelper<CacheEntity, int>(Context);

        helper.BuildDelete(1);
        var cache = GetQueryCache(helper);
        Assert.True(cache.ContainsKey("DeleteById"));

        helper.ClearCaches();
        cache = GetQueryCache(helper);
        Assert.Empty(cache);

        helper.BuildDelete(2);
        cache = GetQueryCache(helper);
        Assert.True(cache.ContainsKey("DeleteById"));
    }

    private static ConcurrentDictionary<string, string> GetQueryCache<TEntity, TId>(EntityHelper<TEntity, TId> helper)
        where TEntity : class, new()
    {
        var field = typeof(EntityHelper<TEntity, TId>).GetField("_queryCache", BindingFlags.NonPublic | BindingFlags.Instance);
        return (ConcurrentDictionary<string, string>)field!.GetValue(helper)!;
    }

    [Table("CacheEntity")]
    private class CacheEntity
    {
        [Id]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)]
        public string? Name { get; set; }
    }
}
