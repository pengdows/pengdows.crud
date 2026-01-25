#region

using System;
using System.Data;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class ProcWrappingStyleTests
{
    [Theory]
    [InlineData("Call", ProcWrappingStyle.Call)]
    [InlineData("Exec", ProcWrappingStyle.Exec)]
    [InlineData("ExecuteProcedure", ProcWrappingStyle.ExecuteProcedure)]
    [InlineData("None", ProcWrappingStyle.None)]
    [InlineData("Oracle", ProcWrappingStyle.Oracle)]
    [InlineData("PostgreSQL", ProcWrappingStyle.PostgreSQL)]
    public void EnumParse_ShouldReturnCorrectValue(string input, ProcWrappingStyle expected)
    {
        var result = Enum.Parse<ProcWrappingStyle>(input, true);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ProcWrappingStyleEnumParse_InvalidValue_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => Enum.Parse<ProcWrappingStyle>("NotAnProcWrappingStyle"));
    }

    [Fact]
    public void ProcWrappingStyle_ShouldContainExpectedValues()
    {
        var names = Enum.GetNames(typeof(ProcWrappingStyle));
        Assert.Equal(new[] { "None", "Call", "Exec", "PostgreSQL", "Oracle", "ExecuteProcedure" }, names);
    }

    private SqlContainer SetupParameterWrapTest(SupportedDatabase product)
    {
        var ctx = new DatabaseContext($"DataSource=:memory:;EmulatedProduct={product}", new fakeDbFactory(product));
        var sc = (SqlContainer)ctx.CreateSqlContainer("dbo.Sqltest");
        for (var i = 0; i < 10; i++)
        {
            sc.AddParameterWithValue($"p{i}", DbType.Int32, i);
        }

        return sc;
    }

    [Fact]
    public void WrapTestCall()
    {
        var sc = SetupParameterWrapTest(SupportedDatabase.MySql);
        var s = sc.WrapForStoredProc(ExecutionType.Read);
        Assert.Equal("CALL \"dbo\".\"Sqltest\"(@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9)", s);
    }

    [Fact]
    public void WrapTestExec()
    {
        var sc = SetupParameterWrapTest(SupportedDatabase.SqlServer);
        var s = sc.WrapForStoredProc(ExecutionType.Read);
        Assert.Equal("EXEC \"dbo\".\"Sqltest\" @p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9", s);
    }

    [Fact]
    public void WrapTestExecCaptureReturn()
    {
        var sc = SetupParameterWrapTest(SupportedDatabase.SqlServer);
        var s = sc.WrapForStoredProc(ExecutionType.Read, captureReturn: true);
        Assert.Equal(
            "DECLARE @__ret INT;\nEXEC @__ret = \"dbo\".\"Sqltest\" @p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9;\nSELECT @__ret;",
            s);
    }

    [Fact]
    public void WrapTestExecute()
    {
        var sc = SetupParameterWrapTest(SupportedDatabase.Firebird);
        var s = sc.WrapForStoredProc(ExecutionType.Read);
        Assert.Equal("SELECT * FROM \"dbo\".\"Sqltest\"(@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9)", s);
        s = sc.WrapForStoredProc(ExecutionType.Write);
        Assert.Equal("EXECUTE PROCEDURE \"dbo\".\"Sqltest\"(@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9)", s);
    }

    [Fact]
    public void WrapTestExecute_CaptureReturn_Throws()
    {
        var sc = SetupParameterWrapTest(SupportedDatabase.Firebird);
        Assert.Throws<NotSupportedException>(() => sc.WrapForStoredProc(ExecutionType.Read, captureReturn: true));
    }

    [Fact]
    public void WrapTestPostgreSQL()
    {
        var sc = SetupParameterWrapTest(SupportedDatabase.PostgreSql);
        var s = sc.WrapForStoredProc(ExecutionType.Read);
        Assert.Equal("SELECT * FROM \"dbo\".\"Sqltest\"(:p0, :p1, :p2, :p3, :p4, :p5, :p6, :p7, :p8, :p9)", s);
        s = sc.WrapForStoredProc(ExecutionType.Write);
        Assert.Equal("CALL \"dbo\".\"Sqltest\"(:p0, :p1, :p2, :p3, :p4, :p5, :p6, :p7, :p8, :p9)", s);
    }

    [Fact]
    public void WrapTestPostgreSQL_CaptureReturn_Throws()
    {
        var sc = SetupParameterWrapTest(SupportedDatabase.PostgreSql);
        Assert.Throws<NotSupportedException>(() => sc.WrapForStoredProc(ExecutionType.Read, captureReturn: true));
    }

    [Fact]
    public void WrapTestOracle()
    {
        var sc = SetupParameterWrapTest(SupportedDatabase.Oracle);
        var s = sc.WrapForStoredProc(ExecutionType.Read);
        Assert.Equal("BEGIN\n\t\"dbo\".\"Sqltest\"(:p0, :p1, :p2, :p3, :p4, :p5, :p6, :p7, :p8, :p9);\nEND;", s);
    }

    [Fact]
    public void CaptureReturn_UnsupportedStyle_ShouldThrow()
    {
        var sc = SetupParameterWrapTest(SupportedDatabase.MySql);
        Assert.Throws<NotSupportedException>(() => sc.WrapForStoredProc(ExecutionType.Read, captureReturn: true));
    }
}