#region
using System;
using System.Linq;
using Xunit;
#endregion

namespace pengdows.crud.Tests;

public class BuildWhereNullIdTests : SqlLiteContextTestBase
{
    private readonly EntityHelper<NullableIdEntity, int?> helper;

    public BuildWhereNullIdTests()
    {
        TypeMap.Register<NullableIdEntity>();
        helper = new EntityHelper<NullableIdEntity, int?>(Context);
    }

    [Fact]
    public void BuildWhere_WithNullId_AddsIsNull()
    {
        var sc = Context.CreateSqlContainer();
        helper.BuildWhere(Context.WrapObjectName("Id"), new int?[] { null }, sc);
        var sql = sc.Query.ToString();
        Assert.Contains("IS NULL", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildWhere_WithMixedIds_IncludesBoth()
    {
        var sc = Context.CreateSqlContainer();
        helper.BuildWhere(Context.WrapObjectName("Id"), new int?[] { 1, null, 2 }, sc);
        var sql = sc.Query.ToString();
        Assert.Contains("IN", sql);
        Assert.Contains("IS NULL", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRetrieve_WithNullId_Throws()
    {
        Assert.Throws<ArgumentException>(() => helper.BuildRetrieve(new int?[] { null }));
    }
}
