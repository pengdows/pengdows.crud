// Exercises the actual SQL output of every proc-wrapping strategy.
// MagicStringRegressionTests pins only the error messages; this file drives
// every branch of each Wrap() implementation and verifies generated SQL.

using System;
using System.Data;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.strategies.proc;
using Xunit;

namespace pengdows.crud.Tests;

// ── ExecProcWrappingStrategy (SQL Server / Sybase) ─────────────────────────

public sealed class ExecProcWrappingStrategyCoverageTests
{
    private static readonly ExecProcWrappingStrategy Strategy = new();

    [Fact]
    public void Wrap_WithArgs_GeneratesExecWithArgs()
    {
        var result = Strategy.Wrap("my_proc", ExecutionType.Write, "@p0, @p1");
        Assert.Equal("EXEC my_proc @p0, @p1", result);
    }

    [Fact]
    public void Wrap_WithoutArgs_OmitsArgSection()
    {
        var result = Strategy.Wrap("my_proc", ExecutionType.Write, "");
        Assert.Equal("EXEC my_proc", result);
    }

    [Fact]
    public void Wrap_NullArgs_OmitsArgSection()
    {
        var result = Strategy.Wrap("my_proc", ExecutionType.Read, null!);
        Assert.Equal("EXEC my_proc", result);
    }

    [Fact]
    public void Wrap_WithCallback_QuotesName()
    {
        var result = Strategy.Wrap("my_proc", ExecutionType.Write, "@p0",
            name => $"[{name}]");
        Assert.Equal("EXEC [my_proc] @p0", result);
    }

    [Fact]
    public void Wrap_ReadVsWrite_SameSyntax()
    {
        // ExecutionType is ignored by Exec
        var read = Strategy.Wrap("p", ExecutionType.Read, "x");
        var write = Strategy.Wrap("p", ExecutionType.Write, "x");
        Assert.Equal(read, write);
    }
}

// ── CallProcWrappingStrategy (MySQL / MariaDB / DB2) ─────────────────────

public sealed class CallProcWrappingStrategyCoverageTests
{
    private static readonly CallProcWrappingStrategy Strategy = new();

    [Fact]
    public void Wrap_WithArgs_GeneratesCallWithParens()
    {
        var result = Strategy.Wrap("my_proc", ExecutionType.Write, ":p0, :p1");
        Assert.Equal("CALL my_proc(:p0, :p1)", result);
    }

    [Fact]
    public void Wrap_WithoutArgs_EmptyParens()
    {
        var result = Strategy.Wrap("my_proc", ExecutionType.Write, "");
        Assert.Equal("CALL my_proc()", result);
    }

    [Fact]
    public void Wrap_WithCallback_QuotesName()
    {
        var result = Strategy.Wrap("sp", ExecutionType.Read, "?",
            name => $"`{name}`");
        Assert.Equal("CALL `sp`(?)", result);
    }

    [Fact]
    public void Wrap_ReadVsWrite_SameSyntax()
    {
        var read = Strategy.Wrap("p", ExecutionType.Read, "x");
        var write = Strategy.Wrap("p", ExecutionType.Write, "x");
        Assert.Equal(read, write);
    }
}

// ── OracleProcWrappingStrategy ─────────────────────────────────────────────

public sealed class OracleProcWrappingStrategyCoverageTests
{
    private static readonly OracleProcWrappingStrategy Strategy = new();

    [Fact]
    public void Wrap_WithArgs_GeneratesPlSqlBlockWithParens()
    {
        var result = Strategy.Wrap("my_pkg.my_proc", ExecutionType.Write, ":p0");
        Assert.Equal("BEGIN\n\tmy_pkg.my_proc(:p0);\nEND;", result);
    }

    [Fact]
    public void Wrap_WithoutArgs_OmitsParens()
    {
        var result = Strategy.Wrap("my_proc", ExecutionType.Write, "");
        Assert.Equal("BEGIN\n\tmy_proc;\nEND;", result);
    }

    [Fact]
    public void Wrap_NullArgs_OmitsParens()
    {
        var result = Strategy.Wrap("my_proc", ExecutionType.Read, null!);
        Assert.Equal("BEGIN\n\tmy_proc;\nEND;", result);
    }

    [Fact]
    public void Wrap_WithCallback_QuotesName()
    {
        var result = Strategy.Wrap("proc", ExecutionType.Write, ":a",
            name => $"\"{name}\"");
        Assert.Equal("BEGIN\n\t\"proc\"(:a);\nEND;", result);
    }

    [Fact]
    public void Wrap_ReadVsWrite_SameSyntax()
    {
        var read = Strategy.Wrap("p", ExecutionType.Read, "x");
        var write = Strategy.Wrap("p", ExecutionType.Write, "x");
        Assert.Equal(read, write);
    }
}

// ── ExecuteProcedureWrappingStrategy (Firebird) ──────────────────────────

public sealed class ExecuteProcedureWrappingStrategyCoverageTests
{
    private static readonly ExecuteProcedureWrappingStrategy Strategy = new();

