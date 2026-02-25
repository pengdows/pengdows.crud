using System;
using pengdows.crud.exceptions;
using Xunit;

namespace pengdows.crud.Tests;

public class BuildWhereTests : SqlLiteContextTestBase
{
    private readonly TableGateway<NullableIdEntity, int?> _helper;

    public BuildWhereTests()
    {
        TypeMap.Register<NullableIdEntity>();
        _helper = new TableGateway<NullableIdEntity, int?>(Context);
    }

    [Fact]
    public void BuildWhere_WithExistingClause_AppendsAnd()
    {
        var sc = Context.CreateSqlContainer("SELECT 1 WHERE 1=1");
        sc.HasWhereAppended = true;
        var wrapped = Context.WrapObjectName("Id");
        _helper.BuildWhere(wrapped, new int?[] { 1, 2 }, sc);
        var sql = sc.Query.ToString();
        Assert.Contains("AND", sql);
        Assert.Contains("IN (", sql);
        Assert.DoesNotContain("IS NULL", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildWhere_TooManyParameters_Throws()
    {
        // SQLite 3.32+ has a parameter limit of 32766. Use 33000 to exceed it.
        var sc = Context.CreateSqlContainer();
        var largeParameterArray = new int?[33000];
        for (var i = 0; i < 33000; i++)
        {
            largeParameterArray[i] = i;
        }

        Assert.Throws<TooManyParametersException>(() =>
            _helper.BuildWhere(Context.WrapObjectName("Id"), largeParameterArray, sc));
    }
}