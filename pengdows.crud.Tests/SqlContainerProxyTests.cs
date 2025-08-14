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
        Assert.Equal(string.Empty, sc.WrapObjectName(null));
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
    public void CreateDbParameter_DelegatesToContext()
    {
        var spy = new SpyDatabaseContext(Context);
        var sc = spy.CreateSqlContainer();
        var p = sc.CreateDbParameter("p1", DbType.Int32, 1);
        Assert.Equal("p1", p.ParameterName);
        Assert.Equal(1, spy.CreateDbParameterCalls);
        Assert.Equal(0, sc.ParameterCount);
    }
}