    [Fact]
    public void Wrap_Read_GeneratesSelectFrom()
    {
        var result = Strategy.Wrap("my_proc", ExecutionType.Read, ":p0");
        Assert.Equal("SELECT * FROM my_proc(:p0)", result);
    }

    [Fact]
    public void Wrap_Write_GeneratesExecuteProcedure()
    {
        var result = Strategy.Wrap("my_proc", ExecutionType.Write, ":p0");
        Assert.Equal("EXECUTE PROCEDURE my_proc(:p0)", result);
    }

    [Fact]
    public void Wrap_ReadWithoutArgs_EmptyParens()
    {
        var result = Strategy.Wrap("my_proc", ExecutionType.Read, "");
        Assert.Equal("SELECT * FROM my_proc()", result);
    }

    [Fact]
    public void Wrap_WriteWithoutArgs_EmptyParens()
    {
        var result = Strategy.Wrap("my_proc", ExecutionType.Write, "");
        Assert.Equal("EXECUTE PROCEDURE my_proc()", result);
    }

    [Fact]
    public void Wrap_WithCallback_QuotesNameForBothModes()
    {
        Func<string, string> wrap = name => $"\"{name}\"";

        var read = Strategy.Wrap("sp", ExecutionType.Read, "x", wrap);
        Assert.Equal("SELECT * FROM \"sp\"(x)", read);

        var write = Strategy.Wrap("sp", ExecutionType.Write, "x", wrap);
        Assert.Equal("EXECUTE PROCEDURE \"sp\"(x)", write);
    }
}

// ── PostgresProcWrappingStrategy ──────────────────────────────────────────

public sealed class PostgresProcWrappingStrategyCoverageTests
{
    private static readonly PostgresProcWrappingStrategy Strategy = new();

    [Fact]
    public void Wrap_Read_GeneratesSelectFrom()
    {
        var result = Strategy.Wrap("my_func", ExecutionType.Read, ":p0");
        Assert.Equal("SELECT * FROM my_func(:p0)", result);
    }

    [Fact]
    public void Wrap_Write_GeneratesCall()
    {
        var result = Strategy.Wrap("my_proc", ExecutionType.Write, ":p0");
        Assert.Equal("CALL my_proc(:p0)", result);
    }

    [Fact]
    public void Wrap_ReadWithoutArgs_EmptyParens()
    {
        var result = Strategy.Wrap("my_func", ExecutionType.Read, "");
        Assert.Equal("SELECT * FROM my_func()", result);
    }

    [Fact]
    public void Wrap_WriteWithoutArgs_EmptyParens()
    {
        var result = Strategy.Wrap("my_proc", ExecutionType.Write, "");
        Assert.Equal("CALL my_proc()", result);
    }

    [Fact]
    public void Wrap_WithCallback_QuotesNameForBothModes()
    {
        Func<string, string> wrap = name => $"\"{name}\"";

        var read = Strategy.Wrap("fn", ExecutionType.Read, ":x", wrap);
        Assert.Equal("SELECT * FROM \"fn\"(:x)", read);

        var write = Strategy.Wrap("fn", ExecutionType.Write, ":x", wrap);
        Assert.Equal("CALL \"fn\"(:x)", write);
    }
}

// ── SqlContainer.WrapForStoredProc integration ───────────────────────────

[Table("wp_entity")]
file class WpEntity
{
    [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("name", DbType.String)]   public string Name { get; set; } = string.Empty;
}

public sealed class SqlContainerWrapForStoredProcTests
{
    private static DatabaseContext CreateContext(SupportedDatabase db) =>
        new($"Data Source=test;EmulatedProduct={db}", new fakeDbFactory(db));

    [Fact]
    public void WrapForStoredProc_NoneStyle_ThrowsNotSupported()
    {
        // Sqlite dialect defaults to ProcWrappingStyle.None
        using var ctx = CreateContext(SupportedDatabase.Sqlite);
        using var sc = (SqlContainer)ctx.CreateSqlContainer("my_proc");

        Assert.Throws<NotSupportedException>(() =>
            sc.WrapForStoredProc(ExecutionType.Write));
    }

    [Fact]
    public void WrapForStoredProc_EmptyProcName_ThrowsInvalidOperation()
    {
        using var ctx = CreateContext(SupportedDatabase.SqlServer);
        using var sc = (SqlContainer)ctx.CreateSqlContainer("   ");

        Assert.Throws<InvalidOperationException>(() =>
            sc.WrapForStoredProc(ExecutionType.Write));
    }

    [Fact]
    public void WrapForStoredProc_ExecStyle_NoParams_GeneratesExec()
    {
        using var ctx = CreateContext(SupportedDatabase.SqlServer);
        using var sc = (SqlContainer)ctx.CreateSqlContainer("my_proc");

        var result = sc.WrapForStoredProc(ExecutionType.Write, includeParameters: false);

        // SqlServer uses ANSI quotes (SET QUOTED_IDENTIFIER ON)
        Assert.Equal("EXEC \"my_proc\"", result);
    }

