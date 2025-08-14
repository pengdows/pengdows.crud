using System;
using System.Reflection;
using Microsoft.Data.Sqlite;
using pengdows.crud;
using pengdows.crud.Tests.Mocks;
using pengdows.crud.exceptions;
using Xunit;

namespace pengdows.crud.Tests;

public class BuildWhereTests : SqlLiteContextTestBase
{
    private readonly EntityHelper<NullableIdEntity, int?> _helper;

    public BuildWhereTests()
    {
        TypeMap.Register<NullableIdEntity>();
        _helper = new EntityHelper<NullableIdEntity, int?>(Context);
    }

    [Fact]
    public void BuildWhere_WithExistingClause_AppendsAnd()
    {
        var sc = Context.CreateSqlContainer("SELECT 1 WHERE 1=1");
        var wrapped = Context.WrapObjectName("Id");
        _helper.BuildWhere(wrapped, new int?[] { 1, null }, sc);
        var sql = sc.Query.ToString();
        Assert.Contains("AND (", sql);
        Assert.Contains("IN (", sql);
        Assert.Contains("IS NULL", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildWhere_TooManyParameters_Throws()
    {
        var sc = Context.CreateSqlContainer();
        var info = (DataSourceInformation)Context.DataSourceInfo;
        var prop = typeof(DataSourceInformation).GetProperty("MaxParameterLimit", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        var original = info.MaxParameterLimit;
        prop.SetValue(info, 1);
        try
        {
            Assert.Throws<TooManyParametersException>(() =>
                _helper.BuildWhere(Context.WrapObjectName("Id"), new int?[] { 1, 2 }, sc));
        }
        finally
        {
            prop.SetValue(info, original);
        }
    }

    [Fact]
    public void BuildWhere_UsesHelperContextForParameters()
    {
        var helperSpy = new SpyDatabaseContext(Context);
        var helper = new EntityHelper<NullableIdEntity, int?>(helperSpy);

        var otherMap = new TypeMapRegistry();
        var otherInner = new DatabaseContext("Data Source=:memory:", SqliteFactory.Instance, otherMap);
        var otherSpy = new SpyDatabaseContext(otherInner);
        var sc = otherSpy.CreateSqlContainer();

        helper.BuildWhere(helperSpy.WrapObjectName("Id"), new int?[] { 1 }, sc);

        Assert.Equal(1, helperSpy.CreateDbParameterCalls);
        Assert.Equal(0, otherSpy.CreateDbParameterCalls);
    }
}
