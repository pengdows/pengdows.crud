#region

using System;
using System.Data;
using Microsoft.Data.Sqlite;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class ProcWrappingStyleTests
{
    private DatabaseContext _dbContext;

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

    private SqlContainer SetupParameterWrapTest()
    {
        if (_dbContext == null) _dbContext = new DatabaseContext("DataSource=:memory:", SqliteFactory.Instance);

        var sc = _dbContext.CreateSqlContainer("dbo.Sqltest") as SqlContainer;
        for (var i = 0; i < 10; i++) sc.AddParameterWithValue($"p{i}", DbType.Int32, i);

        return sc;
    }

    [Fact]
    public void WrapTestCall()
    {
        var sc = SetupParameterWrapTest();
        _dbContext.ProcWrappingStyle = ProcWrappingStyle.Call;
        var s = sc.WrapForStoredProc(ExecutionType.Read);
        Assert.Equal("CALL dbo.Sqltest(@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9)", s);
    }

    [Fact]
    public void WrapTestExec()
    {
        var sc = SetupParameterWrapTest();
        _dbContext.ProcWrappingStyle = ProcWrappingStyle.Exec;
        var s = sc.WrapForStoredProc(ExecutionType.Read);
        Assert.Equal("EXEC dbo.Sqltest @p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9", s);
    }

    [Fact]
    public void WrapTestExecCaptureReturn()
    {
        var sc = SetupParameterWrapTest();
        _dbContext.ProcWrappingStyle = ProcWrappingStyle.Exec;
        var s = sc.WrapForStoredProc(ExecutionType.Read, captureReturn: true);
        Assert.Equal("DECLARE @__ret INT;\nEXEC @__ret = dbo.Sqltest @p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9;\nSELECT @__ret;", s);
    }

    [Fact]
    public void WrapTestExecute()
    {
        var sc = SetupParameterWrapTest();
        _dbContext.ProcWrappingStyle = ProcWrappingStyle.ExecuteProcedure;
        var s = sc.WrapForStoredProc(ExecutionType.Read);
        Assert.Equal("EXECUTE PROCEDURE dbo.Sqltest(@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9)", s);
    }

    [Fact]
    public void WrapTestPostgreSQL()
    {
        var sc = SetupParameterWrapTest();
        _dbContext.ProcWrappingStyle = ProcWrappingStyle.PostgreSQL;
        var s = sc.WrapForStoredProc(ExecutionType.Read);
        Assert.Equal("SELECT * FROM dbo.Sqltest(@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9)", s);
        s = sc.WrapForStoredProc(ExecutionType.Write);
        Assert.Equal("CALL dbo.Sqltest(@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9)", s);
    }

    [Fact]
    public void WrapTestOracle()
    {
        var sc = SetupParameterWrapTest();
        _dbContext.ProcWrappingStyle = ProcWrappingStyle.Oracle;
        var s = sc.WrapForStoredProc(ExecutionType.Read);
        Assert.Equal("BEGIN\n\tdbo.Sqltest(@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9);\nEND;", s);
    }

    [Fact]
    public void CaptureReturn_UnsupportedStyle_ShouldThrow()
    {
        var sc = SetupParameterWrapTest();
        _dbContext.ProcWrappingStyle = ProcWrappingStyle.Call;
        Assert.Throws<NotSupportedException>(() => sc.WrapForStoredProc(ExecutionType.Read, captureReturn: true));
    }
}