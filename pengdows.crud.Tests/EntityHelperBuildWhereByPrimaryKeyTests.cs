using System.Collections.Generic;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class EntityHelperBuildWhereByPrimaryKeyTests : SqlLiteContextTestBase
{
    private readonly EntityHelper<TestEntity, int> _helper;

    public EntityHelperBuildWhereByPrimaryKeyTests()
    {
        TypeMap.Register<TestEntity>();
        _helper = new EntityHelper<TestEntity, int>(Context);
    }

    [Fact]
    public void BuildWhereByPrimaryKey_GeneratesExpectedWhereClause()
    {
        var sc = Context.CreateSqlContainer();
        var list = new List<TestEntity>
        {
            new() { Name = "A" },
            new() { Name = "B" }
        };

        _helper.BuildWhereByPrimaryKey(list, sc, "t");
        var sql = sc.Query.ToString();

        var pattern = "\\n WHERE \\(t\\.\"Name\" = @\\w+\\) OR \\(t\\.\"Name\" = @\\w+\\)";
        Assert.Matches(pattern, sql);
        Assert.Equal(2, sc.ParameterCount);
        Assert.DoesNotContain(":", sql);
    }

    [Fact]
    public void BuildWhereByPrimaryKey_WithPostgresOverride_UsesColonMarker()
    {
        using var overrideCtx = new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=PostgreSql",
            new fakeDbFactory(SupportedDatabase.PostgreSql),
            TypeMap);
        var sc = overrideCtx.CreateSqlContainer();
        var list = new List<TestEntity> { new() { Name = "A" } };

        _helper.BuildWhereByPrimaryKey(list, sc);
        var sql = sc.Query.ToString();

        Assert.Contains(":", sql);
        Assert.DoesNotContain("@", sql);
    }
}

