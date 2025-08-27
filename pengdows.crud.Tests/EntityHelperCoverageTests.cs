using System;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class EntityHelperCoverageTests
{
    [Fact]
    public void BuildUpsert_UsesDuplicate_ForMySql()
    {
        var factory = new FakeDbFactory(SupportedDatabase.MySql);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.MySql}", factory);
           var helper = new EntityHelper<TestEntity, int>(context);
        var entity = new TestEntity { Id = 1, Name = "foo" };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        Assert.Contains("ON DUPLICATE KEY UPDATE", sql, StringComparison.OrdinalIgnoreCase);
    }

}
