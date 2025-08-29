using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DialectCoverageTests
{
    public static IEnumerable<object[]> DialectData()
    {
        yield return new object[]
        {
            new MySqlDialect(new FakeDbFactory(SupportedDatabase.MySql), NullLogger<MySqlDialect>.Instance),
            "\"",
            true,
            "@",
            false
        };

        yield return new object[]
        {
            new OracleDialect(new FakeDbFactory(SupportedDatabase.Oracle), NullLogger<OracleDialect>.Instance),
            "\"",
            true,
            ":",
            false
        };

        yield return new object[]
        {
            new DuckDbDialect(new FakeDbFactory(SupportedDatabase.DuckDB), NullLogger<DuckDbDialect>.Instance),
            "\"",
            true,
            "$",
            false
        };

        yield return new object[]
        {
            new Sql92Dialect(new FakeDbFactory(SupportedDatabase.Unknown), NullLogger<Sql92Dialect>.Instance),
            "\"",
            true,
            "@",
            false
        };

        yield return new object[]
        {
            new PostgreSqlDialect(new FakeDbFactory(SupportedDatabase.PostgreSql), NullLogger<PostgreSqlDialect>.Instance),
            "\"",
            true,
            ":",
            false
        };

        yield return new object[]
        {
            new SqlServerDialect(new FakeDbFactory(SupportedDatabase.SqlServer), NullLogger<SqlServerDialect>.Instance),
            "\"",
            true,
            "@",
            false
        };

        yield return new object[]
        {
            new SqliteDialect(new FakeDbFactory(SupportedDatabase.Sqlite), NullLogger<SqliteDialect>.Instance),
            "\"",
            true,
            "@",
            true
        };

        yield return new object[]
        {
            new FirebirdDialect(new FakeDbFactory(SupportedDatabase.Firebird), NullLogger<FirebirdDialect>.Instance),
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
        var wrapped = dialect.WrapObjectName("schema.table");
        Assert.Equal($"{quote}schema{quote}.{quote}table{quote}", wrapped);
        Assert.NotEqual("schema.table", wrapped);
        Assert.DoesNotContain("`", wrapped);
    }

    [Theory]
    [MemberData(nameof(DialectData))]
    public void WrapObjectName_Null_ReturnsEmpty(SqlDialect dialect, string quote, bool supportsNamed, string marker, bool supportsSavepoints)
    {
        var wrapped = dialect.WrapObjectName(null);
        Assert.Equal(string.Empty, wrapped);
        Assert.NotNull(wrapped);
    }

    [Theory]
    [MemberData(nameof(DialectData))]
    public void MakeParameterName_UsesMarker(SqlDialect dialect, string quote, bool supportsNamed, string marker, bool supportsSavepoints)
        {
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
