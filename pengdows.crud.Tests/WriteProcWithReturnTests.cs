#region

using System;
using System.Data;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class WriteProcWithReturnTests
{
    private DatabaseContext _dbContext = null!;

    private SqlContainer SetupContainer(SupportedDatabase product)
    {
        _dbContext = new DatabaseContext($"DataSource=:memory:;EmulatedProduct={product}", new fakeDbFactory(product));
        var sc = (SqlContainer)_dbContext.CreateSqlContainer("dbo.Sqltest");
        for (var i = 0; i < 2; i++)
        {
            sc.AddParameterWithValue($"p{i}", DbType.Int32, i);
        }

        return sc;
    }

    [Fact]
    public void WrapForCreateWithReturn_ExecStyle_GeneratesSql()
    {
        var sc = SetupContainer(SupportedDatabase.SqlServer);
        var s = sc.WrapForCreateWithReturn();
        Assert.Equal("DECLARE @__ret INT;\nEXEC @__ret = \"dbo\".\"Sqltest\" @p0, @p1;\nSELECT @__ret;", s);
    }

    [Fact]
    public void WrapForCreateWithReturn_UnsupportedStyle_ShouldThrow()
    {
        var sc = SetupContainer(SupportedDatabase.MySql);
        Assert.Throws<NotSupportedException>(() => sc.WrapForCreateWithReturn());
    }

    [Fact]
    public void WrapForUpdateWithReturn_ExecStyle_GeneratesSql()
    {
        var sc = SetupContainer(SupportedDatabase.SqlServer);
        var s = sc.WrapForUpdateWithReturn();
        Assert.Equal("DECLARE @__ret INT;\nEXEC @__ret = \"dbo\".\"Sqltest\" @p0, @p1;\nSELECT @__ret;", s);
    }

    [Fact]
    public void WrapForUpdateWithReturn_UnsupportedStyle_ShouldThrow()
    {
        var sc = SetupContainer(SupportedDatabase.MySql);
        Assert.Throws<NotSupportedException>(() => sc.WrapForUpdateWithReturn());
    }

    [Fact]
    public void WrapForDeleteWithReturn_ExecStyle_GeneratesSql()
    {
        var sc = SetupContainer(SupportedDatabase.SqlServer);
        var s = sc.WrapForDeleteWithReturn();
        Assert.Equal("DECLARE @__ret INT;\nEXEC @__ret = \"dbo\".\"Sqltest\" @p0, @p1;\nSELECT @__ret;", s);
    }

    [Fact]
    public void WrapForDeleteWithReturn_UnsupportedStyle_ShouldThrow()
    {
        var sc = SetupContainer(SupportedDatabase.MySql);
        Assert.Throws<NotSupportedException>(() => sc.WrapForDeleteWithReturn());
    }
}