    [Fact]
    public void WrapForStoredProc_ExecStyle_WithParam_GeneratesExecWithArgs()
    {
        using var ctx = CreateContext(SupportedDatabase.SqlServer);
        using var sc = (SqlContainer)ctx.CreateSqlContainer("my_proc");
        sc.AddParameterWithValue("p0", DbType.String, "val");

        var result = sc.WrapForStoredProc(ExecutionType.Write);

        Assert.Equal("EXEC \"my_proc\" @p0", result);
    }

    [Fact]
    public void WrapForStoredProc_ExecStyle_CaptureReturn_GeneratesDeclare()
    {
        using var ctx = CreateContext(SupportedDatabase.SqlServer);
        using var sc = (SqlContainer)ctx.CreateSqlContainer("my_proc");
        sc.AddParameterWithValue("p0", DbType.Int32, 42);

        var result = sc.WrapForStoredProc(ExecutionType.Write, includeParameters: true, captureReturn: true);

        Assert.Equal("DECLARE @__ret INT;\nEXEC @__ret = \"my_proc\" @p0;\nSELECT @__ret;", result);
    }

    [Fact]
    public void WrapForStoredProc_ExecStyle_CaptureReturn_NoParams()
    {
        using var ctx = CreateContext(SupportedDatabase.SqlServer);
        using var sc = (SqlContainer)ctx.CreateSqlContainer("my_proc");

        var result = sc.WrapForStoredProc(ExecutionType.Write, includeParameters: true, captureReturn: true);

        Assert.Equal("DECLARE @__ret INT;\nEXEC @__ret = \"my_proc\";\nSELECT @__ret;", result);
    }

    [Fact]
    public void WrapForStoredProc_NonExecStyle_CaptureReturn_ThrowsNotSupported()
    {
        using var ctx = CreateContext(SupportedDatabase.MySql);
        using var sc = (SqlContainer)ctx.CreateSqlContainer("my_proc");

        Assert.Throws<NotSupportedException>(() =>
            sc.WrapForStoredProc(ExecutionType.Write, includeParameters: true, captureReturn: true));
    }

    [Fact]
    public void WrapForStoredProc_CallStyle_WithParam_GeneratesCall()
    {
        using var ctx = CreateContext(SupportedDatabase.MySql);
        using var sc = (SqlContainer)ctx.CreateSqlContainer("my_proc");
        sc.AddParameterWithValue("p0", DbType.String, "val");

        var result = sc.WrapForStoredProc(ExecutionType.Write);

        Assert.Contains("CALL", result);
        Assert.Contains("my_proc", result);
    }

    [Fact]
    public void WrapForStoredProc_PostgresStyle_Read_GeneratesSelectFrom()
    {
        using var ctx = CreateContext(SupportedDatabase.PostgreSql);
        using var sc = (SqlContainer)ctx.CreateSqlContainer("my_func");

        var result = sc.WrapForStoredProc(ExecutionType.Read, includeParameters: false);

        Assert.Contains("SELECT * FROM", result);
        Assert.Contains("my_func", result);
    }

    [Fact]
    public void WrapForStoredProc_PostgresStyle_Write_GeneratesCall()
    {
        using var ctx = CreateContext(SupportedDatabase.PostgreSql);
        using var sc = (SqlContainer)ctx.CreateSqlContainer("my_proc");

        var result = sc.WrapForStoredProc(ExecutionType.Write, includeParameters: false);

        Assert.Contains("CALL", result);
        Assert.Contains("my_proc", result);
    }

    [Fact]
    public void WrapForStoredProc_OracleStyle_WithParam_GeneratesPlSql()
    {
        using var ctx = CreateContext(SupportedDatabase.Oracle);
        using var sc = (SqlContainer)ctx.CreateSqlContainer("my_proc");
        sc.AddParameterWithValue("p0", DbType.String, "val");

        var result = sc.WrapForStoredProc(ExecutionType.Write);

        Assert.Contains("BEGIN", result);
        Assert.Contains("END;", result);
        Assert.Contains("my_proc", result);
    }

    [Fact]
    public void WrapForStoredProc_FirebirdStyle_Read_GeneratesSelectFrom()
    {
        using var ctx = CreateContext(SupportedDatabase.Firebird);
        using var sc = (SqlContainer)ctx.CreateSqlContainer("my_proc");

        var result = sc.WrapForStoredProc(ExecutionType.Read, includeParameters: false);

        Assert.Contains("SELECT * FROM", result);
        Assert.Contains("my_proc", result);
    }

    [Fact]
    public void WrapForStoredProc_FirebirdStyle_Write_GeneratesExecuteProcedure()
    {
        using var ctx = CreateContext(SupportedDatabase.Firebird);
        using var sc = (SqlContainer)ctx.CreateSqlContainer("my_proc");

        var result = sc.WrapForStoredProc(ExecutionType.Write, includeParameters: false);

        Assert.Contains("EXECUTE PROCEDURE", result);
        Assert.Contains("my_proc", result);
    }
}
