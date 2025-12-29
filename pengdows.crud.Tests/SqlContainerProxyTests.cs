using System;
using System.Data;
using System.Data.Common;
using pengdows.crud.Tests.Mocks;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlContainerProxyTests : SqlLiteContextTestBase
{
    [Fact]
    public void WrapObjectName_DelegatesToDialect()
    {
        var sc = Context.CreateSqlContainer();
        var expected = Context.WrapObjectName("Test");
        Assert.Equal(expected, sc.WrapObjectName("Test"));
    }

    [Fact]
    public void WrapObjectName_Null_ReturnsEmpty()
    {
        var sc = Context.CreateSqlContainer();
        Assert.Equal(string.Empty, sc.WrapObjectName(null!));
    }

    [Fact]
    public void MakeParameterName_ForwardsToContext()
    {
        var sc = Context.CreateSqlContainer();
        var p = sc.AddParameterWithValue(DbType.Int32, 1);
        Assert.Equal(Context.MakeParameterName(p), sc.MakeParameterName(p));
    }

    [Fact]
    public void MakeParameterName_NullParameter_Throws()
    {
        var sc = Context.CreateSqlContainer();
        Assert.Throws<NullReferenceException>(() => sc.MakeParameterName((DbParameter)null!));
    }

    [Fact]
    public void MakeParameterName_String_ForwardsToContext()
    {
        var sc = Context.CreateSqlContainer();
        Assert.Equal(Context.MakeParameterName("p"), sc.MakeParameterName("p"));
    }

    [Fact]
    public void MakeParameterName_NullString_ReturnsMarker()
    {
        var sc = Context.CreateSqlContainer();
        Assert.Equal(Context.DataSourceInfo.ParameterMarker, sc.MakeParameterName((string)null!));
    }

    [Fact]
    public void CreateDbParameter_DelegatesToDialect()
    {
        var sc = Context.CreateSqlContainer();
        var p = sc.CreateDbParameter("p", DbType.Int32, 1);

        Assert.Equal("p", p.ParameterName);
        Assert.Equal(DbType.Int32, p.DbType);
        Assert.Equal(1, p.Value);
    }

    [Fact]
    public void CreateDbParameter_FactoryReturnsNull_Throws()
    {
        var factory = new NullParameterFactory();
        var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        var sc = ctx.CreateSqlContainer();

        Assert.Throws<InvalidOperationException>(() => sc.CreateDbParameter("p", DbType.Int32, 1));
    }

    [Fact]
    public void CreateDbParameter_WithoutName_DelegatesToDialect()
    {
        var sc = Context.CreateSqlContainer();
        var p = sc.CreateDbParameter(DbType.Int32, 1);

        Assert.False(string.IsNullOrEmpty(p.ParameterName));
        Assert.Equal(DbType.Int32, p.DbType);
        Assert.Equal(1, p.Value);
    }

    [Fact]
    public void CreateDbParameter_WithoutName_NullValue_UsesDbNull()
    {
        var sc = Context.CreateSqlContainer();
        var p = sc.CreateDbParameter<string?>(DbType.String, null);

        Assert.False(string.IsNullOrEmpty(p.ParameterName));
        Assert.Equal(DbType.String, p.DbType);
        Assert.Equal(DBNull.Value, p.Value);
    }

    
}
