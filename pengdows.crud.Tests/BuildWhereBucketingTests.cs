using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class BuildWhereBucketingTests : SqlLiteContextTestBase
{
    private readonly EntityHelper<TestEntity, int> _helper;

    public BuildWhereBucketingTests()
    {
        TypeMap.Register<TestEntity>();
        _helper = new EntityHelper<TestEntity, int>(Context);
    }

    [Fact]
    public void BuildWhere_SameBucket_ReusesSql()
    {
        var wrapped = Context.WrapObjectName("Id");
        var sc1 = Context.CreateSqlContainer("SELECT 1");
        _helper.BuildWhere(wrapped, new[] { 1, 2, 3 }, sc1);
        var sql1 = sc1.Query.ToString();

        var sc2 = Context.CreateSqlContainer("SELECT 1");
        _helper.BuildWhere(wrapped, new[] { 1, 2, 3, 4 }, sc2);
        var sql2 = sc2.Query.ToString();

        Assert.Equal(sql1, sql2);
    }

    [Fact]
    public void BuildWhere_DifferentBucket_ChangesSql()
    {
        var wrapped = Context.WrapObjectName("Id");
        var sc1 = Context.CreateSqlContainer("SELECT 1");
        _helper.BuildWhere(wrapped, new[] { 1, 2, 3, 4 }, sc1);
        var sql1 = sc1.Query.ToString();

        var sc2 = Context.CreateSqlContainer("SELECT 1");
        _helper.BuildWhere(wrapped, new[] { 1, 2, 3, 4, 5 }, sc2);
        var sql2 = sc2.Query.ToString();

        Assert.NotEqual(sql1, sql2);
    }

    [Fact]
    public void BuildWhere_SetValued_UsesAnyAndSingleParameter()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<TestEntity>();
        using var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql",
            new fakeDbFactory(SupportedDatabase.PostgreSql), typeMap);
        var helper = new EntityHelper<TestEntity, int>(ctx);

        var sc = ctx.CreateSqlContainer("SELECT 1");
        helper.BuildWhere(ctx.WrapObjectName("Id"), new[] { 1, 2, 3 }, sc);
        var sql = sc.Query.ToString();

        Assert.Contains("= ANY(", sql);
        Assert.Equal(1, sc.ParameterCount);
    }

    [Fact]
    public void BuildWhere_SetValuedUnsupported_UsesInList()
    {
        var wrapped = Context.WrapObjectName("Id");
        var sc = Context.CreateSqlContainer("SELECT 1");
        _helper.BuildWhere(wrapped, new[] { 1, 2, 3 }, sc);
        var sql = sc.Query.ToString();

        Assert.Contains("IN (", sql);
        Assert.Equal(4, sc.ParameterCount);
        var lastParam = Context.MakeParameterName("w3");
        Assert.Equal(3, sc.GetParameterValue<int>(lastParam));
    }
}