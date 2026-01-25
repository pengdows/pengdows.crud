#region

using System.Collections.Generic;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class EntityHelperBuildWhereSetValuedTests : SqlLiteContextTestBase
{
    [Table("WhereTest")]
    private class WhereEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;
    }

    public EntityHelperBuildWhereSetValuedTests()
    {
        TypeMap.Register<WhereEntity>();
    }

    [Fact]
    public void BuildWhere_Postgres_UsesAnyExpression()
    {
        using var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}",
            new fakeDbFactory(SupportedDatabase.PostgreSql));
        var helper = new EntityHelper<WhereEntity, int>(ctx);

        using var sc = ctx.CreateSqlContainer("SELECT * FROM " + ctx.WrapObjectName("WhereTest"));
        var wrapped = ctx.WrapObjectName("Id");
        var result = helper.BuildWhere(wrapped, new List<int> { 1, 2, 3 }, sc);
        var sql = result.Query.ToString();
        Assert.Contains(" = ANY(", sql);
    }

    [Fact]
    public void BuildWhere_Sqlite_UsesInWithExpandedParameters()
    {
        using var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}",
            new fakeDbFactory(SupportedDatabase.Sqlite));
        var helper = new EntityHelper<WhereEntity, int>(ctx);

        using var sc = ctx.CreateSqlContainer("SELECT * FROM " + ctx.WrapObjectName("WhereTest"));
        var wrapped = ctx.WrapObjectName("Id");
        var result = helper.BuildWhere(wrapped, new List<int> { 1, 2, 3 }, sc);
        var sql = result.Query.ToString();
        Assert.Contains(" IN (", sql);
        // Expect placeholders for w0,w1,w2 bucket expansion
        Assert.Contains("@w0", sql);
    }
}