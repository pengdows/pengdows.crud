using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DialectCoverageTests
{
    public static IEnumerable<object[]> DialectData()
    {
        yield return new object[]
        {
            new MySqlDialect(new fakeDbFactory(SupportedDatabase.MySql), NullLogger<MySqlDialect>.Instance),
            "\"",
            true,
            "@",
            true // MySQL supports savepoints since 5.0.3
        };

        yield return new object[]
        {
            new OracleDialect(new fakeDbFactory(SupportedDatabase.Oracle), NullLogger<OracleDialect>.Instance),
            "\"",
            true,
            ":",
            true // Oracle supports savepoints
        };

        yield return new object[]
        {
            new DuckDbDialect(new fakeDbFactory(SupportedDatabase.DuckDB), NullLogger<DuckDbDialect>.Instance),
            "\"",
            true,
            "$",
            false // DuckDB savepoints are currently disabled until driver support stabilizes
        };

        yield return new object[]
        {
            new Sql92Dialect(new fakeDbFactory(SupportedDatabase.Unknown), NullLogger<Sql92Dialect>.Instance),
            "\"",
            true,
            "@",
            false // SQL-92 base dialect does not assume savepoint support
        };

        yield return new object[]
        {
            new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger<PostgreSqlDialect>.Instance),
            "\"",
            true,
            ":",
            true // PostgreSQL supports savepoints
        };

        yield return new object[]
        {
            new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger<SqlServerDialect>.Instance),
            "\"",
            true,
            "@",
            true // SQL Server supports savepoints (SAVE TRANSACTION)
        };

        yield return new object[]
        {
            new SqliteDialect(new fakeDbFactory(SupportedDatabase.Sqlite), NullLogger<SqliteDialect>.Instance),
            "\"",
            true,
            "@",
            true
        };

        yield return new object[]
        {
            new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird), NullLogger<FirebirdDialect>.Instance),
            "\"",
            true,
            "@",
            true
        };
    }

    [Theory]
    [MemberData(nameof(DialectData))]
    public void WrapObjectName_WrapsIdentifier(SqlDialect dialect, string quote, bool supportsNamed, string marker, bool supportsSavepoints)
    {
        _ = supportsNamed;
        _ = marker;
        _ = supportsSavepoints;
        var wrapped = dialect.WrapObjectName("schema.table");
        Assert.Equal($"{quote}schema{quote}.{quote}table{quote}", wrapped);
        Assert.NotEqual("schema.table", wrapped);
    }

    [Theory]
    [MemberData(nameof(DialectData))]
    public void WrapObjectName_Null_ReturnsEmpty(SqlDialect dialect, string quote, bool supportsNamed, string marker, bool supportsSavepoints)
    {
        _ = quote;
        _ = supportsNamed;
        _ = marker;
        _ = supportsSavepoints;
        var wrapped = dialect.WrapObjectName(null!);
        Assert.Equal(string.Empty, wrapped);
        Assert.NotNull(wrapped);
    }

    [Theory]
    [MemberData(nameof(DialectData))]
    public void MakeParameterName_UsesMarker(SqlDialect dialect, string quote, bool supportsNamed, string marker, bool supportsSavepoints)
    {
        _ = quote;
        _ = supportsSavepoints;
        var name = dialect.MakeParameterName("p");
        if (supportsNamed)
        {
            Assert.Equal($"{marker}p", name);
            var unexpectedMarker = marker switch
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
    [MemberData(nameof(DialectData))]
    public void SupportsSavepoints_ReportsCapability(SqlDialect dialect, string quote, bool supportsNamed, string marker, bool supportsSavepoints)
    {
        _ = quote;
        _ = supportsNamed;
        _ = marker;
        Assert.Equal(supportsSavepoints, dialect.SupportsSavepoints);
        if (supportsSavepoints)
        {
            Assert.True(dialect.SupportsSavepoints);
        }
        else
        {
            Assert.False(dialect.SupportsSavepoints);
        }
    }

    [Fact]
    public void SqlServerDialect_GetSavepointSql_UsesSaveTransactionSyntax()
    {
        var dialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger<SqlServerDialect>.Instance);
        var sql = dialect.GetSavepointSql("test_sp");
        Assert.Equal("SAVE TRANSACTION test_sp", sql);
    }

    [Fact]
    public void SqlServerDialect_GetRollbackToSavepointSql_UsesRollbackTransactionSyntax()
    {
        var dialect = new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger<SqlServerDialect>.Instance);
        var sql = dialect.GetRollbackToSavepointSql("test_sp");
        Assert.Equal("ROLLBACK TRANSACTION test_sp", sql);
    }

    [Fact]
    public void PostgreSqlDialect_GetSavepointSql_UsesStandardSyntax()
    {
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger<PostgreSqlDialect>.Instance);
        var sql = dialect.GetSavepointSql("test_sp");
        Assert.Equal("SAVEPOINT test_sp", sql);
    }

    [Fact]
    public void PostgreSqlDialect_GetRollbackToSavepointSql_UsesStandardSyntax()
    {
        var dialect = new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger<PostgreSqlDialect>.Instance);
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
        var dialect = new SqliteDialect(new fakeDbFactory(SupportedDatabase.Sqlite), NullLogger<SqliteDialect>.Instance);
        var sql = dialect.GetSavepointSql("my_savepoint");
        Assert.Equal("SAVEPOINT my_savepoint", sql);
    }
}
