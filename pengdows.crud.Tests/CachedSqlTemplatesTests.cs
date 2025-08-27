using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using System.Threading.Tasks; 
using Microsoft.Data.Sqlite;
 
using Xunit;

namespace pengdows.crud.Tests;

public class CachedSqlTemplatesTests : SqlLiteContextTestBase
{
    [Fact]
    public void BuildCreate_ReusesCachedTemplates()
    {
        TypeMap.Register<TestEntity>();
        var helper1 = new EntityHelper<TestEntity, int>(Context);
        var helper2 = new EntityHelper<TestEntity, int>(Context);
        var entity1 = new TestEntity { Name = "one" };
        var entity2 = new TestEntity { Name = "two" };

        helper1.BuildCreate(entity1);
        var field = typeof(EntityHelper<TestEntity, int>).GetField("_cachedSqlTemplates", BindingFlags.Static | BindingFlags.NonPublic)!;
        var lazy1 = field.GetValue(null)!;
        var valueProp = lazy1.GetType().GetProperty("Value")!;
        var template1 = valueProp.GetValue(lazy1);

        helper2.BuildCreate(entity2);
        var lazy2 = field.GetValue(null)!;
        var template2 = valueProp.GetValue(lazy2);

        Assert.Same(template1, template2);
    }

    [Fact]
    public void BuildCreate_UsesPredictableParameterNames()
    {
        TypeMap.Register<TestEntity>();
        var helper = new EntityHelper<TestEntity, int>(Context);
        var entity = new TestEntity { Name = "foo" };

        var sc = helper.BuildCreate(entity);

        var sql = sc.Query.ToString();
        Assert.Contains("@p0", sql);
        Assert.Contains("@p1", sql);

        var field = typeof(SqlContainer).GetField("_parameters", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var parameters = (Dictionary<string, DbParameter>)field.GetValue(sc)!;
        Assert.Contains("p0", parameters.Keys);
        Assert.Contains("p1", parameters.Keys);
    }

    [Fact]
    public async Task BuildUpdateAsync_ReusesCachedTemplates()
    {
        TypeMap.Register<TestEntity>();
        var helper1 = new EntityHelper<TestEntity, int>(Context);
        var helper2 = new EntityHelper<TestEntity, int>(Context);

        var entity1 = new TestEntity { Id = 1, Name = "one" };
        var entity2 = new TestEntity { Id = 1, Name = "two" };

 
        await helper1.BuildUpdateAsync(entity1, loadOriginal: false);
 
        var field = typeof(EntityHelper<TestEntity, int>).GetField("_cachedSqlTemplates", BindingFlags.Static | BindingFlags.NonPublic)!;
        var lazy = field.GetValue(null)!;
        var valueProp = lazy.GetType().GetProperty("Value")!;
        var template1 = valueProp.GetValue(lazy);
 
        await helper2.BuildUpdateAsync(entity2, loadOriginal: false);
 
        var template2 = valueProp.GetValue(lazy);

        Assert.Same(template1, template2);
    }
 

    [Fact]
    public async Task BuildUpdateAsync_WhenLoadOriginalTrue_ThrowsIfTableMissing()
    {
        TypeMap.Register<TestEntity>();
        var helper = new EntityHelper<TestEntity, int>(Context);
        var entity = new TestEntity { Id = 1, Name = "one" };

        await Assert.ThrowsAsync<SqliteException>(async () =>
            await helper.BuildUpdateAsync(entity, loadOriginal: true));
    }
 
}
