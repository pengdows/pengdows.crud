#region

using System;
using System.Data;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class ProcWrappingStrategyTests
{
    private DatabaseContext CreateContextWithStyle(ProcWrappingStyle style)
    {
        var ctx = new DatabaseContext("Data Source=:memory:;EmulatedProduct=SqlServer", new fakeDbFactory(SupportedDatabase.SqlServer));
        ctx.ProcWrappingStyle = style;
        return ctx;
    }

    private SqlContainer CreateContainer(DatabaseContext ctx)
    {
        var sc = ctx.CreateSqlContainer("dbo.Sqltest") as SqlContainer;
        sc!.AddParameterWithValue("p0", DbType.Int32, 1);
        sc.AddParameterWithValue("p1", DbType.String, "x");
        return sc;
    }

    [Fact]
    public void ExecStyle_WrapsForReadAndWrite()
    {
        using var ctx = CreateContextWithStyle(ProcWrappingStyle.Exec);
        using var sc = CreateContainer(ctx);

        var read = sc.WrapForStoredProc(ExecutionType.Read, includeParameters: true);
        var write = sc.WrapForStoredProc(ExecutionType.Write, includeParameters: true);
        Assert.Equal("EXEC \"dbo\".\"Sqltest\" @p0, @p1", read);
        Assert.Equal("EXEC \"dbo\".\"Sqltest\" @p0, @p1", write);

        var noArgs = sc.WrapForStoredProc(ExecutionType.Read, includeParameters: false);
        Assert.Equal("EXEC \"dbo\".\"Sqltest\"", noArgs);
    }

    [Fact]
    public void CallStyle_Wraps()
    {
        using var ctx = CreateContextWithStyle(ProcWrappingStyle.Call);
        using var sc = CreateContainer(ctx);

        var s = sc.WrapForStoredProc(ExecutionType.Read, includeParameters: true);
        Assert.Equal("CALL \"dbo\".\"Sqltest\"(@p0, @p1)", s);

        var noArgs = sc.WrapForStoredProc(ExecutionType.Read, includeParameters: false);
        Assert.Equal("CALL \"dbo\".\"Sqltest\"()", noArgs);
    }

    [Fact]
    public void PostgresStyle_SelectForRead_CallForWrite()
    {
        using var ctx = CreateContextWithStyle(ProcWrappingStyle.PostgreSQL);
        using var sc = CreateContainer(ctx);

        var read = sc.WrapForStoredProc(ExecutionType.Read, includeParameters: true);
        var write = sc.WrapForStoredProc(ExecutionType.Write, includeParameters: true);
        Assert.Equal("SELECT * FROM \"dbo\".\"Sqltest\"(@p0, @p1)", read);
        Assert.Equal("CALL \"dbo\".\"Sqltest\"(@p0, @p1)", write);

        var noArgs = sc.WrapForStoredProc(ExecutionType.Read, includeParameters: false);
        Assert.Equal("SELECT * FROM \"dbo\".\"Sqltest\"()", noArgs);
    }

    [Fact]
    public void OracleStyle_WrapsInBeginEnd()
    {
        using var ctx = CreateContextWithStyle(ProcWrappingStyle.Oracle);
        using var sc = CreateContainer(ctx);

        var s = sc.WrapForStoredProc(ExecutionType.Read, includeParameters: true);
        Assert.Equal("BEGIN\n\t\"dbo\".\"Sqltest\"(@p0, @p1);\nEND;", s);

        var noArgs = sc.WrapForStoredProc(ExecutionType.Write, includeParameters: false);
        Assert.Equal("BEGIN\n\t\"dbo\".\"Sqltest\";\nEND;", noArgs);
    }

    [Fact]
    public void ExecuteProcedureStyle_SelectForRead_ExecuteForWrite()
    {
        using var ctx = CreateContextWithStyle(ProcWrappingStyle.ExecuteProcedure);
        using var sc = CreateContainer(ctx);

        var read = sc.WrapForStoredProc(ExecutionType.Read, includeParameters: true);
        var write = sc.WrapForStoredProc(ExecutionType.Write, includeParameters: true);
        Assert.Equal("SELECT * FROM \"dbo\".\"Sqltest\"(@p0, @p1)", read);
        Assert.Equal("EXECUTE PROCEDURE \"dbo\".\"Sqltest\"(@p0, @p1)", write);
    }

    [Fact]
    public void NoneStyle_ThrowsNotSupported()
    {
        using var ctx = CreateContextWithStyle(ProcWrappingStyle.None);
        using var sc = CreateContainer(ctx);
        Assert.Throws<NotSupportedException>(() => sc.WrapForStoredProc(ExecutionType.Read, includeParameters: true));
    }

    [Fact]
    public void EmptyProcName_ThrowsInvalidOperation()
    {
        using var ctx = CreateContextWithStyle(ProcWrappingStyle.Exec);
        using var sc = ctx.CreateSqlContainer("   ") as SqlContainer;
        Assert.Throws<InvalidOperationException>(() => sc!.WrapForStoredProc(ExecutionType.Read, includeParameters: true));
    }
}

