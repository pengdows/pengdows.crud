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
            false
        };

        yield return new object[]
        {
            new OracleDialect(new fakeDbFactory(SupportedDatabase.Oracle), NullLogger<OracleDialect>.Instance),
            "\"",
            true,
            ":",
            false
        };

        yield return new object[]
        {
            new DuckDbDialect(new fakeDbFactory(SupportedDatabase.DuckDB), NullLogger<DuckDbDialect>.Instance),
            "\"",
            true,
            "$",
            false
        };

        yield return new object[]
        {
            new Sql92Dialect(new fakeDbFactory(SupportedDatabase.Unknown), NullLogger<Sql92Dialect>.Instance),
            "\"",
            true,
            "@",
            false
        };

        yield return new object[]
        {
            new PostgreSqlDialect(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger<PostgreSqlDialect>.Instance),
            "\"",
            true,
            ":",
            false
        };

        yield return new object[]
        {
            new SqlServerDialect(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger<SqlServerDialect>.Instance),
            "\"",
            true,
            "@",
            false
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
}
