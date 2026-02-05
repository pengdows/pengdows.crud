// Exercises ContextBase helpers through a real DatabaseContext backed by fakeDb.
// Targets: CreateDbParameter overloads, GenerateRandomName, property delegates.

using System;
using System.Data;
using System.Data.Common;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class ContextBaseCoverageTests
{
    private static DatabaseContext SqlServerContext() =>
        new("Data Source=test;EmulatedProduct=SqlServer", new fakeDbFactory(SupportedDatabase.SqlServer));

    private static DatabaseContext SqliteContext() =>
        new("Data Source=test;EmulatedProduct=Sqlite", new fakeDbFactory(SupportedDatabase.Sqlite));

    private static DatabaseContext PostgresContext() =>
        new("Data Source=test;EmulatedProduct=PostgreSql", new fakeDbFactory(SupportedDatabase.PostgreSql));

    // ── CreateDbParameter overloads ───────────────────────────────────────

    [Fact]
    public void CreateDbParameter_NameTypeValue_DefaultsToInput()
    {
        using var ctx = SqlServerContext();
        var p = ctx.CreateDbParameter("myParam", DbType.String, "hello");

        Assert.Equal("myParam", p.ParameterName);
        Assert.Equal(DbType.String, p.DbType);
        Assert.Equal("hello", p.Value);
        Assert.Equal(ParameterDirection.Input, p.Direction);
    }

    [Fact]
    public void CreateDbParameter_WithDirection_SetsOutput()
    {
        using var ctx = SqlServerContext();
        var p = ctx.CreateDbParameter("outParam", DbType.Int32, 0,
            ParameterDirection.Output);

        Assert.Equal(ParameterDirection.Output, p.Direction);
        Assert.Equal("outParam", p.ParameterName);
    }

    [Fact]
    public void CreateDbParameter_WithDirection_InputOutput()
    {
        using var ctx = SqlServerContext();
        var p = ctx.CreateDbParameter("ioParam", DbType.Int32, 42,
            ParameterDirection.InputOutput);

        Assert.Equal(ParameterDirection.InputOutput, p.Direction);
        Assert.Equal(42, p.Value);
    }

    [Fact]
    public void CreateDbParameter_NoName_TypeValue_DefaultsToInput()
    {
        using var ctx = SqlServerContext();
        var p = ctx.CreateDbParameter(DbType.Int64, 999L);

        Assert.Equal(DbType.Int64, p.DbType);
        Assert.Equal(ParameterDirection.Input, p.Direction);
    }

    [Fact]
    public void CreateDbParameter_NoName_WithDirection()
    {
        using var ctx = SqlServerContext();
        var p = ctx.CreateDbParameter(DbType.Int32, 7, ParameterDirection.Output);

        Assert.Equal(ParameterDirection.Output, p.Direction);
    }

    // ── WrapObjectName / MakeParameterName ────────────────────────────────

    [Fact]
    public void WrapObjectName_SqlServer_UseAnsiQuotes()
    {
        using var ctx = SqlServerContext();
        Assert.Equal("\"mytable\"", ctx.WrapObjectName("mytable"));
    }

    [Fact]
    public void WrapObjectName_Postgres_UseDoubleQuotes()
    {
        using var ctx = PostgresContext();
        Assert.Equal("\"mytable\"", ctx.WrapObjectName("mytable"));
    }

    [Fact]
    public void MakeParameterName_String_SqlServer_AtPrefix()
    {
        using var ctx = SqlServerContext();
        var name = ctx.MakeParameterName("foo");
        Assert.Equal("@foo", name);
    }

    [Fact]
    public void MakeParameterName_String_Postgres_ColonPrefix()
    {
        using var ctx = PostgresContext();
        var name = ctx.MakeParameterName("bar");
        Assert.Equal(":bar", name);
    }

    [Fact]
    public void MakeParameterName_DbParameter_StripsDuplicatePrefix()
    {
        using var ctx = SqlServerContext();
        var p = ctx.CreateDbParameter("@already", DbType.String, "x");
        var name = ctx.MakeParameterName(p);
        // Should not produce @@already
        Assert.Equal("@already", name);
    }

    // ── GenerateRandomName ────────────────────────────────────────────────

    [Fact]
    public void GenerateRandomName_DefaultLength_NonEmpty()
    {
        using var ctx = SqlServerContext();
        var name = ctx.GenerateRandomName();

        Assert.NotEmpty(name);
        Assert.True(name.Length >= 1);
        // First char must be a letter
        Assert.True(char.IsLetter(name[0]));
    }

    [Fact]
    public void GenerateRandomName_CustomLength_RespectsLength()
    {
        using var ctx = SqlServerContext();
        var name = ctx.GenerateRandomName(length: 10);

        Assert.True(name.Length >= 1 && name.Length <= 10);
        Assert.True(char.IsLetter(name[0]));
    }

    [Fact]
    public void GenerateRandomName_UniqueAcrossCalls()
    {
        using var ctx = SqlServerContext();
        var a = ctx.GenerateRandomName(length: 12);
        var b = ctx.GenerateRandomName(length: 12);

        // Astronomically unlikely to collide at length 12
        Assert.NotEqual(a, b);
    }

    // ── QuotePrefix / QuoteSuffix ─────────────────────────────────────────

    [Fact]
    public void QuotePrefix_SqlServer_IsAnsiQuote()
    {
        using var ctx = SqlServerContext();
        Assert.Equal("\"", ctx.QuotePrefix);
        Assert.Equal("\"", ctx.QuoteSuffix);
    }

    [Fact]
    public void QuotePrefix_Postgres_IsDoubleQuote()
    {
        using var ctx = PostgresContext();
        Assert.Equal("\"", ctx.QuotePrefix);
        Assert.Equal("\"", ctx.QuoteSuffix);
    }

    // ── SupportsInsertReturning ───────────────────────────────────────────

    [Fact]
    public void SupportsInsertReturning_Postgres_IsTrue()
    {
        using var ctx = PostgresContext();
        Assert.True(ctx.SupportsInsertReturning);
    }

    [Fact]
    public void SupportsInsertReturning_SqlServer_IsTrue()
    {
        // SqlServer supports OUTPUT INSERTED — SupportsInsertReturning covers both OUTPUT and RETURNING
        using var ctx = SqlServerContext();
        Assert.True(ctx.SupportsInsertReturning);
    }

    // ── CompositeIdentifierSeparator ──────────────────────────────────────

    [Fact]
    public void CompositeIdentifierSeparator_IsDot()
    {
        using var ctx = SqlServerContext();
        Assert.Equal(".", ctx.CompositeIdentifierSeparator);
    }

    // ── MaxOutputParameters ───────────────────────────────────────────────

    [Fact]
    public void MaxOutputParameters_SqlServer_IsPositive()
    {
        using var ctx = SqlServerContext();
        Assert.True(ctx.MaxOutputParameters > 0);
    }
}
