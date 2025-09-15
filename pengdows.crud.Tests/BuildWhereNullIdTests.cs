#region

using System;
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
    public void BuildWhere_WithNullId_RendersIsNull()
    {
        var sc = Context.CreateSqlContainer();
        var wrapped = Context.WrapObjectName("Id");
        helper.BuildWhere(wrapped, new int?[] { null }, sc);
        var sql = sc.Query.ToString();
        Assert.Contains("IS NULL", sql);
    }

    [Fact]
    public void BuildWhere_WithMixedIds_IncludesIsNull()
    {
        var sc = Context.CreateSqlContainer();
        var wrapped = Context.WrapObjectName("Id");
        helper.BuildWhere(wrapped, new int?[] { 1, null, 2 }, sc);
        var sql = sc.Query.ToString();
        Assert.Contains(" IS NULL", sql);
    }

    [Fact]
    public void BuildRetrieve_WithNullId_Throws()
    {
        Assert.Throws<ArgumentException>(() => helper.BuildRetrieve(new int?[] { null }, string.Empty));
    }
}
