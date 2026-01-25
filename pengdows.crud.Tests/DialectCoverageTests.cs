using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DialectCoverageTests
{
    public static IEnumerable<object[]> SupportedDialects()
    {
        yield return new object[] { SupportedDatabase.MySql };
        yield return new object[] { SupportedDatabase.Oracle };
        yield return new object[] { SupportedDatabase.DuckDB };
        yield return new object[] { SupportedDatabase.Unknown };
        yield return new object[] { SupportedDatabase.PostgreSql };
        yield return new object[] { SupportedDatabase.SqlServer };
        yield return new object[] { SupportedDatabase.Sqlite };
        yield return new object[] { SupportedDatabase.Firebird };
    }

    private static DialectTestConfig BuildConfig(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db.ToString());
        return db switch
        {
            SupportedDatabase.MySql => new DialectTestConfig(
                new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance),
                "\"",
                true,
                "@",
                true),
            SupportedDatabase.Oracle => new DialectTestConfig(
                new OracleDialect(factory, NullLogger<OracleDialect>.Instance),
                "\"",
                true,
                ":",
                true),
            SupportedDatabase.DuckDB => new DialectTestConfig(
                new DuckDbDialect(factory, NullLogger<DuckDbDialect>.Instance),
                "\"",
                true,
                "$",
                false),
            SupportedDatabase.Unknown => new DialectTestConfig(
                new Sql92Dialect(factory, NullLogger<Sql92Dialect>.Instance),
                "\"",
                true,
                "@",
                false),
            SupportedDatabase.PostgreSql => new DialectTestConfig(
                new PostgreSqlDialect(factory, NullLogger<PostgreSqlDialect>.Instance),
                "\"",
                true,
                ":",
                true),
            SupportedDatabase.SqlServer => new DialectTestConfig(
                new SqlServerDialect(factory, NullLogger<SqlServerDialect>.Instance),
                "\"",
                true,
                "@",
                true),
            SupportedDatabase.Sqlite => new DialectTestConfig(
                new SqliteDialect(factory, NullLogger<SqliteDialect>.Instance),
                "\"",
                true,
                "@",
                true),
            SupportedDatabase.Firebird => new DialectTestConfig(
                new FirebirdDialect(factory, NullLogger<FirebirdDialect>.Instance),
                "\"",
                true,
                "@",
                true),
            _ => throw new ArgumentOutOfRangeException(nameof(db), db, null)
        };
    }

    [Theory]
    [MemberData(nameof(SupportedDialects))]
    public void WrapObjectName_WrapsIdentifier(SupportedDatabase db)
    {
        var config = BuildConfig(db);
        var wrapped = config.Dialect.WrapObjectName("schema.table");
        Assert.Equal($"{config.Quote}schema{config.Quote}.{config.Quote}table{config.Quote}", wrapped);
        Assert.NotEqual("schema.table", wrapped);
    }

    [Theory]
    [MemberData(nameof(SupportedDialects))]
    public void WrapObjectName_Null_ReturnsEmpty(SupportedDatabase db)
    {
        var config = BuildConfig(db);
        var wrapped = config.Dialect.WrapObjectName(null!);
        Assert.Equal(string.Empty, wrapped);
        Assert.NotNull(wrapped);
    }

    [Theory]
    [MemberData(nameof(SupportedDialects))]
    public void MakeParameterName_UsesMarker(SupportedDatabase db)
    {
        var config = BuildConfig(db);
        var name = config.Dialect.MakeParameterName("p");
        if (config.SupportsNamed)
        {
            Assert.Equal($"{config.Marker}p", name);
            var unexpectedMarker = config.Marker switch
            {
                "@" => ":",
                ":" => "@",
                "$" => "@",
                _ => "$"
            };
            Assert.NotEqual($"{unexpectedMarker}p", name);
        }
        else
        {
            Assert.Equal("?", name);
            Assert.NotEqual("?p", name);
        }
    }

    [Theory]
    [MemberData(nameof(SupportedDialects))]
    public void SupportsSavepoints_ReportsCapability(SupportedDatabase db)
    {
        var config = BuildConfig(db);
        Assert.Equal(config.SupportsSavepoints, config.Dialect.SupportsSavepoints);
        if (config.SupportsSavepoints)
        {
            Assert.True(config.Dialect.SupportsSavepoints);
        }
        else
        {
            Assert.False(config.Dialect.SupportsSavepoints);
        }
    }

    [Fact]
    public void SqlServerDialect_GetSavepointSql_UsesSaveTransactionSyntax()
    {
        var dialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer),
            NullLogger<SqlServerDialect>.Instance);
        var sql = dialect.GetSavepointSql("test_sp");
        Assert.Equal("SAVE TRANSACTION test_sp", sql);
    }

    [Fact]
    public void SqlServerDialect_GetRollbackToSavepointSql_UsesRollbackTransactionSyntax()
    {
        var dialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer),
            NullLogger<SqlServerDialect>.Instance);
        var sql = dialect.GetRollbackToSavepointSql("test_sp");
        Assert.Equal("ROLLBACK TRANSACTION test_sp", sql);
    }

    [Fact]
    public void PostgreSqlDialect_GetSavepointSql_UsesStandardSyntax()
    {
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql),
            NullLogger<PostgreSqlDialect>.Instance);
        var sql = dialect.GetSavepointSql("test_sp");
        Assert.Equal("SAVEPOINT test_sp", sql);
    }

    [Fact]
    public void PostgreSqlDialect_GetRollbackToSavepointSql_UsesStandardSyntax()
    {
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql),
            NullLogger<PostgreSqlDialect>.Instance);
        var sql = dialect.GetRollbackToSavepointSql("test_sp");
        Assert.Equal("ROLLBACK TO SAVEPOINT test_sp", sql);
    }

    [Fact]
    public void MySqlDialect_GetSavepointSql_UsesStandardSyntax()
    {
        var dialect = new MySqlDialect(new fakeDbFactory(SupportedDatabase.MySql), NullLogger<MySqlDialect>.Instance);
        var sql = dialect.GetSavepointSql("test_sp");
        Assert.Equal("SAVEPOINT test_sp", sql);
    }

    [Fact]
    public void SqliteDialect_GetSavepointSql_UsesStandardSyntax()
    {
        var dialect =
            new SqliteDialect(new fakeDbFactory(SupportedDatabase.Sqlite), NullLogger<SqliteDialect>.Instance);
        var sql = dialect.GetSavepointSql("my_savepoint");
        Assert.Equal("SAVEPOINT my_savepoint", sql);
    }

    private sealed record DialectTestConfig(
        ISqlDialect Dialect,
        string Quote,
        bool SupportsNamed,
        string Marker,
        bool SupportsSavepoints);
}