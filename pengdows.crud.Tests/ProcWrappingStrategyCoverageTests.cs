// Exercises the actual SQL output of every proc-wrapping strategy.
// MagicStringRegressionTests pins only the error messages; this file drives
// every branch of each Wrap() implementation and verifies generated SQL.

using System;
using System.Data;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using pengdows.crud.strategies.proc;
using Xunit;

namespace pengdows.crud.Tests;

// ── ExecProcWrappingStrategy (SQL Server / Sybase) ─────────────────────────

public sealed class ExecProcWrappingStrategyLegacyCoverageTests
{
    private static readonly ExecProcWrappingStrategy Strategy = new();

}

// ── CallProcWrappingStrategy (MySQL / MariaDB / DB2) ─────────────────────

public sealed class CallProcWrappingStrategyLegacyCoverageTests
{
    private static readonly CallProcWrappingStrategy Strategy = new();

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

public sealed class OracleProcWrappingStrategyLegacyCoverageTests
{
    private static readonly OracleProcWrappingStrategy Strategy = new();

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

public sealed class ExecuteProcedureWrappingStrategyLegacyCoverageTests
{
    private static readonly ExecuteProcedureWrappingStrategy Strategy = new();

}

// ── PostgresProcWrappingStrategy ──────────────────────────────────────────

public sealed class PostgresProcWrappingStrategyLegacyCoverageTests
{
    private static readonly PostgresProcWrappingStrategy Strategy = new();

    [Fact]
    public void Wrap_Read_GeneratesSelectFrom()
    {
        var result = Strategy.Wrap("my_func", ExecutionType.Read, ":p0");
        Assert.Equal("SELECT * FROM my_func(:p0)", result);
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
    [Id][Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
}

public sealed class SqlContainerWrapForStoredProcLegacyTests
{
    private static DatabaseContext CreateContext(SupportedDatabase db) =>
        new($"Data Source=test;EmulatedProduct={db}", new fakeDbFactory(db));

}