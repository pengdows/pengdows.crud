using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace pengdows.crud.Tests;

public class CachedSqlTemplatesTests : SqlLiteContextTestBase
{
    [Fact]
    public void BuildCreate_ReusesCachedTemplates()
    {
        TypeMap.Register<TestEntity>();
        var helper1 = new EntityHelper<TestEntity, int>(Context, AuditValueResolver);
        var entity1 = new TestEntity { Name = "one" };
        var entity2 = new TestEntity { Name = "two" };

        // Build create twice with same helper to verify template reuse within instance
        helper1.BuildCreate(entity1);
        var field = typeof(EntityHelper<TestEntity, int>).GetField("_cachedSqlTemplates", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var lazy1 = field.GetValue(helper1)!;
        var valueProp1 = lazy1.GetType().GetProperty("Value")!;
        var template1 = valueProp1.GetValue(lazy1);

        helper1.BuildCreate(entity2);
        var lazy2 = field.GetValue(helper1)!;
        var valueProp2 = lazy2.GetType().GetProperty("Value")!;
        var template2 = valueProp2.GetValue(lazy2);

        Assert.Same(template1, template2);
    }

    [Fact]
    public void BuildCreate_UsesPredictableParameterNames()
    {
        TypeMap.Register<TestEntity>();
        var helper = new EntityHelper<TestEntity, int>(Context, AuditValueResolver);
        var entity = new TestEntity { Name = "foo" };

        var sc = helper.BuildCreate(entity);

        var sql = sc.Query.ToString();
        Assert.Contains("@i0", sql);
        Assert.Contains("@i1", sql);

        var field = typeof(SqlContainer).GetField("_parameters", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var parameters = (IDictionary<string, DbParameter>)field.GetValue(sc)!;
        Assert.Contains("i0", parameters.Keys);
        Assert.Contains("i1", parameters.Keys);
    }

    [Fact]
    public async Task BuildUpdateAsync_ReusesCachedTemplates()
    {
        TypeMap.Register<TestEntity>();
        var helper1 = new EntityHelper<TestEntity, int>(Context, AuditValueResolver);

        var entity1 = new TestEntity { Id = 1, Name = "one" };
        var entity2 = new TestEntity { Id = 1, Name = "two" };

        // Build update twice with same helper to verify template reuse within instance
        await helper1.BuildUpdateAsync(entity1, loadOriginal: false);

        var field = typeof(EntityHelper<TestEntity, int>).GetField("_cachedSqlTemplates", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var lazy1 = field.GetValue(helper1)!;
        var valueProp1 = lazy1.GetType().GetProperty("Value")!;
        var template1 = valueProp1.GetValue(lazy1);

        await helper1.BuildUpdateAsync(entity2, loadOriginal: false);
        var lazy2 = field.GetValue(helper1)!;
        var valueProp2 = lazy2.GetType().GetProperty("Value")!;
        var template2 = valueProp2.GetValue(lazy2);

        Assert.Same(template1, template2);
    }


    [Fact]
    public async Task BuildUpdateAsync_WhenLoadOriginalTrue_ThrowsIfTableMissing()
    {
        TypeMap.Register<TestEntity>();
        var helper = new EntityHelper<TestEntity, int>(Context, AuditValueResolver);
        var entity = new TestEntity { Id = 1, Name = "one" };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await helper.BuildUpdateAsync(entity, loadOriginal: true));
    }

}
